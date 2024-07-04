using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Can be used to do various kinds of checks on items: whether a specific kind of item exists, 
    /// if it's in a specific character's inventory or in a container, or whether some conditions are met on the item.
    /// </summary>
    class CheckItemAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Either the tag of the item(s) we want to check, or a character/container the items are inside.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The target item must have one of these identifiers.")]
        public string ItemIdentifiers { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The target item must have at least one of these tags.")]
        public string ItemTags { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "The minimum number of matching items for the check to succeed.")]
        public int Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional tag of a hull the target must be inside.")]
        public Identifier HullTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the first target when the check succeeds.")]
        public Identifier ApplyTagToTarget { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the found item(s) when the check succeeds.")]
        public Identifier ApplyTagToItem { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Does the item need to be equipped for the check to succeed?")]
        public bool RequireEquipped { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Does the item need to be worn for the check to succeed?")]
        public bool RequireWorn { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If enabled, the doesn't need to be directly inside the container/character we're checking, but can be nested inside multiple containers (e.g. in a toolbelt in a character's inventory).")]
        public bool Recursive { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Can be used to require the item to be in a specific ItemContainer of the target container. For example, the input slots of a fabricator (the first ItemContainer of the fabricator, with an index of 0).")]
        public int ItemContainerIndex { get; set; }

        private readonly bool checkPercentage;

        private float requiredConditionalMatchPercentage;

        [Serialize(100.0f, IsPropertySaveable.Yes, description: "What percentage of targets do the conditionals need to match for the check to succeed?")]
        public float RequiredConditionalMatchPercentage 
        {
            get { return requiredConditionalMatchPercentage; }
            set { requiredConditionalMatchPercentage = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "When enabled, the number of matching items is compared to the number of matching items there were at the start of the round. Only valid if RequiredConditionalMatchPercentage is set.")]
        public bool CompareToInitialAmount { get; set; }

        private readonly IReadOnlyList<PropertyConditional> conditionals;

        private readonly Identifier[] itemIdentifierSplit;
        private readonly Identifier[] itemTags;

        public CheckItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            itemIdentifierSplit = ItemIdentifiers.Split(',').ToIdentifiers();
            itemTags = ItemTags.Split(",").ToIdentifiers();
            var conditionalList = new List<PropertyConditional>();
            foreach (ContentXElement subElement in element.GetChildElements("conditional"))
            {
                conditionalList.AddRange(PropertyConditional.FromXElement(subElement));
                break;
            }
            conditionals = conditionalList;

            if (itemTags.None() && 
                ItemIdentifiers.None() &&
                TargetTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". {nameof(CheckItemAction)} does't define either tags or identifiers of the item to check.",
                    contentPackage: element.ContentPackage);
            }
            checkPercentage = element.GetAttribute(nameof(RequiredConditionalMatchPercentage)) is not null;
            if (checkPercentage && conditionals.None())
            {
                DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". {nameof(CheckItemAction)} requires conditionals to be met on {requiredConditionalMatchPercentage}% of the targets, but there are no conditionals defined.");
            }
            if (Amount != 1 && checkPercentage)
            {
                DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". Cannot define both '{Amount}' and '{RequiredConditionalMatchPercentage}' in {nameof(CheckItemAction)}.", 
                    contentPackage: element.ContentPackage);
            }
        }

        private bool EnoughTargets(int totalTargets, int targetsWithConditionalsMatched)
        {
            if (checkPercentage)
            {
                if (CompareToInitialAmount)
                {
                    totalTargets = ParentEvent.GetInitialTargetCount(TargetTag);
                }
                return MathUtils.Percentage(targetsWithConditionalsMatched, totalTargets) >= RequiredConditionalMatchPercentage;
            }
            else
            {
                return targetsWithConditionalsMatched >= Amount;
            }    
        }

        private readonly List<Item> tempTargetItems = new List<Item>();
        protected override bool? DetermineSuccess()
        {
            var targets = ParentEvent.GetTargets(TargetTag);

            if (!HullTag.IsEmpty)
            {
                var hulls = ParentEvent.GetTargets(HullTag).OfType<Hull>();
                targets = targets.Where(t =>
                    (t is Item it && hulls.Contains(it.CurrentHull)) ||
                    (t is Character c && hulls.Contains(c.CurrentHull)));
            }

            if (!targets.Any()) 
            {
                if (conditionals.Any()) 
                { 
                    //conditionals can't be met if there's no targets
                    return false;
                }
                return null; 
            }

            //check if the target(s) are the items we're looking for (instead of characters/containers the items are inside)
            int targetCount = targets.Count();
            if (targetCount >= Amount)
            {
                tempTargetItems.Clear();
                foreach (var target in targets)
                {
                    if (target is not Item item) { continue; }
                    if (itemTags.Any(item.HasTag) || itemIdentifierSplit.Contains(item.Prefab.Identifier) ||
                        (itemTags.None() && itemIdentifierSplit.None() && conditionals.Any()))
                    {
                        if (ConditionalsMatch(item, character: null))
                        {
                            tempTargetItems.Add(item);
                        }
                    }
                }
                if (EnoughTargets(targetCount, tempTargetItems.Count))
                {
                    TryApplyTagToItems(tempTargetItems);
                    return true;
                }
            }

            foreach (var target in targets)
            {
                if (target is Character character)
                {
                    Inventory inventory = character.Inventory;
                    if (CheckInventory(character.Inventory, character))
                    {
                        if (!ApplyTagToTarget.IsEmpty)
                        {
                            ParentEvent.AddTarget(ApplyTagToTarget, target);
                        }
                        return true;
                    }
                }
                else if (target is Item item)
                {
                    int i = 0;
                    foreach (var itemContainer in item.GetComponents<ItemContainer>())
                    {
                        if (ItemContainerIndex == -1 || i == ItemContainerIndex)
                        {
                            if (CheckInventory(itemContainer.Inventory, character: null))
                            {
                                if (!ApplyTagToTarget.IsEmpty)
                                {
                                    ParentEvent.AddTarget(ApplyTagToTarget, target);
                                }
                                return true; 
                            }
                        }
                        i++;
                    }
                }
            }
            return false;
        }

        private bool CheckInventory(Inventory inventory, Character character)
        {
            if (inventory == null) { return false; }
            int targetCount = 0;
            HashSet<Item> eventTargets = new HashSet<Item>();
            tempTargetItems.Clear();
            foreach (Identifier tag in itemTags)
            {
                foreach (var target in ParentEvent.GetTargets(tag))
                {
                    if (target is Item item)
                    {
                        eventTargets.Add(item);
                    }
                }
            }
            foreach (Item item in inventory.FindAllItems(it => 
                    itemTags.Any(it.HasTag) || 
                    itemIdentifierSplit.Contains(it.Prefab.Identifier) || 
                    eventTargets.Contains(it), 
                recursive: Recursive))
            {
                targetCount++;
                if (ConditionalsMatch(item, character)) 
                { 
                    tempTargetItems.Add(item);
                }
            }

            if (EnoughTargets(targetCount, tempTargetItems.Count))
            { 
                TryApplyTagToItems(tempTargetItems);
                return true;
            }
            return false;
            
        }

        private void TryApplyTagToItems(IEnumerable<Item> items)
        {
            if (!ApplyTagToItem.IsEmpty)
            {
                foreach (var targetItem in items)
                {
                    ParentEvent.AddTarget(ApplyTagToItem, targetItem);
                }
            }
        }

        private bool ConditionalsMatch(Item item, Character character = null)
        {
            if (item == null) { return false; }
            foreach (PropertyConditional conditional in conditionals)
            {                
                if (!item.ConditionalMatches(conditional))
                {
                    return false;
                }
            }
            if (RequireEquipped)
            {
                if (character == null) { return false; }
                return character.HasEquippedItem(item);
            }
            if (RequireWorn)
            {
                if (character == null) { return false; }
                foreach (var wearable in item.GetComponents<Wearable>())
                {
                    foreach (var allowedSlot in wearable.AllowedSlots)
                    {
                        if (allowedSlot == InvSlotType.Any) { continue; }
                        if (character.HasEquippedItem(item, allowedSlot)) { return true; }
                    }
                }
                return false;
            }
            return true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CheckItemAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                    (ItemTags.Any() ? $"ItemTags: {ItemTags.ColorizeObject()}, " : $"ItemIdentifiers: {ItemIdentifiers.ColorizeObject()}, ") +
                    $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}