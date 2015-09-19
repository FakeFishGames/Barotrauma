using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Subsurface.Networking;
using System;
using Subsurface.Items.Components;
using System.ComponentModel;
using System.Collections.ObjectModel;
using FarseerPhysics.Dynamics;

namespace Subsurface
{

    public enum ActionType
    {
        OnPicked, OnWearing, OnContaining, OnContained, OnActive, OnUse, OnFailure, OnBroken
    }

    class Item : MapEntity, IDamageable, IPropertyObject
    {
        public static List<Item> itemList = new List<Item>();
        protected ItemPrefab prefab;

        private List<string> tags;


        public Hull CurrentHull;

        //components that determine the functionality of the item
        public List<ItemComponent> components;

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
                    hasInGameEditableProperties = GetProperties<InGameEditable>().Count>0;
                }
                return (bool)hasInGameEditableProperties;
            }
        }

        public PhysicsBody body;
        
        private float condition;

        //the inventory in which the item is contained in
        public Inventory inventory;

        public Item container;
        
        public List<FixRequirement> FixRequirements;

        public override string Name
        {
            get { return prefab.Name; }
        }

        public override Sprite sprite
        {
            get { return prefab.sprite; }
        }

        public float PickDistance
        {
            get { return prefab.PickDistance; }
        }

        public float Condition
        {
            get { return condition; }
            set 
            {
                if (float.IsNaN(value)) return;

                float prev = condition;
                condition = MathHelper.Clamp(value, 0.0f, 100.0f); 
                if (condition == 0.0f && prev>0.0f)
                {
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

        private Color spriteColor;
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

        //public override AITarget AiTarget
        //{
        //    get { return aiTarget; }
        //}

        public bool Updated
        {
            set 
            {
                foreach (ItemComponent ic in components) ic.Updated = value;
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

        //which type of inventory slots (head, torso, any, etc) the item can be placed in
        public LimbSlot AllowedSlots
        {
            get
            {
                Pickable p = GetComponent<Pickable>();
                return (p==null) ? LimbSlot.Any : p.AllowedSlots;
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
                return panel.connections;
            }
        }

        public Item[] ContainedItems
        {
            get
            {
                ItemContainer c = GetComponent<ItemContainer>();
                return (c == null) ? null : Array.FindAll(c.inventory.items, i=>i!=null);
            }
        }

        public override bool IsLinkable
        {
            get { return prefab.IsLinkable; }
        }

        public override string ToString()
        {
            return (GameMain.DebugDraw) ? Name +"(ID: "+ID+")" : Name;
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

        //List<string> highlightText;

        //public List<string> HighlightText
        //{
        //    get { return highlightText;}
            
        //}

        public Item(ItemPrefab itemPrefab, Vector2 position)
            : this(new Rectangle((int)position.X, (int)position.Y, (int)itemPrefab.sprite.size.X, (int)itemPrefab.sprite.size.Y), itemPrefab)
        {

        }

        public Item(Rectangle newRect, ItemPrefab itemPrefab)
        {
            prefab = itemPrefab;

            linkedTo        = new ObservableCollection<MapEntity>();
            components      = new List<ItemComponent>();
            FixRequirements = new List<FixRequirement>();
            tags            = new List<string>();
                       
            rect = newRect;
            
            FindHull();

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
                switch (subElement.Name.ToString().ToLower())
                {
                    case "body":
                        body = new PhysicsBody(subElement, ConvertUnits.ToSimUnits(Position));
                        break;
                    case "trigger":
                    case "sprite":
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
                        //if (!string.IsNullOrWhiteSpace(ic.Msg)) highlightText.Add(ic.Msg);

                        break;
                }
            }

            
            itemList.Add(this);
            mapEntityList.Add(this);
        }

        public T GetComponent<T>()
        {
            foreach (ItemComponent ic in components)
            {
                if (ic is T) return (T)(object)ic;
            }

            return default(T);
        }
        
        public void RemoveContained(Item contained)
        {
            ItemContainer c = GetComponent<ItemContainer>();
            if (c == null) return;
            
            c.RemoveContained(contained);
            contained.container = null;            
        }


        public void SetTransform(Vector2 position, float rotation)
        {
            body.SetTransform(position, rotation);

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);

            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            if (itemList != null && body != null)
            {
                amount = ConvertUnits.ToSimUnits(amount);
                //Vector2 pos = new Vector2(rect.X + rect.Width / 2.0f, rect.Y - rect.Height / 2.0f);
                body.SetTransform(body.SimPosition+amount, body.Rotation);
            }
            foreach (ItemComponent ic in components)
            {
                ic.Move(amount);
            }

            if (body != null) FindHull();
        }

        public Rectangle TransformTrigger(Rectangle trigger)
        {
            return new Rectangle(
                Rect.X + trigger.X,
                Rect.Y + trigger.Y,
                (trigger.Width == 0) ? (int)Rect.Width : trigger.Width,
                (trigger.Height == 0) ? (int)Rect.Height : trigger.Height);
        }
        
        /// <summary>
        /// goes through every item and re-checks which hull they are in
        /// </summary>
        public static void UpdateHulls()
        {
            foreach (Item item in itemList) item.FindHull();
        }
        
        public virtual Hull FindHull()
        {
            CurrentHull = Hull.FindHull((body == null) ? Position : ConvertUnits.ToDisplayUnits(body.SimPosition), CurrentHull);
            return CurrentHull;
        }
        
        public void AddTag(string tag)
        {
            if (tags.Contains(tag)) return;
            tags.Add(tag);
        }

        public bool HasTag(string tag)
        {
            return (tags.Contains(tag));
        }


        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null)
        {
            foreach (ItemComponent ic in components)
            {
                foreach (StatusEffect effect in ic.statusEffects)
                {
                    ApplyStatusEffect(effect, type, deltaTime, character);
                }
            }
        }

        public void ApplyStatusEffect(StatusEffect effect, ActionType type, float deltaTime, Character character = null)
        {
            if (condition == 0.0f && effect.type != ActionType.OnBroken) return;

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

            if (containedItems!=null)
            {
                if (effect.Targets.HasFlag(StatusEffect.TargetType.Contained))
                {        
                    foreach (Item containedItem in containedItems)
                    {
                        if (containedItem == null || containedItem.condition==0.0f) continue;
                        if (effect.TargetNames != null && !effect.TargetNames.Contains(containedItem.Name)) continue;

                        hasTargets = true;
                        targets.Add(containedItem);
                        //effect.Apply(type, deltaTime, containedItem);
                        //containedItem.ApplyStatusEffect(effect, type, deltaTime, containedItem);
                    }
                }
            }


            if (hasTargets)
            {
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
                    //effect.Apply(type, deltaTime, null, character);
                    //ApplyStatusEffect(effect, type, deltaTime, null, character, limb);

                if (container != null && effect.Targets.HasFlag(StatusEffect.TargetType.Parent)) targets.Add(container);
                //{
                //    effect.Apply(type, deltaTime, container);
                //    //container.ApplyStatusEffect(effect, type, deltaTime, container);
                //}

                effect.Apply(type, deltaTime, this, targets);
            }       
        }


        public AttackResult AddDamage(Vector2 position, DamageType damageType, float amount, float bleedingAmount, float stun, bool playSound = true)
        {
            Condition -= amount;

            return new AttackResult(amount, 0.0f, false);
        }


        public override void Update(Camera cam, float deltaTime)
        {             
            foreach (ItemComponent ic in components)
            {
                if (ic.Parent != null) ic.IsActive = ic.Parent.IsActive;

                //if (!ic.WasUsed)
                //{
                //    if (ic.Name == "RepairTool" && ic.IsActive)
                //    {
                //        System.Diagnostics.Debug.WriteLine("stop sounds");
                //    }
                //    ic.StopSounds(ActionType.OnUse);
                //}
                //ic.WasUsed = false;
                

                if (!ic.IsActive)
                {
                    ic.StopSounds(ActionType.OnActive);
                    ic.StopSounds(ActionType.OnUse);
                    continue;
                }
                if (condition > 0.0f)
                {
                    ic.Update(deltaTime, cam);
                    
                    ic.PlaySound(ActionType.OnActive, Position);
                    ic.ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
                }
                else
                {
                    ic.UpdateBroken(deltaTime, cam);
                }
            }
            
            
            if (body == null) return;

            if (body.LinearVelocity.Length()>0.001f) FindHull();

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.SimPosition);

            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            body.SetToTargetPosition();

            if (CurrentHull != null)
            {
                float surfaceY = ConvertUnits.ToSimUnits(CurrentHull.Surface);
                if (surfaceY > body.SimPosition.Y) return;

                //the item has gone through the surface of the water -> apply an impulse which serves as surface tension
                if ((body.SimPosition.Y - (body.LinearVelocity.Y / 60.0f)) < surfaceY)
                {
                    Vector2 impulse = -body.LinearVelocity * (body.Mass / body.Density);
                    body.ApplyLinearImpulse(impulse);
                    int n = (int)((displayPos.X - CurrentHull.Rect.X) / Hull.WaveWidth);
                    CurrentHull.WaveVel[n] = impulse.Y * 10.0f;
                }
            }

            //calculate (a rough approximation of) buoyancy
            float volume = body.Mass / body.Density;
            Vector2 buoyancy = new Vector2(0, volume * 20.0f);

            //apply buoyancy and drag
            try
            {
                //if ((buoyancy - body.LinearVelocity * volume) == Vector2.Zero) DebugConsole.ThrowError("v.zero ");
                if (body.LinearVelocity != Vector2.Zero && body.LinearVelocity.Length() > 1000.0f)
                {
                    body.ResetDynamics();
                    if (body.SimPosition.Length() > 1000.0f)
                    {
                        Remove();
                        return;
                    }
                }
                body.ApplyForce(buoyancy - body.LinearVelocity * volume);
                
                //apply simple angular drag
                body.ApplyTorque(body.AngularVelocity * volume * -0.05f);
            }

            catch
            {
                DebugConsole.ThrowError("something bad happened with the physics");
            }
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            Color color = (isSelected && editing) ? color = Color.Red : spriteColor;
            if (isHighlighted) color = Color.Orange;
            
            if (prefab.sprite!=null)
            {
                if (body==null)
                {
                    prefab.sprite.DrawTiled(spriteBatch, new Vector2(rect.X, -rect.Y), new Vector2(rect.Width, rect.Height), color);
                }
                else if (body.Enabled)
                {
                    body.Draw(spriteBatch, prefab.sprite, color);
                }
            }


            foreach (ItemComponent component in components) component.Draw(spriteBatch, editing);
            
            if (!editing || (body!=null && !body.Enabled))
            {
                isHighlighted = false;
                return;
            }

            GUI.DrawRectangle(spriteBatch, new Vector2(rect.X, -rect.Y), new Vector2(rect.Width, rect.Height), Color.Green);

            foreach (Rectangle t in prefab.Triggers)
            {
                Rectangle transformedTrigger = TransformTrigger(t);
                GUI.DrawRectangle(spriteBatch, 
                    new Vector2(transformedTrigger.X, -transformedTrigger.Y),
                    new Vector2(transformedTrigger.Width, transformedTrigger.Height), 
                    Color.Green);
            }

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(rect.X + rect.Width / 2, -rect.Y + rect.Height / 2),
                    new Vector2(e.Rect.X + e.Rect.Width / 2, -e.Rect.Y + e.Rect.Height / 2),
                    Color.Red);
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
                if (!entity.Contains(position)) continue;

                linkedTo.Add(entity);
                if (entity.IsLinkable && entity.linkedTo != null) entity.linkedTo.Add(this);
            }
        }

        private GUIComponent CreateEditingHUD(bool inGame=false)
        {
            int width = 500;
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
                        GUI.Style, Alignment.TopLeft, Alignment.TopRight, editingHUD);
                    y += 25;
                }
                foreach (ItemComponent ic in components)
                {
                    foreach (RelatedItem relatedItem in ic.requiredItems)
                    {
                        new GUITextBlock(new Rectangle(0, y, 100, 20), ic.Name + ": " + relatedItem.Type.ToString() + " required", GUI.Style, editingHUD);
                        GUITextBox namesBox = new GUITextBox(new Rectangle(0, y, 200, 20), Alignment.Right, GUI.Style, editingHUD);

                        PropertyDescriptorCollection properties = TypeDescriptor.GetProperties (relatedItem);
                        PropertyDescriptor property = properties.Find("JoinedNames", false);

                        namesBox.Text = relatedItem.JoinedNames;
                        namesBox.UserData = new ObjectProperty(property, relatedItem);
                        namesBox.OnEnter = EnterProperty;
                        namesBox.OnTextChanged = PropertyChanged;

                        y += 30;
                    }
                }

            }
            
            foreach (var objectProperty in editableProperties)
            {
                new GUITextBlock(new Rectangle(0, y, 100, 20), objectProperty.Name, Color.Transparent, Color.White, Alignment.Left, null, editingHUD);

                int height = 20;
                var editable = objectProperty.Attributes.OfType<Editable>().FirstOrDefault<Editable>();
                if (editable != null) height = (int)(Math.Ceiling(editable.MaxLength / 20.0f) * 20.0f);

                GUITextBox propertyBox = new GUITextBox(new Rectangle(100, y, 200, height), GUI.Style, editingHUD);
                if (height>20) propertyBox.Wrap = true;

                object value = objectProperty.GetValue();
                if (value != null)
                {
                    propertyBox.Text = value.ToString();
                }

                propertyBox.UserData = objectProperty;
                propertyBox.OnEnter = EnterProperty;
                propertyBox.OnTextChanged = PropertyChanged;
                y = y + height+10;
            }
            return editingHUD;
        }

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (condition<=0.0f)
            {
                FixRequirement.DrawHud(spriteBatch, this, character);
                return;
            }

            if (!HasInGameEditableProperties)
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

            foreach (ItemComponent ic in components)
            {
                ic.DrawHUD(spriteBatch, character);
            }
        }

        public void SendSignal(string signal, string connectionName, float power = 0.0f)
        {
            ConnectionPanel panel = GetComponent<ConnectionPanel>();
            if (panel == null) return;
            foreach (Connection c in panel.connections)
            {
                if (c.Name != connectionName) continue;

                c.SendSignal(signal, this, power);
            }
        }

        /// <param name="position">Position of the character doing the pick, only items that are close enough to this are checked</param>
        /// <param name="pickPosition">the item closest to pickPosition is returned</param>
        /// <param name="hull">If a hull is specified, only items within that hull are checked</param>
        public static Item FindPickable(Vector2 position, Vector2 pickPosition, Hull hull = null, Item[] ignoredItems=null)
        {
            float closestDist = 0.0f, dist;
            Item closest = null;

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(position);
            Vector2 displayPickPos = ConvertUnits.ToDisplayUnits(pickPosition);

            foreach (Item item in itemList)
            {
                if (ignoredItems!=null && ignoredItems.Contains(item)) continue;
                if (hull != null && item.CurrentHull != hull) continue;
                if (item.body != null && !item.body.Enabled) continue;

                Pickable pickableComponent = item.GetComponent<Pickable>();
                if (pickableComponent != null && (pickableComponent.Picker != null && !pickableComponent.Picker.IsDead)) continue;

                foreach (Rectangle trigger in item.prefab.Triggers)
                {
                    Rectangle transformedTrigger = item.TransformTrigger(trigger);
                    
                    if (!Submarine.RectContains(transformedTrigger, displayPos))continue;
                                        
                    Vector2 triggerCenter =
                        new Vector2(
                            transformedTrigger.X + transformedTrigger.Width / 2.0f,
                            transformedTrigger.Y - transformedTrigger.Height / 2.0f);

                    dist = MathHelper.Min(Math.Abs(triggerCenter.X - displayPos.X), Math.Abs(triggerCenter.Y-displayPos.Y));
                    dist = ConvertUnits.ToSimUnits(dist);
                    if (dist > closestDist && closest!=null) continue;

                    dist = MathHelper.Min(Math.Abs(triggerCenter.X - displayPickPos.X), Math.Abs(triggerCenter.Y - displayPickPos.Y));
                    dist = ConvertUnits.ToSimUnits(dist);
                    if (closest == null || dist < closestDist)
                    {
                        closest = item;
                        closestDist = dist;
                    }
                }
                
                if (item.prefab.PickDistance == 0.0f) continue;  
                if (Vector2.Distance(position, item.SimPosition) > item.prefab.PickDistance) continue;

                Body body = Submarine.CheckVisibility(position, item.SimPosition);
                if (body != null && body.UserData as Item != item) continue;

                dist = Vector2.Distance(pickPosition, item.SimPosition);
                if ((closest == null || dist < closestDist))
                {
                    closest = item;
                    closestDist = dist;
                }
            }
            
            return closest;
        }

        public bool Pick(Character picker, bool forcePick=false)
        {

            bool hasRequiredSkills = true;

            bool picked = false, selected = false;

            Skill requiredSkill = null;

            foreach (ItemComponent ic in components)
            {
                Skill tempRequiredSkill;
                if (!ic.HasRequiredSkills(picker, out tempRequiredSkill)) hasRequiredSkills = false;

                if (tempRequiredSkill != null) requiredSkill = tempRequiredSkill;

                if (!forcePick && !ic.HasRequiredItems(picker, picker == Character.Controlled)) continue;
                if ((ic.CanBePicked && ic.Pick(picker)) || (ic.CanBeSelected && ic.Select(picker)))                     
                {
                    picked = true;
                    ic.ApplyStatusEffects(ActionType.OnPicked, 1.0f, picker);
                    if (ic.CanBeSelected) selected = true;
                }
            }

            if (!picked) return false;
            if (selected)
            {
                picker.SelectedConstruction = (picker.SelectedConstruction == this) ? null : this;
            }

            if (!hasRequiredSkills && Character.Controlled==picker)
            {
                GUI.AddMessage("Your skills may be insufficient to use the item!", Color.Red, 5.0f);
                if (requiredSkill != null)
                {
                    GUI.AddMessage("("+requiredSkill.Name+" level "+requiredSkill.Level+" required)", Color.Red, 5.0f);
                }
            }

            if (container!=null) container.RemoveContained(this);

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

                    ic.PlaySound(ActionType.OnUse, Position);

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
            if (dropper == Character.Controlled)
                new NetworkEvent(NetworkEventType.DropItem, ID, true);
            
            foreach (ItemComponent ic in components) ic.Drop(dropper);

            if (container != null) container.RemoveContained(this);
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

        private bool EnterProperty(GUITextBox textBox, string text)
        {
            textBox.Color = Color.White;

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


        //private void Init()
        //{

        //}
                    
        public override XElement Save(XDocument doc)
        {
            XElement element = new XElement("Item");

            element.Add(new XAttribute("name", prefab.Name),
                new XAttribute("ID", ID));

            if (prefab.ResizeHorizontal || prefab.ResizeVertical)
            {
                element.Add(new XAttribute("rect", rect.X + "," + rect.Y + "," + rect.Width + "," + rect.Height));
            }
            else
            {
                element.Add(new XAttribute("rect", rect.X + "," + rect.Y));
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

        public static void Load(XElement element)
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
            } else
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

                Item item = new Item(rect, ip);
                item.ID = int.Parse(element.Attribute("ID").Value);
                                
                item.linkedToID = new List<int>();

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
                        item.linkedToID.Add(int.Parse(linkedToIds[i]));
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
        

        public void NewComponentEvent(ItemComponent ic, bool isClient)
        {
            int index = components.IndexOf(ic);

            new NetworkEvent(NetworkEventType.UpdateComponent, ID, isClient, index);
        }

        public override void FillNetworkData(NetworkEventType type, NetOutgoingMessage message, object data)
        {
            message.Write(condition);

            switch (type)
            {
                case NetworkEventType.DropItem:
                    if (body != null) body.FillNetworkData(type, message);
                    break;
                case NetworkEventType.UpdateComponent:
                    message.Write((int)data);
                    components[(int)data].FillNetworkData(type, message);
                    break;
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
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
            Condition = message.ReadFloat();

            switch (type)
            {
                case NetworkEventType.DropItem:
                    if (body != null) body.ReadNetworkData(type, message);
                    Drop(null, false);
                    break;
                case NetworkEventType.UpdateComponent:
                    int componentIndex = message.ReadInt32();
                    if (componentIndex < 0 || componentIndex > components.Count - 1) return;
                    components[componentIndex].ReadNetworkData(type, message);
                    break;
                case NetworkEventType.UpdateProperty:
                    string propertyName = "";

                    try
                    {
                        propertyName = message.ReadString();
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

            //sprite.Remove();
            if (body != null) body.Remove();

            foreach (ItemComponent ic in components)
            {
                ic.Remove();
            }

            itemList.Remove(this);

            foreach (Item it in itemList)
            {
                if (it.linkedTo.Contains(this))
                {
                    it.linkedTo.Remove(this);
                }
            }                        
        }
        
    }
}
