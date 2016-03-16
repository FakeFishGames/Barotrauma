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
                var oxygenTank = Array.Find(containedItems, i => i.Name == "Oxygen Tank");

                if (oxygenTank != null)
                {
                    if (oxygenTank.Condition > 0.0f)
                    {
                        return;
                    }
                    else
                    {
                        oxygenTank.Drop();
                    }
                }


                if (!(subObjective is AIObjectiveContainItem) || subObjective.IsCompleted())
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

        public override float GetPriority(Character character)
        {
            if (character.AnimController.CurrentHull == null) return 100.0f;

            return 100.0f - character.Oxygen;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveFindDivingGear;
        }
    }
}
