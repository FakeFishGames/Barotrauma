using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Barotrauma.Networking;
using System;
using Barotrauma.Items.Components;
using System.ComponentModel;
using System.Collections.ObjectModel;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;

namespace Barotrauma
{

    public enum ActionType
    {
        Always, OnPicked, OnUse, 
        OnWearing, OnContaining, OnContained, 
        OnActive, OnFailure, OnBroken, 
        OnFire, InWater,
        OnImpact
    }

    class Item : MapEntity, IDamageable, IPropertyObject
    {
        public static List<Item> ItemList = new List<Item>();
        private ItemPrefab prefab;

        public static ItemSpawner Spawner = new ItemSpawner();
        public static ItemRemover Remover = new ItemRemover();

        public static bool ShowLinks = true;

        private List<string> tags;
        
        public Hull CurrentHull;

        //components that determine the functionality of the item
        public List<ItemComponent> components;
        public List<IDrawableComponent> drawableComponents;

        public PhysicsBody body;

        private float condition;

        private bool inWater;
                
        private Inventory parentInventory;

        //a dictionary containing lists of the status effects in all the components of the item
        private Dictionary<ActionType, List<StatusEffect>> statusEffectLists;

        public readonly Dictionary<string, ObjectProperty> properties;
        public Dictionary<string, ObjectProperty> ObjectProperties
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

        public override bool SelectableInEditor
        {
            get
            {
                return parentInventory == null && (body == null || body.Enabled);
            }
        }

        public List<FixRequirement> FixRequirements;

        public override string Name
        {
            get { return prefab.Name; }
        }

        public string Description
        {
            get { return prefab.Description; }
        }

        public float ImpactTolerance
        {
            get { return prefab.ImpactTolerance; }
        }

        public override Sprite Sprite
        {
            get { return prefab.sprite; }
        }

        public float PickDistance
        {
            get { return prefab.PickDistance; }
        }

        public override Vector2 SimPosition
        {
            get
            {
                return (body==null) ? base.SimPosition : body.SimPosition;
            }
        }

        protected Color spriteColor;
        [Editable, HasDefaultValue("1.0,1.0,1.0,1.0", true)]
        public string SpriteColor
        {
            get { return ToolBox.Vector4ToString(spriteColor.ToVector4()); }
            set
            {
                spriteColor = new Color(ToolBox.ParseToVector4(value));
            }
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
                if (!MathUtils.IsValid(value)) return;

                float prev = condition;
                condition = MathHelper.Clamp(value, 0.0f, 100.0f); 
                if (condition == 0.0f && prev>0.0f)
                {
                    new NetworkEvent(this.ID, false);

                    ApplyStatusEffects(ActionType.OnBroken, 1.0f, null);
                    foreach (FixRequirement req in FixRequirements)
                    {
                        req.Fixed = false;
                    }
                }
            }
        }

        public float Health
        {
            get { return condition; }
        }

        [Editable, HasDefaultValue("", true)]
        public string Tags
        {
            get { return string.Join(",",tags); }
            set
            {
                tags.Clear();
                if (value == null) return;

                string[] newTags = value.Split(',');
                foreach (string tag in newTags)
                {
                    string newTag = tag.Trim();
                    if (!tags.Contains(newTag)) tags.Add(newTag);
                }   

            }
        }

        public bool FireProof
        {
            get { return prefab.FireProof; }
        }

        public bool CanUseOnSelf
        {
            get { return prefab.CanUseOnSelf; }
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
        
        public ItemPrefab Prefab
        {
            get { return prefab; }
        }

        public string ConfigFile
        {
            get { return prefab.ConfigFile; }
        }

        public bool Removed
        {
            get;
            private set;
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

        public int Capacity
        {
            get
            {
                ItemContainer c = GetComponent<ItemContainer>();
                return (c == null) ? 0 : c.Capacity;
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
                ItemContainer c = GetComponent<ItemContainer>();
                return (c == null) ? null : Array.FindAll(c.Inventory.Items, i=>i!=null);
            }
        }

        public override bool IsLinkable
        {
            get { return prefab.IsLinkable; }
        }

        public override string ToString()
        {
            return (GameMain.DebugDraw) ? Name + "(ID: " + ID + ")" : Name;
        }

        public List<IPropertyObject> AllPropertyObjects
        {
            get
            {
                List<IPropertyObject> pobjects = new List<IPropertyObject>();
                pobjects.Add(this);
                foreach (ItemComponent ic in components)
                {
                    pobjects.Add(ic);
                }
                return pobjects;
            }
        }

        public Item(ItemPrefab itemPrefab, Vector2 position, Submarine submarine)
            : this(new Rectangle((int)position.X, (int)position.Y, (int)itemPrefab.sprite.size.X, (int)itemPrefab.sprite.size.Y), itemPrefab, submarine)
        {

        }

        public Item(Rectangle newRect, ItemPrefab itemPrefab, Submarine submarine)
            : base(itemPrefab, submarine)
        {
            prefab = itemPrefab;

            spriteColor = prefab.SpriteColor;

            linkedTo            = new ObservableCollection<MapEntity>();
            components          = new List<ItemComponent>();
            drawableComponents  = new List<IDrawableComponent>();
            FixRequirements     = new List<FixRequirement>();
            tags                = new List<string>();
                       
            rect = newRect;
            
            if (submarine==null || !submarine.Loading) FindHull();

            condition = 100.0f;

            XElement element = ToolBox.TryLoadXml(Prefab.ConfigFile).Root;
            if (element == null) return;

            if (ToolBox.GetAttributeString(element, "name", "") != Name)
            {
                foreach (XElement subElement in element.Elements())
                {
                    if (ToolBox.GetAttributeString(subElement, "name", "") != Name) continue;

                    element = subElement;
                    break;
                }
            }

            properties = ObjectProperty.InitProperties(this, element);

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "body":
                        body = new PhysicsBody(subElement, ConvertUnits.ToSimUnits(Position));
                        break;
                    case "trigger":
                    case "sprite":
                    case "deconstruct":
                        break;
                    case "aitarget":
                        aiTarget = new AITarget(this);
                        aiTarget.SightRange = ToolBox.GetAttributeFloat(subElement, "sightrange", 1000.0f);
                        aiTarget.SoundRange = ToolBox.GetAttributeFloat(subElement, "soundrange", 0.0f);
                        break;
                    case "fixrequirement":
                        FixRequirements.Add(new FixRequirement(subElement));
                        break;
                    default:
                        ItemComponent ic = ItemComponent.Load(subElement, this, prefab.ConfigFile);
                        if (ic == null) break;

                        components.Add(ic);

                        if (ic is IDrawableComponent && ic.Drawable) drawableComponents.Add(ic as IDrawableComponent);

                        if (ic.statusEffectLists == null) continue;

                        if (statusEffectLists == null) 
                            statusEffectLists = new Dictionary<ActionType, List<StatusEffect>>();

                        //go through all the status effects of the component 
                        //and add them to the corresponding statuseffect list
                        foreach (List<StatusEffect> componentEffectList in ic.statusEffectLists.Values)
                        {

                            ActionType actionType = componentEffectList.First().type;

                            List<StatusEffect> statusEffectList;
                            if (!statusEffectLists.TryGetValue(actionType, out statusEffectList))
                            {
                                statusEffectList = new List<StatusEffect>();
                                statusEffectLists.Add(actionType, statusEffectList);
                            }

                            foreach (StatusEffect effect in componentEffectList)
                            {
                                statusEffectList.Add(effect);
                            }
                        }

                        break;
                }
            }
            
            //containers need to handle collision events to notify items inside them about the impact
            if (ImpactTolerance > 0.0f || GetComponent<ItemContainer>() != null)
            {
                if (body != null) body.FarseerBody.OnCollision += OnCollision;
            }

            InsertToList();
            ItemList.Add(this);
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
            ItemContainer c = GetComponent<ItemContainer>();
            if (c == null) return;
            
            c.RemoveContained(contained);
            contained.Container = null;            
        }


        public void SetTransform(Vector2 simPosition, float rotation)
        {
            if (body != null)
            {
                try
                {
                    body.SetTransform(simPosition, rotation);
                }
                catch (Exception e)
                {
#if DEBUG
                    DebugConsole.ThrowError("Failed to set item transform", e);
#endif
                }
            }

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(simPosition);

            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            FindHull();
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            if (ItemList != null && body != null)
            {
                //Vector2 pos = new Vector2(rect.X + rect.Width / 2.0f, rect.Y - rect.Height / 2.0f);
                body.SetTransform(body.SimPosition+ConvertUnits.ToSimUnits(amount), body.Rotation);
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
        
        public virtual Hull FindHull()
        {
            if (parentInventory != null && parentInventory.Owner != null)
            {
                if (parentInventory.Owner is Character)
                {
                    CurrentHull = (parentInventory.Owner as Character).AnimController.CurrentHull;
                }
                else if (parentInventory.Owner is Item)
                {
                    CurrentHull = (parentInventory.Owner as Item).CurrentHull;
                }

                Submarine = parentInventory.Owner.Submarine;
                if (body != null) body.Submarine = Submarine;

                return CurrentHull;
            }

            CurrentHull = Hull.FindHull(WorldPosition, CurrentHull);
            if (body != null)
            {
                Submarine = CurrentHull == null ? null : Submarine.Loaded;
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
        
        public void AddTag(string tag)
        {
            if (tags.Contains(tag)) return;
            tags.Add(tag);
        }

        public bool HasTag(string tag)
        {
            if (tag == null) return true;

            return (tags.Contains(tag) || tags.Contains(tag.ToLowerInvariant()));
        }


        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null)
        {
            if (statusEffectLists == null) return;

            List<StatusEffect> statusEffects;
            if (!statusEffectLists.TryGetValue(type, out statusEffects)) return;

            foreach (StatusEffect effect in statusEffects)
            {
                ApplyStatusEffect(effect, type, deltaTime, character);
            }
        }

        public void ApplyStatusEffect(StatusEffect effect, ActionType type, float deltaTime, Character character = null)
        {
            if (condition == 0.0f && effect.type != ActionType.OnBroken) return;
            if (effect.type != type) return;
            
            bool hasTargets = (effect.TargetNames == null);

            Item[] containedItems = ContainedItems;  
            if (effect.OnContainingNames!=null)
            {
                foreach (string s in effect.OnContainingNames)
                {
                    if (containedItems.FirstOrDefault(x => x!=null && x.Name==s && x.Condition>0.0f) == null) return;
                }
            }

            List<IPropertyObject> targets = new List<IPropertyObject>();

            if (containedItems != null)
            {
                if (effect.Targets.HasFlag(StatusEffect.TargetType.Contained))
                {
                    foreach (Item containedItem in containedItems)
                    {
                        if (containedItem == null) continue;
                        if (effect.TargetNames != null && !effect.TargetNames.Contains(containedItem.Name))
                        {
                            bool tagFound = false;
                            foreach (string targetName in effect.TargetNames)
                            {
                                if (!containedItem.HasTag(targetName)) continue;
                                tagFound = true;
                                break;
                            }
                            if (!tagFound) continue;
                        }

                        hasTargets = true;
                        targets.Add(containedItem);
                        //effect.Apply(type, deltaTime, containedItem);
                        //containedItem.ApplyStatusEffect(effect, type, deltaTime, containedItem);
                    }
                }
            }

            if (!hasTargets) return;

            if (effect.Targets.HasFlag(StatusEffect.TargetType.Hull) && CurrentHull != null)
            {
                targets.Add(CurrentHull);
            }


            if (effect.Targets.HasFlag(StatusEffect.TargetType.This))
            {
                foreach (var pobject in AllPropertyObjects)
                {
                    targets.Add(pobject);
                }
            }
                //effect.Apply(type, deltaTime, this);
                //ApplyStatusEffect(effect, type, deltaTime, this);

            if (effect.Targets.HasFlag(StatusEffect.TargetType.Character)) targets.Add(character);
                //effect.Apply(type, deltaTime, null, Character);
                //ApplyStatusEffect(effect, type, deltaTime, null, Character, limb);

            if (Container != null && effect.Targets.HasFlag(StatusEffect.TargetType.Parent)) targets.Add(Container);
            //{
            //    effect.Apply(type, deltaTime, container);
            //    //container.ApplyStatusEffect(effect, type, deltaTime, container);
            //}
            
            effect.Apply(type, deltaTime, this, targets);
            
        }


        public AttackResult AddDamage(IDamageable attacker, Vector2 worldPosition, Attack attack, float deltaTime, bool playSound = true)
        {
            float damageAmount = attack.GetDamage(deltaTime);
            Condition -= damageAmount;

            return new AttackResult(damageAmount, 0.0f, false);
        }

        private bool IsInWater()
        {
            if (CurrentHull == null) return true;
            
            float surfaceY = CurrentHull.Surface;

            return Position.Y < surfaceY;            
        }


        public override void Update(Camera cam, float deltaTime)
        {

            ApplyStatusEffects(ActionType.Always, deltaTime, null);

            foreach (ItemComponent ic in components)
            {
                if (ic.Parent != null) ic.IsActive = ic.Parent.IsActive;

                if (!ic.WasUsed)
                {
                    ic.StopSounds(ActionType.OnUse);
                }
                ic.WasUsed = false;

                if (parentInventory!=null) ic.ApplyStatusEffects(ActionType.OnContained, deltaTime);
                
                if (!ic.IsActive) continue;

                if (condition > 0.0f)
                {
                    ic.Update(deltaTime, cam);
                    
                    if (ic.IsActive) ic.PlaySound(ActionType.OnActive, WorldPosition);
                    //ic.ApplyStatusEffects(ActionType.OnActive, deltaTime, null);                    
                }
                else
                {
                    ic.UpdateBroken(deltaTime, cam);
                }
            }


            inWater = IsInWater();
            if (inWater) ApplyStatusEffects(ActionType.InWater, deltaTime);
            
            if (body == null || !body.Enabled) return;

            if (Math.Abs(body.LinearVelocity.X) > 0.01f || Math.Abs(body.LinearVelocity.Y) > 0.01f)
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
                
                Vector2 moveAmount = body.SimPosition - body.LastSentPosition;
                if (parentInventory == null && moveAmount != Vector2.Zero && moveAmount.Length() > NetConfig.ItemPosUpdateDistance)
                {
                    new NetworkEvent(NetworkEventType.PhysicsBodyPosition, ID, false);
                }

                Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);
                rect.X = (int)(displayPos.X - rect.Width / 2.0f);
                rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
            }

            body.MoveToTargetPosition();

            if (!inWater || Container != null || body == null) return;

            if (body.LinearVelocity != Vector2.Zero && body.LinearVelocity.Length() > 1000.0f)
            {
                body.ResetDynamics();
            }

            ApplyWaterForces();

            if(CurrentHull != null) CurrentHull.ApplyFlowForces(deltaTime, this);

        }

        /// <summary>
        /// Applies buoyancy, drag and angular drag caused by water
        /// </summary>
        private void ApplyWaterForces()
        {
            if (!InWater || Container != null || body == null || !body.Enabled) return;

            float forceFactor = 1.0f;
            if (CurrentHull != null)
            {
                float floor = CurrentHull.Rect.Y - CurrentHull.Rect.Height;
                float waterLevel = (floor + CurrentHull.Volume / CurrentHull.Rect.Width);

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
            if (GameMain.Client != null) return true;

            Vector2 normal = contact.Manifold.LocalNormal;
            
            float impact = Vector2.Dot(f1.Body.LinearVelocity, -normal);

            if (ImpactTolerance > 0.0f && impact > ImpactTolerance)
            {
                ApplyStatusEffects(ActionType.OnImpact, 1.0f);
                new NetworkEvent(NetworkEventType.ApplyStatusEffect, this.ID, false, ActionType.OnImpact);
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

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            Color color = (isSelected && editing) ? color = Color.Red : spriteColor;
            if (isHighlighted) color = Color.Orange;

            if (prefab.sprite != null)
            {
                float depth = Sprite.Depth;
                depth += (ID % 255) * 0.000001f;

                if (body == null)
                {
                    if (prefab.ResizeHorizontal || prefab.ResizeVertical)
                    {
                        prefab.sprite.DrawTiled(spriteBatch, new Vector2(DrawPosition.X-rect.Width/2, -(DrawPosition.Y+rect.Height/2)), new Vector2(rect.Width, rect.Height), color);
                    }
                    else
                    {
                        prefab.sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, 0.0f, 1.0f, SpriteEffects.None, depth);
                    }

                }
                else if (body.Enabled)
                {
                    var holdable = GetComponent<Holdable>();
                    if (holdable!=null && holdable.Picker !=null)
                    {
                        if (holdable.Picker.SelectedItems[0]==this)
                        {
                            depth = holdable.Picker.AnimController.GetLimb(LimbType.RightHand).sprite.Depth + 0.000001f;
                        }
                        else if (holdable.Picker.SelectedItems[1] == this)
                        {
                            depth = holdable.Picker.AnimController.GetLimb(LimbType.LeftArm).sprite.Depth - 0.000001f;
                        }

                        body.Draw(spriteBatch, prefab.sprite, color, depth);
                    }
                    else
                    {
                        body.Draw(spriteBatch, prefab.sprite, color, depth);
                    }                    
                }
            }

            for (int i = 0; i < drawableComponents.Count; i++ )
            {
                drawableComponents[i].Draw(spriteBatch, editing);
            }

                //foreach (ItemComponent component in components) component.Draw(spriteBatch, editing);

            if (GameMain.DebugDraw && aiTarget!=null) aiTarget.Draw(spriteBatch);
            
            if (!editing || (body != null && !body.Enabled))
            {
                isHighlighted = false;
                return;
            }

            if (isSelected || isHighlighted)
            {
                GUI.DrawRectangle(spriteBatch, new Vector2(DrawPosition.X - rect.Width / 2, -(DrawPosition.Y+rect.Height/2)), new Vector2(rect.Width, rect.Height), Color.Green);

                foreach (Rectangle t in prefab.Triggers)
                {
                    Rectangle transformedTrigger = TransformTrigger(t);

                    Vector2 rectWorldPos = new Vector2(transformedTrigger.X, transformedTrigger.Y);
                    if (Submarine!=null) rectWorldPos += Submarine.Position;
                    rectWorldPos.Y = -rectWorldPos.Y;

                    GUI.DrawRectangle(spriteBatch, 
                        rectWorldPos,
                        new Vector2(transformedTrigger.Width, transformedTrigger.Height), 
                        Color.Green);
                }
            }
            
            if (!ShowLinks) return;

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                     new Vector2(e.WorldPosition.X, -e.WorldPosition.Y),
                    Color.Red*0.3f);
            }
        }

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            if (editingHUD==null || editingHUD.UserData as Item != this)
            {
                editingHUD = CreateEditingHUD();            
            }

            editingHUD.Draw(spriteBatch);
            editingHUD.Update((float)Physics.step);

            if (!prefab.IsLinkable) return;

            if (!PlayerInput.LeftButtonClicked() || !PlayerInput.KeyDown(Keys.Space)) return;

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            foreach (MapEntity entity in mapEntityList)
            {
                if (entity == this || !entity.IsHighlighted) continue;
                if (linkedTo.Contains(entity)) continue;
                if (!entity.IsMouseOn(position)) continue;

                linkedTo.Add(entity);
                if (entity.IsLinkable && entity.linkedTo != null) entity.linkedTo.Add(this);
            }
        }

        public void DrawInGameEditing(SpriteBatch spriteBatch)
        {
            if (editingHUD == null || editingHUD.UserData as Item != this)
            {
                editingHUD = CreateEditingHUD(true);
            }

            if (editingHUD.Rect.Height > 60)
            {
                editingHUD.Update((float)Physics.step);
                editingHUD.Draw(spriteBatch);
            }
        }

        private GUIComponent CreateEditingHUD(bool inGame=false)
        {
            int width = 450;
            int x = GameMain.GraphicsWidth/2-width/2, y = 10;

            List<ObjectProperty> editableProperties = inGame ? GetProperties<InGameEditable>() : GetProperties<Editable>();
            
            int requiredItemCount = 0;
            if (!inGame)
            {
                foreach (ItemComponent ic in components)
                {
                    requiredItemCount += ic.requiredItems.Count;                    
                }
            }

            editingHUD = new GUIFrame(new Rectangle(x, y, width, 70 + (editableProperties.Count() + requiredItemCount) * 30), GUI.Style);
            editingHUD.Padding = new Vector4(10, 10, 0, 0);
            editingHUD.UserData = this;
            
            new GUITextBlock(new Rectangle(0, 0, 100, 20), prefab.Name, GUI.Style, 
                Alignment.TopLeft, Alignment.TopLeft, editingHUD, false, GUI.LargeFont);

            y += 20;

            if (!inGame)
            {
                if (prefab.IsLinkable) 
                {
                    new GUITextBlock(new Rectangle(0, 0, 0, 20), "Hold space to link to another item", 
                        GUI.Style, Alignment.TopRight, Alignment.TopRight, editingHUD).Font = GUI.SmallFont;
                    y += 25;
                }
                foreach (ItemComponent ic in components)
                {
                    foreach (RelatedItem relatedItem in ic.requiredItems)
                    {
                        new GUITextBlock(new Rectangle(0, y, 100, 20), ic.Name + ": " + relatedItem.Type.ToString() + " required", GUI.Style, editingHUD);
                        GUITextBox namesBox = new GUITextBox(new Rectangle(-10, y, 160, 20), Alignment.Right, GUI.Style, editingHUD);

                        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties (relatedItem);
                        PropertyDescriptor property = properties.Find("JoinedNames", false);

                        namesBox.Text = relatedItem.JoinedNames;
                        namesBox.UserData = new ObjectProperty(property, relatedItem);
                        namesBox.OnEnterPressed = EnterProperty;
                        namesBox.OnTextChanged = PropertyChanged;

                        y += 30;
                    }
                }

            }

            foreach (var objectProperty in editableProperties)
            {
                int height = 20;
                var editable = objectProperty.Attributes.OfType<Editable>().FirstOrDefault();
                if (editable != null) height = (int)(Math.Ceiling(editable.MaxLength / 20.0f) * 20.0f);

                object value = objectProperty.GetValue();

                if (value is bool)
                {
                    GUITickBox propertyTickBox = new GUITickBox(new Rectangle(10, y, 20, 20), objectProperty.Name,
                        Alignment.Left, editingHUD);

                    propertyTickBox.Selected = (bool)value;

                    propertyTickBox.UserData = objectProperty;
                    propertyTickBox.OnSelected = EnterProperty;
                }
                else
                {

                    new GUITextBlock(new Rectangle(0, y, 100, 20), objectProperty.Name, Color.Transparent, Color.White, Alignment.Left, GUI.Style, editingHUD);

                    GUITextBox propertyBox = new GUITextBox(new Rectangle(180, y, 250, height), GUI.Style, editingHUD);
                    if (height > 20) propertyBox.Wrap = true;

                    if (value != null)
                    {
                        if (value is float)
                        {
                            propertyBox.Text = ((float)value).ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {

                            propertyBox.Text = value.ToString();
                        }
                    }

                    propertyBox.UserData = objectProperty;
                    propertyBox.OnEnterPressed = EnterProperty;
                    propertyBox.OnTextChanged = PropertyChanged;

                }
                y = y + height + 10;   

            }
            return editingHUD;
        }

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (condition <= 0.0f)
            {
                FixRequirement.DrawHud(spriteBatch, this, character);
                return;
            }

            if (HasInGameEditableProperties)
            {
                DrawInGameEditing(spriteBatch);
            }

            foreach (ItemComponent ic in components)
            {
                ic.DrawHUD(spriteBatch, character);
            }
        }

        public List<T> GetConnectedComponents<T>()
        {
            ConnectionPanel connectionPanel = GetComponent<ConnectionPanel>();
            if (connectionPanel == null) return new List<T>();

            List<T> connectedComponents = new List<T>();

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

        public void SendSignal(int stepsTaken, string signal, string connectionName, float power = 0.0f)
        {
            stepsTaken++;

            ConnectionPanel panel = GetComponent<ConnectionPanel>();
            if (panel == null) return;
            foreach (Connection c in panel.Connections)
            {
                if (c.Name != connectionName) continue;
                
                if (stepsTaken > 10)
                {
                    //use a coroutine to prevent infinite loops by creating a one 
                    //frame delay if the "signal chain" gets too long
                    CoroutineManager.StartCoroutine(SendSignal(signal, c, power));
                }
                else
                {
                    c.SendSignal(stepsTaken, signal, this, power);
                }
            }
        }

        private IEnumerable<object> SendSignal(string signal, Connection connection, float power = 0.0f)
        {
            //wait one frame
            yield return CoroutineStatus.Running;

            ConnectionPanel panel = GetComponent<ConnectionPanel>();
            if (panel == null) yield return CoroutineStatus.Success;

            connection.SendSignal(0, signal, this, power);

            yield return CoroutineStatus.Success;
        }

        /// <param name="position">Position of the Character doing the pick, only items that are close enough to this are checked</param>
        /// <param name="pickPosition">the item closest to pickPosition is returned</param>
        /// <param name="hull">If a hull is specified, only items within that hull are checked</param>
        public static Item FindPickable(Vector2 position, Vector2 pickPosition, Hull hull = null, Item[] ignoredItems=null)
        {
            float closestDist = 0.0f, dist;
            Item closest = null;

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(position);
            Vector2 displayPickPos = ConvertUnits.ToDisplayUnits(pickPosition);

            foreach (Item item in ItemList)
            {
                if (ignoredItems!=null && ignoredItems.Contains(item)) continue;
                //if (hull != item.CurrentHull && (hull==null || (item.Rect.Height<hull.Rect.Height && item.rect.Width < hull.Rect.Width))) continue;
                if (item.body != null && !item.body.Enabled) continue;

                Pickable pickableComponent = item.GetComponent<Pickable>();
                if (pickableComponent != null && (pickableComponent.Picker != null && !pickableComponent.Picker.IsDead)) continue;

                foreach (Rectangle trigger in item.prefab.Triggers)
                {
                    Rectangle transformedTrigger = item.TransformTrigger(trigger, true);
                    
                    if (!Submarine.RectContains(transformedTrigger, displayPos)) continue;
                                        
                    Vector2 triggerCenter =
                        new Vector2(
                            transformedTrigger.X + transformedTrigger.Width / 2.0f,
                            transformedTrigger.Y - transformedTrigger.Height / 2.0f);

                    //dist = MathHelper.Min(Math.Abs(triggerCenter.X - displayPos.X), Math.Abs(triggerCenter.Y-displayPos.Y));
                    //if (dist > closestDist && closest!=null) continue;

                    dist = MathHelper.Min(Math.Abs(triggerCenter.X - displayPickPos.X), Math.Abs(triggerCenter.Y - displayPickPos.Y));
                    if (closest == null || dist < closestDist)
                    {
                        closest = item;
                        closestDist = dist;
                    }
                }
                
                if (item.prefab.PickDistance == 0.0f) continue;

                if (Vector2.Distance(displayPos, item.WorldPosition) > item.prefab.PickDistance) continue;

                if (!item.prefab.PickThroughWalls)
                {
                    Body body = Submarine.CheckVisibility(item.Submarine == null ? position : position - item.Submarine.SimPosition, item.SimPosition, true);
                    if (body != null && body.UserData as Item != item) continue;
                }

                dist = Vector2.Distance(displayPickPos, item.WorldPosition);
                if (dist < item.prefab.PickDistance && (closest == null || dist < closestDist))
                {
                    closest = item;
                    closestDist = dist;
                }
            }
            
            return closest;
        }

        public bool IsInsideTrigger(Vector2 worldPosition)
        {
            foreach (Rectangle trigger in prefab.Triggers)
            {
                Rectangle transformedTrigger = TransformTrigger(trigger, true);

                if (Submarine.RectContains(transformedTrigger, worldPosition)) return true;
            }

            return false;
        }

        public bool IsInPickRange(Vector2 worldPosition)
        {
            if (IsInsideTrigger(worldPosition)) return true;

            return Vector2.Distance(WorldPosition, worldPosition) < PickDistance;
        }

        public bool Pick(Character picker, bool ignoreRequiredItems=false, bool forceSelectKey=false, bool forceActionKey=false)
        {
            bool hasRequiredSkills = true;

            bool picked = false, selected = false;

            Skill requiredSkill = null;

            foreach (ItemComponent ic in components)
            {
                bool pickHit = false, selectHit = false;
                if (Screen.Selected == GameMain.EditMapScreen)
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
                    }
                }

                
                if (!pickHit && !selectHit) continue;

                Skill tempRequiredSkill;
                if (!ic.HasRequiredSkills(picker, out tempRequiredSkill)) hasRequiredSkills = false;

                if (tempRequiredSkill != null) requiredSkill = tempRequiredSkill;

                bool showUiMsg = picker == Character.Controlled && Screen.Selected != GameMain.EditMapScreen;
                if (!ignoreRequiredItems && !ic.HasRequiredItems(picker, showUiMsg)) continue;
                if ((ic.CanBePicked && pickHit && ic.Pick(picker)) || 
                    (ic.CanBeSelected && selectHit && ic.Select(picker)))
                {
                    picked = true;
                    ic.ApplyStatusEffects(ActionType.OnPicked, 1.0f, picker);

                    if (picker==Character.Controlled) GUIComponent.MouseOn = null;

                    if (ic.CanBeSelected) selected = true;
                }
            }

            if (!picked) return false;

            System.Diagnostics.Debug.WriteLine("Item.Pick(" + picker + ", " + forceSelectKey + ")");

            if (picker.SelectedConstruction == this)
            {
                if (picker.IsKeyHit(InputType.Select)) picker.SelectedConstruction = null;
            }
            else if (selected)
            {        
                picker.SelectedConstruction = this;
            }
            
            if (!hasRequiredSkills && Character.Controlled==picker && Screen.Selected != GameMain.EditMapScreen)
            {
                GUI.AddMessage("Your skills may be insufficient to use the item!", Color.Red, 5.0f);
                if (requiredSkill != null)
                {
                    GUI.AddMessage("("+requiredSkill.Name+" level "+requiredSkill.Level+" required)", Color.Red, 5.0f);
                }
            }

            if (Container!=null) Container.RemoveContained(this);

            return true;
        }


        public void Use(float deltaTime, Character character = null)
        {
            if (condition == 0.0f) return;

            bool remove = false;

            foreach (ItemComponent ic in components)
            {
                if (!ic.HasRequiredContainedItems(character == Character.Controlled)) continue;
                if (ic.Use(deltaTime, character))
                {
                    ic.WasUsed = true;

                    ic.PlaySound(ActionType.OnUse, WorldPosition);
    
                    ic.ApplyStatusEffects(ActionType.OnUse, deltaTime, character);

                    if (ic.DeleteOnUse) remove = true;
                }
            }

            if (remove) Remove();
        }

        public void SecondaryUse(float deltaTime, Character character = null)
        {
            foreach (ItemComponent ic in components)
            {
                if (!ic.HasRequiredContainedItems(character == Character.Controlled)) continue;
                ic.SecondaryUse(deltaTime, character);
            }
        }

        public List<ColoredText> GetHUDTexts(Character character)
        {
            List<ColoredText> texts = new List<ColoredText>();
            
            foreach (ItemComponent ic in components)
            {
                if (string.IsNullOrEmpty(ic.Msg)) continue;
                if (!ic.CanBePicked && !ic.CanBeSelected) continue;
               
                Color color = Color.Red;
                if (ic.HasRequiredSkills(character) && ic.HasRequiredItems(character, false)) color = Color.Orange;

                texts.Add(new ColoredText(ic.Msg, color));
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

        public void Drop(Character dropper = null, bool createNetworkEvent = true)
        {
            //if (dropper == Character.Controlled)
            //    new NetworkEvent(NetworkEventType.DropItem, ID, true);


            //if (dropper != null) GameServer.Log(dropper.Name + " dropped " + Name, Color.Orange);
            
            foreach (ItemComponent ic in components) ic.Drop(dropper);

            if (Container != null) Container.RemoveContained(this);
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


        public List<ObjectProperty> GetProperties<T>()
        {

            List<ObjectProperty> editableProperties = ObjectProperty.GetProperties<T>(this);
            
            foreach (ItemComponent ic in components)
            {
                List<ObjectProperty> componentProperties = ObjectProperty.GetProperties<T>(ic);
                foreach (var property in componentProperties)
                {
                    editableProperties.Add(property);
                }
            }

            return editableProperties;
        }

        private bool EnterProperty(GUITickBox tickBox)
        {
            var objectProperty = tickBox.UserData as ObjectProperty;
            if (objectProperty == null) return false;

            objectProperty.TrySetValue(tickBox.Selected);

            return true;
        }

        private bool EnterProperty(GUITextBox textBox, string text)
        {
            textBox.Color = Color.DarkGreen;

            var objectProperty = textBox.UserData as ObjectProperty;
            if (objectProperty == null) return false;

            object prevValue = objectProperty.GetValue();

            textBox.Deselect();
            
            if (objectProperty.TrySetValue(text))
            {
                textBox.Text = text;

                new NetworkEvent(NetworkEventType.UpdateProperty, ID, true, objectProperty.Name);

                return true;
            }
            else
            {
                if (prevValue != null)
                {
                    textBox.Text = prevValue.ToString();
                }
                return false;                
            }
        }

        private bool PropertyChanged(GUITextBox textBox, string text)
        {
            textBox.Color = Color.Red;

            return true;
        }
       
        public override XElement Save(XDocument doc)
        {
            XElement element = new XElement("Item");

            element.Add(new XAttribute("name", prefab.Name),
                new XAttribute("ID", ID));
            
            if (ResizeHorizontal || ResizeVertical)
            {
                element.Add(new XAttribute("rect", 
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," + 
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y) + "," + 
                    rect.Width + "," + rect.Height));
            }
            else
            {
                element.Add(new XAttribute("rect", 
                    (int)(rect.X - Submarine.HiddenSubPosition.X) + "," + 
                    (int)(rect.Y - Submarine.HiddenSubPosition.Y)));
            }

            if (linkedTo != null && linkedTo.Count>0)
            {
                string[] linkedToIDs = new string[linkedTo.Count];

                for (int i = 0; i < linkedTo.Count; i++ )
                {
                    linkedToIDs[i] = linkedTo[i].ID.ToString();
                }

                element.Add(new XAttribute("linked", string.Join(",", linkedToIDs)));
            }


            ObjectProperty.SaveProperties(this, element);

            foreach (ItemComponent ic in components)
            {
                ic.Save(element);
            }

            doc.Root.Add(element);

            return element;
        }

        public static void Load(XElement element, Submarine submarine)
        {          
            string rectString = ToolBox.GetAttributeString(element, "rect", "0,0,0,0");
            string[] rectValues = rectString.Split(',');
            Rectangle rect = Rectangle.Empty;
            if (rectValues.Length==4)
            {
                rect = new Rectangle(
                    int.Parse(rectValues[0]),
                    int.Parse(rectValues[1]),
                    int.Parse(rectValues[2]),
                    int.Parse(rectValues[3]));
            } 
            else
            {
                rect = new Rectangle(
                    int.Parse(rectValues[0]),
                    int.Parse(rectValues[1]),
                    0, 0);
            }


            string name = element.Attribute("name").Value;
            
            foreach (MapEntityPrefab ep in MapEntityPrefab.list)
            {
                ItemPrefab ip = ep as ItemPrefab;
                if (ip == null) continue;

                if (ip.Name != name) continue;

                if (rect.Width==0 && rect.Height==0)
                {
                    rect.Width = (int)ip.Size.X;
                    rect.Height = (int)ip.Size.Y;
                }

                Item item = new Item(rect, ip, submarine);
                item.Submarine = submarine;
                item.ID = (ushort)int.Parse(element.Attribute("ID").Value);
                                
                item.linkedToID = new List<ushort>();

                foreach (XAttribute attribute in element.Attributes())
                {
                    ObjectProperty property = null;
                    if (!item.properties.TryGetValue(attribute.Name.ToString(), out property)) continue;

                    bool shouldBeLoaded = false;

                    foreach (var propertyAttribute in property.Attributes.OfType<HasDefaultValue>())
                    {
                        if (propertyAttribute.isSaveable)
                        {
                            shouldBeLoaded = true;
                            break;
                        }
                    }
                    
                    if (shouldBeLoaded) property.TrySetValue(attribute.Value);
                }

                string linkedToString = ToolBox.GetAttributeString(element, "linked", "");
                if (linkedToString!="")
                {
                    string[] linkedToIds = linkedToString.Split(',');
                    for (int i = 0; i<linkedToIds.Length;i++)
                    {
                        item.linkedToID.Add((ushort)int.Parse(linkedToIds[i]));
                    }
                }

                foreach (XElement subElement in element.Elements())
                {
                    ItemComponent component = item.components.Find(x => x.Name == subElement.Name.ToString());

                    if (component == null) continue;

                    component.Load(subElement);
                }
                
                break;
            }

        }

        public override void OnMapLoaded()
        {
            FindHull();

            foreach (ItemComponent ic in components)
            {
                ic.OnMapLoaded();
            }            
        }
        

        public void NewComponentEvent(ItemComponent ic, bool isClient, bool isImportant)
        {
            int index = components.IndexOf(ic);

            new NetworkEvent(isImportant ? 
                NetworkEventType.ImportantComponentUpdate : NetworkEventType.ComponentUpdate, ID, isClient, index);
        }

        public override bool FillNetworkData(NetworkEventType type, NetBuffer message, object data)
        {
            message.Write((byte)MathHelper.Clamp(condition*2.55f,0.0f,255.0f));

            switch (type)
            {
                case NetworkEventType.DropItem:
                    if (body != null) body.FillNetworkData(message);
                    break;
                case NetworkEventType.PhysicsBodyPosition:
                    System.Diagnostics.Debug.Assert(body != null, "Tried to send a PhysicsBodyPosition message for an item that has no physics body");
                    body.FillNetworkData(message);
                    break;
                case NetworkEventType.ItemFixed:
                    byte requirementIndex = (byte)data;

                    message.Write(requirementIndex);
                    break;
                case NetworkEventType.InventoryUpdate:
                    var itemContainers = GetComponents<ItemContainer>();
                    if (itemContainers == null || !itemContainers.Any()) return false;

                    message.WriteRangedInteger(1, ItemContainer.MaxInventoryCount, itemContainers.Count);
                    foreach (ItemContainer container in itemContainers)
                    {
                        container.Inventory.FillNetworkData(NetworkEventType.InventoryUpdate, message, data);
                    }

                    return true;
                case NetworkEventType.ComponentUpdate:
                case NetworkEventType.ImportantComponentUpdate:

                    int componentIndex = (int)data;
                    if (componentIndex < 0 || componentIndex >= components.Count) return false;
                    
                    message.Write((byte)componentIndex);
                    bool sent = components[componentIndex].FillNetworkData(type, message);
                    if (sent) components[componentIndex].NetworkUpdateSent = true;
                    return sent;
                case NetworkEventType.ApplyStatusEffect:

                    ActionType actionType = (ActionType)data;
                    message.WriteRangedInteger(0, Enum.GetValues(typeof(ActionType)).Length, (int)actionType);

                    return true;
                case NetworkEventType.UpdateProperty:                                       
                    var allProperties = GetProperties<InGameEditable>();

                    ObjectProperty objectProperty = allProperties.Find(op => op.Name == (string)data);
                    if (objectProperty != null)
                    {
                        message.Write((string)data);
                        object value = objectProperty.GetValue();
                        if (value is string)
                        {
                            message.Write((byte)0);
                            message.Write((string)value);
                        }
                        else if (value is float)
                        {
                            message.Write((byte)1);
                            message.Write((float)value);
                        }
                        else if (value is int)
                        {
                            message.Write((byte)2);
                            message.Write((int)value);
                        }
                        else if (value is bool)
                        {
                            message.Write((byte)3);
                            message.Write((bool)value);
                        }
                        else
                        {
                            message.Write((byte)200);
                        }                        
                    }

                    
                    break;
            }

            return true;
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;

            Condition = (float)message.ReadByte()/2.55f;

            switch (type)
            {
                case NetworkEventType.DropItem:
                    Drop(null, false);
                    if (body != null) 
                    {
                        body.ReadNetworkData(message, sendingTime);
                        body.MoveToTargetPosition();
                    }
                    break;
                case NetworkEventType.PhysicsBodyPosition:
                    if (body != null) body.ReadNetworkData(message, sendingTime);

                    FindHull();
                    break;
                case NetworkEventType.ItemFixed:

                    byte requirementIndex = message.ReadByte();
                    data = requirementIndex;

                    if (requirementIndex >= FixRequirements.Count) return;

                    FixRequirements[requirementIndex].Fixed = true;
                    break;
                case NetworkEventType.InventoryUpdate:
                    var itemContainers = GetComponents<ItemContainer>();
                    if (itemContainers == null || !itemContainers.Any()) return;

                    int containerCount = message.ReadRangedInteger(1, ItemContainer.MaxInventoryCount);
                    for (int i = 0; i < containerCount;i++ )
                    {
                        itemContainers[i].Inventory.ReadNetworkData(type, message, sendingTime);
                    }

                    break;
                case NetworkEventType.ComponentUpdate:
                case NetworkEventType.ImportantComponentUpdate:
                    int componentIndex = message.ReadByte();

                    data = componentIndex;

                    if (componentIndex < 0 || componentIndex > components.Count - 1) return;

                    components[componentIndex].NetworkUpdateSent = true;
                    components[componentIndex].ReadNetworkData(type, message, sendingTime);
                    break;
                case NetworkEventType.ApplyStatusEffect:

                    ActionType actionType = (ActionType)message.ReadRangedInteger(0, Enum.GetValues(typeof(ActionType)).Length);

                    data = actionType;

                    ApplyStatusEffects(actionType, 1.0f);

                    break;
                case NetworkEventType.UpdateProperty:
                    string propertyName = "";

                    try
                    {
                        propertyName = message.ReadString();
                        data = propertyName;
                    }
                    catch
                    {
                        return;
                    }

                    var allProperties = GetProperties<InGameEditable>();
                    ObjectProperty property = allProperties.Find(op => op.Name == propertyName);
                    if (property == null) return;

                    try
                    {
                        switch (message.ReadByte())
                        {
                            case 0:
                                property.TrySetValue(message.ReadString());
                                break;                            
                            case 1:
                                property.TrySetValue(message.ReadFloat());
                                break;
                            case 2:
                                property.TrySetValue(message.ReadInt32());
                                break;
                            case 3:
                                property.TrySetValue(message.ReadBoolean());
                                break;
                        }
                    }

                    catch
                    {
                        return;
                    }

                    break;
            }
        }

        public override void Remove()
        {
            base.Remove();

            Removed = true;

            //sprite.Remove();
            //if (body != null) body.Remove();

            foreach (ItemComponent ic in components)
            {
                ic.Remove();
            }
            ItemList.Remove(this);

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
