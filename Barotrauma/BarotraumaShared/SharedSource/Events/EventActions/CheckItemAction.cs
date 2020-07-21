using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CheckItemAction : BinaryOptionAction
    {
        [Serialize("", true)]
        public string TargetTag { get; set; }

        [Serialize("", true)]
        public string ItemIdentifiers { get; set; }

        [Serialize("", true)]
        public string ItemTags { get; set; }
        
        private readonly string[] itemIdentifierSplit;
        private readonly string[] itemTags;

        public CheckItemAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element)
        {
            itemIdentifierSplit = ItemIdentifiers.Split(',');
            itemTags = ItemTags.Split(",");
        }

        protected override bool? DetermineSuccess()
        {
            var targets = ParentEvent.GetTargets(TargetTag);
            if (!targets.Any()) { return null; }
            foreach (var target in targets)
            {
                if (!(target is Character chr)) { continue; }
                if (chr.Inventory == null) { continue; }

                if (itemTags.Any(tag => chr.Inventory.Items.Any(item => item != null && item.HasTag(tag)))) { return true; }

                foreach (var identifier in itemIdentifierSplit)
                {
                    if (chr.Inventory.Items.Any(it => it != null && it.Prefab.Identifier.Equals(identifier, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public override string ToDebugString()
        {
            string subActionStr = "";
            if (succeeded.HasValue)
            {
                subActionStr = $"\n            Sub action: {(succeeded.Value ? Success : Failure)?.CurrentSubAction.ColorizeObject()}";
            }
            return $"{ToolBox.GetDebugSymbol(DetermineFinished())} {nameof(CheckItemAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"ItemIdentifiers: {ItemIdentifiers.ColorizeObject()}" +
                   $"Succeeded: {(succeeded.HasValue ? succeeded.Value.ToString() : "not determined").ColorizeObject()})" +
                   subActionStr;
        }
    }
}