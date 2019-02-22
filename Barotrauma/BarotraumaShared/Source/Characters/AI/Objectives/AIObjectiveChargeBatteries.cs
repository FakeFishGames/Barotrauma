using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIObjective
    {
        public override string DebugTag => "charge batteries";

        private List<PowerContainer> availableBatteries = new List<PowerContainer>();
        private Dictionary<PowerContainer, AIObjectiveOperateItem> chargeObjectives = new Dictionary<PowerContainer, AIObjectiveOperateItem>();
        private string orderOption;
        
        public AIObjectiveChargeBatteries(Character character, string option) : base(character, option)
        {
            orderOption = option;
            
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
            SyncRemovedObjectives(chargeObjectives, availableBatteries);
            foreach (PowerContainer battery in availableBatteries)
            {
                if (!chargeObjectives.TryGetValue(battery, out AIObjectiveOperateItem objective))
                {
                    objective = new AIObjectiveOperateItem(battery, character, orderOption, false);
                    chargeObjectives.Add(battery, objective);
                    AddSubObjective(objective);
                }
            }
        }
    }
}
