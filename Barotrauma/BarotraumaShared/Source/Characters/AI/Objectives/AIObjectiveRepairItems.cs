using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjective
    {
        public AIObjectiveRepairItems(Character character)
            : base(character, "")
        {
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            GetBrokenItems();
            if (subObjectives.Count > 0 && objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 1.0f;
        }
                
        public override bool IsCompleted()
        {
            return false;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveRepairItems;
        }

        protected override void Act(float deltaTime)
        {
            GetBrokenItems();
        }

        private void GetBrokenItems()
        {
            foreach (Item item in Item.ItemList)
            {
                if (item.Condition > 0.0f) continue;
                foreach (FixRequirement fixRequirement in item.FixRequirements)
                {
                    //ignore fix requirements that are already fixed or can't be fixed by this character
                    if (fixRequirement.Fixed || !fixRequirement.HasRequiredSkills(character)) continue;

                    AddSubObjective(new AIObjectiveRepairItem(character, item));
                    break;
                }
            }
        }
    }
}
