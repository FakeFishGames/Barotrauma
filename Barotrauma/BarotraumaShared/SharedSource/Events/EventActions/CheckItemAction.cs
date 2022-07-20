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
                if (!(target is Character chr)) { continue; }
                if (chr.Inventory == null) { continue; }

                if (itemTags.Any(tag => chr.Inventory.FindItemByTag(tag, recursive: true) != null)) { return true; }

                foreach (var identifier in itemIdentifierSplit)
                {
                    if (chr.Inventory.FindItemByIdentifier(identifier, recursive: true) != null)
                    {
                        return true;
                    }
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