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

namespace Subsurface
{

    public enum ActionType
    {
        OnPicked, OnWearing, OnContaining, OnContained, OnActive, OnUse
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

        public PhysicsBody body;
        
        private float condition;

        //the inventory in which the item is contained in
        public Inventory inventory;

        public Item container;
        
        public override string Name
        {
            get { return prefab.Name; }
        }

        public override Sprite sprite
        {
            get { return prefab.sprite; }
        }


        public float Condition
        {
            get { return condition; }
            set { condition = MathHelper.Clamp(value, 0.0f, 100.0f); }
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
                return (c == null) ? null : c.inventory.items;
            }
        }

        public override bool IsLinkable
        {
            get { return prefab.IsLinkable; }
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

        List<string> highlightText;

        public List<string> HighlightText
        {
            get { return highlightText;}
            
        }

        public Item(ItemPrefab itemPrefab, Vector2 position)
            : this(new Rectangle((int)position.X, (int)position.Y, (int)itemPrefab.sprite.size.X, (int)itemPrefab.sprite.size.Y), itemPrefab)
        {

        }

        public Item(Rectangle newRect, ItemPrefab itemPrefab)
        {
            prefab = itemPrefab;

            linkedTo = new ObservableCollection<MapEntity>();
            components = new List<ItemComponent>();

            tags = new List<string>();
                       
            rect = newRect;
            //rect.X -= rect.Width / 2;
            //rect.Y += rect.Height / 2;

            //dir = 1.0f;

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

            //foreach (XAttribute attribute in element.Attributes())
            //{
            //    ObjectProperty property = null;
            //    if (!properties.TryGetValue(attribute.Name.ToString().ToLower(), out property)) continue;
            //    if (property.Attributes.OfType<Initable>().Count() == 0) continue;
            //    property.TrySetValue(attribute.Value);
            //}



            highlightText = new List<string>();

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
                    default:
                        ItemComponent ic = ItemComponent.Load(subElement, this, prefab.ConfigFile);
                        if (ic == null) break;

                        components.Add(ic);
                        if (!string.IsNullOrWhiteSpace(ic.Msg)) highlightText.Add(ic.Msg);

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

            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.Position);

            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);
        }

        public override void Move(Vector2 amount)
        {
            base.Move(amount);

            if (itemList != null && body!=null)
            {
                amount = ConvertUnits.ToSimUnits(amount);
                //Vector2 pos = new Vector2(rect.X + rect.Width / 2.0f, rect.Y - rect.Height / 2.0f);
                body.SetTransform(body.Position+amount, body.Rotation);
            }
            foreach (ItemComponent ic in components)
            {
                ic.Move(amount);
            }

            FindHull();
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
            CurrentHull = Hull.FindHull((body == null) ? Position : ConvertUnits.ToDisplayUnits(body.Position), CurrentHull);
            return CurrentHull;
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
            if (condition == 0.0f) return;

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

                effect.Apply(type, deltaTime, SimPosition, targets);
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
                if (!ic.IsActive) continue;
                if (condition > 0.0f)
                {
                    ic.Update(deltaTime, cam);
                    
                    ic.PlaySound(ActionType.OnActive, 1.0f, Position, true);
                    ic.ApplyStatusEffects(ActionType.OnActive, deltaTime, null);
                }
                else
                {
                    ic.UpdateBroken(deltaTime, cam);
                }
            }
            
            
            if (body == null) return;

            if (body.LinearVelocity.Length()>0.001f) FindHull();


            Vector2 displayPos = ConvertUnits.ToDisplayUnits(body.Position);

            rect.X = (int)(displayPos.X - rect.Width / 2.0f);
            rect.Y = (int)(displayPos.Y + rect.Height / 2.0f);

            body.SetToTargetPosition();

            if (CurrentHull != null)
            {
                float surfaceY = ConvertUnits.ToSimUnits(CurrentHull.Surface);
                if (surfaceY > body.Position.Y) return;

                //the item has gone through the surface of the water -> apply an impulse which serves as surface tension
                if ((body.Position.Y - (body.LinearVelocity.Y / 60.0f)) < surfaceY)
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
                    if (body.Position.Length() > 1000.0f)
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
            Color color = (isSelected && editing) ? color = Color.Red : Color.White;
            if (isHighlighted) color = Color.Orange;
            
            if (body==null)
            {
                prefab.sprite.DrawTiled(spriteBatch, new Vector2(rect.X, -rect.Y), new Vector2(rect.Width, rect.Height), color);
            }
            else if (body.Enabled)
            {
                body.Draw(spriteBatch, prefab.sprite, color);                
            }

            foreach (ItemComponent component in components) component.Draw(spriteBatch);
            
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
            int x = Game1.GraphicsWidth/2-width/2, y = 10;

            List<ObjectProperty> editableProperties = inGame ? GetProperties<InGameEditable>() : GetProperties<Editable>();
            
            int requiredItemCount = 0;
            if (!inGame)
            {
                foreach (ItemComponent ic in components)
                {
                    requiredItemCount += ic.requiredItems.Count;                    
                }
            }
             
            editingHUD = new GUIFrame(new Rectangle(x, y, width, 110 + (editableProperties.Count()+requiredItemCount) * 30), Color.Black * 0.5f);
            editingHUD.Padding = new Vector4(10, 10, 0, 0);
            editingHUD.UserData = this;
            
            new GUITextBlock(new Rectangle(0, 0, 100, 20), prefab.Name, GUI.style, editingHUD);

            y += 20;

            if (!inGame)
            {
                if (prefab.IsLinkable) 
                {
                    new GUITextBlock(new Rectangle(0, 20, 100, 20), "Hold space to link to another construction", GUI.style, editingHUD);
                    y += 25;
                }
                foreach (ItemComponent ic in components)
                {
                    foreach (RelatedItem relatedItem in ic.requiredItems)
                    {

                        new GUITextBlock(new Rectangle(0, y, 100, 20), ic.Name + ": " + relatedItem.Type.ToString() + " required", GUI.style, editingHUD);
                        GUITextBox namesBox = new GUITextBox(new Rectangle(0, y, 200, 20), Alignment.Right, GUI.style, editingHUD);

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
                GUITextBox propertyBox = new GUITextBox(new Rectangle(100, y, 200, 20), GUI.style, editingHUD);

                object value = objectProperty.GetValue();
                if (value != null)
                {
                    propertyBox.Text = value.ToString();
                }

                propertyBox.UserData = objectProperty;
                propertyBox.OnEnter = EnterProperty;
                propertyBox.OnTextChanged = PropertyChanged;
                y = y + 30;
            }
            return editingHUD;
        }

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (editingHUD==null || editingHUD.UserData as Item != this)
            {
                editingHUD = CreateEditingHUD(true);
            }

            editingHUD.Draw(spriteBatch);

            foreach (ItemComponent ic in components)
            {
                ic.DrawHUD(spriteBatch, character);
            }
        }
        
        public void SendSignal(string signal, string connectionName, float power=0.0f)
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

                dist = Vector2.Distance(pickPosition, item.SimPosition);
                if (closest == null || dist < closestDist)
                {
                    closest = item;
                    closestDist = dist;
                }
            }
            
            return closest;
        }

        public bool Pick(Character picker, bool forcePick=false)
        {
            
            bool picked = false, selected = false;
            foreach (ItemComponent ic in components)
            {
                if ((ic.CanBePicked || ic.CanBeSelected) 
                    && (ic.HasRequiredEquippedItems(picker, picker == Character.Controlled) || forcePick) 
                    && ic.Pick(picker))
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

            if (container!=null) container.RemoveContained(this);

            return true;
        }


        public void Use(float deltaTime, Character character = null)
        {
            if (condition == 0.0f) return;

            foreach (ItemComponent ic in components)
            {
                if (!ic.HasRequiredContainedItems(character == Character.Controlled)) continue;
                if (ic.Use(deltaTime, character))
                {
                    ic.PlaySound(ActionType.OnUse, 1.0f, Position);

                    ic.ApplyStatusEffects(ActionType.OnUse, deltaTime, character);
                }
            }
        }

        public void SecondaryUse(float deltaTime, Character character = null)
        {
            foreach (ItemComponent ic in components)
            {
                if (!ic.HasRequiredContainedItems(character == Character.Controlled)) continue;
                ic.SecondaryUse(deltaTime, character);
            }
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
            
            if (objectProperty.TrySetValue(text))
            {
                textBox.Text = text;
                return true;
            }
            else
            {
                if (prevValue!=null)
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
                new XAttribute("ID", ID),
                new XAttribute("rect", rect.X + "," + rect.Y+","+rect.Width+","+rect.Height));

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

            //var saveProperties = ObjectProperty.GetProperties<Saveable>(this);
            //foreach (var property in saveProperties)
            //{
            //    object value = property.GetValue();
            //    if (value == null) continue;

            //    bool dontSave=false;
            //    foreach (var ini in property.Attributes.OfType<Initable>())
            //    {
            //        if (ini.defaultValue==value)
            //        {
            //            dontSave = true;
            //            break;
            //        }
            //    }

            //    if (dontSave) continue;

            //    element.Add(new XAttribute(property.Name.ToLower(), value));
            //}

            //if (tags.Count>0)
            //{
            //    element.Add(new XAttribute("tags",string.Join(", ",tags)));
            //}
            
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

            Rectangle rect = new Rectangle(
                int.Parse(rectValues[0]),
                int.Parse(rectValues[1]),
                int.Parse(rectValues[2]),
                int.Parse(rectValues[3]));

            string name = element.Attribute("name").Value;
            
            foreach (MapEntityPrefab ep in MapEntityPrefab.list)
            {
                ItemPrefab ip = ep as ItemPrefab;
                if (ip == null) continue;

                if (ip.Name != name) continue;

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
            switch (type)
            {
                case NetworkEventType.DropItem:
                    if (body != null) body.FillNetworkData(type, message);
                    break;
                case NetworkEventType.UpdateComponent:
                    message.Write((int)data);
                    components[(int)data].FillNetworkData(type, message);
                    break;
            }
        }

        public override void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
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
            }
        }

        public override void Remove()
        {
            base.Remove();
            
            //sprite.Remove();
            if (body!=null) body.Remove();

            foreach (ItemComponent ic in components)
            {
                ic.Remove();
            }

            itemList.Remove(this);
        }
        
    }
}
