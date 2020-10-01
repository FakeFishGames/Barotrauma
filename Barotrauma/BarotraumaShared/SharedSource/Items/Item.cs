using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
#endif

namespace Barotrauma
{

    partial class Item : MapEntity, IDamageable, ISerializableEntity, IServerSerializable, IClientSerializable
    {
        public static List<Item> ItemList = new List<Item>();
        public ItemPrefab Prefab => prefab as ItemPrefab;

        public static bool ShowLinks = true;
                
        private HashSet<string> tags;

        private bool isWire;

        private Hull currentHull;
        public Hull CurrentHull
        {
            get { return currentHull; }
            set
            {
                currentHull = value;
                ParentRuin = currentHull?.ParentRuin;
            }
        }
        
        public bool Visible = true;

#if CLIENT
        public SpriteEffects SpriteEffects = SpriteEffects.None;
#endif

        //components that determine the functionality of the item
        private Dictionary<Type, ItemComponent> componentsByType = new Dictionary<Type, ItemComponent>();
        private List<ItemComponent> components;
        /// <summary>
        /// Components that are Active or need to be updated for some other reason (status effects, sounds)
        /// </summary>
        private readonly List<ItemComponent> updateableComponents = new List<ItemComponent>();
        private List<IDrawableComponent> drawableComponents;
        private bool hasComponentsToDraw;

        public PhysicsBody body;

        public readonly XElement StaticBodyConfig;

        public List<Fixture> StaticFixtures = new List<Fixture>();

        private bool transformDirty = true;

        private float lastSentCondition;
        private float sendConditionUpdateTimer;
        private bool conditionUpdatePending;

        private float condition;

        private bool inWater;
        private readonly bool hasWaterStatusEffects;

        private Inventory parentInventory;
        private readonly Inventory ownInventory;

        private Rectangle defaultRect;

        private Dictionary<string, Connection> connections;

        private List<Repairable> repairables;

        private Queue<float> impactQueue = new Queue<float>();

        //a dictionary containing lists of the status effects in all the components of the item
        private bool[] hasStatusEffectsOfType;
        private Dictionary<ActionType, List<StatusEffect>> statusEffectLists;
        
        public Dictionary<string, SerializableProperty> SerializableProperties { get; protected set; }

        private bool? hasInGameEditableProperties;
        bool HasInGameEditableProperties
        {
            get
            {
                if (hasInGameEditableProperties == null)
                {
                    hasInGameEditableProperties = false;
                    if (SerializableProperties.Values.Any(p => p.Attributes.OfType<InGameEditable>().Any()))
                    {
                        hasInGameEditableProperties = true;
                    }
                    else
                    {
                        foreach (ItemComponent component in components)
                        {
                            if (!component.AllowInGameEditing) { continue; }
                            if (component.SerializableProperties.Values.Any(p => p.Attributes.OfType<InGameEditable>().Any()))
                            {
                                hasInGameEditableProperties = true;
                                break;
                            }
                        }
                    }
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

        private Item container;
        public Item Container
        {
            get { return container; }
            private set
            {
                if (value != container)
                {
                    container = value;
                    SetActiveSprite();
                }
            }
        }
                
        public override string Name
        {
            get { return prefab.Name; }
        }

        private string description;
        public string Description
        {
            get { return description ?? prefab.Description; }
            set { description = value; }
        }

        [Editable, Serialize(false, true, alwaysUseInstanceValues: true)]
        public bool NonInteractable
        {
            get;
            set;
        }

        private float rotationRad;

        [Editable(0.0f, 360.0f, DecimalCount = 1, ValueStep = 1f), Serialize(0.0f, true)]
        public float Rotation
        {
            get
            {
                return MathHelper.ToDegrees(rotationRad);
            }
            set
            {
                if (!Prefab.AllowRotatingInEditor) { return; }
                rotationRad = MathHelper.ToRadians(value);
            }
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
                return (body == null) ? base.Position : body.Position;
            }
        }

        public override Vector2 SimPosition
        {
            get
            {
                return (body == null) ? ConvertUnits.ToSimUnits(base.Position) : body.SimPosition;
            }
        }

        public Rectangle InteractionRect
        {
            get
            {
                return WorldRect;
            }
        }

        private float scale = 1.0f;
        public override float Scale
        {
            get { return scale; }
            set
            {
                if (scale == value) { return; }
                scale = MathHelper.Clamp(value, 0.01f, 10.0f);

                float relativeScale = scale / prefab.Scale;

                if (!ResizeHorizontal || !ResizeVertical)
                {
                    int newWidth = ResizeHorizontal ? rect.Width : (int)(defaultRect.Width * relativeScale);
                    int newHeight = ResizeVertical ? rect.Height : (int)(defaultRect.Height * relativeScale);
                    Rect = new Rectangle(rect.X, rect.Y, newWidth, newHeight);
                }

                if (components != null)
                {
                    foreach (ItemComponent component in components)
                    {
                        component.OnScaleChanged();
                    }
                }
            }
        }

        public float PositionUpdateInterval
        {
            get;
            set;
        } = float.PositiveInfinity;

        protected Color spriteColor;
        [Editable, Serialize("1.0,1.0,1.0,1.0", true)]
        public Color SpriteColor
        {
            get { return spriteColor; }
            set { spriteColor = value; }
        }

        [Serialize("1.0,1.0,1.0,1.0", true), Editable]
        public Color InventoryIconColor
        {
            get;
            protected set;
        }
        
        [Editable, Serialize("1.0,1.0,1.0,1.0", true, description: "Changes the color of the item this item is contained inside. Only has an effect if either of the UseContainedSpriteColor or UseContainedInventoryIconColor property of the container is set to true.")]
        public Color ContainerColor
        {
            get;
            protected set;
        }

        [Serialize("", false)]
        /// <summary>
        /// Can be used by status effects or conditionals to check what item this item is contained inside
        /// </summary>
        public string ContainerIdentifier
        {
            get
            {
                return 
                    Container?.prefab.Identifier ?? 
                    ParentInventory?.Owner?.ToString() ?? 
                    "";
            }
            set { /*do nothing*/ }
        }


        [Serialize("", true)]

        /// <summary>
        /// Can be used to modify the AITarget's label using status effects
        /// </summary>
        public string SonarLabel
        {
            get { return AiTarget?.SonarLabel ?? ""; }
            set
            {
                if (AiTarget != null)
                {
                    AiTarget.SonarLabel = value;
                }
            }
        }

        [Serialize(false, false)]
        /// <summary>
        /// Can be used by status effects or conditionals to check if the physics body of the item is active
        /// </summary>
        public bool PhysicsBodyActive
        {
            get
            {
                return body != null && body.Enabled;
            }
            set { /*do nothing*/ }
        }

        [Serialize(0.0f, false)]
        /// <summary>
        /// Can be used by status effects or conditionals to modify the sound range
        /// </summary>
        public new float SoundRange
        {
            get { return aiTarget == null ? 0.0f : aiTarget.SoundRange; }
            set { if (aiTarget != null) { aiTarget.SoundRange = Math.Max(0.0f, value); } }
        }

        [Serialize(0.0f, false)]
        /// <summary>
        /// Can be used by status effects or conditionals to modify the sound range
        /// </summary>
        public new float SightRange
        {
            get { return aiTarget == null ? 0.0f : aiTarget.SightRange; }
            set { if (aiTarget != null) { aiTarget.SightRange = Math.Max(0.0f, value); } }
        }

        /// <summary>
        /// Should the item's Use method be called with the "Use" or with the "Shoot" key?
        /// </summary>
        [Serialize(false, false)]
        public bool IsShootable { get; set; }

        /// <summary>
        /// If true, the user has to hold the "aim" key before use is registered. False by default.
        /// </summary>
        [Serialize(false, false)]
        public bool RequireAimToUse
        {
            get; set;
        }

        /// <summary>
        /// If true, the user has to hold the "aim" key before secondary use is registered. True by default.
        /// </summary>
        [Serialize(true, false)]
        public bool RequireAimToSecondaryUse
        {
            get; set;
        }

        public Color Color
        {
            get { return spriteColor; }
        }

        public bool IsFullCondition => MathUtils.NearlyEqual(Condition, MaxCondition);
        public float MaxCondition => Prefab.Health * healthMultiplier;
        public float ConditionPercentage => MathUtils.Percentage(Condition, MaxCondition);

        private float offsetOnSelectedMultiplier = 1.0f;
        
        [Serialize(1.0f, false)]
        public float OffsetOnSelectedMultiplier
        {
            get => offsetOnSelectedMultiplier;
            set => offsetOnSelectedMultiplier = value;
        }
        
        private float healthMultiplier = 1.0f;

        [Serialize(1.0f, true, "Multiply the maximum condition by this value")]
        public float HealthMultiplier
        {
            get => healthMultiplier;
            set
            {
                healthMultiplier = value;
            }
        }
        
        //the default value should be Prefab.Health, but because we can't use it in the attribute, 
        //we'll just use NaN (which does nothing) and set the default value in the constructor/load
        [Serialize(float.NaN, false), Editable]
        public float Condition
        {
            get { return condition; }
            set 
            {
#if CLIENT
                if (GameMain.Client != null) return;
#endif
                if (!MathUtils.IsValid(value)) return;
                if (Indestructible) return;

                float prev = condition;
                bool wasInFullCondition = IsFullCondition;

                condition = MathHelper.Clamp(value, 0.0f, MaxCondition);
                if (condition == 0.0f && prev > 0.0f)
                {
#if CLIENT
                    foreach (ItemComponent ic in components)
                    {
                        ic.PlaySound(ActionType.OnBroken);
                    }
                    if (Screen.Selected == GameMain.SubEditorScreen) return;
#endif
                    ApplyStatusEffects(ActionType.OnBroken, 1.0f, null);
                }
                
                SetActiveSprite();

                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    if (Math.Abs(lastSentCondition - condition) > 1.0f)
                    {
                        conditionUpdatePending = true;
                        isActive = true;
                    }
                    else if (wasInFullCondition != IsFullCondition)
                    {
                        conditionUpdatePending = true;
                        isActive = true;
                    }
                    else if (!MathUtils.NearlyEqual(lastSentCondition, condition) && (condition <= 0.0f || condition >= MaxCondition))
                    {
                        sendConditionUpdateTimer = 0.0f;
                        conditionUpdatePending = true;
                        isActive = true;
                    }
                }
            }
        }

        public float Health
        {
            get { return condition; }
        }

        private bool? indestructible;
        /// <summary>
        /// Per-instance value - if not set, the value of the prefab is used.
        /// </summary>
        public bool Indestructible
        {
            get { return indestructible ?? Prefab.Indestructible; }
            set { indestructible = value; }
        }

        public bool StolenDuringRound;

        private bool spawnedInOutpost;
        public bool SpawnedInOutpost
        {
            get { return spawnedInOutpost; }
            set
            {
                if (!spawnedInOutpost && value)
                {
                    OriginalOutpost = GameMain.GameSession?.StartLocation?.BaseName ?? "";
                }
                spawnedInOutpost = value;
            }
        }

        private string originalOutpost;
        [Serialize("", true, alwaysUseInstanceValues: true)]
        public string OriginalOutpost
        {
            get { return originalOutpost; }
            set
            {
                originalOutpost = value;
                if (!string.IsNullOrEmpty(value) && GameMain.GameSession?.StartLocation?.BaseName == value)
                {
                    spawnedInOutpost = true;
                }
            }
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
                    string[] splitTags = value.Split(',');
                    foreach (string tag in splitTags)
                    {
                        string[] splitTag = tag.Trim().Split(':');
                        splitTag[0] = splitTag[0].ToLowerInvariant();
                        tags.Add(string.Join(":", splitTag));
                    }
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
                if (body != null && body.Enabled) { return inWater; }
                if (hasWaterStatusEffects) { return inWater; }

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
        } = new List<Item>(20);

        public string ConfigFile
        {
            get { return Prefab.FilePath; }
        }

        //which type of inventory slots (head, torso, any, etc) the item can be placed in
        public List<InvSlotType> AllowedSlots
        {
            get
            {
                Pickable p = GetComponent<Pickable>();
                return (p == null) ? new List<InvSlotType>() { InvSlotType.Any } : p.AllowedSlots;
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

        public IEnumerable<Item> ContainedItems
        {
            get
            {
                // It's not a good practice to return null if the method tells that it returns a collection, because:
                // a) the user has to handle this -> more code and more null reference exceptions
                // b) it makes it more difficult to make use of chained function calls (which are quite powerful), although '?' makes it possible
                // c) it's against the functional paradigm that e.g. Linq follows (for good reasons)
                // In general, it's better to return an empty collection instead,
                // but changing it here might cause unwanted implications.
                // Also it can be a minor optimization to return null instead of creating an empty collection, 
                // but if that's the case I'd prefer caching an empty collection and using that instead. Just something to consider in the future.
                return ownInventory?.Items.Where(i => i != null);
            }
        }

        public Inventory OwnInventory
        {
            get { return ownInventory; }
        }

        [Editable, Serialize(false, true, description:
            "Enable if you want to display the item HUD side by side with another item's HUD, when linked together. " +
            "Disclaimer: It's possible or even likely that the views block each other, if they were not designed to be viewed together!")]
        public bool DisplaySideBySideWhenLinked { get; set; }

        public IEnumerable<Repairable> Repairables
        {
            get { return repairables; }
        }

        public IEnumerable<ItemComponent> Components
        {
            get { return components; }
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

        private readonly List<ISerializableEntity> allPropertyObjects = new List<ISerializableEntity>();
        public IEnumerable<ISerializableEntity> AllPropertyObjects
        {
            get { return allPropertyObjects; }
        }

        public Item(ItemPrefab itemPrefab, Vector2 position, Submarine submarine)
            : this(new Rectangle(
                (int)(position.X - itemPrefab.sprite.size.X / 2 * itemPrefab.Scale), 
                (int)(position.Y + itemPrefab.sprite.size.Y / 2 * itemPrefab.Scale), 
                (int)(itemPrefab.sprite.size.X * itemPrefab.Scale), 
                (int)(itemPrefab.sprite.size.Y * itemPrefab.Scale)), 
            itemPrefab, submarine)
        {

        }

        /// <summary>
        /// Creates a new item
        /// </summary>
        /// <param name="callOnItemLoaded">Should the OnItemLoaded methods of the ItemComponents be called. Use false if the item needs additional initialization before it can be considered fully loaded (e.g. when loading an item from a sub file or cloning an item).</param>
        public Item(Rectangle newRect, ItemPrefab itemPrefab, Submarine submarine, bool callOnItemLoaded = true)
            : base(itemPrefab, submarine)
        {
            spriteColor = prefab.SpriteColor;

            components          = new List<ItemComponent>();
            drawableComponents  = new List<IDrawableComponent>(); hasComponentsToDraw = false;
            tags                = new HashSet<string>();
            repairables         = new List<Repairable>();

            defaultRect = newRect;
            rect = newRect;

            condition = MaxCondition;
            lastSentCondition = condition;

            allPropertyObjects.Add(this);

            XElement element = itemPrefab.ConfigElement;
            if (element == null) return;

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            if (submarine == null || !submarine.Loading) { FindHull(); }

            SetActiveSprite();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "body":
                        body = new PhysicsBody(subElement, ConvertUnits.ToSimUnits(Position), Scale);
                        string collisionCategory = subElement.GetAttributeString("collisioncategory", null);
                        if ((Prefab.DamagedByProjectiles || Prefab.DamagedByMeleeWeapons) && Condition > 0)
                        {
                            //force collision category to Character to allow projectiles and weapons to hit
                            //(we could also do this by making the projectiles and weapons hit CollisionItem
                            //and check if the collision should be ignored in the OnCollision callback, but
                            //that'd make the hit detection more expensive because every item would be included)
                            body.CollisionCategories = Physics.CollisionCharacter;
                            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionPlatform | Physics.CollisionProjectile;
                        }
                        if (collisionCategory != null)
                        {                            
                            if (!Physics.TryParseCollisionCategory(collisionCategory, out Category cat))
                            {
                                DebugConsole.ThrowError("Invalid collision category in item \"" + Name+"\" (" + collisionCategory + ")");
                            }
                            else
                            {
                                body.CollisionCategories = cat;
                                if (cat.HasFlag(Physics.CollisionCharacter))
                                {
                                    body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionPlatform | Physics.CollisionProjectile;
                                }
                            }
                        }

                        body.FarseerBody.AngularDamping = element.GetAttributeFloat("angulardamping", 0.2f);
                        body.FarseerBody.LinearDamping = element.GetAttributeFloat("lineardamping", 0.1f);
                        body.UserData = this;
                        break;
                    case "trigger":
                    case "inventoryicon":
                    case "sprite":
                    case "deconstruct":
                    case "brokensprite":
                    case "decorativesprite":
                    case "price":
                    case "levelcommonness":
                    case "suitabletreatment":
                    case "containedsprite":
                    case "fabricate":
                    case "fabricable":
                    case "fabricableitem":
                    case "upgrade":
                    case "preferredcontainer":
                    case "upgrademodule":
                    case "upgradeoverride":
                    case "minimapicon":
                        break;
                    case "staticbody":
                        StaticBodyConfig = subElement;
                        break;
                    case "aitarget":
                        aiTarget = new AITarget(this, subElement);
                        break;
                    default:
                        ItemComponent ic = ItemComponent.Load(subElement, this, itemPrefab.FilePath);
                        if (ic == null) break;

                        AddComponent(ic);

                        if (ic is IDrawableComponent && ic.Drawable)
                        {
                            drawableComponents.Add(ic as IDrawableComponent);
                            hasComponentsToDraw = true;
                        }
                        if (ic is Repairable) repairables.Add((Repairable)ic);
                        break;
                }
            }

            hasStatusEffectsOfType = new bool[Enum.GetValues(typeof(ActionType)).Length];
            foreach (ItemComponent ic in components)
            {
                if (ic.statusEffectLists == null) continue;

                if (statusEffectLists == null)
                {
                    statusEffectLists = new Dictionary<ActionType, List<StatusEffect>>();
                }

                //go through all the status effects of the component 
                //and add them to the corresponding statuseffect list
                foreach (List<StatusEffect> componentEffectList in ic.statusEffectLists.Values)
                {
                    ActionType actionType = componentEffectList.First().type;
                    if (!statusEffectLists.TryGetValue(actionType, out List<StatusEffect> statusEffectList))
                    {
                        statusEffectList = new List<StatusEffect>();
                        statusEffectLists.Add(actionType, statusEffectList);
                        hasStatusEffectsOfType[(int)actionType] = true;
                    }

                    foreach (StatusEffect effect in componentEffectList)
                    {
                        statusEffectList.Add(effect);
                    }
                }
            }

            hasWaterStatusEffects = hasStatusEffectsOfType[(int)ActionType.InWater] || hasStatusEffectsOfType[(int)ActionType.NotInWater];

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

            if (body != null)
            {
                body.FarseerBody.OnCollision += OnCollision;
            }

            var itemContainer = GetComponent<ItemContainer>();
            if (itemContainer != null)
            {
                ownInventory = itemContainer.Inventory;
            }

            InitProjSpecific();

            if (callOnItemLoaded)
            {
                foreach (ItemComponent ic in components)
                {
                    ic.OnItemLoaded();
                }
            }

            InsertToList();
            ItemList.Add(this);

            DebugConsole.Log("Created " + Name + " (" + ID + ")");

            if (Components.All(ic => ic is Wire || ic is Holdable)) { isWire = true; }
        }

        partial void InitProjSpecific();

        public override MapEntity Clone()
        {
            Item clone = new Item(rect, Prefab, Submarine, callOnItemLoaded: false)
            {
                defaultRect = defaultRect
            };
            foreach (KeyValuePair<string, SerializableProperty> property in SerializableProperties)
            {
                if (!property.Value.Attributes.OfType<Editable>().Any()) continue;
                clone.SerializableProperties[property.Key].TrySetValue(clone, property.Value.GetValue(this));
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
                foreach (KeyValuePair<string, SerializableProperty> property in components[i].SerializableProperties)
                {
                    if (!property.Value.Attributes.OfType<Editable>().Any()) continue;
                    clone.components[i].SerializableProperties[property.Key].TrySetValue(clone.components[i], property.Value.GetValue(components[i]));
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
            
            foreach (ItemComponent component in clone.components)
            {
                component.OnItemLoaded();
            }

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

        public void AddComponent(ItemComponent component)
        {
            allPropertyObjects.Add(component);
            components.Add(component);

            if (component.IsActive || component.Parent != null || (component.IsActiveConditionals != null && component.IsActiveConditionals.Any()))
            {
                updateableComponents.Add(component);
            }

            component.OnActiveStateChanged += (bool isActive) => 
            {
                bool hasSounds = false;
#if CLIENT
                hasSounds = component.HasSounds;
#endif
                //component doesn't need to be updated if it isn't active, doesn't have a parent that could activate it, 
                //nor status effects, sounds or conditionals that would need to run
                if (!isActive && 
                    !hasSounds &&
                    component.Parent == null &&
                    (component.IsActiveConditionals == null || !component.IsActiveConditionals.Any()) &&
                    (component.statusEffectLists == null || !component.statusEffectLists.Any()))
                {
                    if (updateableComponents.Contains(component)) { updateableComponents.Remove(component); }
                }
                else
                {
                    if (!updateableComponents.Contains(component)) 
                    { 
                        updateableComponents.Add(component);
                        this.isActive = true;
                    }
                }
            };

            Type type = component.GetType();
            if (!componentsByType.ContainsKey(type))
            {
                componentsByType.Add(type, component);
                Type baseType = type.BaseType;
                while (baseType != null && baseType != typeof(ItemComponent))
                {
                    if (!componentsByType.ContainsKey(baseType))
                    {
                        componentsByType.Add(baseType, component);
                    }
                    baseType = baseType.BaseType;
                }
            }
        }

        public void EnableDrawableComponent(IDrawableComponent drawable)
        {
            if (!drawableComponents.Contains(drawable))
            {
                drawableComponents.Add(drawable);
                hasComponentsToDraw = true;
#if CLIENT
                cachedVisibleSize = null;
#endif
            }
        }

        public void DisableDrawableComponent(IDrawableComponent drawable)
        {
            if (drawableComponents.Contains(drawable))
            {
                drawableComponents.Remove(drawable);
                hasComponentsToDraw = drawableComponents.Count > 0;
#if CLIENT
                cachedVisibleSize = null;
#endif
            }
        }

        public int GetComponentIndex(ItemComponent component)
        {
            return components.IndexOf(component);
        }

        public T GetComponent<T>() where T : ItemComponent
        {
            if (componentsByType.TryGetValue(typeof(T), out ItemComponent component))
            {
                return (T)component;
            }
            
            return default;
        }

        public IEnumerable<T> GetComponents<T>()
        {
            if (!componentsByType.ContainsKey(typeof(T))) { return Enumerable.Empty<T>(); }

            return components.Where(c => c is T).Cast<T>();
        }
        
        public void RemoveContained(Item contained)
        {
            if (ownInventory != null)
            {
                ownInventory.RemoveItem(contained);
            }

            contained.Container = null;            
        }

        public void SetTransform(Vector2 simPosition, float rotation, bool findNewHull = true, bool setPrevTransform = true)
        {
            if (!MathUtils.IsValid(simPosition))
            {
                string errorMsg =
                    "Attempted to move the item " + Name +
                    " to an invalid position (" + simPosition + ")\n" + Environment.StackTrace.CleanupStackTrace();

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
                    if (body.PhysEnabled)
                    {
                        body.SetTransform(simPosition, rotation, setPrevTransform);
                    }
                    else
                    {
                        body.SetTransformIgnoreContacts(simPosition, rotation, setPrevTransform);
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

            if (findNewHull) { FindHull(); }
        }

        public void SetActiveSprite()
        {
            SetActiveSpriteProjSpecific();
        }

        partial void SetActiveSpriteProjSpecific();

        public override void Move(Vector2 amount)
        {
            Move(amount, ignoreContacts: false);
        }

        public void Move(Vector2 amount, bool ignoreContacts)
        {
            if (!MathUtils.IsValid(amount))
            {
                DebugConsole.ThrowError($"Attempted to move an item by an invalid amount ({amount})\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            base.Move(amount);

            if (ItemList != null && body != null)
            {
                if (ignoreContacts)
                {
                    body.SetTransformIgnoreContacts(body.SimPosition + ConvertUnits.ToSimUnits(amount), body.Rotation);
                }
                else
                {
                    body.SetTransform(body.SimPosition + ConvertUnits.ToSimUnits(amount), body.Rotation);
                }
            }
            foreach (ItemComponent ic in components)
            {
                ic.Move(amount);
            }

            if (body != null && (Submarine == null || !Submarine.Loading)) { FindHull(); }
        }

        public Rectangle TransformTrigger(Rectangle trigger, bool world = false)
        {
            Rectangle baseRect = world ? WorldRect : Rect;

            Rectangle transformedRect =
                new Rectangle(
                    (int)(baseRect.X + trigger.X * Scale),
                    (int)(baseRect.Y + trigger.Y * Scale),
                    (trigger.Width == 0) ? Rect.Width : (int)(trigger.Width * Scale),
                    (trigger.Height == 0) ? Rect.Height : (int)(trigger.Height * Scale));

            if (FlippedX)
            {
                transformedRect.X = baseRect.X + (baseRect.Right - transformedRect.Right);
            }
            if (FlippedY)
            {
                transformedRect.Y = baseRect.Y + ((baseRect.Y - baseRect.Height) - (transformedRect.Y - transformedRect.Height));
            }

            return transformedRect;
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
                Submarine = CurrentHull?.Submarine;
                body.Submarine = Submarine;
            }

            return CurrentHull;
        }

        public Item GetRootContainer()
        {
            if (Container == null) { return null; }

            Item rootContainer = Container;
            while (rootContainer.Container != null)
            {
                rootContainer = rootContainer.Container;
            }

            return rootContainer;
        }

        public bool IsOwnedBy(Entity entity) => FindParentInventory(i => i.Owner == entity) != null;

        public Entity GetRootInventoryOwner()
        {
            if (ParentInventory == null) { return this; }
            if (ParentInventory.Owner is Character) { return ParentInventory.Owner; }
            var rootContainer = GetRootContainer();
            if (rootContainer?.ParentInventory?.Owner is Character) { return rootContainer.ParentInventory.Owner; }
            return rootContainer ?? this;
        }

        public Inventory FindParentInventory(Func<Inventory, bool> predicate)
        {
            if (parentInventory != null)
            {
                if (predicate(parentInventory))
                {
                    return parentInventory;
                }
                if (parentInventory.Owner is Item owner)
                {
                    return owner.FindParentInventory(predicate);
                }
            }
            return null;
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
            if (tags.Contains(tag)) { return; }
            tags.Add(tag);
        }

        public bool HasTag(string tag)
        {
            if (tag == null) { return true; }
            return tags.Contains(tag) || prefab.Tags.Contains(tag);
        }

        public void ReplaceTag(string tag, string newTag)
        {
            if (!tags.Contains(tag)) { return; }
            tags.Remove(tag);
            tags.Add(newTag);
        }

        public IEnumerable<string> GetTags()
        {
            return tags;
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

        private bool ConditionalMatches(PropertyConditional conditional)
        {
            if (string.IsNullOrEmpty(conditional.TargetItemComponentName))
            {
                if (!conditional.Matches(this)) { return false; }
            }
            else
            {
                foreach (ItemComponent component in components)
                {
                    if (component.Name != conditional.TargetItemComponentName) { continue; }
                    if (!conditional.Matches(component)) { return false; }
                }
            }
            return true;
        }

        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null, Limb limb = null, Entity useTarget = null, bool isNetworkEvent = false, Vector2? worldPosition = null)
        {
            if (!hasStatusEffectsOfType[(int)type]) { return; }

            foreach (StatusEffect effect in statusEffectLists[type])
            {
                ApplyStatusEffect(effect, type, deltaTime, character, limb, useTarget, isNetworkEvent, false, worldPosition);
            }
        }
        
        readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();

        public void ApplyStatusEffect(StatusEffect effect, ActionType type, float deltaTime, Character character = null, Limb limb = null, Entity useTarget = null, bool isNetworkEvent = false, bool checkCondition = true, Vector2? worldPosition = null)
        {
            if (!isNetworkEvent && checkCondition)
            {
                if (condition == 0.0f && effect.type != ActionType.OnBroken) return;
            }
            if (effect.type != type) return;
            
            bool hasTargets = (effect.TargetIdentifiers == null);

            targets.Clear();
            
            if (effect.HasTargetType(StatusEffect.TargetType.Contained))
            {
                var containedItems = ownInventory?.Items;
                if (containedItems != null)
                {
                    foreach (Item containedItem in containedItems)
                    {
                        if (containedItem == null) { continue; }
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

            if (effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters) || effect.HasTargetType(StatusEffect.TargetType.NearbyItems))
            {
                effect.GetNearbyTargets(WorldPosition, targets);
                if (targets.Count > 0) { hasTargets = true; }
            }

            if (effect.HasTargetType(StatusEffect.TargetType.UseTarget) && useTarget is ISerializableEntity serializableTarget)
            {
                hasTargets = true;
                targets.Add(serializableTarget);
            }

            if (!hasTargets) { return; }

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

            if (character != null)
            {
                if (effect.HasTargetType(StatusEffect.TargetType.Character))
                {
                    if (type == ActionType.OnContained && ParentInventory is CharacterInventory characterInventory)
                    {
                        targets.Add(characterInventory.Owner as ISerializableEntity);
                    }
                    else
                    {
                        targets.Add(character);
                    }
                }
                if (effect.HasTargetType(StatusEffect.TargetType.AllLimbs))
                {
                    targets.AddRange(character.AnimController.Limbs.ToList());
                }
            }
            if (effect.HasTargetType(StatusEffect.TargetType.Limb))
            {
                targets.Add(limb);
            }
            
            if (Container != null && effect.HasTargetType(StatusEffect.TargetType.Parent)) targets.Add(Container);
            
            effect.Apply(type, deltaTime, this, targets, worldPosition);            
        }


        public AttackResult AddDamage(Character attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = true)
        {
            if (Indestructible) { return new AttackResult(); }

            float damageAmount = attack.GetItemDamage(deltaTime);
            Condition -= damageAmount;

            if (damageAmount > 0)
            {
                ApplyStatusEffects(ActionType.OnDamaged, 1.0f);
            }

            return new AttackResult(damageAmount, null);
        }

        private bool IsInWater()
        {
            if (CurrentHull == null) { return true; }
                        
            float surfaceY = CurrentHull.Surface;
            return CurrentHull.WaterVolume > 0.0f && Position.Y < surfaceY;
        }

        public void SendPendingNetworkUpdates()
        {
            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsServer) { return; }
            if (conditionUpdatePending)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Status });
                lastSentCondition = condition;
                sendConditionUpdateTimer = NetConfig.ItemConditionUpdateInterval;
                conditionUpdatePending = false;
            }
        }

        private bool isActive = true;

        public override void Update(float deltaTime, Camera cam)
        {
            while (impactQueue.Count > 0)
            {
                float impact = impactQueue.Dequeue();
                HandleCollision(impact);
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer && (!Submarine?.Loading ?? true))
            {
                sendConditionUpdateTimer -= deltaTime;
                if (conditionUpdatePending && sendConditionUpdateTimer <= 0.0f)
                {
                    SendPendingNetworkUpdates();
                }
            }

            aiTarget?.Update(deltaTime);

            if (!isActive) { return; }

            ApplyStatusEffects(ActionType.Always, deltaTime, character: (parentInventory as CharacterInventory)?.Owner as Character);

            for (int i = 0; i < updateableComponents.Count; i++)
            {
                ItemComponent ic = updateableComponents[i];

                if (ic.IsActiveConditionals != null)
                {
                    bool shouldBeActive = true;
                    foreach (var conditional in ic.IsActiveConditionals)
                    {
                        if (!ConditionalMatches(conditional)) 
                        {
                            shouldBeActive = false;
                            break;
                        }
                    }
                    ic.IsActive = shouldBeActive;
                }
#if CLIENT
                if (ic.HasSounds)
                {
                    ic.PlaySound(ActionType.Always);
                    ic.UpdateSounds();
                    if (!ic.WasUsed)
                    {
                        ic.StopSounds(ActionType.OnUse);
                        ic.StopSounds(ActionType.OnSecondaryUse);
                    }
                }
#endif
                ic.WasUsed = false;

                ic.ApplyStatusEffects(parentInventory == null ? ActionType.OnNotContained : ActionType.OnContained, deltaTime, character: (parentInventory as CharacterInventory)?.Owner as Character);

                if (ic.IsActive)
                {
                    if (condition <= 0.0f)
                    {
                        ic.UpdateBroken(deltaTime, cam);
                    }
                    else
                    {
                        ic.Update(deltaTime, cam);
#if CLIENT
                        if (ic.IsActive)
                        {
                            if (ic.IsActiveTimer > 0.02f)
                            {
                                ic.PlaySound(ActionType.OnActive);
                            }
                            ic.IsActiveTimer += deltaTime;
                        }
#endif
                    }
                }
            }

            if (Removed) { return; }

            bool needsWaterCheck = hasWaterStatusEffects;
            if (body != null && body.Enabled)
            {
                System.Diagnostics.Debug.Assert(body.FarseerBody.FixtureList != null);

                if (Math.Abs(body.LinearVelocity.X) > 0.01f || Math.Abs(body.LinearVelocity.Y) > 0.01f || transformDirty)
                {
                    UpdateTransform();
                    if (CurrentHull == null && body.SimPosition.Y < ConvertUnits.ToSimUnits(Level.MaxEntityDepth))
                    {
                        Spawner?.AddToRemoveQueue(this);
                        return;
                    }
                }
                needsWaterCheck = true;
                UpdateNetPosition(deltaTime);
                if (inWater)
                {
                    ApplyWaterForces();
                    CurrentHull?.ApplyFlowForces(deltaTime, this);
                }
            }

            if (needsWaterCheck)
            {
                inWater = IsInWater();
                bool waterProof = WaterProof;
                if (inWater)
                {
                    Item container = this.Container;
                    while (!waterProof && container != null)
                    {
                        waterProof = container.WaterProof;
                        container = container.Container;
                    }
                }
                if (hasWaterStatusEffects && condition > 0.0f)
                {
                    ApplyStatusEffects(!waterProof && inWater ? ActionType.InWater : ActionType.NotInWater, deltaTime);
                }
            }
            else
            {
                if (updateableComponents.Count == 0 && aiTarget == null && !hasStatusEffectsOfType[(int)ActionType.Always] && (body == null || !body.Enabled))
                {
#if CLIENT
                    positionBuffer.Clear();
#endif
                    isActive = false;
                }
            }
        }

                
        public void UpdateTransform()
        {
            if (body == null) { return; }

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
            else if (Submarine != null && prevSub != null && Submarine != prevSub)
            {
                body.SetTransform(body.SimPosition + prevSub.SimPosition - Submarine.SimPosition, body.Rotation);
            }

            var containedItems = ownInventory?.Items;
            if (Submarine != prevSub && containedItems != null)
            {
                foreach (Item containedItem in ContainedItems)
                {
                    if (containedItem == null) { continue; }
                    containedItem.Submarine = Submarine;
                }
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            if (Math.Abs(body.LinearVelocity.X) > NetConfig.MaxPhysicsBodyVelocity || 
                Math.Abs(body.LinearVelocity.Y) > NetConfig.MaxPhysicsBodyVelocity)
            {
                body.LinearVelocity = new Vector2(
                    MathHelper.Clamp(body.LinearVelocity.X, -NetConfig.MaxPhysicsBodyVelocity, NetConfig.MaxPhysicsBodyVelocity),
                    MathHelper.Clamp(body.LinearVelocity.Y, -NetConfig.MaxPhysicsBodyVelocity, NetConfig.MaxPhysicsBodyVelocity));
            }

            transformDirty = false;
        }

        /// <summary>
        /// Applies buoyancy, drag and angular drag caused by water
        /// </summary>
        private void ApplyWaterForces()
        {
            if (body.Mass <= 0.0f)
            {
                return;
            }

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

            body.ApplyForce((uplift - drag) * 10.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);

            //apply simple angular drag
            body.ApplyTorque(body.AngularVelocity * volume * -0.05f);
        }        


        private bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (transformDirty) { return false; }

            contact.GetWorldManifold(out Vector2 normal, out _);
            if (contact.FixtureA.Body == f1.Body) { normal = -normal; }
            float impact = Vector2.Dot(f1.Body.LinearVelocity, -normal);

            lock (impactQueue)
            {
                impactQueue.Enqueue(impact);
            }

            return true;
        }

        private void HandleCollision(float impact)
        {
            OnCollisionProjSpecific(impact);
            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (ImpactTolerance > 0.0f && condition > 0.0f && Math.Abs(impact) > ImpactTolerance)
                {
                    ApplyStatusEffects(ActionType.OnImpact, 1.0f);
#if SERVER
                    GameMain.Server?.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnImpact });
#endif
                }

                var containedItems = ownInventory?.Items;
                if (containedItems != null)
                {
                    foreach (Item contained in containedItems)
                    {
                        if (contained == null) { continue; }
                        if (contained.body != null) { contained.HandleCollision(impact); }
                    }
                }
            }
        }

        partial void OnCollisionProjSpecific(float impact);

        public override void FlipX(bool relativeToSub)
        {
            if (!Prefab.CanFlipX) { return; }

            base.FlipX(relativeToSub);

#if CLIENT
            if (Prefab.CanSpriteFlipX)
            {
                SpriteEffects ^= SpriteEffects.FlipHorizontally;
            }
#endif

            foreach (ItemComponent component in components)
            {
                component.FlipX(relativeToSub);
            }            
        }

        public override void FlipY(bool relativeToSub)
        {
            if (!Prefab.CanFlipY) { return; }

            base.FlipY(relativeToSub);

#if CLIENT
            if (Prefab.CanSpriteFlipY)
            {
                SpriteEffects ^= SpriteEffects.FlipVertically;
            }
#endif

            foreach (ItemComponent component in components)
            {
                component.FlipY(relativeToSub);
            }
        }

        /// <summary>
        /// Note: This function generates garbage and might be a bit too heavy to be used once per frame.
        /// </summary>
        public List<T> GetConnectedComponents<T>(bool recursive = false) where T : ItemComponent
        {
            List<T> connectedComponents = new List<T>();

            if (recursive)
            {
                HashSet<Connection> alreadySearched = new HashSet<Connection>();
                GetConnectedComponentsRecursive(alreadySearched, connectedComponents);
                return connectedComponents;
            }

            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) { return connectedComponents; }

            foreach (Connection c in connectionPanel.Connections)
            {
                var recipients = c.Recipients;
                foreach (Connection recipient in recipients)
                {
                    var component = recipient.Item.GetComponent<T>();
                    if (component != null && !connectedComponents.Contains(component))
                    {
                        connectedComponents.Add(component);
                    } 
                }
            }

            return connectedComponents;
        }

        private void GetConnectedComponentsRecursive<T>(HashSet<Connection> alreadySearched, List<T> connectedComponents) where T : ItemComponent
        {
            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) { return; }

            foreach (Connection c in connectionPanel.Connections)
            {
                if (alreadySearched.Contains(c)) { continue; }
                alreadySearched.Add(c);
                GetConnectedComponentsRecursive(c, alreadySearched, connectedComponents);
            }
        }

        /// <summary>
        /// Note: This function generates garbage and might be a bit too heavy to be used once per frame.
        /// </summary>
        public List<T> GetConnectedComponentsRecursive<T>(Connection c) where T : ItemComponent
        {
            List<T> connectedComponents = new List<T>();
            HashSet<Connection> alreadySearched = new HashSet<Connection>();
            GetConnectedComponentsRecursive(c, alreadySearched, connectedComponents);

            return connectedComponents;
        }
        
        public static readonly Pair<string, string>[] connectionPairs = new Pair<string, string>[]
        {
            new Pair<string, string>("power_in", "power_out"),
            new Pair<string, string>("signal_in1", "signal_out1"),
            new Pair<string, string>("signal_in2", "signal_out2"),
            new Pair<string, string>("signal_in3", "signal_out3"),
            new Pair<string, string>("signal_in4", "signal_out4"),
            new Pair<string, string>("signal_in", "signal_out"),
            new Pair<string, string>("signal_in1", "signal_out"),
            new Pair<string, string>("signal_in2", "signal_out")
        };

        private void GetConnectedComponentsRecursive<T>(Connection c, HashSet<Connection> alreadySearched, List<T> connectedComponents) where T : ItemComponent
        {
            alreadySearched.Add(c);
                        
            var recipients = c.Recipients;
            foreach (Connection recipient in recipients)
            {
                if (alreadySearched.Contains(recipient)) { continue; }
                var component = recipient.Item.GetComponent<T>();                    
                if (component != null && !connectedComponents.Contains(component))
                {
                    connectedComponents.Add(component);
                }

                //connected to a wifi component -> see which other wifi components it can communicate with
                var wifiComponent = recipient.Item.GetComponent<WifiComponent>();
                if (wifiComponent != null && wifiComponent.CanTransmit())
                {
                    foreach (var wifiReceiver in wifiComponent.GetReceiversInRange())
                    {
                        var receiverConnections = wifiReceiver.Item.Connections;
                        if (receiverConnections == null) { continue; }
                        foreach (Connection wifiOutput in receiverConnections)
                        {
                            if ((wifiOutput.IsOutput == recipient.IsOutput) || alreadySearched.Contains(wifiOutput)) { continue; }
                            GetConnectedComponentsRecursive(wifiOutput, alreadySearched, connectedComponents);
                        }
                    }
                }

                recipient.Item.GetConnectedComponentsRecursive(recipient, alreadySearched, connectedComponents);                   
            }

            foreach (Pair<string, string> connectionPair in connectionPairs)
            {
                if (connectionPair.First == c.Name)
                {
                    var pairedConnection = c.Item.Connections.FirstOrDefault(c2 => c2.Name == connectionPair.Second);
                    if (pairedConnection != null)
                    {
                        if (alreadySearched.Contains(pairedConnection)) { continue; }
                        GetConnectedComponentsRecursive(pairedConnection, alreadySearched, connectedComponents);
                    }
                }
                else if (connectionPair.Second == c.Name)
                {
                    var pairedConnection = c.Item.Connections.FirstOrDefault(c2 => c2.Name == connectionPair.First);
                    if (pairedConnection != null)
                    {
                        if (alreadySearched.Contains(pairedConnection)) { continue; }
                        GetConnectedComponentsRecursive(pairedConnection, alreadySearched, connectedComponents);
                    }
                }
            }
        }

        public Controller FindController()
        {
            //try finding the controller with the simpler non-recursive method first
            var controllers = GetConnectedComponents<Controller>();
            if (controllers.None()) { controllers = GetConnectedComponents<Controller>(recursive: true); }
            return controllers.Count < 2 ? controllers.FirstOrDefault() :
                (controllers.FirstOrDefault(c => c.GetFocusTarget() == this) ?? controllers.FirstOrDefault());
        }

        public bool TryFindController(out Controller controller)
        {
            controller = FindController();
            return controller != null;
        }

        public void SendSignal(int stepsTaken, string signal, string connectionName, Character sender, float power = 0.0f, Item source = null, float signalStrength = 1.0f)
        {
            if (connections == null) { return; }
            if (!connections.TryGetValue(connectionName, out Connection c)) { return; }
            SendSignal(stepsTaken, signal, c, sender, power, source, signalStrength);           
        }

        public void SendSignal(int stepsTaken, string signal, Connection connection, Character sender, float power = 0.0f, Item source = null, float signalStrength = 1.0f)
        {
            LastSentSignalRecipients.Clear();
            if (connections == null || connection == null) { return; }

            stepsTaken++;
            
            if (stepsTaken > 10)
            {
                //use a coroutine to prevent infinite loops by creating a one 
                //frame delay if the "signal chain" gets too long
                CoroutineManager.StartCoroutine(SendSignal(signal, connection, sender, power, signalStrength));
            }
            else
            {
                foreach (StatusEffect effect in connection.Effects)
                {
                    if (condition <= 0.0f && effect.type != ActionType.OnBroken) { continue; }
                    if (signal != "0" && !string.IsNullOrEmpty(signal)) { ApplyStatusEffect(effect, ActionType.OnUse, (float)Timing.Step); }
                }
                connection.SendSignal(stepsTaken, signal, source ?? this, sender, power, signalStrength);
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
            bool picked = false, selected = false;
#if CLIENT
            bool hasRequiredSkills = true;
            Skill requiredSkill = null;
            float skillMultiplier = 1;
#endif
            if (NonInteractable) { return false; }
            foreach (ItemComponent ic in components)
            {
                bool pickHit = false, selectHit = false;

                if (picker.IsKeyDown(InputType.Aim))
                {
                    pickHit = false;
                    selectHit = false;
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
#if CLIENT
                //use the non-mouse interaction key (E on both default and legacy keybinds) in wiring mode
                //LMB is used to manipulate wires, so using E to select connection panels is much easier
                if (Screen.Selected == GameMain.SubEditorScreen && GameMain.SubEditorScreen.WiringMode)
                {
                    pickHit = selectHit = GameMain.Config.KeyBind(InputType.Use).MouseButton == MouseButton.None ?
                        picker.IsKeyHit(InputType.Use) :
                        picker.IsKeyHit(InputType.Select);
                }
#endif
                if (!pickHit && !selectHit) { continue; }
                
                bool showUiMsg = false;
#if CLIENT
                if (!ic.HasRequiredSkills(picker, out Skill tempRequiredSkill)) { hasRequiredSkills = false; skillMultiplier = ic.GetSkillMultiplier(); }
                showUiMsg = picker == Character.Controlled && Screen.Selected != GameMain.SubEditorScreen;
#endif
                if (!ignoreRequiredItems && !ic.HasRequiredItems(picker, showUiMsg)) continue;
                if ((ic.CanBePicked && pickHit && ic.Pick(picker)) ||
                    (ic.CanBeSelected && selectHit && ic.Select(picker)))
                {
                    picked = true;
                    ic.ApplyStatusEffects(ActionType.OnPicked, 1.0f, picker);
#if CLIENT
                    if (picker == Character.Controlled) { GUI.ForceMouseOn(null); }
                    if (tempRequiredSkill != null) { requiredSkill = tempRequiredSkill; }
#endif
                    if (ic.CanBeSelected) selected = true;
                }
            }

            if (!picked) return false;

            if (picker != null)
            {
                if (picker.SelectedConstruction == this)
                {
                    if (picker.IsKeyHit(InputType.Select) || forceSelectKey)
                    {
                        picker.SelectedConstruction = null;
                    }
                }
                else if (selected)
                {
                    picker.SelectedConstruction = this;
                }
            }

#if CLIENT
            if (!hasRequiredSkills && Character.Controlled == picker && Screen.Selected != GameMain.SubEditorScreen)
            {
                if (requiredSkill != null)
                {
                    GUI.AddMessage(TextManager.GetWithVariables("InsufficientSkills", new string[2] { "[requiredskill]", "[requiredlevel]" },
                        new string[2] { TextManager.Get("SkillName." + requiredSkill.Identifier), ((int)(requiredSkill.Level * skillMultiplier)).ToString() }, new bool[2] { true, false }), GUI.Style.Red);
                }
            }
#endif

            if (Container != null) Container.RemoveContained(this);

            return true;         
        }

        public float GetContainedItemConditionPercentage()
        {
            var containedItems = ContainedItems;

            if (containedItems != null)
            {
                float condition = 0f;
                float maxCondition = 0f;

                foreach (Item item in containedItems)
                {
                    condition += item.condition;
                    maxCondition += item.MaxCondition;
                }

                if (maxCondition > 0.0f)
                {
                    return condition / maxCondition;
                }
            }

            return -1;
        }

        public void Use(float deltaTime, Character character = null, Limb targetLimb = null)
        {
            if (RequireAimToUse && (character == null || !character.IsKeyDown(InputType.Aim)))
            {
                return;
            }

            if (condition == 0.0f) { return; }
        
            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                bool isControlled = false;
#if CLIENT
                isControlled = character == Character.Controlled;
#endif
                if (!ic.HasRequiredContainedItems(character, isControlled)) { continue; }
                if (ic.Use(deltaTime, character))
                {
                    ic.WasUsed = true;

#if CLIENT
                    ic.PlaySound(ActionType.OnUse, character);
#endif
    
                    ic.ApplyStatusEffects(ActionType.OnUse, deltaTime, character, targetLimb);

                    if (ic.DeleteOnUse) { remove = true; }
                }
            }

            if (remove)
            {
                Spawner.AddToRemoveQueue(this);
            }
        }

        public void SecondaryUse(float deltaTime, Character character = null)
        {
            if (condition == 0.0f) { return; }

            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                bool isControlled = false;
#if CLIENT
                isControlled = character == Character.Controlled;
#endif
                if (!ic.HasRequiredContainedItems(character, isControlled)) { continue; }
                if (ic.SecondaryUse(deltaTime, character))
                {
                    ic.WasUsed = true;

#if CLIENT
                    ic.PlaySound(ActionType.OnSecondaryUse, character);
#endif

                    ic.ApplyStatusEffects(ActionType.OnSecondaryUse, deltaTime, character);

                    if (ic.DeleteOnUse) { remove = true; }
                }
            }

            if (remove)
            {
                Spawner.AddToRemoveQueue(this);
            }
        }

        public void ApplyTreatment(Character user, Character character, Limb targetLimb)
        {
            //can't apply treatment to dead characters
            if (character.IsDead) return;
            if (!UseInHealthInterface) return;

#if CLIENT
            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Treatment, character.ID, targetLimb });
                return;
            }
#endif

            bool remove = false;
            foreach (ItemComponent ic in components)
            {
                if (!ic.HasRequiredContainedItems(user, addMessage: user == Character.Controlled)) continue;

                bool success = Rand.Range(0.0f, 0.5f) < ic.DegreeOfSuccess(user);
                ActionType actionType = success ? ActionType.OnUse : ActionType.OnFailure;

#if CLIENT
                ic.PlaySound(actionType, user);
#endif
                ic.WasUsed = true;
                ic.ApplyStatusEffects(actionType, 1.0f, character, targetLimb, user: user);

                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    GameMain.NetworkMember.CreateEntityEvent(this, new object[]
                    {
                        NetEntityEvent.Type.ApplyStatusEffect, actionType, ic, character.ID, targetLimb
                    });
                }

                if (ic.DeleteOnUse) remove = true;
            }

            if (remove) { Spawner?.AddToRemoveQueue(this); }
        }

        public bool Combine(Item item, Character user)
        {
            if (item == this) { return false; }
            bool isCombined = false;
            foreach (ItemComponent ic in components)
            {
                if (ic.Combine(item, user)) { isCombined = true; }
            }
#if CLIENT
            if (isCombined) { GameMain.Client?.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.Combine, item.ID }); }
#endif
            return isCombined;
        }

        public void Drop(Character dropper, bool createNetworkEvent = true)
        {
            if (createNetworkEvent)
            {
                if (parentInventory != null && !parentInventory.Owner.Removed && !Removed &&
                    GameMain.NetworkMember != null && (GameMain.NetworkMember.IsServer || Character.Controlled == dropper))
                {
                    parentInventory.CreateNetworkEvent();
                    //send frequent updates after the item has been dropped
                    PositionUpdateInterval = 0.0f;
                }
            }

            if (body != null)
            {
                isActive = true;
                body.Enabled = true;
                body.PhysEnabled = true;
                body.ResetDynamics();
                if (dropper != null)
                {
                    if (body.Removed)
                    {
                        DebugConsole.ThrowError(
                            "Failed to drop the item \"" + Name + "\" (body has been removed"
                            + (Removed ? ", item has been removed)" : ")"));
                    }
                    else
                    {
                        body.SetTransform(dropper.SimPosition, 0.0f);
                    }
                }
            }

            foreach (ItemComponent ic in components) { ic.Drop(dropper); }
            
            if (Container != null)
            {
                SetTransform(Container.SimPosition, 0.0f);
                Container.RemoveContained(this);
                Container = null;
            }
            
            if (parentInventory != null)
            {
                parentInventory.RemoveItem(this);
                parentInventory = null;
            }
        }

        public void Equip(Character character)
        {
            if (Removed)
            {
                DebugConsole.ThrowError($"Tried to equip a removed item ({Name}).\n{Environment.StackTrace.CleanupStackTrace()}");
                return;
            }

            foreach (ItemComponent ic in components) { ic.Equip(character); }
        }

        public void Unequip(Character character)
        {
            character.DeselectItem(this);
            foreach (ItemComponent ic in components) { ic.Unequip(character); }
        }

        public List<Pair<object, SerializableProperty>> GetProperties<T>()
        {
            List<Pair<object, SerializableProperty>> allProperties = new List<Pair<object, SerializableProperty>>();

            List<SerializableProperty> itemProperties = SerializableProperty.GetProperties<T>(this);
            foreach (var itemProperty in itemProperties)
            {
                allProperties.Add(new Pair<object, SerializableProperty>(this, itemProperty));
            }            
            foreach (ItemComponent ic in components)
            {
                List<SerializableProperty> componentProperties = SerializableProperty.GetProperties<T>(ic);
                foreach (var componentProperty in componentProperties)
                {
                    allProperties.Add(new Pair<object, SerializableProperty>(ic, componentProperty));
                }
            }
            return allProperties;
        }

        private void WritePropertyChange(IWriteMessage msg, object[] extraData, bool inGameEditableOnly)
        {
            var allProperties = inGameEditableOnly ? GetProperties<InGameEditable>() : GetProperties<Editable>();
            SerializableProperty property = extraData[1] as SerializableProperty;
            if (property != null)
            {
                var propertyOwner = allProperties.Find(p => p.Second == property);
                if (allProperties.Count > 1)
                {
                    msg.WriteRangedInteger(allProperties.FindIndex(p => p.Second == property), 0, allProperties.Count - 1);
                }

                object value = property.GetValue(propertyOwner.First);
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
                else if (value is Color color)
                {
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
                else if (value is Point)
                {
                    msg.Write(((Point)value).X);
                    msg.Write(((Point)value).Y);
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
                    throw new NotImplementedException("Serializing item properties of the type \"" + value.GetType() + "\" not supported");
                }
            }
            else
            {
                throw new ArgumentException("Failed to write propery value - property \"" + (property == null ? "null" : property.Name) + "\" is not serializable.");
            }
        }

        private CoroutineHandle logPropertyChangeCoroutine;

        private void ReadPropertyChange(IReadMessage msg, bool inGameEditableOnly, Client sender = null)
        {
            var allProperties = inGameEditableOnly ? GetProperties<InGameEditable>() : GetProperties<Editable>();
            if (allProperties.Count == 0) { return; }

            int propertyIndex = 0;
            if (allProperties.Count > 1)
            {
                propertyIndex = msg.ReadRangedInteger(0, allProperties.Count - 1);
            }

            bool allowEditing = true;
            object parentObject = allProperties[propertyIndex].First;
            SerializableProperty property = allProperties[propertyIndex].Second;
            if (inGameEditableOnly && parentObject is ItemComponent ic)
            {
                if (!ic.AllowInGameEditing) { allowEditing = false; }
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer && !CanClientAccess(sender))
            {
                allowEditing = false;
            }

            Type type = property.PropertyType;
            string logValue = "";
            if (type == typeof(string))
            {
                string val = msg.ReadString();
                if (allowEditing) 
                { 
                    property.TrySetValue(parentObject, val);
                }
            }
            else if (type == typeof(float))
            {
                float val = msg.ReadSingle();
                logValue = val.ToString("G", CultureInfo.InvariantCulture);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(int))
            {
                int val = msg.ReadInt32();
                logValue = val.ToString();
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(bool))
            {
                bool val = msg.ReadBoolean();
                logValue = val.ToString();
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Color))
            {
                Color val = new Color(msg.ReadByte(), msg.ReadByte(), msg.ReadByte(), msg.ReadByte());
                logValue = XMLExtensions.ColorToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Vector2))
            {
                Vector2 val = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                logValue = XMLExtensions.Vector2ToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Vector3))
            {
                Vector3 val = new Vector3(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
                logValue = XMLExtensions.Vector3ToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Vector4))
            {
                Vector4 val = new Vector4(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
                logValue = XMLExtensions.Vector4ToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Point))
            {
                Point val = new Point(msg.ReadInt32(), msg.ReadInt32());
                logValue = XMLExtensions.PointToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (type == typeof(Rectangle))
            {
                Rectangle val = new Rectangle(msg.ReadInt32(), msg.ReadInt32(), msg.ReadInt32(), msg.ReadInt32());
                logValue = XMLExtensions.RectToString(val);
                if (allowEditing) { property.TrySetValue(parentObject, val); }
            }
            else if (typeof(Enum).IsAssignableFrom(type))
            {
                int intVal = msg.ReadInt32();
                try
                {
                    if (allowEditing) 
                    { 
                        property.TrySetValue(parentObject, Enum.ToObject(type, intVal));
                        logValue = property.GetValue(parentObject).ToString();
                    }
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

#if SERVER
            if (allowEditing)
            {
                //the property change isn't logged until the value stays unchanged for 1 second to prevent log spam when a player adjusts a value
                if (logPropertyChangeCoroutine != null)
                {
                    CoroutineManager.StopCoroutines(logPropertyChangeCoroutine);
                }
                logPropertyChangeCoroutine = CoroutineManager.InvokeAfter(() =>
                {
                    GameServer.Log($"{sender.Character.Name} set the value \"{property.Name}\" of the item \"{Name}\" to \"{logValue}\".", ServerLog.MessageType.ItemInteraction);
                }, delay: 1.0f);
            }
#endif

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(this, new object[] { NetEntityEvent.Type.ChangeProperty, property });
            }
        }

        partial void UpdateNetPosition(float deltaTime);

        public static Item Load(XElement element, Submarine submarine)
        {
            return Load(element, submarine, createNetworkEvent: false);
        }

        /// <summary>
        /// Instantiate a new item and load its data from the XML element.
        /// </summary>
        /// <param name="element">The element containing the data of the item</param>
        /// <param name="submarine">The submarine to spawn the item in (can be null)</param>
        /// <param name="createNetworkEvent">Should an EntitySpawner event be created to notify clients about the item being created.</param>
        /// <returns></returns>
        public static Item Load(XElement element, Submarine submarine, bool createNetworkEvent)
        {
            string name = element.Attribute("name").Value;            
            string identifier = element.GetAttributeString("identifier", "");

            ItemPrefab prefab = ItemPrefab.Find(name, identifier);

            if (prefab == null)
            {
                return null;
            }
                                   
            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            if (rect.Width == 0 && rect.Height == 0)
            {
                rect.Width = (int)(prefab.Size.X * prefab.Scale);
                rect.Height = (int)(prefab.Size.Y * prefab.Scale);
            }

            Item item = new Item(rect, prefab, submarine, callOnItemLoaded: false)
            {
                Submarine = submarine,
                ID = (ushort)int.Parse(element.Attribute("ID").Value),
                linkedToID = new List<ushort>()
            };
            item.OriginalID = item.ID;

#if SERVER
            if (createNetworkEvent)
            {
                Spawner.CreateNetworkEvent(item, remove: false);
            }
#endif

            foreach (XAttribute attribute in element.Attributes())
            {
                if (!item.SerializableProperties.TryGetValue(attribute.Name.ToString(), out SerializableProperty property)) continue;
                bool shouldBeLoaded = false;
                foreach (var propertyAttribute in property.Attributes.OfType<Serialize>())
                {
                    if (propertyAttribute.isSaveable)
                    {
                        shouldBeLoaded = true;
                        break;
                    }
                }

                if (shouldBeLoaded) { property.TrySetValue(item, attribute.Value); }
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

            bool thisIsOverride = element.GetAttributeBool("isoverride", false);

            //if we're overriding a non-overridden item in a sub/assembly xml or vice versa, 
            //use the values from the prefab instead of loading them from the sub/assembly xml
            bool usePrefabValues = thisIsOverride != prefab.IsOverride;
            List<ItemComponent> unloadedComponents = new List<ItemComponent>(item.components);
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant()) 
                {
                    case "upgrade":
                    {
                        var upgradeIdentifier = subElement.GetAttributeString("identifier", string.Empty);
                        UpgradePrefab upgradePrefab = UpgradePrefab.Find(upgradeIdentifier);
                        int level = subElement.GetAttributeInt("level", 1);
                        if (upgradePrefab != null)
                        {
                            item.AddUpgrade(new Upgrade(item, upgradePrefab, level, subElement));
                        }
                        else
                        {
                            DebugConsole.AddWarning($"An upgrade with identifier \"{upgradeIdentifier}\" on {item.Name} was not found. " +
                                                    "It's effect will not be applied and won't be saved after the round ends.");
                        }
                        break;
                    }
                    default: 
                    {
                        ItemComponent component = unloadedComponents.Find(x => x.Name == subElement.Name.ToString());
                        if (component == null) { continue; }
                        component.Load(subElement, usePrefabValues);
                        unloadedComponents.Remove(component);
                        break;
                    }
                }
            }
            if (usePrefabValues)
            {
                //use prefab scale when overriding a non-overridden item or vice versa
                item.Scale = prefab.ConfigElement.GetAttributeFloat(item.scale, "scale", "Scale");
            }
            
            item.Upgrades.ForEach(upgrade => upgrade.ApplyUpgrade());

            if (element.GetAttributeBool("flippedx", false)) { item.FlipX(false); }
            if (element.GetAttributeBool("flippedy", false)) { item.FlipY(false); }

            float condition = element.GetAttributeFloat("condition", item.MaxCondition);
            item.condition = MathHelper.Clamp(condition, 0, item.MaxCondition);
            item.lastSentCondition = item.condition;

            item.SetActiveSprite();

            if (submarine?.Info.GameVersion != null)
            {
                SerializableProperty.UpgradeGameVersion(item, item.Prefab.ConfigElement, submarine.Info.GameVersion);
            }

            foreach (ItemComponent component in item.components)
            {
                component.OnItemLoaded();
            }
            
            return item;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = new XElement("Item");

            element.Add(
                new XAttribute("name", Prefab.OriginalName),
                new XAttribute("identifier", Prefab.Identifier),
                new XAttribute("ID", ID));

            if (Rotation != 0f) { element.Add(new XAttribute("rotation", Rotation)); }

            if (Prefab.IsOverride) { element.Add(new XAttribute("isoverride", "true")); }
            if (FlippedX) { element.Add(new XAttribute("flippedx", true)); }
            if (FlippedY) { element.Add(new XAttribute("flippedy", true)); }

            if (condition < MaxCondition)
            {
                element.Add(new XAttribute("condition", condition.ToString("G", CultureInfo.InvariantCulture)));
            }

            if (!MathUtils.NearlyEqual(healthMultiplier, 1.0f))
            {
                element.Add(new XAttribute("healthmultiplier", HealthMultiplier.ToString("G", CultureInfo.InvariantCulture)));
            }

            Item rootContainer = GetRootContainer() ?? this;
            System.Diagnostics.Debug.Assert(Submarine != null || rootContainer.ParentInventory?.Owner is Character);

            Vector2 subPosition = Submarine == null ? Vector2.Zero : Submarine.HiddenSubPosition;

            int width = ResizeHorizontal ? rect.Width : defaultRect.Width;
            int height = ResizeVertical ? rect.Height : defaultRect.Height;
            element.Add(new XAttribute("rect",
                (int)(rect.X - subPosition.X) + "," +
                (int)(rect.Y - subPosition.Y) + "," +
                width + "," + height));
            
            if (linkedTo != null && linkedTo.Count > 0)
            {
                bool isOutpost = Submarine != null && Submarine.Info.IsOutpost;
                var saveableLinked = linkedTo.Where(l => l.ShouldBeSaved && (l.Removed == Removed) && (l.Submarine == null || l.Submarine.Info.IsOutpost == isOutpost));
                element.Add(new XAttribute("linked", string.Join(",", saveableLinked.Select(l => l.ID.ToString()))));
            }

            SerializableProperty.SerializeProperties(this, element);

            foreach (ItemComponent ic in components)
            {
                ic.Save(element);
            }

            foreach (var upgrade in Upgrades)
            {
                upgrade.Save(element);
            }

            parentElement.Add(element);

            return element;
        }

        public virtual void Reset()
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, Prefab.ConfigElement);
            Sprite.ReloadXML();
            SpriteDepth = Sprite.Depth;
            condition = MaxCondition;
            components.ForEach(c => c.Reset());
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
                DebugConsole.ThrowError("Attempting to remove an already removed item (" + Name + ")\n" + Environment.StackTrace.CleanupStackTrace());
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

            Door door = GetComponent<Door>();
            Ladder ladder = GetComponent<Ladder>();
            if (door != null || ladder != null)
            {
                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (door != null && wp.ConnectedDoor == door) { wp.ConnectedGap = null; }
                    if (ladder != null && wp.Ladders == ladder) { wp.Ladders = null; }
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

            if (StaticFixtures != null)
            {
                foreach (Fixture fixture in StaticFixtures)
                {
                    //if the world is null, the body has already been removed
                    //happens if the sub the fixture is attached to is removed before the item
                    if (fixture.Body?.World == null) { continue; }
                    fixture.Body.Remove(fixture);
                }
                StaticFixtures.Clear();
            }

            foreach (Item it in ItemList)
            {
                if (it.linkedTo.Contains(this))
                {
                    it.linkedTo.Remove(this);
                }
            }

            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();

        public static void RemoveByPrefab(ItemPrefab prefab)
        {
            if (ItemList == null) { return; }
            List<Item> list = new List<Item>(ItemList);
            foreach (Item item in list)
            {
                if (item.prefab == prefab)
                {
                    item.Remove();
                }
            }
        }
    }
}
