using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class RemoveItemAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string ItemIdentifiers { get; set; }

        [Serialize(1, IsPropertySaveable.Yes)]
        public int Amount { get; set; }

        private readonly ImmutableHashSet<Identifier> itemIdentifierSplit;

        public RemoveItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (string.IsNullOrEmpty(ItemIdentifiers))
            {
                ItemIdentifiers = element.GetAttributeString("itemidentifier", element.GetAttributeString("identifier", string.Empty));
            }
            itemIdentifierSplit = ItemIdentifiers.Split(',').ToIdentifiers().ToImmutableHashSet();
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
                            (itemIdentifierSplit.Count == 0 || itemIdentifierSplit.Contains(it.Prefab.Identifier)), recursive: true);
                        if (item == null) { break; }
                        Entity.Spawner.AddItemToRemoveQueue(item);
                        removedItems.Add(item);
                    }                    
                }
                else if (target is Item item)
                {
                    if (itemIdentifierSplit.Count == 0 || itemIdentifierSplit.Contains(item.Prefab.Identifier))
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
