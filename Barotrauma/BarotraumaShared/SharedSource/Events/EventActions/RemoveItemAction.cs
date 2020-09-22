using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class RemoveItemAction : EventAction
    {
        [Serialize("", true)]
        public string TargetTag { get; set; }

        [Serialize("", true)]
        public string ItemIdentifier { get; set; }

        [Serialize(1, true)]
        public int Amount { get; set; }

        public RemoveItemAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) 
        { 
            if (string.IsNullOrWhiteSpace(ItemIdentifier))
            {
                ItemIdentifier = element.GetAttributeString("itemidentifiers", "");
            }
            if (string.IsNullOrWhiteSpace(ItemIdentifier))
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\" - RemoveItemAction without an item identifier.");
            }
        }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            var targets = ParentEvent.GetTargets(TargetTag);
            bool hasValidTargets = false;
            foreach (Entity target in targets)
            {
                if (target is Character character && character.Inventory != null) 
                {
                    hasValidTargets = true;
                    break;
                }
            }
            if (!hasValidTargets) { return; }

            List<Item> usedItems = new List<Item>();
            foreach (Entity target in targets)
            {
                Inventory inventory = (target as Character)?.Inventory;
                if (inventory == null) { continue; }
                while (usedItems.Count < Amount)
                {
                    var item = inventory.FindItem(it => 
                        it != null && 
                        !usedItems.Contains(it) &&
                        it.Prefab.Identifier.Equals(ItemIdentifier, StringComparison.InvariantCultureIgnoreCase), recursive: true);
                    if (item == null) { break; }
                    Entity.Spawner.AddToRemoveQueue(item);
                    usedItems.Add(item);
                }
            }
            isFinished = true;
        }
    }
}
