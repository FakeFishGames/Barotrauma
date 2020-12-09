using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Barotrauma.Extensions;
#if CLIENT
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Sounds;
#endif

namespace Barotrauma.Items.Components
{
    interface IDrawableComponent
    {
#if CLIENT
        /// <summary>
        /// The extents of the sprites or other graphics this component needs to draw. Used to determine which items are visible on the screen.
        /// </summary>
        Vector2 DrawSize { get; }

        void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1);
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
        public readonly List<RelatedItem> DisabledRequiredItems = new List<RelatedItem>();

        public List<Skill> requiredSkills;

        private ItemComponent parent;
        public ItemComponent Parent
        {
            get { return parent; }
            set
            {
                if (parent == value) { return; }
                if (parent != null) { parent.OnActiveStateChanged -= SetActiveState; }
                if (value != null) { value.OnActiveStateChanged += SetActiveState; }
                parent = value;
            }
        }

        public readonly XElement originalElement;

        protected const float CorrectionDelay = 1.0f;
        protected CoroutineHandle delayedCorrectionCoroutine;

        [Editable, Serialize(0.0f, false, description: "How long it takes to pick up the item (in seconds).")]
        public float PickingTime
        {
            get;
            set;
        }

        [Serialize("", false, description: "What to display on the progress bar when this item is being picked.")]
        public string PickingMsg
        {
            get;
            set;
        }

        public Dictionary<string, SerializableProperty> SerializableProperties { get; protected set; }

        public Action<bool> OnActiveStateChanged;

        public virtual bool IsActive
        {
            get { return isActive; }
            set
            {
#if CLIENT
                if (!value)
                {
                    IsActiveTimer = 0.0f;
                    if (isActive)
                    {
                        StopSounds(ActionType.OnActive);
                    }
                }
#endif
                if (value != IsActive) { OnActiveStateChanged?.Invoke(value); }
                isActive = value;
            }
        }

        private bool drawable = true;

        public List<PropertyConditional> IsActiveConditionals;

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
                    item.EnableDrawableComponent((IDrawableComponent)this);
                }
                else
                {
                    item.DisableDrawableComponent((IDrawableComponent)this);
                }
            }
        }

        [Editable, Serialize(false, false, description: "Can the item be picked up (or interacted with, if the pick action does something else than picking up the item).")] //Editable for doors to do their magic
        public bool CanBePicked
        {
            get { return canBePicked; }
            set { canBePicked = value; }
        }

        [Serialize(false, false, description: "Should the interface of the item (if it has one) be drawn when the item is equipped.")]
        public bool DrawHudWhenEquipped
        {
            get;
            protected set;
        }

        [Serialize(false, false, description: "Can the item be selected by interacting with it.")]
        public bool CanBeSelected
        {
            get { return canBeSelected; }
            set { canBeSelected = value; }
        }

        [Serialize(false, false, description: "Can the item be combined with other items of the same type.")]
        public bool CanBeCombined
        {
            get { return canBeCombined; }
            set { canBeCombined = value; }
        }

        [Serialize(false, false, description: "Should the item be removed if combining it with an other item causes the condition of this item to drop to 0.")]
        public bool RemoveOnCombined
        {
            get { return removeOnCombined; }
            set { removeOnCombined = value; }
        }

        [Serialize(false, false, description: "Can the \"Use\" action of the item be triggered by characters or just other items/StatusEffects.")]
        public bool CharacterUsable
        {
            get { return characterUsable; }
            set { characterUsable = value; }
        }

        //Remove item if combination results in 0 condition
        [Serialize(true, false, description: "Can the properties of the component be edited in-game (only applicable if the component has in-game editable properties)."), Editable()]
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

        [Serialize(false, false, description: "Should the item be deleted when it's used.")]
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

        [Editable, Serialize("", true, translationTextTag: "ItemMsg", description: "A text displayed next to the item when it's highlighted (generally instructs how to interact with the item, e.g. \"[Mouse1] Pick up\").")]
        public string Msg
        {
            get;
            set;
        }

        public string DisplayMsg
        {
            get;
            set;
        }

        public virtual bool RecreateGUIOnResolutionChange => false;

        /// <summary>
        /// How useful the item is in combat? Used by AI to decide which item it should use as a weapon. For the sake of clarity, use a value between 0 and 100 (not enforced).
        /// </summary>
        [Serialize(0f, false, description: "How useful the item is in combat? Used by AI to decide which item it should use as a weapon. For the sake of clarity, use a value between 0 and 100 (not enforced).")]
        public float CombatPriority { get; private set; }

        public ItemComponent(Item item, XElement element)
        {
            this.item = item;
            originalElement = element;
            name = element.Name.ToString();
            SerializableProperties = SerializableProperty.GetProperties(this);
            requiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>();
            requiredSkills = new List<Skill>();

#if CLIENT
            hasSoundsOfType = new bool[Enum.GetValues(typeof(ActionType)).Length];
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
                PickKey = (InputType)Enum.Parse(typeof(InputType), pickKeyStr, true);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Invalid pick key in " + element + "!", e);
            }

            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            ParseMsg();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "activeconditional":
                    case "isactive":
                        IsActiveConditionals = IsActiveConditionals ?? new List<PropertyConditional>();
                        foreach (XAttribute attribute in subElement.Attributes())
                        {
                            if (PropertyConditional.IsValid(attribute))
                            {
                                IsActiveConditionals.Add(new PropertyConditional(attribute));
                            }
                        }
                        break;
                    case "requireditem":
                    case "requireditems":
                        SetRequiredItems(subElement);
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
                    default:
                        if (LoadElemProjSpecific(subElement)) { break; }
                        ItemComponent ic = Load(subElement, item, item.ConfigFile, false);
                        if (ic == null) { break; }

                        ic.Parent = this;
                        ic.IsActive = isActive;
                        OnActiveStateChanged += ic.SetActiveState;

                        item.AddComponent(ic);
                        break;
                }
            }
        }

        private void SetActiveState(bool isActive)
        {
            IsActive = isActive;
        }

        public void SetRequiredItems(XElement element)
        {
            bool returnEmpty = false;
#if CLIENT
            returnEmpty = Screen.Selected == GameMain.SubEditorScreen;
#endif
            RelatedItem ri = RelatedItem.Load(element, returnEmpty, item.Name);
            if (ri != null)
            {
                if (ri.Identifiers.Length == 0)
                {
                    DisabledRequiredItems.Add(ri);
                }
                else
                {
                    if (!requiredItems.ContainsKey(ri.Type))
                    {
                        requiredItems.Add(ri.Type, new List<RelatedItem>());
                    }
                    requiredItems[ri.Type].Add(ri);
                }
            }
            else
            {
                DebugConsole.ThrowError("Error in item config \"" + item.ConfigFile + "\" - component " + GetType().ToString() + " requires an item with no identifiers.");
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
        public virtual void Drop(Character dropper) { }

        /// <returns>true if the operation was completed</returns>
        public virtual bool AIOperate(float deltaTime, Character character, AIObjectiveOperateItem objective)
        {
            return false;
        }

        //called when isActive is true and condition > 0.0f
        public virtual void Update(float deltaTime, Camera cam) 
        {
            ApplyStatusEffects(ActionType.OnActive, deltaTime);
        }

        //called when isActive is true and condition == 0.0f
        public virtual void UpdateBroken(float deltaTime, Camera cam)
        {
#if CLIENT
            StopSounds(ActionType.OnActive);
#endif
        }

        //called when the item is equipped and the "use" key is pressed
        //returns true if the item was used succesfully (not out of ammo, reloading, etc)
        public virtual bool Use(float deltaTime, Character character = null)
        {
            return characterUsable || character == null;
        }

        //called when the item is equipped and the "aim" key is pressed or when the item is selected if it doesn't require aiming.
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
                case "trigger_in":
                    if (signal != "0")
                    {
                        item.Use(1.0f, sender);
                    }
                    break;
                case "toggle":
                    if (signal != "0")
                    {
                        IsActive = !isActive;
                    }
                    break;
                case "set_active":
                case "set_state":
                    IsActive = signal != "0";
                    break;
            }
        }

        public virtual bool Combine(Item item, Character user)
        {
            if (canBeCombined && this.item.Prefab == item.Prefab && item.Condition > 0.0f && this.item.Condition > 0.0f)
            {
                float transferAmount = 0.0f;
                if (this.Item.Condition <= item.Condition)
                    transferAmount = Math.Min(item.Condition, this.item.MaxCondition - this.item.Condition);
                else
                    transferAmount = -Math.Min(this.item.Condition, item.MaxCondition - item.Condition);

                if (transferAmount == 0.0f) { return false; }
                if (removeOnCombined)
                {
                    if (item.Condition - transferAmount <= 0.0f)
                    {
                        if (item.ParentInventory != null)
                        {
                            if (item.ParentInventory.Owner is Character owner && owner.HasSelectedItem(item))
                            {
                                item.Unequip(owner);
                            }
                            item.ParentInventory.RemoveItem(item);
                        }
                        Entity.Spawner.AddToRemoveQueue(item);
                    }
                    else
                    {
                        item.Condition -= transferAmount;
                    }
                    if (this.Item.Condition + transferAmount <= 0.0f)
                    {
                        if (this.Item.ParentInventory != null)
                        {
                            if (this.Item.ParentInventory.Owner is Character owner && owner.HasSelectedItem(this.Item))
                            {
                                this.Item.Unequip(owner);
                            }
                            this.Item.ParentInventory.RemoveItem(this.Item);
                        }
                        Entity.Spawner.AddToRemoveQueue(this.Item);
                    }
                    else
                    {
                        this.Item.Condition += transferAmount;
                    }
                }
                else
                {
                    this.Item.Condition += transferAmount;
                    item.Condition -= transferAmount;
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

            //no need to Dispose these - SoundManager will do it when it when it needs a free channel and the sound has stopped playing 
            //disposing immediately on Remove will for example prevent explosives from playing a sound if the explosion removes the item
            /*foreach (SoundChannel channel in playingOneshotSoundChannels)
            {
                channel.Dispose();
            }*/

            if (GuiFrame != null)
            {
                GUI.RemoveFromUpdateList(GuiFrame, true);
                GuiFrame.RectTransform.Parent = null;
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
            if (loopingSoundChannel != null)
            {
                loopingSoundChannel.Dispose();
                loopingSoundChannel = null;
            }
#endif

            ShallowRemoveComponentSpecific();
        }

        protected virtual void ShallowRemoveComponentSpecific()
        {
            RemoveComponentSpecific();
        }


        protected virtual void RemoveComponentSpecific()
        {
        }

        public bool HasRequiredSkills(Character character)
        {
            return HasRequiredSkills(character, out Skill temp);
        }

        public bool HasRequiredSkills(Character character, out Skill insufficientSkill)
        {
            foreach (Skill skill in requiredSkills)
            {
                float characterLevel = character.GetSkillLevel(skill.Identifier);
                if (characterLevel < skill.Level * GetSkillMultiplier())
                {
                    insufficientSkill = skill;
                    return false;
                }
            }
            insufficientSkill = null;
            return true;
        }

        public virtual float GetSkillMultiplier() { return 1; }

        /// <summary>
        /// Returns 0.0f-1.0f based on how well the Character can use the itemcomponent
        /// </summary>
        /// <returns>0.5f if all the skills meet the skill requirements exactly, 1.0f if they're way above and 0.0f if way less</returns>
        public float DegreeOfSuccess(Character character)
        {
            return DegreeOfSuccess(character, requiredSkills);
        }

        /// <summary>
        /// Returns 0.0f-1.0f based on how well the Character can use the itemcomponent
        /// </summary>
        /// <returns>0.5f if all the skills meet the skill requirements exactly, 1.0f if they're way above and 0.0f if way less</returns>
        public float DegreeOfSuccess(Character character, List<Skill> requiredSkills)
        {
            if (requiredSkills.Count == 0) return 1.0f;

            if (character == null)
            {
                string errorMsg = "ItemComponent.DegreeOfSuccess failed (character was null).\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("ItemComponent.DegreeOfSuccess:CharacterNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return 0.0f;
            }

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

        public bool HasRequiredContainedItems(Character user, bool addMessage, string msg = null)
        {
            if (!requiredItems.ContainsKey(RelatedItem.RelationType.Contained)) { return true; }
            if (item.OwnInventory == null) { return false; }

            foreach (RelatedItem ri in requiredItems[RelatedItem.RelationType.Contained])
            {
                if (!ri.CheckRequirements(user, item))
                {
#if CLIENT
                    msg = msg ?? ri.Msg;
                    if (addMessage && !string.IsNullOrEmpty(msg))
                    {
                        GUI.AddMessage(msg, Color.Red);
                    }
#endif
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Only checks if any of the Picked requirements are matched (used for checking id card(s)). Much simpler and a bit different than HasRequiredItems.
        /// </summary>
        public bool HasAccess(Character character)
        {
            if (character.Inventory == null) { return false; }
            if (requiredItems.None()) { return true; }

            foreach (Item item in character.Inventory.Items)
            {
                if (requiredItems.Any(ri => ri.Value.Any(r => r.Type == RelatedItem.RelationType.Picked && r.MatchesItem(item))))
                {
                    return true;
                }
            }
            return false;
        }

        public virtual bool HasRequiredItems(Character character, bool addMessage, string msg = null)
        {
            if (requiredItems.None()) { return true; }
            if (character.Inventory == null) { return false; }
            bool hasRequiredItems = false;
            bool canContinue = true;
            if (requiredItems.ContainsKey(RelatedItem.RelationType.Equipped))
            {
                foreach (RelatedItem ri in requiredItems[RelatedItem.RelationType.Equipped])
                {
                    canContinue = CheckItems(ri, character.SelectedItems);
                    if (!canContinue) { break; }
                }
            }
            if (canContinue)
            {
                if (requiredItems.ContainsKey(RelatedItem.RelationType.Picked))
                {
                    foreach (RelatedItem ri in requiredItems[RelatedItem.RelationType.Picked])
                    {
                        if (!CheckItems(ri, character.Inventory.Items)) { break; }
                    }
                }
            }

#if CLIENT
            if (!hasRequiredItems && addMessage && !string.IsNullOrEmpty(msg))
            {
                GUI.AddMessage(msg, Color.Red);
            }
#endif
            return hasRequiredItems;

            bool CheckItems(RelatedItem relatedItem, IEnumerable<Item> itemList)
            {
                bool Predicate(Item it) => it != null && it.Condition > 0.0f && relatedItem.MatchesItem(it);
                bool shouldBreak = false;
                bool inEditor = false;
#if CLIENT
                inEditor = Screen.Selected == GameMain.SubEditorScreen;
#endif
                if (relatedItem.IgnoreInEditor && inEditor)
                {
                    hasRequiredItems = true;
                }
                else if (relatedItem.IsOptional)
                {
                    if (!hasRequiredItems)
                    {
                        hasRequiredItems = itemList.Any(Predicate);
                    }
                }
                else
                {
                    hasRequiredItems = itemList.Any(Predicate);
                    if (!hasRequiredItems)
                    {
                        shouldBreak = true;
                    }
                }
                if (!hasRequiredItems)
                {
                    if (msg == null && !string.IsNullOrEmpty(relatedItem.Msg))
                    {
                        msg = relatedItem.Msg;
                    }
                }
                return !shouldBreak;
            }
        }

        public void ApplyStatusEffects(ActionType type, float deltaTime, Character character = null, Limb targetLimb = null, Entity useTarget = null, Character user = null, Vector2? worldPosition = null)
        {
            if (statusEffectLists == null) { return; }

            if (!statusEffectLists.TryGetValue(type, out List<StatusEffect> statusEffects)) { return; }

            bool broken = item.Condition <= 0.0f;
            bool reducesCondition = false;
            foreach (StatusEffect effect in statusEffects)
            {
                if (broken && effect.type != ActionType.OnBroken) { continue; }
                if (user != null) { effect.SetUser(user); }
                item.ApplyStatusEffect(effect, type, deltaTime, character, targetLimb, useTarget, false, false, worldPosition);
                reducesCondition |= effect.ReducesItemCondition();
            }
            //if any of the effects reduce the item's condition, set the user for OnBroken effects as well
            if (reducesCondition && user != null && type != ActionType.OnBroken)
            {
                foreach (ItemComponent ic in item.Components)
                {
                    if (ic.statusEffectLists == null || !ic.statusEffectLists.TryGetValue(ActionType.OnBroken, out List<StatusEffect> brokenEffects)) { continue; }
                    brokenEffects.ForEach(e => e.SetUser(user));
                }
            }
        }

        public virtual void Load(XElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            if (componentElement != null) 
            { 
                foreach (XAttribute attribute in componentElement.Attributes())
                {
                    if (!SerializableProperties.TryGetValue(attribute.Name.ToString().ToLowerInvariant(), out SerializableProperty property)) { continue; }
                    if (property.OverridePrefabValues || !usePrefabValues)
                    {
                        property.TrySetValue(this, attribute.Value);
                    }
                }
                ParseMsg();
                OverrideRequiredItems(componentElement);
            }

            if (item.Submarine != null) { SerializableProperty.UpgradeGameVersion(this, originalElement, item.Submarine.Info.GameVersion); }
        }

        /// <summary>
        /// Called when all items have been loaded. Use to initialize connections between items.
        /// </summary>
        public virtual void OnMapLoaded() { }

        /// <summary>
        /// Called when all the components of the item have been loaded. Use to initialize connections between components and such.
        /// </summary>
        public virtual void OnItemLoaded() { }

        public virtual void OnScaleChanged() { }

        // TODO: Consider using generics, interfaces, or inheritance instead of reflection -> would be easier to debug when something changes/goes wrong.
        // For example, currently we can edit the constructors but they will fail in runtime because the parameters are not changed here.
        // It's also painful to find where the constructors are used, because the references exist only at runtime.
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
                    "Error while loading entity of the type " + t + " (" + e.InnerException + ")\n" + Environment.StackTrace.CleanupStackTrace());
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
            foreach (RelatedItem ri in DisabledRequiredItems)
            {
                XElement newElement = new XElement("requireditem");
                ri.Save(newElement);
                componentElement.Add(newElement);
            }


            SerializableProperty.SerializeProperties(this, componentElement);

            parentElement.Add(componentElement);
            return componentElement;
        }

        public virtual void Reset()
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, originalElement);
            if (this is Pickable) { canBePicked = true; }
            ParseMsg();
            OverrideRequiredItems(originalElement);
        }

        private void OverrideRequiredItems(XElement element)
        {
            var prevRequiredItems = new Dictionary<RelatedItem.RelationType, List<RelatedItem>>(requiredItems);
            requiredItems.Clear();

            bool returnEmptyRequirements = false;
#if CLIENT
            returnEmptyRequirements = Screen.Selected == GameMain.SubEditorScreen;
#endif
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requireditem":
                    case "requireditems":
                        RelatedItem newRequiredItem = RelatedItem.Load(subElement, returnEmptyRequirements, item.Name);
                        if (newRequiredItem == null) continue;

                        var prevRequiredItem = prevRequiredItems.ContainsKey(newRequiredItem.Type) ?
                            prevRequiredItems[newRequiredItem.Type].Find(ri => ri.JoinedIdentifiers == newRequiredItem.JoinedIdentifiers) : null;
                        if (prevRequiredItem != null)
                        {
                            newRequiredItem.statusEffects = prevRequiredItem.statusEffects;
                            newRequiredItem.Msg = prevRequiredItem.Msg;
                            newRequiredItem.IsOptional = prevRequiredItem.IsOptional;
                            newRequiredItem.IgnoreInEditor = prevRequiredItem.IgnoreInEditor;
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

        public void ParseMsg()
        {
            string msg = TextManager.Get(Msg, true);
            if (msg != null)
            {
                msg = TextManager.ParseInputTypes(msg);
                DisplayMsg = msg;
            }
            else
            {
                DisplayMsg = Msg;
            }
        }

        #region AI related
        protected const float AIUpdateInterval = 0.2f;
        protected float aiUpdateTimer;
        private int itemIndex;
        private Character previousUser;
        protected bool FindSuitableContainer(Character character, Func<Item, float> priority, out Item suitableContainer)
        {
            suitableContainer = null;
            if (character.AIController is HumanAIController aiController)
            {
                if (previousUser != character)
                {
                    previousUser = character;
                    itemIndex = 0;
                }
                if (character.FindItem(ref itemIndex, out Item targetContainer, ignoredItems: aiController.IgnoredItems, customPriorityFunction: priority))
                {
                    suitableContainer = targetContainer;
                    return true;
                }
            }
            return false;
        }

        protected AIObjectiveContainItem AIContainItems<T>(ItemContainer container, Character character, AIObjective currentObjective, int itemCount, bool equip, bool removeEmpty, bool spawnItemIfNotFound = false, bool dropItemOnDeselected = false) where T : ItemComponent
        {
            AIObjectiveContainItem containObjective = null;
            if (character.AIController is HumanAIController aiController)
            {
                containObjective = new AIObjectiveContainItem(character, container.GetContainableItemIdentifiers.ToArray(), container, currentObjective.objectiveManager, spawnItemIfNotFound: spawnItemIfNotFound)
                {
                    targetItemCount = itemCount,
                    Equip = equip,
                    RemoveEmpty = removeEmpty,
                    GetItemPriority = i =>
                    {
                        if (i.ParentInventory?.Owner is Item)
                        {
                            //don't take items from other items of the same type
                            if (((Item)i.ParentInventory.Owner).GetComponent<T>() != null)
                            {
                                return 0.0f;
                            }
                        }
                        return 1.0f;
                    }
                };
                containObjective.Abandoned += () => aiController.IgnoredItems.Add(container.Item);
                if (dropItemOnDeselected)
                {
                    currentObjective.Deselected += () =>
                    {
                        if (containObjective == null) { return; }
                        if (containObjective.IsCompleted) { return; }
                        Item item = containObjective.ItemToContain;
                        if (item != null && character.CanInteractWith(item, checkLinked: false))
                        {
                            item.Drop(character);
                        }
                    };
                }
                currentObjective.AddSubObjective(containObjective);
            }
            return containObjective;
        }

        /// <summary>
        /// Returns true when done seeking the suitable container.
        /// </summary>
        protected bool AIDecontainEmptyItems(Character character, AIObjective objective, bool equip, ItemContainer sourceContainer = null)
        {
            if (character.AIController is HumanAIController aiController)
            {
                ItemContainer sourceC = sourceContainer ?? (item.OwnInventory?.Owner is Item it ? it.GetComponent<ItemContainer>() : null);
                var containedItems = sourceContainer != null ? sourceContainer.Inventory.Items : item.OwnInventory.Items;
                foreach (Item containedItem in containedItems)
                {
                    if (containedItem != null && containedItem.Condition <= 0.0f)
                    {
                        if (FindSuitableContainer(character,
                            i =>
                            {
                                if (i.IsThisOrAnyContainerIgnoredByAI()) { return 0; }
                                var container = i.GetComponent<ItemContainer>();
                                if (container == null) { return 0; }
                                if (container.Inventory.IsFull()) { return 0; }
                                // Ignore containers that are identical to the source container
                                if (sourceC != null && container.Item.Prefab == sourceC.Item.Prefab) { return 0; }
                                if (container.ShouldBeContained(containedItem, out bool isRestrictionsDefined))
                                {
                                    if (isRestrictionsDefined)
                                    {
                                        return 4;
                                    }
                                    else
                                    {
                                        if (containedItem.Prefab.IsContainerPreferred(container, out bool isPreferencesDefined, out bool isSecondary))
                                        {
                                            return isPreferencesDefined ? isSecondary ? 2 : 3 : 1;
                                        }
                                        else
                                        {
                                            return isPreferencesDefined ? 0 : 1;
                                        }
                                    }
                                }
                                else
                                {
                                    return 0;
                                }
                            }, out Item targetContainer))
                        {
                            var decontainObjective = new AIObjectiveDecontainItem(character, containedItem, objective.objectiveManager, sourceC, targetContainer?.GetComponent<ItemContainer>())
                            {
                                Equip = equip
                            };
                            decontainObjective.Abandoned += () =>
                            {
                                itemIndex = 0;
                                if (targetContainer != null)
                                {
                                    aiController.IgnoredItems.Add(targetContainer);
                                }
                            };
                            decontainObjective.Completed += () =>
                            {
                                if (targetContainer == null)
                                {
                                    itemIndex = 0;
                                }
                            };
                            objective.AddSubObjectiveInQueue(decontainObjective);
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
        #endregion
    }
}
