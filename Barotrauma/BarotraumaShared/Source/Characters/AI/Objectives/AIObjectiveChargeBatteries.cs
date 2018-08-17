using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIObjective
    {
        private List<PowerContainer> availableBatteries;

        private string orderOption;

        public AIObjectiveChargeBatteries(Character character, string option)
            : base(character, option)
        {
            orderOption = option;

            availableBatteries = new List<PowerContainer>();
            foreach (Item item in Item.ItemList)
            {
                if (item.Submarine == null) continue;
                if (!item.Prefab.NameMatches("Battery") && !item.HasTag("Battery")) continue;

                var powerContainer = item.GetComponent<PowerContainer>();
                availableBatteries.Add(powerContainer);
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

        public override bool IsCompleted()
        {
            return false;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            var other = otherObjective as AIObjectiveChargeBatteries;
            return other != null && other.orderOption == orderOption;
        }

        protected override void Act(float deltaTime)
        {
            foreach (PowerContainer battery in availableBatteries)
            {
                AddSubObjective(new AIObjectiveOperateItem(battery, character, orderOption, false));
            }
        }
    }
}
