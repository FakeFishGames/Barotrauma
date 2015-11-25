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

        public override bool CanBeCompleted
        {
            get { return canBeCompleted; }
        }

        public AIObjectiveGetItem(Character character, string itemName)
            : base (character)
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
                return;
            }
            
            if (currSearchIndex >= Item.ItemList.Count) 
            {
                canBeCompleted = false;
                return;
            }
            
            if (Item.ItemList[currSearchIndex].HasTag(itemName) || Item.ItemList[currSearchIndex].Name == itemName)
            {
                targetItem = Item.ItemList[currSearchIndex];

                while (targetItem.container != null)
                {
                    targetItem = targetItem.container;
                }

                subObjectives.Add(new AIObjectiveGoTo(targetItem.Position, character));
            }

            currSearchIndex++;
        }
        
        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveGetItem getItem = otherObjective as AIObjectiveGetItem;
            if (getItem == null) return false;
            return (getItem.itemName == itemName);
        }

        public override bool IsCompleted()
        {
            return character.Inventory.Items.FirstOrDefault(i => i != null && (i.HasTag(itemName) || i.Name == itemName)) != null;
        }
    }
}
