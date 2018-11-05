using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
#if CLIENT
using Barotrauma.Sounds;
#endif

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
    partial class ItemComponent : ISerializableEntity
    {
        protected Item item;

        protected string name;

        private bool isActive;

        protected bool characterUsable;

        protected bool canBePicked;
        protected bool canBeSelected;
        protected bool canBeCombined;
        protected bool removeOnCombined;

        public bool WasUsed;

        public readonly Dictionary<ActionType, List<StatusEffect>> statusEffectLists;
                
        public Dictionary<RelatedItem.RelationType, List<RelatedItem>> requiredItems;

        public List<Skill> requiredSkills;

        public ItemComponent Parent;

        protected const float CorrectionDelay = 1.0f;
        protected CoroutineHandle delayedCorrectionCoroutine;
        protected float correctionTimer;

        private string msg;
        
        [Editable, Serialize(0.0f, false)]
        public float PickingTime
        {
            get;
            private set;
        }

        public readonly Dictionary<string, SerializableProperty> properties;
        public Dictionary<string, SerializableProperty> SerializableProperties
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
                if (AITarget != null) AITarget.Enabled = value;
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
                    DebugConsole.ThrowError("Couldn't make \"" + this + "\" drawable (the component doesn't implement the IDrawableComponent interface)");
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

        [Editable, Serialize(false, false)] //Editable for doors to do their magic
        public bool CanBePicked
        {
            get { return canBePicked; }
            set { canBePicked = value; }
        }

        [Serialize(false, false)]
        public bool DrawHudWhenEquipped
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool CanBeSelected
        {
            get { return canBeSelected; }
            set { canBeSelected = value; }
        }

        //Transfer conditions between same prefab items
        [Serialize(false, false)]
        public bool CanBeCombined
        {
            get { return canBeCombined; }
            set { canBeCombined = value; }
        }

        //Remove item if combination results in 0 condition
        [Serialize(false, false)]
        public bool RemoveOnCombined
        {
            get { return removeOnCombined; }
            set { removeOnCombined = value; }
        }
        
        //Can the "Use" action be triggered by characters or just other items/statuseffects
        [Serialize(false, false)]
        public bool CharacterUsable
        {
            get { return characterUsable; }
            set { characterUsable = value; }
        }

        //Remove item if combination results in 0 condition
        [Serialize(true, false), Editable(ToolTip = "Can the properties of the component be edited in-game (only applicable if the component has in-game editable properties).")]
        public bool AllowInGameEditing
        {
            get;
            set;
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

        [Serialize(false, false)]
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

        [Editable, Serialize("", false)]
        public string Msg
        {
            get { return msg; }
            set { msg = value; }
        }

        public AITarget AITarget
        {
            get;
            private set;
        }
        
        public ItemComponent(Item item, XElement element) 
        {
            this.item = item;
            name = element.Name.ToString();
            properties = SerializableProperty.GetProperties(this);            
            requiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>();
            requiredSkills = new List<Skill>();

#if CLIENT
            sounds = new Dictionary<ActionType, List<ItemSound>>();
#endif

            SelectKey = InputType.Select;

            try
            {
                string selectKeyStr = element.GetAttributeString("selectkey", "Select");
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
                string pickKeyStr = element.GetAttributeString("pickkey", "Select");
                pickKeyStr = ToolBox.ConvertInputType(pickKeyStr);
                PickKey = (InputType)Enum.Parse(typeof(InputType),pickKeyStr, true);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Invalid pick key in " + element + "!", e);
            }
            
            properties = SerializableProperty.DeserializeProperties(this, element);
#if CLIENT
            string msg = TextManager.Get(Msg, true);
            if (msg != null)
            {
                foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
                {
                    msg = msg.Replace("[" + inputType.ToString().ToLowerInvariant() + "]", GameMain.Config.KeyBind(inputType).ToString());
                }
                Msg = msg;
            }
#endif
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requireditem":
                    case "requireditems":
                        RelatedItem ri = RelatedItem.Load(subElement, item.Name);
                        if (ri != null)
                        {
                            if (!requiredItems.ContainsKey(ri.Type))
                            {
                                requiredItems.Add(ri.Type, new List<RelatedItem>());
                            }
                            requiredItems[ri.Type].Add(ri);
                        }
                        else
                        {
                            DebugConsole.ThrowError("Error in item config \"" + item.ConfigFile + "\" - component " + GetType().ToString() + " requires an item with no identifiers.");
                        }
                        break;
                    case "requiredskill":
                    case "requiredskills":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in item config \"" + item.ConfigFile + "\" - skill requirement in component " + GetType().ToString() + " should use a skill identifier instead of the name of the skill.");
                            continue;
                        }

                        string skillIdentifier = subElement.GetAttributeString("identifier", "");
                        requiredSkills.Add(new Skill(skillIdentifier, subElement.GetAttributeInt("level", 0)));
                        break;
                    case "statuseffect":
                        var statusEffect = StatusEffect.Load(subElement, item.Name);

                        if (statusEffectLists == null) statusEffectLists = new Dictionary<ActionType, List<StatusEffect>>();

                        List<StatusEffect> effectList;
                        if (!statusEffectLists.TryGetValue(statusEffect.type, out effectList))
                        {
                            effectList = new List<StatusEffect>();
                            statusEffectLists.Add(statusEffect.type, effectList);
                        }

                        effectList.Add(statusEffect);

                        break;
                    case "aitarget":
                        AITarget = new AITarget(item, subElement);
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
            return characterUsable || character == null;
        }

        //called when the item is equipped and right mouse button is pressed
        public virtual bool SecondaryUse(float deltaTime, Character character = null)
        {
            return false;
        }

        //called when the item is placed in a "limbslot"
        public virtual void Equip(Character character) { }

        //called then the item is dropped or dragged out of a "limbslot"
        public virtual void Unequip(Character character) { }

        public virtual void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f) 
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
            if (canBeCombined && this.item.Prefab == item.Prefab && item.Condition > 0.0f && this.item.Condition > 0.0f)
            {
                float transferAmount = 0.0f;
                if (this.Item.Condition <= item.Condition)
                    transferAmount = Math.Min(item.Condition, this.item.Prefab.Health - this.item.Condition);
                else
                    transferAmount = -Math.Min(this.item.Condition, item.Prefab.Health - item.Condition);

                if (transferAmount == 0.0f)
                    return false;
                this.Item.Condition += transferAmount;
                item.Condition -= transferAmount;
                if (removeOnCombined)
                {
                    if (item.Condition <= 0.0f)
                    {
                        if (item.ParentInventory != null)
                        {
                            Character owner = (Character)item.ParentInventory.Owner;
                            if (owner != null && owner.HasSelectedItem(item)) item.Unequip(owner);
                            item.ParentInventory.RemoveItem(item);
                        }
                        Entity.Spawner.AddToRemoveQueue(item);
                    }
                    if (this.Item.Condition <= 0.0f)
                    {
                        if (this.Item.ParentInventory != null)
                        {
                            Character owner = (Character)this.Item.ParentInventory.Owner;
                            if (owner != null && owner.HasSelectedItem(this.Item)) this.Item.Unequip(owner);
                            this.Item.ParentInventory.RemoveItem(this.Item);
                        }
                        Entity.Spawner.AddToRemoveQueue(this.Item);
                    }
                }
                return true;
            }
            return false;
        }

        public void Remove()
        {
#if CLIENT
            if (loopingSoundChannel != null)
            {
                loopingSoundChannel.Dispose();
                loopingSoundChannel = null;
            }                
            if (GuiFrame != null) GUI.RemoveFromUpdateList(GuiFrame, true);
#endif

            if (delayedCorrectionCoroutine != null)
            {
                CoroutineManager.StopCoroutines(delayedCorrectionCoroutine);
                delayedCorrectionCoroutine = null;
            }

            if (AITarget != null)
            {
                AITarget.Remove();
                AITarget = null;
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
            if (loopingSoundChannel != null)
            {
                loopingSoundChannel.Dispose();
                loopingSoundChannel = null;
            }
#endif
            if (AITarget != null)
            {
                AITarget.Remove();
                AITarget = null;
            }

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
            return HasRequiredSkills(character, out Skill temp);
        }

        public bool HasRequiredSkills(Character character, out Skill insufficientSkill)
        {
            foreach (Skill skill in requiredSkills)
            {
                float characterLevel = character.GetSkillLevel(skill.Identifier);
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
        public float DegreeOfSuccess(Character character)
        {
            if (requiredSkills.Count == 0) return 1.0f;

            float skillSuccessSum = 0.0f;
            for (int i = 0; i < requiredSkills.Count; i++)
            {
                float characterLevel = character.GetSkillLevel(requiredSkills[i].Identifier);
                skillSuccessSum += (characterLevel - requiredSkills[i].Level);
            }
            float average = skillSuccessSum / requiredSkills.Count;

            return ((average + 100.0f) / 2.0f) / 100.0f;
        }

        public virtual void FlipX(bool relativeToSub) { }

        public virtual void FlipY(bool relativeToSub) { }

        public bool HasRequiredContainedItems(bool addMessage)
        {
            if (!requiredItems.ContainsKey(RelatedItem.RelationType.Contained)) return true;
            if (item.OwnInventory == null) return false;
            
            foreach (RelatedItem ri in requiredItems[RelatedItem.RelationType.Contained])
            {
                if (!item.OwnInventory.Items.Any(it => it != null && it.Condition > 0.0f && ri.MatchesItem(it)))
                {
#if CLIENT
                    if (addMessage && !string.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
#endif
                    return false;
                }
            }

            return true;
        }

        public virtual bool HasRequiredItems(Character character, bool addMessage)
        {
            if (!requiredItems.Any()) return true;
            if (character.Inventory == null) return false;
                       
            if (requiredItems.ContainsKey(RelatedItem.RelationType.Equipped))
            {
                foreach (RelatedItem ri in requiredItems[RelatedItem.RelationType.Equipped])
                {
                    if (character.SelectedItems.FirstOrDefault(it => it != null && it.Condition > 0.0f && ri.MatchesItem(it)) == null)
                    {
#if CLIENT
                    if (addMessage && !string.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
#endif
                        return false;
                    }
                }
            }
            if (requiredItems.ContainsKey(RelatedItem.RelationType.Picked))
            {
                foreach (RelatedItem ri in requiredItems[RelatedItem.RelationType.Picked])
                {
                    if (character.Inventory.Items.FirstOrDefault(it => it != null && it.Condition > 0.0f && ri.MatchesItem(it)) == null)
                    {
#if CLIENT
                    if (addMessage && !string.IsNullOrEmpty(ri.Msg)) GUI.AddMessage(ri.Msg, Color.Red);
#endif
                        return false;
                    }
                }
            }
            
            return true;
        }
        
        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null, Limb targetLimb = null)
        {
            if (statusEffectLists == null) return;

            List<StatusEffect> statusEffects;
            if (!statusEffectLists.TryGetValue(type, out statusEffects)) return;

            bool broken = item.Condition <= 0.0f;
            foreach (StatusEffect effect in statusEffects)
            {
                if (broken && effect.type != ActionType.OnBroken) continue;
                item.ApplyStatusEffect(effect, type, deltaTime, character, targetLimb, false, false);
            }
        }
        
        public virtual void Load(XElement componentElement)
        {
            if (componentElement == null) return;

            foreach (XAttribute attribute in componentElement.Attributes())
            {
                if (!properties.TryGetValue(attribute.Name.ToString().ToLowerInvariant(), out SerializableProperty property)) continue;
                property.TrySetValue(attribute.Value);
            }
#if CLIENT 
            string msg = TextManager.Get(Msg, true);
            if (msg != null)
            {
                foreach (InputType inputType in Enum.GetValues(typeof(InputType)))
                {
                    msg = msg.Replace("[" + inputType.ToString().ToLowerInvariant() + "]", GameMain.Config.KeyBind(inputType).ToString());
                }
                Msg = msg;
            }
#endif
            var prevRequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(requiredItems);
            bool overrideRequiredItems = false;

            foreach (XElement subElement in componentElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requireditem":
                        if (!overrideRequiredItems) requiredItems.Clear();
                        overrideRequiredItems = true;

                        RelatedItem newRequiredItem = RelatedItem.Load(subElement, item.Name);                        
                        if (newRequiredItem == null) continue;

                        var prevRequiredItem = prevRequiredItems.ContainsKey(newRequiredItem.Type) ?
                            prevRequiredItems[newRequiredItem.Type].Find(ri => ri.JoinedIdentifiers == newRequiredItem.JoinedIdentifiers) : null;
                        if (prevRequiredItem != null)
                        {
                            newRequiredItem.statusEffects = prevRequiredItem.statusEffects;
                            newRequiredItem.Msg = prevRequiredItem.Msg;
                        }

                        if (!requiredItems.ContainsKey(newRequiredItem.Type))
                        {
                            requiredItems[newRequiredItem.Type] = new List<RelatedItem>();
                        }
                        requiredItems[newRequiredItem.Type].Add(newRequiredItem);
                        break;
                }
            }
        }

        /// <summary>
        /// Called when all items have been loaded. Use to initialize connections between items.
        /// </summary>
        public virtual void OnMapLoaded() { }

        /// <summary>
        /// Called when all the components of the item have been loaded. Use to initialize connections between components and such.
        /// </summary>
        public virtual void OnItemLoaded() { }
        
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
            ItemComponent ic = null;
            try
            {
                object[] lobject = new object[] { item, element };
                object component = constructor.Invoke(lobject);
                ic = (ItemComponent)component;
                ic.name = element.Name.ToString();
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Error while loading entity of the type " + t + ".", e.InnerException);
                GameAnalyticsManager.AddErrorEventOnce("ItemComponent.Load:TargetInvocationException" + item.Name + element.Name,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Error while loading entity of the type " + t + " (" + e.InnerException + ")\n" + Environment.StackTrace);
            }

            return ic;
        }

        public virtual XElement Save(XElement parentElement)
        {
            XElement componentElement = new XElement(name);

            foreach (var kvp in requiredItems)
            {
                foreach (RelatedItem ri in kvp.Value)
                {
                    XElement newElement = new XElement("requireditem");
                    ri.Save(newElement);
                    componentElement.Add(newElement);
                }
            }


            SerializableProperty.SerializeProperties(this, componentElement);

            parentElement.Add(componentElement);
            return componentElement;
        }
    }
}
