using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveContainItem: AIObjective
    {
        public override string DebugTag => "contain item";

        public Func<Item, float> GetItemPriority;

        public int targetItemCount = 1;
        public string[] ignoredContainerIdentifiers;
        public bool checkInventory = true;

        //can either be a tag or an identifier
        public readonly string[] itemIdentifiers;
        public readonly ItemContainer container;

        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveGoTo goToObjective;

        private readonly HashSet<Item> containedItems = new HashSet<Item>();

        public AIObjectiveContainItem(Character character, string itemIdentifier, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : this(character, new string[] { itemIdentifier }, container, objectiveManager, priorityModifier) { }

        public AIObjectiveContainItem(Character character, string[] itemIdentifiers, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base (character, objectiveManager, priorityModifier)
        {
            this.itemIdentifiers = itemIdentifiers;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }

            this.container = container;
        }

        public override bool IsCompleted()
        {
            int containedItemCount = 0;
            foreach (Item item in container.Inventory.Items)
            {
                if (item != null && itemIdentifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id)))
                {
                    containedItemCount++;
                }
            }
            return containedItemCount >= targetItemCount;
        }

        public override float GetPriority()
        {
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return 1.0f;
        }

        protected override void Act(float deltaTime)
        {
            //get the item that should be contained
            Item itemToContain = null;
            foreach (string identifier in itemIdentifiers)
            {
                itemToContain = character.Inventory.FindItemByIdentifier(identifier, true) ?? character.Inventory.FindItemByTag(identifier, true);
                if (itemToContain != null && itemToContain.Condition > 0.0f) { break; }
            }            
            if (itemToContain == null)
            {
                if (getItemObjective != null)
                {
                    if (getItemObjective.IsCompleted())
                    {
                        if (getItemObjective.TargetItem != null)
                        {
                            containedItems.Add(getItemObjective.TargetItem);
                        }
                        else
                        {
                            // Reduce the target item count to prevent getting stuck here, if the target item for some reason is null, which shouldn't happen.
                            targetItemCount--;
                        }
                        getItemObjective = null;
                    }
                    else if (!getItemObjective.CanBeCompleted)
                    {
                        getItemObjective = null;
                        targetItemCount--;
                    }
                }
                TryAddSubObjective(ref getItemObjective, () =>
                    new AIObjectiveGetItem(character, itemIdentifiers, objectiveManager, checkInventory: checkInventory)
                    {
                        GetItemPriority = GetItemPriority,
                        ignoredContainerIdentifiers = ignoredContainerIdentifiers,
                        ignoredItems = containedItems
                    });
                return;
            }
            if (container.Item.ParentInventory == character.Inventory)
            {           
                character.Inventory.RemoveItem(itemToContain);
                container.Inventory.TryPutItem(itemToContain, null);
            }
            else
            {
                if (!character.CanInteractWith(container.Item, out _, checkLinked: false))
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(container.Item, character, objectiveManager));
                    return;
                }
                container.Combine(itemToContain);
            }
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveContainItem objective)) { return false; }
            if (objective.container != container) { return false; }
            if (objective.itemIdentifiers.Length != itemIdentifiers.Length) { return false; }
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                if (objective.itemIdentifiers[i] != itemIdentifiers[i])
                {
                    return false;
                }
            }
            return true;
        }    
    }
}
