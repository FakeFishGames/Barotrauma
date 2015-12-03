using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveContainItem: AIObjective
    {
        private string itemName;

        private ItemContainer container;

        bool canBeCompleted;

        bool isCompleted;

        public bool IgnoreAlreadyContainedItems;

        public AIObjectiveContainItem(Character character, string itemName, ItemContainer container)
            : base (character, "")
        {
            this.itemName = itemName;
            this.container = container;

            //check if the container has room for more items
            canBeCompleted = false;
            foreach (Item contained in container.inventory.Items)
            {
                if (contained != null) continue;
                canBeCompleted = true;
                break;
            }
        }

        public override bool IsCompleted()
        {
            return isCompleted;
        }

        protected override void Act(float deltaTime)
        {
            if (isCompleted) return;

            //get the item that should be contained
            var itemToContain = character.Inventory.FindItem(itemName);
            if (itemToContain == null)
            {
                var getItem = new AIObjectiveGetItem(character, itemName);
                getItem.IgnoreContainedItems = IgnoreAlreadyContainedItems;
                AddSubObjective(getItem);
                return;
            }

            if (Vector2.Distance(character.SimPosition, container.Item.SimPosition) > container.Item.PickDistance
                && !container.Item.IsInsideTrigger(character.Position))
            {
                AddSubObjective(new AIObjectiveGoTo(container.Item, character));
                return;
            }

            container.Combine(itemToContain);
            isCompleted = true;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveContainItem objective = otherObjective as AIObjectiveContainItem;
            if (objective == null) return false;

            return objective.itemName == itemName && objective.container == container;
        }

    
    }
}
