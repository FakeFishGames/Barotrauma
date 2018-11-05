using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public enum ActionType
    {
        Always, OnPicked, OnUse, OnSecondaryUse,
        OnWearing, OnContaining, OnContained, OnNotContained,
        OnActive, OnFailure, OnBroken, 
        OnFire, InWater,
        OnImpact,
        OnEating,
        OnDeath = OnBroken
    }

    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        const float MaxVel = 64.0f;

        public static List<Item> ItemList = new List<Item>();
        public ItemPrefab Prefab => prefab as ItemPrefab;

        public static bool ShowLinks = true;
        
        private HashSet<string> tags;
        
        public Hull CurrentHull;
        
        public bool Visible = true;

        public SpriteEffects SpriteEffects = SpriteEffects.None;
        
        //components that determine the functionality of the item
        public List<ItemComponent> components;
        public List<IDrawableComponent> drawableComponents;

        public PhysicsBody body;

        public readonly XElement StaticBodyConfig;
        
        private Vector2 lastSentPos;
        private bool prevBodyAwake;

        private bool needsPositionUpdate;
        private float lastSentCondition;

        private float condition;

        private bool inWater;
                
        private Inventory parentInventory;
        private Inventory ownInventory;
        
        private Dictionary<string, Connection> connections;

        private List<Repairable> repairables;

        //a dictionary containing lists of the status effects in all the components of the item
        private Dictionary<ActionType, List<StatusEffect>> statusEffectLists;
        
        public readonly Dictionary<string, SerializableProperty> properties;
        public Dictionary<string, SerializableProperty> SerializableProperties
        {
            get { return properties; }
        }

        private bool? hasInGameEditableProperties;
        bool HasInGameEditableProperties
        {
            get
            {
                if (hasInGameEditableProperties==null)
                {
                    hasInGameEditableProperties = GetProperties<InGameEditable>().Any();
                }
                return (bool)hasInGameEditableProperties;
            }
        }

        //the inventory in which the item is contained in
        public Inventory ParentInventory
        {
            get
            {
                return parentInventory;
            }
            set
            {
                parentInventory = value;

                if (parentInventory != null) Container = parentInventory.Owner as Item;                
            }
        }

        public Item Container
        {
            get;
            private set;
        }
                
        public override string Name
        {
            get { return prefab.Name; }
        }

        private string description;
        [Editable, Serialize("", true)]
        public string Description
        {
            get { return description ?? prefab.Description; }
            set { description = value; }
        }

        public float ImpactTolerance
        {
            get { return Prefab.ImpactTolerance; }
        }
        
        public float InteractDistance
        {
            get { return Prefab.InteractDistance; }
        }

        public float InteractPriority
        {
            get { return Prefab.InteractPriority; }
        }

        public override Vector2 Position
        {
            get
            {
                return (body == null) ? base.Position : ConvertUnits.ToDisplayUnits(SimPosition);
            }
        }

        public override Vector2 SimPosition
        {
            get
            {
                return (body == null) ? base.SimPosition : body.SimPosition;
            }
        }

        public Rectangle InteractionRect
        {
            get
            {
                return WorldRect;
            }
        }

        public bool NeedsPositionUpdate
        {
            get
            {
                if (body == null || !body.Enabled) return false;
                return needsPositionUpdate;
            }
            set
            {
                needsPositionUpdate = value;
            }
        }

        protected Color spriteColor;
        [Editable, Serialize("1.0,1.0,1.0,1.0", true)]
        public Color SpriteColor
        {
            get { return spriteColor; }
            set { spriteColor = value; }
        }

        public Color Color
        {
            get { return spriteColor; }
        }

        public float Condition
        {
            get { return condition; }
            set 
            {
#if CLIENT
                if (GameMain.Client != null) return;
#endif
                if (!MathUtils.IsValid(value)) return;
                if (Prefab.Indestructible) return;

                float prev = condition;
                condition = MathHelper.Clamp(value, 0.0f, Prefab.Health);
                if (condition == 0.0f && prev > 0.0f)
                {
#if CLIENT
                    foreach (ItemComponent ic in components)
                    {
                        ic.PlaySound(ActionType.OnBroken, WorldPosition);
                    }
#endif
                    ApplyStatusEffects(ActionType.OnBroken, 1.0f, null);
                    foreach (Repairable repairable in GetComponents<Repairable>())
                    {
                        repairable.RepairProgress = 0.0f;
                    }
                }
                
                SetActiveSprite();

                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer && lastSentCondition != condition)
                {
                    if (Math.Abs(lastSentCondition - condition) > 1.0f || condition == 0.0f || condition == Prefab.Health)
                    {
                        GameMain.NetworkMember.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                        lastSentCondition = condition;
                    }
                }
            }
        }

        public float Health
        {
            get { return condition; }
        }

        [Editable, Serialize("", true)]
        public string Tags
        {
            get { return string.Join(",", tags); }
            set
            {
                tags.Clear();
                // Always add prefab tags
                prefab.Tags.ForEach(t => tags.Add(t));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    // Process and add new tags
                    value.Split(',').ForEach(t => tags.Add(t.ToLowerInvariant().Trim()));
                }
            }
        }

        public bool FireProof
        {
            get { return Prefab.FireProof; }
        }

        public bool WaterProof
        {
            get { return Prefab.WaterProof; }
        }

        public bool UseInHealthInterface
        {
            get { return Prefab.UseInHealthInterface; }
        }

        public bool InWater
        {
            get 
            { 
                //if the item has an active physics body, inWater is updated in the Update method
                if (body != null && body.Enabled) return inWater;

                //if not, we'll just have to check
                return IsInWater();
            }
        }

        /// <summary>
        /// A list of items the last signal sent by this item went through
        /// </summary>
        public List<Item> LastSentSignalRecipients
        {
            get;
            private set;
        } = new List<Item>();

        public string ConfigFile
        {
            get { return Prefab.ConfigFile; }
        }
        
        //which type of inventory slots (head, torso, any, etc) the item can be placed in
        public List<InvSlotType> AllowedSlots
        {
            get
            {
                Pickable p = GetComponent<Pickable>();
                return (p==null) ? new List<InvSlotType>() { InvSlotType.Any } : p.AllowedSlots;
            }
        }
        
        public List<Connection> Connections
        {
            get 
            {
                ConnectionPanel panel = GetComponent<ConnectionPanel>();
                if (panel == null) return null;
                return panel.Connections;
            }
        }

        public Item[] ContainedItems
        {
            get
            {
                return (ownInventory == null) ? null : Array.FindAll(ownInventory.Items, i => i != null);
            }
        }

        public Inventory OwnInventory
        {
            get { return ownInventory; }
        }

        public IEnumerable<Repairable> Repairables
        {
            get { return repairables; }
        }

        public override bool Linkable
        {
            get { return Prefab.Linkable; }
        }

        public override string ToString()
        {
#if CLIENT
            return (GameMain.DebugDraw) ? Name + " (ID: " + ID + ")" : Name;
#elif SERVER
            return Name + " (ID: " + ID + ")";
#endif
        }

        public List<ISerializableEntity> AllPropertyObjects
        {
            get
            {
                List<ISerializableEntity> pobjects = new List<ISerializableEntity>();
                pobjects.Add(this);
                foreach (ItemComponent ic in components)
                {
                    pobjects.Add(ic);
                }
                return pobjects;
            }
        }

        public Item(ItemPrefab itemPrefab, Vector2 position, Submarine submarine)
            : this(new Rectangle(
                (int)(position.X - itemPrefab.sprite.size.X / 2), 
                (int)(position.Y + itemPrefab.sprite.size.Y / 2), 
                (int)itemPrefab.sprite.size.X, 
                (int)itemPrefab.sprite.size.Y), 
            itemPrefab, submarine)
        {

        }

        public Item(Rectangle newRect, ItemPrefab itemPrefab, Submarine submarine)
            : base(itemPrefab, submarine)
        {
            spriteColor = prefab.SpriteColor;

            linkedTo            = new ObservableCollection<MapEntity>();
            components          = new List<ItemComponent>();
            drawableComponents  = new List<IDrawableComponent>();
            tags                = new HashSet<string>();
            repairables         = new List<Repairable>();
                       
            rect = newRect;
                        
            condition = itemPrefab.Health;
            lastSentCondition = condition;

            XElement element = itemPrefab.ConfigElement;
            if (element == null) return;
            
            properties = SerializableProperty.DeserializeProperties(this, element);

            if (submarine == null || !submarine.Loading) FindHull();

            SetActiveSprite();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "body":
                        body = new PhysicsBody(subElement, ConvertUnits.ToSimUnits(Position));
                        body.FarseerBody.AngularDamping = 0.2f;
                        body.FarseerBody.LinearDamping  = 0.1f;
                        break;
                    case "trigger":
                    case "inventoryicon":
                    case "sprite":
                    case "deconstruct":
                    case "brokensprite":
                    case "price":
                        break;
                    case "staticbody":
                        StaticBodyConfig = subElement;
                        break;
                    case "aitarget":
                        aiTarget = new AITarget(this, subElement);
                        break;
                    default:
                        ItemComponent ic = ItemComponent.Load(subElement, this, itemPrefab.ConfigFile);
                        if (ic == null) break;

                        components.Add(ic);

                        if (ic is IDrawableComponent && ic.Drawable) drawableComponents.Add(ic as IDrawableComponent);
                        if (ic is Repairable) repairables.Add((Repairable)ic);
                        break;
                }
            }

            foreach (ItemComponent ic in components)
            {
                if (ic.statusEffectLists == null) continue;

                if (statusEffectLists == null)
                    statusEffectLists = new Dictionary<ActionType, List<StatusEffect>>();

                //go through all the status effects of the component 
                //and add them to the corresponding statuseffect list
                foreach (List<StatusEffect> componentEffectList in ic.statusEffectLists.Values)
                {
                    ActionType actionType = componentEffectList.First().type;
                    if (!statusEffectLists.TryGetValue(actionType, out List<StatusEffect> statusEffectList))
                    {
                        statusEffectList = new List<StatusEffect>();
                        statusEffectLists.Add(actionType, statusEffectList);
                    }

                    foreach (StatusEffect effect in componentEffectList)
                    {
                        statusEffectList.Add(effect);
                    }
                }
            }
            
            if (body != null)
            {
                body.Submarine = submarine;
            }

            //cache connections into a dictionary for faster lookups
            var connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel != null)
            {
                connections = new Dictionary<string, Connection>();
                foreach (Connection c in connectionPanel.Connections)
                {
                    if (!connections.ContainsKey(c.Name))
                        connections.Add(c.Name, c);
                }
            }

            //containers need to handle collision events to notify items inside them about the impact
            var itemContainer = GetComponent<ItemContainer>();
            if (ImpactTolerance > 0.0f || itemContainer != null)
            {
                if (body != null) body.FarseerBody.OnCollision += OnCollision;
            }

            if (itemContainer != null)
            {
                ownInventory = itemContainer.Inventory;
            }
                        
            InsertToList();
            ItemList.Add(this);

            foreach (ItemComponent ic in components)
            {
                ic.OnItemLoaded();
            }
        }

        public override MapEntity Clone()
        {
            Item clone = new Item(rect, Prefab, Submarine);
            foreach (KeyValuePair<string, SerializableProperty> property in properties)
            {
                if (!property.Value.Attributes.OfType<Editable>().Any()) continue;
                clone.properties[property.Key].TrySetValue(property.Value.GetValue());
            }

            if (components.Count != clone.components.Count)
            {
                string errorMsg = "Error while cloning item \"" + Name + "\" - clone does not have the same number of components. ";
                errorMsg += "Original components: " + string.Join(", ", components.Select(c => c.GetType().ToString()));
                errorMsg += ", cloned components: " + string.Join(", ", clone.components.Select(c => c.GetType().ToString()));
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Item.Clone:" + Name, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
            }

            for (int i = 0; i < components.Count && i < clone.components.Count; i++)
            {
                foreach (KeyValuePair<string, SerializableProperty> property in components[i].properties)
                {
                    if (!property.Value.Attributes.OfType<Editable>().Any()) continue;
                    clone.components[i].properties[property.Key].TrySetValue(property.Value.GetValue());
                }

                //clone requireditem identifiers
                foreach (var kvp in components[i].requiredItems)
                {
                    for (int j = 0; j < kvp.Value.Count; j++)
                    {
                        if (!clone.components[i].requiredItems.ContainsKey(kvp.Key) ||
                            clone.components[i].requiredItems[kvp.Key].Count <= j)
                        {
                            continue;
                        }

                        clone.components[i].requiredItems[kvp.Key][j].JoinedIdentifiers = 
                            kvp.Value[j].JoinedIdentifiers;
                    }
                }
            }

            if (FlippedX) clone.FlipX(false);
            if (FlippedY) clone.FlipY(false);

            if (ContainedItems != null)
            {
                foreach (Item containedItem in ContainedItems)
                {
                    var containedClone = containedItem.Clone();
                    clone.ownInventory.TryPutItem(containedClone as Item, null);
                }
            }
            return clone;
        }

        public T GetComponent<T>()
        {
            foreach (ItemComponent ic in components)
            {
                if (ic is T) return (T)(object)ic;
            }

            return default(T);
        }

        public List<T> GetComponents<T>()
        {
            List<T> components = new List<T>();
            foreach (ItemComponent ic in this.components)
            {
                if (ic is T) components.Add((T)(object)ic);
            }

            return components;
        }
        
        public void RemoveContained(Item contained)
        {
            if (ownInventory != null)
            {
                ownInventory.RemoveItem(contained);
            }

            contained.Container = null;            
        }


        public void SetTransform(Vector2 simPosition, float rotation, bool findNewHull = true)
        {
            if (!MathUtils.IsValid(simPosition))
            {
                string errorMsg =
                    "Attempted to move the item " + Name +
                    " to an invalid position (" + simPosition + ")\n" + Environment.StackTrace;

                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "Item.SetPosition:InvalidPosition" + ID,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMsg);
                return;
            }

            if (body != null)
            {
#if DEBUG
                try
                {
#endif
                    if (body.Enabled)
                    {
                        body.SetTransform(simPosition, rotation);
                    }
                    else
                    {
                        body.SetTransformIgnoreContacts(simPosition, rotation);
                    }
#if DEBUG
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to set item transform", e);
                }
#endif
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(simPosition);

            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            if (findNewHull) FindHull();
        }

        partial void SetActiveSprite();

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            if (ItemList != null && body != null)
            {
                body.SetTransform(body.SimPosition + ConvertUnits.ToSimUnits(amount), body.Rotation);
            }
            foreach (ItemComponent ic in components)
            {
                ic.Move(amount);
            }

            if (body != null && (Submarine==null || !Submarine.Loading)) FindHull();
        }

        public Rectangle TransformTrigger(Rectangle trigger, bool world = false)
        {
            return world ? 
                new Rectangle(
                    WorldRect.X + trigger.X,
                    WorldRect.Y + trigger.Y,
                    (trigger.Width == 0) ? Rect.Width : trigger.Width,
                    (trigger.Height == 0) ? Rect.Height : trigger.Height)
                    :
                new Rectangle(
                    Rect.X + trigger.X,
                    Rect.Y + trigger.Y,
                    (trigger.Width == 0) ? Rect.Width : trigger.Width,
                    (trigger.Height == 0) ? Rect.Height : trigger.Height);
        }

        /// <summary>
        /// goes through every item and re-checks which hull they are in
        /// </summary>
        public static void UpdateHulls()
        {
            foreach (Item item in ItemList) item.FindHull();
        }
        
        public Hull FindHull()
        {
            if (parentInventory != null && parentInventory.Owner != null)
            {
                if (parentInventory.Owner is Character)
                {
                    CurrentHull = ((Character)parentInventory.Owner).AnimController.CurrentHull;
                }
                else if (parentInventory.Owner is Item)
                {
                    CurrentHull = ((Item)parentInventory.Owner).CurrentHull;
                }

                Submarine = parentInventory.Owner.Submarine;
                if (body != null) body.Submarine = Submarine;

                return CurrentHull;
            }


            CurrentHull = Hull.FindHull(WorldPosition, CurrentHull);
            if (body != null && body.Enabled)
            {
                Submarine = CurrentHull == null ? null : CurrentHull.Submarine;
                body.Submarine = Submarine;
            }

            return CurrentHull;
        }

        public Item GetRootContainer()
        {
            if (Container == null) return null;

            Item rootContainer = Container;
            while (rootContainer.Container != null)
            {
                rootContainer = rootContainer.Container;
            }

            return rootContainer;
        }
                
        public void SetContainedItemPositions()
        {
            foreach (ItemComponent component in components)
            {
                (component as ItemContainer)?.SetContainedItemPositions();
            }
        }
        
        public void AddTag(string tag)
        {
            if (tags.Contains(tag)) return;
            tags.Add(tag);
        }

        public bool HasTag(string tag)
        {
            if (tag == null) return true;
            return tags.Contains(tag) || prefab.Tags.Contains(tag);
        }

        public bool HasTag(IEnumerable<string> allowedTags)
        {
            if (allowedTags == null) return true;
            foreach (string tag in allowedTags)
            {
                if (tags.Contains(tag)) return true;
            }
            return false;
        }
        
        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null, Limb limb = null, bool isNetworkEvent = false)
        {
            if (statusEffectLists == null) return;

            List<StatusEffect> statusEffects;
            if (!statusEffectLists.TryGetValue(type, out statusEffects)) return;

            bool broken = condition <= 0.0f;
            foreach (StatusEffect effect in statusEffects)
            {
                if (broken && effect.type != ActionType.OnBroken) continue;
                ApplyStatusEffect(effect, type, deltaTime, character, limb, isNetworkEvent, false);
            }
        }
        
        public void ApplyStatusEffect(StatusEffect effect, ActionType type, float deltaTime, Character character = null, Limb limb = null, bool isNetworkEvent = false, bool checkCondition = true)
        {
            if (!isNetworkEvent && checkCondition)
            {
                if (condition == 0.0f && effect.type != ActionType.OnBroken) return;
            }
            if (effect.type != type) return;
            
            bool hasTargets = (effect.TargetIdentifiers == null);
            List<ISerializableEntity> targets = new List<ISerializableEntity>();

            Item[] containedItems = ContainedItems;  
            if (containedItems != null)
            {
                if (effect.HasTargetType(StatusEffect.TargetType.Contained))
                {
                    foreach (Item containedItem in containedItems)
                    {
                        if (containedItem == null) continue;
                        if (effect.TargetIdentifiers != null &&
                            !effect.TargetIdentifiers.Contains(containedItem.prefab.Identifier) &&
                            !effect.TargetIdentifiers.Any(id => containedItem.HasTag(id)))
                        {
                            continue;
                        }

                        hasTargets = true;
                        targets.Add(containedItem);
                    }
                }
            }

            if (!hasTargets) return;

            if (effect.HasTargetType(StatusEffect.TargetType.Hull) && CurrentHull != null)
            {
                targets.Add(CurrentHull);
            }

            if (effect.HasTargetType(StatusEffect.TargetType.This))
            {
                foreach (var pobject in AllPropertyObjects)
                {
                    targets.Add(pobject);
                }
            }

            if (effect.HasTargetType(StatusEffect.TargetType.Character)) targets.Add(character);

            if (effect.HasTargetType(StatusEffect.TargetType.Limb))
            {
                targets.Add(limb);
            }
            if (effect.HasTargetType(StatusEffect.TargetType.AllLimbs))
            {
                targets.AddRange(character.AnimController.Limbs.ToList());
            }

            if (Container != null && effect.HasTargetType(StatusEffect.TargetType.Parent)) targets.Add(Container);
            
            effect.Apply(type, deltaTime, this, targets);            
        }


        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = true)
        {
            if (Prefab.Indestructible) return new AttackResult();

            float damageAmount = attack.GetItemDamage(deltaTime);
            Condition -= damageAmount;

            return new AttackResult(damageAmount, null);
        }

        private bool IsInWater()
        {
            if (CurrentHull == null) return true;
                        
            float surfaceY = CurrentHull.Surface;

            return CurrentHull.WaterVolume > 0.0f && Position.Y < surfaceY;
        }


        public override void Update(float deltaTime, Camera cam)
        {
            //aitarget goes silent/invisible if the components don't keep it active
            if (aiTarget != null)
            {
                aiTarget.SightRange -= deltaTime * 1000.0f;
                aiTarget.SoundRange -= deltaTime * 1000.0f;
            }

            ApplyStatusEffects(ActionType.Always, deltaTime, null);

            foreach (ItemComponent ic in components)
            {
                if (ic.Parent != null) ic.IsActive = ic.Parent.IsActive;

#if CLIENT
                if (!ic.WasUsed)
                {
                    ic.StopSounds(ActionType.OnUse);
                    ic.StopSounds(ActionType.OnSecondaryUse);
                }
#endif
                ic.WasUsed = false;

                ic.ApplyStatusEffects(parentInventory == null ? ActionType.OnNotContained : ActionType.OnContained, deltaTime);

                if (!ic.IsActive) continue;

                if (condition > 0.0f)
                {
                    ic.Update(deltaTime, cam);

#if CLIENT
                    if (ic.IsActive) ic.PlaySound(ActionType.OnActive, WorldPosition);
#endif
                }
                else
                {
                    ic.UpdateBroken(deltaTime, cam);
                }
            }

            /*if (condition <= 0.0f && FixRequirements.Count > 0)
            {
                bool isFixed = true;
                foreach (FixRequirement fixRequirement in FixRequirements)
                {
                    fixRequirement.Update(deltaTime);
                    if (!fixRequirement.Fixed) isFixed = false;
                }
                if (isFixed)
                {
                    GameMain.Server?.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                    condition = Prefab.Health;
                }
            }*/
            
            if (body != null && body.Enabled)
            {
                System.Diagnostics.Debug.Assert(body.FarseerBody.FixtureList != null);

                if (Math.Abs(body.LinearVelocity.X) > 0.01f || Math.Abs(body.LinearVelocity.Y) > 0.01f)
                {
                    UpdateTransform();
                    if (CurrentHull == null && body.SimPosition.Y < ConvertUnits.ToSimUnits(Level.MaxEntityDepth))
                    {
                        Spawner.AddToRemoveQueue(this);
                        return;
                    }
                }

                UpdateNetPosition();
            }

            inWater = IsInWater();
            if (inWater)
            {
                bool waterProof = WaterProof;
                Item container = this.Container;
                while (!waterProof && container != null)
                {
                    waterProof = container.WaterProof;
                    container = container.Container;
                }
                if (!waterProof) ApplyStatusEffects(ActionType.InWater, deltaTime);
            }

            if (body == null || !body.Enabled || !inWater || ParentInventory != null) return;

            ApplyWaterForces();
            CurrentHull?.ApplyFlowForces(deltaTime, this);
        }

        public void UpdateTransform()
        {
            Submarine prevSub = Submarine;

            FindHull();

            if (Submarine == null && prevSub != null)
            {
                body.SetTransform(body.SimPosition + prevSub.SimPosition, body.Rotation);
            }
            else if (Submarine != null && prevSub == null)
            {
                body.SetTransform(body.SimPosition - Submarine.SimPosition, body.Rotation);
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            if (Math.Abs(body.LinearVelocity.X) > MaxVel || Math.Abs(body.LinearVelocity.Y) > MaxVel)
            {
                body.LinearVelocity = new Vector2(
                    MathHelper.Clamp(body.LinearVelocity.X, -MaxVel, MaxVel),
                    MathHelper.Clamp(body.LinearVelocity.Y, -MaxVel, MaxVel));
            }
        }

        /// <summary>
        /// Applies buoyancy, drag and angular drag caused by water
        /// </summary>
        private void ApplyWaterForces()
        {
            float forceFactor = 1.0f;
            if (CurrentHull != null)
            {
                float floor = CurrentHull.Rect.Y - CurrentHull.Rect.Height;
                float waterLevel = floor + CurrentHull.WaterVolume / CurrentHull.Rect.Width;

                //forceFactor is 1.0f if the item is completely submerged, 
                //and goes to 0.0f as the item goes through the surface
                forceFactor = Math.Min((waterLevel - Position.Y) / rect.Height, 1.0f);
                if (forceFactor <= 0.0f) return;
            }

            float volume = body.Mass / body.Density;

            var uplift = -GameMain.World.Gravity * forceFactor * volume;

            Vector2 drag = body.LinearVelocity * volume;

            body.ApplyForce((uplift - drag) * 10.0f);

            //apply simple angular drag
            body.ApplyTorque(body.AngularVelocity * volume * -0.05f);                    
        }

        private bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
#if CLIENT
            if (GameMain.Client != null) return true;
#endif

            Vector2 normal = contact.Manifold.LocalNormal;
            
            float impact = Vector2.Dot(f1.Body.LinearVelocity, -normal);

            if (ImpactTolerance > 0.0f && impact > ImpactTolerance)
            {
                ApplyStatusEffects(ActionType.OnImpact, 1.0f);
#if SERVER
                GameMain.Server?.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnImpact });
#endif
            }

            var containedItems = ContainedItems;
            if (containedItems != null)
            {
                foreach (Item contained in containedItems)
                {
                    if (contained.body == null) continue;
                    contained.OnCollision(f1, f2, contact);
                }
            }

            return true;
        }

        public override void FlipX(bool relativeToSub)
        {
            base.FlipX(relativeToSub);
            
            if (Prefab.CanSpriteFlipX)
            {
                SpriteEffects ^= SpriteEffects.FlipHorizontally;
            }

            foreach (ItemComponent component in components)
            {
                component.FlipX(relativeToSub);
            }            
        }

        public override void FlipY(bool relativeToSub)
        {
            base.FlipY(relativeToSub);

            if (Prefab.CanSpriteFlipY)
            {
                SpriteEffects ^= SpriteEffects.FlipVertically;
            }

            foreach (ItemComponent component in components)
            {
                component.FlipY(relativeToSub);
            }
        }

        public override bool IsVisible(Rectangle worldView)
        {
            return drawableComponents.Count > 0 || body == null || body.Enabled;
        }

        public List<T> GetConnectedComponents<T>(bool recursive = false)
        {
            List<T> connectedComponents = new List<T>();

            if (recursive)
            {
                List<Item> alreadySearched = new List<Item>() {this};
                GetConnectedComponentsRecursive<T>(alreadySearched, connectedComponents);

                return connectedComponents;
            }

            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) return connectedComponents;


            foreach (Connection c in connectionPanel.Connections)
            {
                var recipients = c.Recipients;
                foreach (Connection recipient in recipients)
                {
                    var component = recipient.Item.GetComponent<T>();
                    if (component != null) connectedComponents.Add(component);
                }
            }

            return connectedComponents;
        }

        private void GetConnectedComponentsRecursive<T>(List<Item> alreadySearched, List<T> connectedComponents)
        {
            alreadySearched.Add(this);

            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) return;

            foreach (Connection c in connectionPanel.Connections)
            {
                var recipients = c.Recipients;
                foreach (Connection recipient in recipients)
                {
                    if (alreadySearched.Contains(recipient.Item)) continue;

                    var component = recipient.Item.GetComponent<T>();
                    
                    if (component != null)
                    {
                        connectedComponents.Add(component);
                    }

                    recipient.Item.GetConnectedComponentsRecursive<T>(alreadySearched, connectedComponents);                   
                }
            }
        }

        public List<T> GetConnectedComponentsRecursive<T>(Connection c)
        {
            List<T> connectedComponents = new List<T>();            
            List<Item> alreadySearched = new List<Item>() { this };
            GetConnectedComponentsRecursive<T>(c, alreadySearched, connectedComponents);

            return connectedComponents;
        }

        private void GetConnectedComponentsRecursive<T>(Connection c, List<Item> alreadySearched, List<T> connectedComponents)
        {
            alreadySearched.Add(this);
                        
            var recipients = c.Recipients;
            foreach (Connection recipient in recipients)
            {
                if (alreadySearched.Contains(recipient.Item)) continue;

                var component = recipient.Item.GetComponent<T>();                    
                if (component != null)
                {
                    connectedComponents.Add(component);
                }

                recipient.Item.GetConnectedComponentsRecursive<T>(recipient, alreadySearched, connectedComponents);                   
            }            
        }


        public void SendSignal(int stepsTaken, string signal, string connectionName, Character sender, float power = 0.0f, Item source = null, float signalStrength = 1.0f)
        {
            LastSentSignalRecipients.Clear();
            if (connections == null) return;

            stepsTaken++;

            if (!connections.TryGetValue(connectionName, out Connection c)) return;

            if (stepsTaken > 10)
            {
                //use a coroutine to prevent infinite loops by creating a one 
                //frame delay if the "signal chain" gets too long
                CoroutineManager.StartCoroutine(SendSignal(signal, c, sender, power, signalStrength));
            }
            else
            {
                c.SendSignal(stepsTaken, signal, source ?? this, sender, power, signalStrength);
            }            
        }

        private IEnumerable<object> SendSignal(string signal, Connection connection, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            //wait one frame
            yield return CoroutineStatus.Running;

            connection.SendSignal(0, signal, this, sender, power, signalStrength);

            yield return CoroutineStatus.Success;
        }

        public float GetDrawDepth()
        {
            return SpriteDepth + ((ID % 255) * 0.000001f);
        }

        public bool IsInsideTrigger(Vector2 worldPosition)
        {
            return IsInsideTrigger(worldPosition, out _);
        }

        public bool IsInsideTrigger(Vector2 worldPosition, out Rectangle transformedTrigger)
        {
            foreach (Rectangle trigger in Prefab.Triggers)
            {
                transformedTrigger = TransformTrigger(trigger, true);
                if (Submarine.RectContains(transformedTrigger, worldPosition)) return true;
            }

            transformedTrigger = Rectangle.Empty;
            return false;
        }

        public bool CanClientAccess(Client c)
        {
            return c != null && c.Character != null && c.Character.CanInteractWith(this);
        }

        public bool TryInteract(Character picker, bool ignoreRequiredItems = false, bool forceSelectKey = false, bool forceActionKey = false)
        {
            bool hasRequiredSkills = true;

            bool picked = false, selected = false;

            Skill requiredSkill = null;
            
            foreach (ItemComponent ic in components)
            {
                bool pickHit = false, selectHit = false;
                if (Screen.Selected == GameMain.SubEditorScreen)
                {
                    pickHit = picker.IsKeyHit(InputType.Select);
                    selectHit = picker.IsKeyHit(InputType.Select);
                }
                else
                {
                    if (forceSelectKey)
                    {
                        if (ic.PickKey == InputType.Select) pickHit = true;
                        if (ic.SelectKey == InputType.Select) selectHit = true;
                    }
                    else if (forceActionKey)
                    {
                        if (ic.PickKey == InputType.Use) pickHit = true;
                        if (ic.SelectKey == InputType.Use) selectHit = true;
                    }
                    else
                    {
                        pickHit = picker.IsKeyHit(ic.PickKey);
                        selectHit = picker.IsKeyHit(ic.SelectKey);

#if CLIENT
                        //if the cursor is on a UI component, disable interaction with the left mouse button
                        //to prevent accidentally selecting items when clicking UI elements
                        if (picker == Character.Controlled && GUI.MouseOn != null)
                        {
                            if (GameMain.Config.KeyBind(ic.PickKey).MouseButton == 0) pickHit = false;
                            if (GameMain.Config.KeyBind(ic.SelectKey).MouseButton == 0) selectHit = false;
                        }
#endif
                    }
                }


                if (!pickHit && !selectHit) continue;

                Skill tempRequiredSkill;
                if (!ic.HasRequiredSkills(picker, out tempRequiredSkill)) hasRequiredSkills = false;

                if (tempRequiredSkill != null) requiredSkill = tempRequiredSkill;

                bool showUiMsg = false;
#if CLIENT
                showUiMsg = picker == Character.Controlled && Screen.Selected != GameMain.SubEditorScreen;
#endif
                if (!ignoreRequiredItems && !ic.HasRequiredItems(picker, showUiMsg)) continue;
                if ((ic.CanBePicked && pickHit && ic.Pick(picker)) ||
                    (ic.CanBeSelected && selectHit && ic.Select(picker)))
                {
                    picked = true;
                    ic.ApplyStatusEffects(ActionType.OnPicked, 1.0f, picker);

#if CLIENT
                    if (picker == Character.Controlled) GUI.ForceMouseOn(null);
#endif

                    if (ic.CanBeSelected) selected = true;
                }
            }

            if (!picked) return false;

            if (picker.SelectedConstruction == this)
            {
                if (picker.IsKeyHit(InputType.Select) || forceSelectKey) picker.SelectedConstruction = null;
            }
            else if (selected)
            {
                picker.SelectedConstruction = this;
            }

#if CLIENT
            if (!hasRequiredSkills && Character.Controlled == picker && Screen.Selected != GameMain.SubEditorScreen)
            {
                if (requiredSkill != null)
                {
                    GUI.AddMessage(TextManager.Get("InsufficientSkills")
                        .Replace("[requiredskill]", TextManager.Get("SkillName." + requiredSkill.Identifier))
                        .Replace("[requiredlevel]", ((int)requiredSkill.Level).ToString()), Color.Red);
                }
            }
#endif

            if (Container != null) Container.RemoveContained(this);

            return true;         
        }


        public void Use(float deltaTime, Character character = null, Limb targetLimb = null)
        {
            if (condition == 0.0f) return;

            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                bool isControlled = false;
#if CLIENT
                isControlled = character == Character.Controlled;
#endif
                if (!ic.HasRequiredContainedItems(isControlled)) continue;
                if (ic.Use(deltaTime, character))
                {
                    ic.WasUsed = true;

#if CLIENT
                    ic.PlaySound(ActionType.OnUse, WorldPosition, character);
#endif
    
                    ic.ApplyStatusEffects(ActionType.OnUse, deltaTime, character, targetLimb);

                    if (ic.DeleteOnUse) remove = true;
                }
            }

            if (remove)
            {
                Spawner.AddToRemoveQueue(this);
            }
        }

        public void SecondaryUse(float deltaTime, Character character = null)
        {
            if (condition == 0.0f) return;

            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                bool isControlled = false;
#if CLIENT
                isControlled = character == Character.Controlled;
#endif
                if (!ic.HasRequiredContainedItems(isControlled)) continue;
                if (ic.SecondaryUse(deltaTime, character))
                {
                    ic.WasUsed = true;

#if CLIENT
                    ic.PlaySound(ActionType.OnSecondaryUse, WorldPosition, character);
#endif

                    ic.ApplyStatusEffects(ActionType.OnSecondaryUse, deltaTime, character);

                    if (ic.DeleteOnUse) remove = true;
                }
            }

            if (remove)
            {
                Spawner.AddToRemoveQueue(this);
            }
        }

        public List<ColoredText> GetHUDTexts(Character character)
        {
            List<ColoredText> texts = new List<ColoredText>();
            
            foreach (ItemComponent ic in components)
            {
                if (string.IsNullOrEmpty(ic.Msg)) continue;
                if (!ic.CanBePicked && !ic.CanBeSelected) continue;
                if (ic is Holdable holdable && !holdable.CanBeDeattached()) continue;
               
                Color color = Color.Red;
                if (ic.HasRequiredSkills(character) && ic.HasRequiredItems(character, false)) color = Color.Orange;

                texts.Add(new ColoredText(ic.Msg, color, false));
            }

            return texts;
        }

        public bool Combine(Item item)
        {
            bool isCombined = false;
            foreach (ItemComponent ic in components)
            {
                if (ic.Combine(item)) isCombined = true;
            }
            return isCombined;
        }

        public void Drop(Character dropper = null)
        {
            foreach (ItemComponent ic in components) ic.Drop(dropper);

            if (Container != null)
            {
                if (body != null)
                {
                    body.Enabled = true;
                    body.LinearVelocity = Vector2.Zero;
                }
                SetTransform(Container.SimPosition, 0.0f);

                Container.RemoveContained(this);
                Container = null;
            }

            if (parentInventory != null)
            {
                parentInventory.RemoveItem(this);
                parentInventory = null;
            }

            lastSentPos = SimPosition;
        }

        public void Equip(Character character)
        {
            foreach (ItemComponent ic in components) ic.Equip(character);
        }

        public void Unequip(Character character)
        {
            character.DeselectItem(this);
            foreach (ItemComponent ic in components) ic.Unequip(character);
        }


        public List<SerializableProperty> GetProperties<T>()
        {
            List<SerializableProperty> editableProperties = SerializableProperty.GetProperties<T>(this);
            
            foreach (ItemComponent ic in components)
            {
                List<SerializableProperty> componentProperties = SerializableProperty.GetProperties<T>(ic);
                foreach (var property in componentProperties)
                {
                    editableProperties.Add(property);
                }
            }

            return editableProperties;
        }
        
        private void WritePropertyChange(NetBuffer msg, object[] extraData, bool inGameEditableOnly)
        {
            var allProperties = inGameEditableOnly ? GetProperties<InGameEditable>() : GetProperties<Editable>();
            SerializableProperty property = extraData[1] as SerializableProperty;
            if (property != null)
            {
                if (allProperties.Count > 1)
                {
                    msg.WriteRangedInteger(0, allProperties.Count - 1, allProperties.IndexOf(property));
                }

                object value = property.GetValue();
                if (value is string)
                {
                    msg.Write((string)value);
                }
                else if (value is float)
                {
                    msg.Write((float)value);
                }
                else if (value is int)
                {
                    msg.Write((int)value);
                }
                else if (value is bool)
                {
                    msg.Write((bool)value);
                }
                else if (value is Color)
                {
                    Color color = (Color)value;
                    msg.Write(color.R);
                    msg.Write(color.G);
                    msg.Write(color.B);
                    msg.Write(color.A);
                }
                else if (value is Vector2)
                {
                    msg.Write(((Vector2)value).X);
                    msg.Write(((Vector2)value).Y);
                }
                else if (value is Vector3)
                {
                    msg.Write(((Vector3)value).X);
                    msg.Write(((Vector3)value).Y);
                    msg.Write(((Vector3)value).Z);
                }
                else if (value is Vector4)
                {
                    msg.Write(((Vector4)value).X);
                    msg.Write(((Vector4)value).Y);
                    msg.Write(((Vector4)value).Z);
                    msg.Write(((Vector4)value).W);
                }
                else if (value is Rectangle)
                {
                    msg.Write(((Rectangle)value).X);
                    msg.Write(((Rectangle)value).Y);
                    msg.Write(((Rectangle)value).Width);
                    msg.Write(((Rectangle)value).Height);
                }
                else if (value is Enum)
                {
                    msg.Write((int)value);
                }
                else
                {
                    throw new System.NotImplementedException("Serializing item properties of the type \"" + value.GetType() + "\" not supported");
                }
            }
            else
            {
                throw new ArgumentException("Failed to write propery value - property \"" + (property == null ? "null" : property.Name) + "\" is not serializable.");
            }
        }

        private void ReadPropertyChange(NetBuffer msg, bool inGameEditableOnly)
        {
            var allProperties = inGameEditableOnly ? GetProperties<InGameEditable>() : GetProperties<Editable>();
            if (allProperties.Count == 0) return;

            int propertyIndex = 0;
            if (allProperties.Count > 1)
            {
                propertyIndex = msg.ReadRangedInteger(0, allProperties.Count-1);
            }

            SerializableProperty property = allProperties[propertyIndex];

            Type type = property.PropertyType;
            if (type == typeof(string))
            {
                property.TrySetValue(msg.ReadString());
            }
            else if (type == typeof(float))
            {
                property.TrySetValue(msg.ReadFloat());
            }
            else if (type == typeof(int))
            {
                property.TrySetValue(msg.ReadInt32());
            }
            else if (type == typeof(bool))
            {
                property.TrySetValue(msg.ReadBoolean());
            }
            else if (type == typeof(Color))
            {
                property.TrySetValue(new Color(msg.ReadByte(), msg.ReadByte(),msg.ReadByte(),msg.ReadByte()));
            }
            else if (type == typeof(Vector2))
            {
                property.TrySetValue(new Vector2(msg.ReadFloat(), msg.ReadFloat()));
            }
            else if (type == typeof(Vector3))
            {
                property.TrySetValue(new Vector3(msg.ReadFloat(), msg.ReadFloat(), msg.ReadFloat()));
            }
            else if (type == typeof(Vector4))
            {
                property.TrySetValue(new Vector4(msg.ReadFloat(), msg.ReadFloat(), msg.ReadFloat(), msg.ReadFloat()));
            }
            else if (type == typeof(Rectangle))
            {
                property.TrySetValue(new Vector4(msg.ReadInt32(), msg.ReadInt32(), msg.ReadInt32(), msg.ReadInt32()));
            }
            else if (typeof(Enum).IsAssignableFrom(type))
            {
                int intVal = msg.ReadInt32();
                try
                {
                    property.TrySetValue(Enum.ToObject(type, intVal));
                }
                catch (Exception e)
                {
#if DEBUG
                    DebugConsole.ThrowError("Failed to convert the int value \"" + intVal + "\" to " + type, e);
#endif
                    GameAnalyticsManager.AddErrorEventOnce(
                        "Item.ReadPropertyChange:" + Name + ":" + type,
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Warning,
                        "Failed to convert the int value \"" + intVal + "\" to " + type + " (item " + Name + ")");
                }
            }
            else
            {
                return;
            }
            
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ChangeProperty, property });
            }
        }

        partial void UpdateNetPosition();

        public void ServerWritePosition(NetBuffer msg, Client c, object[] extraData = null)
        {
            msg.Write(ID);
            //length in bytes
            if (body.FarseerBody.Awake)
            {
                msg.Write((byte)(4 + 4 + 1 + 3));
            }
            else
            {
                msg.Write((byte)(4 + 4 + 1));
            }

            msg.Write(SimPosition.X);
            msg.Write(SimPosition.Y);

            msg.WriteRangedSingle(MathUtils.WrapAngleTwoPi(body.Rotation), 0.0f, MathHelper.TwoPi, 7);

#if DEBUG
            if (Math.Abs(body.LinearVelocity.X) > MaxVel || Math.Abs(body.LinearVelocity.Y) > MaxVel)
            {

                DebugConsole.ThrowError("Item velocity out of range (" + body.LinearVelocity + ")");

            }
#endif

            msg.Write(body.FarseerBody.Awake);
            if (body.FarseerBody.Awake)
            {
                body.Enabled = true;
                msg.WriteRangedSingle(MathHelper.Clamp(body.LinearVelocity.X, -MaxVel, MaxVel), -MaxVel, MaxVel, 12);
                msg.WriteRangedSingle(MathHelper.Clamp(body.LinearVelocity.Y, -MaxVel, MaxVel), -MaxVel, MaxVel, 12);
            }

            msg.WritePadBits();

            lastSentPos = SimPosition;
        }

        public static Item Load(XElement element, Submarine submarine)
        {
            string name = element.Attribute("name").Value;            
            string identifier = element.GetAttributeString("identifier", "");

            ItemPrefab prefab;
            if (string.IsNullOrEmpty(identifier))
            {
                //legacy support: 
                //1. attempt to find a prefab with an empty identifier and a matching name
                prefab = MapEntityPrefab.Find(name, "") as ItemPrefab;
                //2. not found, attempt to find a prefab with a matching name
                if (prefab == null) prefab = MapEntityPrefab.Find(name) as ItemPrefab;
            }
            else
            {
                prefab = MapEntityPrefab.Find(null, identifier) as ItemPrefab;
            }

            if (prefab == null)
            {
                DebugConsole.ThrowError("Error loading item - item prefab \"" + name + "\" (identifier \"" + identifier + "\") not found.");
                return null;
            }

            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            if (rect.Width == 0 && rect.Height == 0)
            {
                rect.Width = (int)prefab.Size.X;
                rect.Height = (int)prefab.Size.Y;
            }

            Item item = new Item(rect, prefab, submarine)
            {
                Submarine = submarine,
                ID = (ushort)int.Parse(element.Attribute("ID").Value),
                linkedToID = new List<ushort>()
            };

            foreach (XAttribute attribute in element.Attributes())
            {
                if (!item.properties.TryGetValue(attribute.Name.ToString(), out SerializableProperty property)) continue;

                bool shouldBeLoaded = false;

                foreach (var propertyAttribute in property.Attributes.OfType<Serialize>())
                {
                    if (propertyAttribute.isSaveable)
                    {
                        shouldBeLoaded = true;
                        break;
                    }
                }

                if (shouldBeLoaded) property.TrySetValue(attribute.Value);
            }

            string linkedToString = element.GetAttributeString("linked", "");
            if (linkedToString != "")
            {
                string[] linkedToIds = linkedToString.Split(',');
                for (int i = 0; i < linkedToIds.Length; i++)
                {
                    item.linkedToID.Add((ushort)int.Parse(linkedToIds[i]));
                }
            }

            List<ItemComponent> unloadedComponents = new List<ItemComponent>(item.components);
            foreach (XElement subElement in element.Elements())
            {
                ItemComponent component = unloadedComponents.Find(x => x.Name == subElement.Name.ToString());
                if (component == null) continue;

                component.Load(subElement);
                unloadedComponents.Remove(component);
            }

            if (element.GetAttributeBool("flippedx", false)) item.FlipX(false);
            if (element.GetAttributeBool("flippedy", false)) item.FlipY(false);

            item.condition = element.GetAttributeFloat("condition", item.Prefab.Health);
            item.SetActiveSprite();

            return item;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Item");

            element.Add(
                new XAttribute("name", prefab.Name),
                new XAttribute("identifier", prefab.Identifier),
                new XAttribute("ID", ID));

            if (FlippedX) element.Add(new XAttribute("flippedx", true));
            if (FlippedY) element.Add(new XAttribute("flippedy", true));

            if (condition < Prefab.Health)
            {
                element.Add(new XAttribute("condition", condition.ToString("G", CultureInfo.InvariantCulture)));
            }

            System.Diagnostics.Debug.Assert(Submarine != null);

            element.Add(new XAttribute("rect",
                (int)(rect.X - Submarine.HiddenSubPosition.X) + "," +
                (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," +
                rect.Width + "," + rect.Height));
            
            if (linkedTo != null && linkedTo.Count > 0)
            {
                var saveableLinked = linkedTo.Where(l => l.ShouldBeSaved).ToList();
                string[] linkedToIDs = new string[saveableLinked.Count];
                for (int i = 0; i < saveableLinked.Count; i++)
                {
                    linkedToIDs[i] = saveableLinked[i].ID.ToString();
                }
                element.Add(new XAttribute("linked", string.Join(",", linkedToIDs)));
            }

            SerializableProperty.SerializeProperties(this, element);

            foreach (ItemComponent ic in components)
            {
                ic.Save(element);
            }

            parentElement.Add(element);

            return element;
        }

        public override void OnMapLoaded()
        {
            FindHull();

            foreach (ItemComponent ic in components)
            {
                ic.OnMapLoaded();
            }
        }
        
        /// <summary>
        /// Remove the item so that it doesn't appear to exist in the game world (stop sounds, remove bodies etc)
        /// but don't reset anything that's required for cloning the item
        /// </summary>
        public override void ShallowRemove()
        {
            base.ShallowRemove();
            
            foreach (ItemComponent ic in components)
            {
                ic.ShallowRemove();
            }
            ItemList.Remove(this);

            if (body != null)
            {
                body.Remove();
                body = null;
            }
        }

        public override void Remove()
        {
            if (Removed)
            {
                DebugConsole.ThrowError("Attempting to remove an already removed item\n" + Environment.StackTrace);
                return;
            }
            DebugConsole.Log("Removing item " + Name + " (ID: " + ID + ")");

            base.Remove();

            foreach (Character character in Character.CharacterList)
            {
                if (character.SelectedConstruction == this) character.SelectedConstruction = null;
                for (int i = 0; i < character.SelectedItems.Length; i++)
                {
                    if (character.SelectedItems[i] == this) character.SelectedItems[i] = null;
                }
            }

            if (parentInventory != null)
            {
                parentInventory.RemoveItem(this);
                parentInventory = null;
            }

            foreach (ItemComponent ic in components)
            {
                ic.Remove();
            }
            ItemList.Remove(this);

            if (body != null)
            {
                body.Remove();
                body = null;
            }

            foreach (Item it in ItemList)
            {
                if (it.linkedTo.Contains(this))
                {
                    it.linkedTo.Remove(this);
                }
            }
        }
    }
}