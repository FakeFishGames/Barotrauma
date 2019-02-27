using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

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
            if (availableBatteries.None()) { return 0; }
            float avgNeedOfCharge = availableBatteries.Average(b => 100 - b.ChargePercentage);
            if (objectiveManager.CurrentOrder == this && avgNeedOfCharge > 10)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return MathHelper.Lerp(0, 50, avgNeedOfCharge / 100);
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveChargeBatteries other && other.orderOption == orderOption;
        }

        protected override void Act(float deltaTime)
        {
            SyncRemovedObjectives(chargeObjectives, availableBatteries);
            availableBatteries.Sort((x, y) => x.ChargePercentage.CompareTo(y.ChargePercentage));
            foreach (PowerContainer battery in availableBatteries)
            {
                // Ignore batteries that are almost full
                if (battery.ChargePercentage > 90) { continue; }
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
