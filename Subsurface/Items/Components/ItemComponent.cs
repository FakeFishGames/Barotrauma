using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using Subsurface.Items.Components;
using System.IO;
using System.Globalization;

namespace Subsurface
{
    class ItemSound
    {
        public readonly Sound sound;
        public readonly ActionType type;

        public string volumeProperty;

        public readonly float range;
        
        public ItemSound(Sound sound, ActionType type, float range)
        {
            this.sound = sound;
            this.type = type;
            this.range = range;
        }
    }

    /// <summary>
    /// The base class for components holding the different functionalities of the item
    /// </summary>
    class ItemComponent : IPropertyObject
    {
        protected Item item;

        protected string name;

        protected bool isActive;

        protected bool characterUsable;

        protected bool canBePicked;
        protected bool canBeSelected;

        public List<StatusEffect> statusEffects;
        
        protected bool updated;
        
        public List<RelatedItem> requiredItems;

        private List<ItemSound> sounds;

        public readonly Dictionary<string, ObjectProperty> properties;
        public Dictionary<string, ObjectProperty> ObjectProperties
        {
            get { return properties; }
        }
        //has the component already been updated this frame
        public bool Updated
        {
            get { return updated; }
            set { updated = value; }
        }
        
        public virtual bool IsActive
        {
            get { return isActive; }
            set { isActive = value; }
        }

        [HasDefaultValue(false, false)]
        public bool CanBePicked
        {
            get { return canBePicked; }
            set { canBePicked = value; }
        }

        [HasDefaultValue(false, false)]
        public bool CanBeSelected
        {
            get { return canBeSelected; }
            set { canBeSelected = value; }
        }

        public Item Item
        {
            get { return item; }
        }

        public string Name
        {
            get { return name; }
        }

        [HasDefaultValue("", false)]
        public string Msg
        {
            get { return msg; }
            set { msg = value; }
        }

        private string msg;

        public ItemComponent(Item item, XElement element) 
        {
            this.item = item;

            properties = ObjectProperty.GetProperties(this);

            //canBePicked = ToolBox.GetAttributeBool(element, "canbepicked", false);
            //canBeSelected = ToolBox.GetAttributeBool(element, "canbeselected", false);
            
            //msg = ToolBox.GetAttributeString(element, "msg", "");
            
            requiredItems = new List<RelatedItem>();

            sounds = new List<ItemSound>();

            statusEffects = new List<StatusEffect>();

            //var initableProperties = ObjectProperty.GetProperties<Initable>(this);
            //foreach (ObjectProperty initableProperty in initableProperties)
            //{
            //    object value = ToolBox.GetAttributeObject(element, initableProperty.Name.ToLower());
            //    if (value==null)
            //    {
            //        foreach (var ini in initableProperty.Attributes.OfType<Initable>())
            //        {
            //            value = ini.defaultValue;
            //            break;
            //        }
            //    }

            //    initableProperty.TrySetValue(value);
            //}


            properties = ObjectProperty.InitProperties(this, element);
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "requireditem":
                        RelatedItem ri = RelatedItem.Load(subElement);
                        if (ri != null) requiredItems.Add(ri);
                        break;
                    case "statuseffect":
                        statusEffects.Add(StatusEffect.Load(subElement));
                        break;
                    case "sound":
                        string filePath = ToolBox.GetAttributeString(subElement, "file", "");
                        if (filePath=="") continue;
                        if (!filePath.Contains("\\")) filePath = Path.GetDirectoryName(item.Prefab.ConfigFile)+"\\"+filePath;

                        //int index = item.Prefab.sounds.FindIndex(x => x.FilePath == filePath);


                        ActionType type;

                        try
                        {
                            type = (ActionType)Enum.Parse(typeof(ActionType), ToolBox.GetAttributeString(subElement, "type", ""), true);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Invalid sound type in "+subElement+"!", e);
                            break;
                        }

                        Sound sound = Sound.Load(filePath);

                        float range = ToolBox.GetAttributeFloat(subElement, "range", 800.0f);
                        ItemSound itemSound = new ItemSound(sound, type, range);
                        itemSound.volumeProperty = ToolBox.GetAttributeString(subElement, "volume", "");
                        sounds.Add(itemSound);
                        break;
                }
                
            }        
        }

        private ItemSound loopingSound;
        private int loopingSoundIndex;
        public void PlaySound(ActionType type, float volume, Vector2 position, bool loop=false)
        {
            ItemSound itemSound = null;
            if (!loop || !Sounds.SoundManager.IsPlaying(loopingSoundIndex))
            {
                List<ItemSound> matchingSounds = sounds.FindAll(x => x.type == type);
                if (matchingSounds.Count == 0) return;

                int index = Game1.localRandom.Next(matchingSounds.Count);
                itemSound = sounds[index];

                if (itemSound.volumeProperty != "")
                {
                    ObjectProperty op = null;
                    if (properties.TryGetValue(itemSound.volumeProperty.ToLower(), out op))
                    {
                        float newVolume = 0.0f;
                        float.TryParse(op.GetValue().ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out newVolume);
                        volume = MathHelper.Clamp(newVolume, 0.0f, 1.0f);
                    }

                }

                if (loop) loopingSound = itemSound;
            }
                        

            if (loop)
            {
                //if (loopingSound != null && loopingSound.volumeProperty != "") volume = float.Parse(properties[loopingSound.volumeProperty].GetValue().ToString());
                loopingSoundIndex = loopingSound.sound.Loop(loopingSoundIndex, volume, position, loopingSound.range);
            }
            else
            {
                
                itemSound.sound.Play(volume, itemSound.range, position);  
            } 
        }

        public virtual void Move(Vector2 amount) { }
        
        /// <summary>a character has picked the item</summary>
        public virtual bool Pick(Character picker) 
        {
            return false;
        }
        
        /// <summary>a character has dropped the item</summary>
        public virtual void Drop(Character dropper)  { }

        public virtual void Draw(SpriteBatch spriteBatch) { }

        public virtual void DrawHUD(SpriteBatch spriteBatch, Character character) { }

        /// <summary>
        /// a construction has activated the item (such as a turret shooting a projectile)
        /// call the Activate-methods of the components</summary>
        /// <param name="c"> The construction which activated the item</param>
        /// <param name="modifier"> A vector that can be used to pass additional information to the components</param>
        public virtual void ItemActivate(Item item, Vector2 modifier) { }

        //called when isActive is true and condition > 0.0f
        public virtual void Update(float deltaTime, Camera cam) { }

        //called when isActive is true and condition == 0.0f
        public virtual void UpdateBroken(float deltaTime, Camera cam) 
        {
            if (loopingSoundIndex <= 0) return;
            
            if (Sounds.SoundManager.IsPlaying(loopingSoundIndex))
            {
                Sounds.SoundManager.Stop(loopingSoundIndex);
            }
            
        }

        //called when the item is equipped and left mouse button is pressed
        //returns true if the item was used succesfully (not out of ammo, reloading, etc)
        public virtual bool Use(Character character = null) 
        {
            return false;
        }

        //called when the item is equipped and right mouse button is pressed
        public virtual void SecondaryUse(Character character = null) { }  

        //called when the item is placed in a "limbslot"
        public virtual void Equip(Character character) { }

        //called then the item is dropped or dragged out of a "limbslot"
        public virtual void Unequip(Character character) { }

        public virtual bool UseOtherItem(Item item)
        {
            return false;
        }

        public virtual void ReceiveSignal(string signal, Connection connection, Item sender) { }

        public virtual bool Combine(Item item) 
        {
            return false;
        }

        public virtual void Remove() { }

        public bool HasRequiredContainedItems(bool addMessage)
        {
            if (!requiredItems.Any()) return true;

            Item[] containedItems = item.ContainedItems;
            if (containedItems == null || !containedItems.Any()) return false;

            foreach (RelatedItem ri in requiredItems)
            {
                if (ri.Type != RelatedItem.RelationType.Contained) continue;
                Item containedItem = Array.Find(containedItems, x => x != null && x.Condition > 0.0f && ri.MatchesItem(x));
                if (containedItem == null)
                {
                    //if (addMessage && !String.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
                    return false;
                }
            }

            return true;
        }

        public bool HasRequiredEquippedItems(Character character, bool addMessage)
        {
            if (!requiredItems.Any()) return true;

            foreach (RelatedItem ri in requiredItems)
            {
                if (ri.Type == RelatedItem.RelationType.Equipped)
                {
                    for (int i = 0; i < character.SelectedItems.Length; i++ )
                    {
                        Item selectedItem = character.SelectedItems[i];
                        if (selectedItem !=null && selectedItem.Condition>0.0f && ri.MatchesItem(selectedItem ))
                        {
                            return true;
                        }
                    }

                    //if (addMessage && !String.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
                    return false;
                }
                else if (ri.Type == RelatedItem.RelationType.Picked)
                {
                    Item pickedItem = character.Inventory.items.FirstOrDefault(x => x!=null && x.Condition>0.0f && ri.MatchesItem(x));
                    if (pickedItem == null)
                    {
                        //if (addMessage && !String.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
                        return false;
                    }
                }
            }

            return true;
        }

        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null, Limb limb = null)
        {
            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.type != type) continue;
                item.ApplyStatusEffect(effect, type, deltaTime, character, limb);
            }
        }

        public virtual XElement Save(XElement parentElement)
        {
            XElement componentElement = new XElement(name);

            foreach (RelatedItem ri in requiredItems)
            {
                XElement newElement = new XElement("requireditem");
                ri.Save(newElement);
                componentElement.Add(newElement);
            }

            ObjectProperty.SaveProperties(this, componentElement);

            //var saveProperties = ObjectProperty.GetProperties<Saveable>(this);
            //foreach (var property in saveProperties)
            //{
            //    object value = property.GetValue();
            //    if (value == null) continue;

            //    bool dontSave = false;
            //    foreach (var ini in property.Attributes.OfType<Initable>())
            //    {
            //        if (ini.defaultValue != value) continue;
                    
            //        dontSave = true;
            //        break;                    
            //    }

            //    if (dontSave) continue;

            //    componentElement.Add(new XAttribute(property.Name.ToLower(), value));
            //}

            parentElement.Add(componentElement);
            return componentElement;
        }

        public virtual void Load(XElement componentElement)
        {
            if (componentElement == null) return;

            bool requiredItemsCleared = false;

            foreach (XAttribute attribute in componentElement.Attributes())
            {
                ObjectProperty property = null;
                if (!properties.TryGetValue(attribute.Name.ToString().ToLower(), out property)) continue;
                
                property.TrySetValue(attribute.Value);
            }

            foreach (XElement subElement in componentElement.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "requireditem":
                        RelatedItem newRequiredItem = RelatedItem.Load(subElement);
                        
                        if (newRequiredItem == null) continue;

                        if (!requiredItemsCleared)
                        {
                            requiredItems.Clear();
                            requiredItemsCleared = true;
                        }

                        requiredItems.Add(newRequiredItem);
                        break;
                }
            }
        }

        public virtual void OnMapLoaded() { }
        
        public static ItemComponent Load(XElement element, Item item, string file)
        {
            Type t;
            string type = element.Name.ToString().ToLower();
            try
            {
                // Get the type of a specified class.
                t = Type.GetType("Subsurface.Items.Components." + type + ", Subsurface", true, true);
                if (t == null)
                {
                    DebugConsole.ThrowError("Could not find the component ''" + type + "'' (" + file + ")");
                    return null;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not find the component ''" + type + "'' (" + file + ")", e);
                return null;
            }

            ConstructorInfo constructor;
            try
            {
                if (!t.IsSubclassOf(typeof(ItemComponent))) return null;
                constructor = t.GetConstructor(new Type[] { typeof(Item), typeof(XElement) });
                if (constructor == null)
                {
                    DebugConsole.ThrowError("Could not find the constructor of the component ''" + type + "'' (" + file + ")");
                    return null;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not find the constructor of the component ''" + type + "'' (" + file + ")", e);
                return null;
            }

            object[] lobject = new object[] { item, element };
            object component = constructor.Invoke(lobject);

            ItemComponent ic = (ItemComponent)component;
            ic.name = element.Name.ToString();

            return ic;
        }

        public virtual void FillNetworkData(NetworkEventType type, NetOutgoingMessage message)
        {
        }

        public virtual void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
        }
    }
}
