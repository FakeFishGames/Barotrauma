using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    interface IDrawableComponent
    {
#if CLIENT
        void Draw(SpriteBatch spriteBatch, bool editing);
#endif
    }
    
    /// <summary>
    /// The base class for components holding the different functionalities of the item
    /// </summary>
    partial class ItemComponent : IPropertyObject
    {
        protected Item item;

        protected string name;

        private bool isActive;

        protected bool characterUsable;

        protected bool canBePicked;
        protected bool canBeSelected;

        public bool WasUsed;

        public readonly Dictionary<ActionType, List<StatusEffect>> statusEffectLists;
                
        public List<RelatedItem> requiredItems;

        public List<Skill> requiredSkills;

        public ItemComponent Parent;

        protected const float CorrectionDelay = 1.0f;
        protected CoroutineHandle delayedCorrectionCoroutine;
        protected float correctionTimer;

        private string msg;
        
        [HasDefaultValue(0.0f, false)]
        public float PickingTime
        {
            get;
            private set;
        }

        public readonly Dictionary<string, ObjectProperty> properties;
        public Dictionary<string, ObjectProperty> ObjectProperties
        {
            get { return properties; }
        }
                
        public virtual bool IsActive
        {
            get { return isActive; }
            set 
            {
#if CLIENT
                if (!value && isActive)
                {
                    StopSounds(ActionType.OnActive);                    
                }
#endif

                isActive = value; 
            }
        }

        private bool drawable = true;

        public bool Drawable
        {
            get { return drawable; }
            set 
            {
                if (value == drawable) return;
                if (!(this is IDrawableComponent))
                {
                    DebugConsole.ThrowError("Couldn't make \""+this+"\" drawable (the component doesn't implement the IDrawableComponent interface)");
                    return;
                }  
              
                drawable = value;
                if (drawable)
                {
                    if (!item.drawableComponents.Contains((IDrawableComponent)this))
                        item.drawableComponents.Add((IDrawableComponent)this);
                }
                else
                {
                    item.drawableComponents.Remove((IDrawableComponent)this);
                }                
            }
        }

        [HasDefaultValue(false, false)]
        public bool CanBePicked
        {
            get { return canBePicked; }
            set { canBePicked = value; }
        }

        [HasDefaultValue(false, false)]
        public bool DrawHudWhenEquipped
        {
            get;
            private set;
        }

        [HasDefaultValue(false, false)]
        public bool CanBeSelected
        {
            get { return canBeSelected; }
            set { canBeSelected = value; }
        }

        public InputType PickKey
        {
            get;
            protected set;
        }

        public InputType SelectKey
        {
            get;
            protected set;
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

        [HasDefaultValue("", false)]
        public string Msg
        {
            get { return msg; }
            set { msg = value; }
        }
        
        public ItemComponent(Item item, XElement element) 
        {
            this.item = item;

            properties = ObjectProperty.GetProperties(this);

            //canBePicked = ToolBox.GetAttributeBool(element, "canbepicked", false);
            //canBeSelected = ToolBox.GetAttributeBool(element, "canbeselected", false);
            
            //msg = ToolBox.GetAttributeString(element, "msg", "");
            
            requiredItems = new List<RelatedItem>();

            requiredSkills = new List<Skill>();

#if CLIENT
            sounds = new Dictionary<ActionType, List<ItemSound>>();
#endif

            SelectKey = InputType.Select;

            try
            {
                string selectKeyStr = ToolBox.GetAttributeString(element, "selectkey", "Select");
                selectKeyStr = ToolBox.ConvertInputType(selectKeyStr);
                SelectKey = (InputType)Enum.Parse(typeof(InputType), selectKeyStr, true);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Invalid select key in " + element + "!", e);
            }

            PickKey = InputType.Select;

            try
            {
                string pickKeyStr = ToolBox.GetAttributeString(element, "selectkey", "Select");
                pickKeyStr = ToolBox.ConvertInputType(pickKeyStr);
                PickKey = (InputType)Enum.Parse(typeof(InputType),pickKeyStr, true);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Invalid pick key in " + element + "!", e);
            }
            
            properties = ObjectProperty.InitProperties(this, element);
            
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
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
                        var statusEffect = StatusEffect.Load(subElement);

                        if (statusEffectLists == null) statusEffectLists = new Dictionary<ActionType, List<StatusEffect>>();

                        List<StatusEffect> effectList;
                        if (!statusEffectLists.TryGetValue(statusEffect.type, out effectList))
                        {
                            effectList = new List<StatusEffect>();
                            statusEffectLists.Add(statusEffect.type, effectList);
                        }

                        effectList.Add(statusEffect);

                        break;
                    default:
                        if (LoadElemProjSpecific(subElement)) break;
                        ItemComponent ic = Load(subElement, item, item.ConfigFile, false);                        
                        if (ic == null) break;

                        ic.Parent = this;
                        item.components.Add(ic);
                        break;
                }
                
            }        
        }

        public virtual void Move(Vector2 amount) { }
        
        /// <summary>a Character has picked the item</summary>
        public virtual bool Pick(Character picker) 
        {
            return false;
        }

        public virtual bool Select(Character character)
        {
            return CanBeSelected;
        }
        
        /// <summary>a Character has dropped the item</summary>
        public virtual void Drop(Character dropper)  { }

        /// <returns>true if the operation was completed</returns>
        public virtual bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective) 
        {
            return false;
        }

        //called when isActive is true and condition > 0.0f
        public virtual void Update(float deltaTime, Camera cam) { }

        //called when isActive is true and condition == 0.0f
        public virtual void UpdateBroken(float deltaTime, Camera cam) 
        {
#if CLIENT
            StopSounds(ActionType.OnActive);
#endif
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
        
        public virtual void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f) 
        {
        
            switch (connection.Name)
            {
                case "activate":
                case "use":
                    item.Use(1.0f);
                    break;
                case "toggle":
                    IsActive = !isActive;
                    break;
                case "set_active":
                case "set_state":
                    IsActive = signal != "0";
                    break;
            }
        }

        public virtual bool Combine(Item item) 
        {
            return false;
        }

        public void Remove()
        {
#if CLIENT
            if (loopingSound != null)
            {
                Sounds.SoundManager.Stop(loopingSoundIndex);
            }
#endif

            if (delayedCorrectionCoroutine != null)
            {
                CoroutineManager.StopCoroutines(delayedCorrectionCoroutine);
                delayedCorrectionCoroutine = null;
            }

            RemoveComponentSpecific();
        }

        /// <summary>
        /// Remove the component so that it doesn't appear to exist in the game world (stop sounds, remove bodies etc)
        /// but don't reset anything that's required for cloning the item
        /// </summary>
        public void ShallowRemove()
        {
#if CLIENT
            if (loopingSound != null)
            {
                Sounds.SoundManager.Stop(loopingSoundIndex);
            }
#endif

            ShallowRemoveComponentSpecific();
        }

        protected virtual void ShallowRemoveComponentSpecific()
        {
            RemoveComponentSpecific();
        }

        protected virtual void RemoveComponentSpecific() 
        { }

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
        /// Returns 0.0f-1.0f based on how well the Character can use the itemcomponent
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

        public virtual void FlipX() { }

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
#if CLIENT
                    if (addMessage && !string.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
#endif
                    return false;
                }
            }

            return true;
        }

        public bool HasRequiredItems(Character character, bool addMessage)
        {
            if (!requiredItems.Any()) return true;
            if (character.Inventory == null) return false;
                       
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
                    if (character.Inventory.Items.FirstOrDefault(x => x != null && x.Condition > 0.0f && ri.MatchesItem(x)) != null) hasItem = true;
                }
                if (!hasItem)
                {
#if CLIENT
                    if (addMessage && !string.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
#endif
                    return false;
                }
            }

            return true;
        }
        
        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null)
        {
            if (statusEffectLists == null) return;

            List<StatusEffect> statusEffects;
            if (!statusEffectLists.TryGetValue(type, out statusEffects)) return;

            foreach (StatusEffect effect in statusEffects)
            {
                item.ApplyStatusEffect(effect, type, deltaTime, character);
            }
        }

        public void ApplyStatusEffects(ActionType type, List<IPropertyObject> targets, float deltaTime)
        {
            if (statusEffectLists == null) return;

            List<StatusEffect> statusEffects;
            if (!statusEffectLists.TryGetValue(type, out statusEffects)) return;

            foreach (StatusEffect effect in statusEffects)
            {
                effect.Apply(type, deltaTime, item, targets);
            }
        }

        public virtual void Load(XElement componentElement)
        {
            if (componentElement == null) return;            

            foreach (XAttribute attribute in componentElement.Attributes())
            {
                ObjectProperty property = null;
                if (!properties.TryGetValue(attribute.Name.ToString().ToLowerInvariant(), out property)) continue;
                
                property.TrySetValue(attribute.Value);
            }

            List<RelatedItem> prevRequiredItems = new List<RelatedItem>(requiredItems);
            requiredItems.Clear();

            foreach (XElement subElement in componentElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
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
            string type = element.Name.ToString().ToLowerInvariant();
            try
            {
                // Get the type of a specified class.                
                t = Type.GetType("Barotrauma.Items.Components." + type + "", false, true);
                if (t == null)
                {
                    if (errorMessages) DebugConsole.ThrowError("Could not find the component \"" + type + "\" (" + file + ")");
                    return null;
                }
            }
            catch (Exception e)
            {
                if (errorMessages) DebugConsole.ThrowError("Could not find the component \"" + type + "\" (" + file + ")", e);
                return null;
            }

            ConstructorInfo constructor;
            try
            {
                if (t != typeof(ItemComponent) && !t.IsSubclassOf(typeof(ItemComponent))) return null;
                constructor = t.GetConstructor(new Type[] { typeof(Item), typeof(XElement) });
                if (constructor == null)
                {
                    DebugConsole.ThrowError("Could not find the constructor of the component \"" + type + "\" (" + file + ")");
                    return null;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Could not find the constructor of the component \"" + type + "\" (" + file + ")", e);
                return null;
            }

            object[] lobject = new object[] { item, element };
            object component = constructor.Invoke(lobject);

            ItemComponent ic = (ItemComponent)component;
            ic.name = element.Name.ToString();

            return ic;
        }
        
    }
}
