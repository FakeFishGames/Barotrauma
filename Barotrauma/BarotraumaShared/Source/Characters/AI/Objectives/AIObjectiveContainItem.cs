using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveContainItem: AIObjective
    {
        private string[] itemNames;

        private ItemContainer container;
        
        bool isCompleted;

        public bool IgnoreAlreadyContainedItems;

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
            return isCompleted || itemNames.Any(name => container.Inventory.FindItem(name) != null);
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
                var getItem = new AIObjectiveGetItem(character, itemNames);
                getItem.IgnoreContainedItems = IgnoreAlreadyContainedItems;
                AddSubObjective(getItem);
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
                    && !container.Item.IsInsideTrigger(character.Position))
                {
                    AddSubObjective(new AIObjectiveGoTo(container.Item, character));
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
