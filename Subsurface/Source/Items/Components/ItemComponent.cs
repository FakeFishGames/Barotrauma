using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using System.IO;
using System.Globalization;

namespace Subsurface.Items.Components
{
    class ItemSound
    {
        public readonly Sound Sound;
        public readonly ActionType Type;

        public string VolumeProperty;

        public float VolumeMultiplier;

        public readonly float Range;

        public readonly bool Loop;
        
        public ItemSound(Sound sound, ActionType type, float range, bool loop = true)
        {
            this.Sound = sound;
            this.Type = type;
            this.Range = range;

            this.Loop = loop;
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

        public bool WasUsed;

        public List<StatusEffect> statusEffects;
        
        protected bool updated;
        
        public List<RelatedItem> requiredItems;

        public List<Skill> requiredSkills;

        private List<ItemSound> sounds;

        private GUIFrame guiFrame;

        public ItemComponent Parent;

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
            set 
            {
                if (!value && isActive)
                {
                    StopSounds(ActionType.OnActive);
                    StopSounds(ActionType.OnUse);
                }

                isActive = value; 
            }
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

        [HasDefaultValue(false, false)]
        public bool DeleteOnUse
        {
            get;
            set;
        }

        public Item Item
        {
            get { return item; }
        }

        public string Name
        {
            get { return name; }
        }

        protected GUIFrame GuiFrame
        {
            get 
            { 
                if (guiFrame==null)
                {
                    DebugConsole.ThrowError("Error: the component "+name+" in "+item.Name+" doesn't have a guiFrame");
                    guiFrame = new GUIFrame(new Rectangle(0, 0, 100, 100), Color.Black);
                }
                return guiFrame; 
            }
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

            requiredSkills = new List<Skill>();

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
                    case "requireditems":
                        RelatedItem ri = RelatedItem.Load(subElement);
                        if (ri != null) requiredItems.Add(ri);
                        break;

                    case "requiredskill":
                    case "requiredskills":
                        string skillName = ToolBox.GetAttributeString(subElement, "name", "");
                        requiredSkills.Add(new Skill(skillName, ToolBox.GetAttributeInt(subElement, "level", 0)));
                        break;
                    case "statuseffect":
                        statusEffects.Add(StatusEffect.Load(subElement));
                        break;
                    case "guiframe":
                        Vector4 rect = ToolBox.GetAttributeVector4(subElement, "rect", Vector4.One);
                        rect.X *= GameMain.GraphicsWidth;
                        rect.Y *= GameMain.GraphicsHeight;
                        rect.Z *= GameMain.GraphicsWidth;
                        rect.W *= GameMain.GraphicsHeight;

                        Vector4 color = ToolBox.GetAttributeVector4(subElement, "color", Vector4.One);

                        Alignment alignment = Alignment.Center;
                        try
                        {
                            alignment = (Alignment)Enum.Parse(typeof(Alignment),
                                ToolBox.GetAttributeString(subElement, "alignment", "Center"), true);
                        }
                        catch
                        {
                            DebugConsole.ThrowError("Error in " + element + "! ''" + element.Attribute("type").Value + "'' is not a valid alignment");
                        }

                        guiFrame = new GUIFrame(
                            new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Z, (int)rect.W), 
                            new Color(color.X, color.Y, color.Z, color.W), alignment, GUI.Style);
                        //guiFrame.Alpha = color.W;

                        break;
                    case "sound":
                        string filePath = ToolBox.GetAttributeString(subElement, "file", "");
                        if (filePath=="") continue;
                        if (!filePath.Contains("/") && !filePath.Contains("\\") && !filePath.Contains(Path.DirectorySeparatorChar))
                        {
                            filePath = Path.Combine(Path.GetDirectoryName(item.Prefab.ConfigFile), filePath);
                        }

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
                        itemSound.VolumeProperty = ToolBox.GetAttributeString(subElement, "volume", "");
                        itemSound.VolumeMultiplier = ToolBox.GetAttributeFloat(subElement, "volumemultiplier", 1.0f);
                        sounds.Add(itemSound);
                        break;
                    default:
                        ItemComponent ic = ItemComponent.Load(subElement, item, item.ConfigFile, false);                        
                        if (ic == null) break;

                        ic.Parent = this;
                        item.components.Add(ic);
                        break;
                }
                
            }        
        }

        private ItemSound loopingSound;
        private int loopingSoundIndex;
        public void PlaySound(ActionType type, Vector2 position)
        {
            ItemSound itemSound = null;
            if (!Sounds.SoundManager.IsPlaying(loopingSoundIndex))
            {
                List<ItemSound> matchingSounds = sounds.FindAll(x => x.Type == type);
                if (matchingSounds.Count == 0) return;

                int index = Rand.Int(matchingSounds.Count);
                itemSound = matchingSounds[index];
            }


            if (loopingSound!=null)
            {
                loopingSoundIndex = loopingSound.Sound.Loop(loopingSoundIndex, GetSoundVolume(loopingSound), position, loopingSound.Range);
            }
            else if (itemSound!=null)
            {
                if (itemSound.Loop)
                {
                    loopingSound = itemSound;
                }
                else
                {
                    itemSound.Sound.Play(GetSoundVolume(itemSound), itemSound.Range, position); 
                } 
            } 
        }

        public void StopSounds(ActionType type)
        {
            if (loopingSoundIndex <= 0) return;

            if (loopingSound == null) return;

            if (loopingSound.Type != type) return;

            if (Sounds.SoundManager.IsPlaying(loopingSoundIndex))
            {
                Sounds.SoundManager.Stop(loopingSoundIndex);
                loopingSound = null;
                loopingSoundIndex = -1;
            }
        }

        private float GetSoundVolume(ItemSound sound)
        {
            if (sound.VolumeProperty == "") return 1.0f;
            
            ObjectProperty op = null;
            if (properties.TryGetValue(sound.VolumeProperty.ToLower(), out op))
            {
                float newVolume = 0.0f;
                float.TryParse(op.GetValue().ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out newVolume);

                newVolume *= sound.VolumeMultiplier;

                return MathHelper.Clamp(newVolume, 0.0f, 1.0f);
            }

            return 0.0f;            
        }

        public virtual void Move(Vector2 amount) { }
        
        /// <summary>a character has picked the item</summary>
        public virtual bool Pick(Character picker) 
        {
            return false;
        }

        public virtual bool Select(Character character)
        {
            return CanBeSelected;
        }
        
        /// <summary>a character has dropped the item</summary>
        public virtual void Drop(Character dropper)  { }

        public virtual void Draw(SpriteBatch spriteBatch, bool editing = false) { }

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
            StopSounds(ActionType.OnActive);          
        }

        //called when the item is equipped and left mouse button is pressed
        //returns true if the item was used succesfully (not out of ammo, reloading, etc)
        public virtual bool Use(float deltaTime, Character character = null) 
        {
            return false;
        }

        //called when the item is equipped and right mouse button is pressed
        public virtual void SecondaryUse(float deltaTime, Character character = null) { }  

        //called when the item is placed in a "limbslot"
        public virtual void Equip(Character character) { }

        //called then the item is dropped or dragged out of a "limbslot"
        public virtual void Unequip(Character character) { }

        public virtual bool UseOtherItem(Item item)
        {
            return false;
        }

        public virtual void ReceiveSignal(string signal, Connection connection, Item sender, float power = 0.0f) 
        {
        
            switch (connection.Name)
            {
                case "activate":
                case "use":
                    item.Use(1.0f);
                    break;
            }
        }

        public virtual bool Combine(Item item) 
        {
            return false;
        }

        public virtual void Remove() 
        {
            if (loopingSound!=null)
            {
                Sounds.SoundManager.Stop(loopingSoundIndex);
            }
        }

        public bool HasRequiredSkills(Character character)
        {
            Skill temp;
            return HasRequiredSkills(character, out temp);
        }

        public bool HasRequiredSkills(Character character, out Skill insufficientSkill)
        {
            foreach (Skill skill in requiredSkills)
            {
                int characterLevel = character.GetSkillLevel(skill.Name);
                if (characterLevel < skill.Level)
                {
                    insufficientSkill = skill;
                    return false;
                }
            }
            insufficientSkill = null;
            return true;
        }

        /// <summary>
        /// Returns 0.0f-1.0f based on how well the character can use the itemcomponent
        /// </summary>
        /// <returns>0.5f if all the skills meet the skill requirements exactly, 1.0f if they're way above and 0.0f if way less</returns>
        protected float DegreeOfSuccess(Character character)
        {
            if (requiredSkills.Count == 0) return 100.0f;

            float[] skillSuccess = new float[requiredSkills.Count];

            for (int i = 0; i < requiredSkills.Count; i++ )
            {
                int characterLevel = character.GetSkillLevel(requiredSkills[i].Name);

                skillSuccess[i] = (characterLevel - requiredSkills[i].Level);
            }

            float average = skillSuccess.Average();

            return (average+100.0f)/2.0f;        
        }

        //public bool CheckFailure(Character character)
        //{
        //    foreach (Skill skill in requiredSkills)
        //    {
        //        int characterLevel = character.GetSkillLevel(skill.Name);
        //        if (characterLevel > skill.Level) continue;

        //        item.ApplyStatusEffects(ActionType.OnFailure, 1.0f, character);
        //        //Item.ApplyStatusEffects();
        //        return true;
                
        //    }

        //    return false;
        //}

        public bool HasRequiredContainedItems(bool addMessage)
        {
            List<RelatedItem> requiredContained = requiredItems.FindAll(ri=> ri.Type == RelatedItem.RelationType.Contained);

            if (!requiredContained.Any()) return true;

            Item[] containedItems = item.ContainedItems;
            if (containedItems == null || !containedItems.Any()) return false;

            foreach (RelatedItem ri in requiredContained)
            {
                Item containedItem = Array.Find(containedItems, x => x != null && x.Condition > 0.0f && ri.MatchesItem(x));
                if (containedItem == null)
                {
                    if (addMessage && !string.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
                    return false;
                }
            }

            return true;
        }

        public bool HasRequiredItems(Character character, bool addMessage)
        {
            if (!requiredItems.Any()) return true;
                       
            foreach (RelatedItem ri in requiredItems)
            {
                if (!ri.Type.HasFlag(RelatedItem.RelationType.Equipped) && !ri.Type.HasFlag(RelatedItem.RelationType.Picked)) continue;

                bool hasItem = false;
                if (ri.Type.HasFlag(RelatedItem.RelationType.Equipped))
                {
                    if (character.SelectedItems.FirstOrDefault(it => it != null && it.Condition > 0.0f && ri.MatchesItem(it)) != null) hasItem = true;
                }
                if (!hasItem && ri.Type.HasFlag(RelatedItem.RelationType.Picked))
                {
                    if (character.Inventory.items.FirstOrDefault(x => x!=null && x.Condition>0.0f && ri.MatchesItem(x))!=null) hasItem = true;
                }
                if (!hasItem)
                {
                    if (addMessage && !string.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
                    return false;
                }
            }

            return true;
        }
        
        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null)
        {
            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.type != type) continue;
                item.ApplyStatusEffect(effect, type, deltaTime, character);
            }
        }

        public void ApplyStatusEffects(ActionType type, float deltaTime, IPropertyObject target)
        {
            foreach (StatusEffect effect in statusEffects)
            {
                if (effect.type != type) continue;
                effect.Apply(type, deltaTime, item, target);
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

            foreach (XAttribute attribute in componentElement.Attributes())
            {
                ObjectProperty property = null;
                if (!properties.TryGetValue(attribute.Name.ToString().ToLower(), out property)) continue;
                
                property.TrySetValue(attribute.Value);
            }

            List<RelatedItem> prevRequiredItems = new List<RelatedItem>(requiredItems);
            requiredItems.Clear();

            foreach (XElement subElement in componentElement.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "requireditem":
                        RelatedItem newRequiredItem = RelatedItem.Load(subElement);
                        
                        if (newRequiredItem == null) continue;

                        var prevRequiredItem = prevRequiredItems.Find(ri => ri.JoinedNames == newRequiredItem.JoinedNames);
                        if (prevRequiredItem!=null)
                        {
                            newRequiredItem.statusEffects = prevRequiredItem.statusEffects;
                            newRequiredItem.Msg = prevRequiredItem.Msg;
                        }

                        requiredItems.Add(newRequiredItem);
                        break;
                }
            }
        }

        public virtual void OnMapLoaded() { }
        
        public static ItemComponent Load(XElement element, Item item, string file, bool errorMessages = true)
        {
            Type t;
            string type = element.Name.ToString().ToLower();
            try
            {
                // Get the type of a specified class.                
                t = Type.GetType("Subsurface.Items.Components." + type + "", false, true);
                if (t == null)
                {
                    if (errorMessages) DebugConsole.ThrowError("Could not find the component ''" + type + "'' (" + file + ")");
                    return null;
                }
            }
            catch (Exception e)
            {
                if (errorMessages) DebugConsole.ThrowError("Could not find the component ''" + type + "'' (" + file + ")", e);
                return null;
            }

            ConstructorInfo constructor;
            try
            {
                if (t != typeof(ItemComponent) && !t.IsSubclassOf(typeof(ItemComponent))) return null;
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
