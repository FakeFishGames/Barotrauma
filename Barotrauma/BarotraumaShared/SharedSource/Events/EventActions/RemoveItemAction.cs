using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class RemoveItemAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ItemIdentifier { get; set; }

        [Serialize(1, IsPropertySaveable.Yes)]
        public int Amount { get; set; }

        public RemoveItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (ItemIdentifier.IsEmpty)
            {
                ItemIdentifier = element.GetAttributeIdentifier("itemidentifiers", element.GetAttributeIdentifier("identifier", Identifier.Empty));
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
                if (target is Character character && character.Inventory != null || target is Item) 
                {
                    hasValidTargets = true;
                    break;
                }
            }
            if (!hasValidTargets) { return; }

            HashSet<Item> removedItems = new HashSet<Item>();
            foreach (Entity target in targets)
            {
                Inventory inventory = (target as Character)?.Inventory;
                if (inventory != null) 
                {
                    while (removedItems.Count < Amount)
                    {
                        var item = inventory.FindItem(it => 
                            it != null && 
                            !removedItems.Contains(it) &&
                            (ItemIdentifier.IsEmpty || it.Prefab.Identifier == ItemIdentifier), recursive: true);
                        if (item == null) { break; }
                        Entity.Spawner.AddItemToRemoveQueue(item);
                        removedItems.Add(item);
                    }                    
                }
                else if (target is Item item)
                {
                    if (ItemIdentifier.IsEmpty || item.Prefab.Identifier == ItemIdentifier)
                    {
                        Entity.Spawner.AddItemToRemoveQueue(item);
                        removedItems.Add(item);
                        if (removedItems.Count >= Amount) { break; }
                    }
                }
            }
            isFinished = true;
        }
    }
}
