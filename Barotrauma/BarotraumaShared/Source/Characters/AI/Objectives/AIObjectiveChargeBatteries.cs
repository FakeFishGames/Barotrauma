using Barotrauma.Items.Components;
using System.Collections.Generic;

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
                if (item.Prefab.Identifier != "battery" && !item.HasTag("battery")) continue;

                var powerContainer = item.GetComponent<PowerContainer>();
                availableBatteries.Add(powerContainer);
            }

            if (availableBatteries.Count == 0)
            {
                character?.Speak(TextManager.Get("DialogNoBatteries"), null, 4.0f, "nobatteries", 10.0f);
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
            return otherObjective is AIObjectiveChargeBatteries other && other.orderOption == orderOption;
        }

        protected override void Act(float deltaTime)
        {
            if (availableBatteries.Count == 0)
            {
                AddSubObjective(new AIObjectiveIdle(character));
                return;
            }
            foreach (PowerContainer battery in availableBatteries)
            {
                AddSubObjective(new AIObjectiveOperateItem(battery, character, orderOption, false));
            }
        }
    }
}
