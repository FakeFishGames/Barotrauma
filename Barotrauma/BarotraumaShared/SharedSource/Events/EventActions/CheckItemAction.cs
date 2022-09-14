using System;
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

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool RequireEquipped { get; set; }
        
        private readonly Identifier[] itemIdentifierSplit;
        private readonly Identifier[] itemTags;

        public CheckItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            itemIdentifierSplit = ItemIdentifiers.Split(',').ToIdentifiers();
            itemTags = ItemTags.Split(",").ToIdentifiers();
        }

        protected override bool? DetermineSuccess()
        {
            var targets = ParentEvent.GetTargets(TargetTag);
            if (!targets.Any()) { return null; }
            foreach (var target in targets)
            {
                if (target is Character character)
                {
                    if (RequireEquipped)
                    {
                        if (itemTags.Any(tag => character.HasEquippedItem(tag))) { return true; }
                        if (itemIdentifierSplit.Any(identifier => character.HasEquippedItem(identifier))) { return true; }
                        return false;
                    }
                    if (character.Inventory is not CharacterInventory inventory) { continue; }
                    if (itemTags.Any(tag => inventory.FindItemByTag(tag, recursive: true) is not null)) { return true; }
                    if (itemIdentifierSplit.Any(identifier => inventory.FindItemByIdentifier(identifier, recursive: true) is not null)) { return true; }
                }
                else if (target is Item item && item.OwnInventory is ItemInventory inventory)
                {
                    if (itemTags.Any(tag => inventory.FindItemByTag(tag, recursive: true) is not null)) { return true; }
                    if (itemIdentifierSplit.Any(identifier => inventory.FindItemByIdentifier(identifier, recursive: true) is not null)) { return true; }
                }
            }
            return false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(HasBeenDetermined())} {nameof(CheckItemAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"ItemIdentifiers: {ItemIdentifiers.ColorizeObject()}" +
                   $"Succeeded: {succeeded.ColorizeObject()})";
        }
    }
}