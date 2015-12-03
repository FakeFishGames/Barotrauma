using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveGetItem : AIObjective
    {
        private string itemName;

        private Item targetItem;

        private int currSearchIndex;

        private bool canBeCompleted;

        public bool IgnoreContainedItems;

        public override bool CanBeCompleted
        {
            get { return canBeCompleted; }
        }


        public AIObjectiveGetItem(Character character, string itemName)
            : base (character, "")
        {
            canBeCompleted = true;

            currSearchIndex = 0;

            this.itemName = itemName;
        }

        protected override void Act(float deltaTime)
        {
            if (targetItem != null)
            {
                if (Vector2.Distance(character.SimPosition, targetItem.SimPosition) < targetItem.PickDistance)
                {
                    targetItem.Pick(character, false, true);
                }
            }
            
            if (currSearchIndex >= Item.ItemList.Count) 
            {
                canBeCompleted = false;
                return;
            }

            currSearchIndex++;

            if (!Item.ItemList[currSearchIndex].HasTag(itemName) && Item.ItemList[currSearchIndex].Name != itemName) return;
            if (IgnoreContainedItems && Item.ItemList[currSearchIndex].container != null) return;
            
            targetItem = Item.ItemList[currSearchIndex];

            Item moveToTarget = targetItem;
            while (moveToTarget.container != null)
            {
                moveToTarget = moveToTarget.container;
            }

            subObjectives.Add(new AIObjectiveGoTo(moveToTarget, character));
            

        }
        
        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveGetItem getItem = otherObjective as AIObjectiveGetItem;
            if (getItem == null) return false;
            return (getItem.itemName == itemName);
        }

        public override bool IsCompleted()
        {
            return character.Inventory.FindItem(itemName) != null;
        }
    }
}
