using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    /// <summary>
    /// Removes (deletes) a specific item or items.
    /// </summary>
    class RemoveItemAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the item(s) to remove, or characters whose inventory the item(s) should be removed from.")]
        public Identifier TargetTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional list of identifiers the item(s) must have. You might for example want to go through all tagged items inside a cabinet, but only remove specific types of items.")]
        public string ItemIdentifiers { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Optional list of tags the item(s) must have.")]
        public string ItemTags { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "Maximum number of items to remove.")]
        public int Amount { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If set, the action removes the items contained inside the target item, instead of the target item itself.")]
        public bool RemoveContainedItems { get; set; }

        private readonly ImmutableHashSet<Identifier> itemIdentifierSplit;
        private readonly ImmutableHashSet<Identifier> itemTagsSplit;

        public RemoveItemAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (string.IsNullOrEmpty(ItemIdentifiers))
            {
                ItemIdentifiers = element.GetAttributeString("itemidentifier", element.GetAttributeString("identifier", string.Empty));
            }
            itemIdentifierSplit = ItemIdentifiers.ToIdentifiers().ToImmutableHashSet();
            if (string.IsNullOrEmpty(ItemTags))
            {
                ItemTags = element.GetAttributeString("itemtags", element.GetAttributeString("tag", string.Empty));
            }
            itemTagsSplit = ItemTags.ToIdentifiers().ToImmutableHashSet();
        }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset(bool shouldEndConversations = true)
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
                            (itemTagsSplit.Count == 0 || it.HasTag(itemTagsSplit)) &&
                            (itemIdentifierSplit.Count == 0 || itemIdentifierSplit.Contains(it.Prefab.Identifier)), recursive: true);
                        if (item == null) { break; }
                        Entity.Spawner.AddItemToRemoveQueue(item);
                        removedItems.Add(item);
                    }                    
                }
                else if (target is Item item)
                {
                    if ((itemTagsSplit.Count == 0 || item.HasTag(itemTagsSplit)) &&
                        (itemIdentifierSplit.Count == 0 || itemIdentifierSplit.Contains(item.Prefab.Identifier)))
                    {
                        if (RemoveContainedItems)
                        {
                            foreach (var containedItem in item.ContainedItems)
                            {
                                Entity.Spawner.AddItemToRemoveQueue(containedItem);
                                removedItems.Add(containedItem);
                                if (removedItems.Count >= Amount) { break; }
                            }
                            if (removedItems.Count >= Amount) { break; }
                        }
                        else
                        {
                            Entity.Spawner.AddItemToRemoveQueue(item);
                            removedItems.Add(item);
                            if (removedItems.Count >= Amount) { break; }
                        }
                    }
                }
            }
            isFinished = true;
        }
    }
}
