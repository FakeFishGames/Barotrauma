using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveContainItem: AIObjective
    {
        public int MinContainedAmount = 1;

        private string[] itemNames;

        private ItemContainer container;

        private bool isCompleted;

        public bool IgnoreAlreadyContainedItems;

        public Func<Item, float> GetItemPriority;

        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveGoTo goToObjective;

        public AIObjectiveContainItem(Character character, string itemName, ItemContainer container)
            : this(character, new string[] { itemName }, container)
        {
        }

        public AIObjectiveContainItem(Character character, string[] itemNames, ItemContainer container)
            : base (character, "")
        {
            this.itemNames = itemNames;
            this.container = container;
        }

        public override bool IsCompleted()
        {
            if (isCompleted) return true;

            int containedItemCount = 0;
            foreach (Item item in container.Inventory.Items)
            {
                if (item != null && itemNames.Any(name => item.Prefab.NameMatches(name) || item.HasTag(name))) containedItemCount++;
            }

            return containedItemCount >= MinContainedAmount;
        }

        public override bool CanBeCompleted
        {
            get
            {
                if (goToObjective != null)
                {
                    return goToObjective.CanBeCompleted;
                }

                return getItemObjective == null || getItemObjective.CanBeCompleted;
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 1.0f;
        }

        protected override void Act(float deltaTime)
        {
            if (isCompleted) return;

            //get the item that should be contained
            var itemToContain = character.Inventory.FindItem(itemNames);
            if (itemToContain == null)
            {
                getItemObjective = new AIObjectiveGetItem(character, itemNames);
                getItemObjective.GetItemPriority = GetItemPriority;
                getItemObjective.IgnoreContainedItems = IgnoreAlreadyContainedItems;
                AddSubObjective(getItemObjective);
                return;
            }

            if (container.Item.ParentInventory == character.Inventory)
            {
                var containedItems = container.Inventory.Items;
                //if there's already something in the mask (empty oxygen tank?), drop it
                var existingItem = containedItems.FirstOrDefault(i => i != null);
                if (existingItem != null) existingItem.Drop(character);
                
                character.Inventory.RemoveItem(itemToContain);
                container.Inventory.TryPutItem(itemToContain, null);
            }
            else
            {
                if (Vector2.Distance(character.Position, container.Item.Position) > container.Item.InteractDistance
                    && !container.Item.IsInsideTrigger(character.WorldPosition))
                {
                    goToObjective = new AIObjectiveGoTo(container.Item, character);
                    AddSubObjective(goToObjective);
                    return;
                }

                container.Combine(itemToContain);
            }

            isCompleted = true;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveContainItem objective = otherObjective as AIObjectiveContainItem;
            if (objective == null) return false;
            if (objective.container != container) return false;
            if (objective.itemNames.Length != itemNames.Length) return false;

            for (int i = 0; i < itemNames.Length; i++)
            {
                if (objective.itemNames[i] != itemNames[i]) return false;
            }

            return true;
        }

    
    }
}
