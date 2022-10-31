using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CheckItemAction : BinaryOptionAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string ItemIdentifiers { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string ItemTags { get; set; }

        [Serialize(1, IsPropertySaveable.Yes)]
        public int Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the first target when the check succeeds.")]
        public Identifier ApplyTagToTarget { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool RequireEquipped { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes)]
        public int ItemContainerIndex { get; set; }

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
                foreach (XAttribute attribute in subElement.Attributes())
                {
                    if (PropertyConditional.IsValid(attribute))
                    {
                        conditionalList.Add(new PropertyConditional(attribute));
                    }
                }
                break;
            }
            conditionals = conditionalList;
        }

        protected override bool? DetermineSuccess()
        {
            var targets = ParentEvent.GetTargets(TargetTag);
            if (!targets.Any()) { return null; }
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
            int count = 0;
            foreach (Item item in inventory.FindAllItems(it => itemTags.Any(it.HasTag) || itemIdentifierSplit.Contains(it.Prefab.Identifier), recursive: true))
            {
                if (!ConditionalsMatch(item, character)) { continue; }
                count++;
                if (count >= Amount) { return true; }
            }
            return false;
        }

        private bool ConditionalsMatch(Item item, Character character = null)
        {
            if (item == null) { return false; }
            foreach (PropertyConditional conditional in conditionals)
            {
                if (!conditional.Matches(item))
                {
                    return false;
                }
            }
            if (RequireEquipped)
            {
                if (character == null) { return false; }
                return character.HasEquippedItem(item);
            }
            return true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CheckItemAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"ItemIdentifiers: {ItemIdentifiers.ColorizeObject()}" +
                   $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}