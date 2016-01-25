using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveFindDivingGear : AIObjective
    {
        private AIObjective subObjective;

        private string gearName;

        public override bool IsCompleted()
        {
            var item = character.Inventory.FindItem(gearName);
            if (item == null) return false;

            var containedItems = item.ContainedItems;
            var oxygenTank = Array.Find(containedItems, i => i.Name == "Oxygen Tank" && i.Condition > 0.0f);
            return oxygenTank != null;
        }

        public AIObjectiveFindDivingGear(Character character, bool needDivingSuit)
            : base(character, "")
        {
            gearName = needDivingSuit ? "Diving Suit" : "diving";
        }

        protected override void Act(float deltaTime)
        {
            var item = character.Inventory.FindItem(gearName);
            if (item == null)
            {
                //get a diving mask/suit first
                if (!(subObjective is AIObjectiveGetItem))
                {
                    subObjective = new AIObjectiveGetItem(character, gearName, true);
                }
            }
            else
            {
                var containedItems = item.ContainedItems;
                if (containedItems == null) return;

                //check if there's an oxygen tank in the mask
                var oxygenTank = Array.Find(containedItems, i => i.Name == "Oxygen Tank" && i.Condition > 0.0f);

                if (oxygenTank != null)
                {
                    //isCompleted = true;
                    return;
                }


                if (!(subObjective is AIObjectiveContainItem))
                {
                    subObjective = new AIObjectiveContainItem(character, "Oxygen Tank", item.GetComponent<ItemContainer>());
                }
            }

            if (subObjective != null)
            {
                subObjective.TryComplete(deltaTime);

                //isCompleted = subObjective.IsCompleted();
            }
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveFindDivingGear;
        }
    }
}
