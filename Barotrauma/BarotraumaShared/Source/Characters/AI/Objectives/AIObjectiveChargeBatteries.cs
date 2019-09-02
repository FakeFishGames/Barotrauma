using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIObjectiveLoop<PowerContainer>
    {
        public override string DebugTag => "charge batteries";
        private IEnumerable<PowerContainer> batteryList;

        public AIObjectiveChargeBatteries(Character character, AIObjectiveManager objectiveManager, string option, float priorityModifier) 
            : base(character, objectiveManager, priorityModifier, option) { }

        protected override bool Filter(PowerContainer battery)
        {
            if (battery == null) { return false; }
            var item = battery.Item;
            if (item.Submarine == null) { return false; }
            if (item.CurrentHull == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (item.ConditionPercentage <= 0) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { return false; }
            if (Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !HumanAIController.IsFriendly(c))) { return false; }
            if (IsReady(battery)) { return false; }
            return true;
        }

        protected override float TargetEvaluation()
        {
            if (Option == "charge")
            {
                return Targets.Max(t => MathHelper.Lerp(100, 0, Math.Abs(PowerContainer.aiRechargeTargetRatio - t.RechargeRatio)));
            }
            else
            {

                return Targets.Max(t => MathHelper.Lerp(0, 100, t.RechargeRatio));
            }
        }

        protected override IEnumerable<PowerContainer> GetList()
        {
            if (batteryList == null)
            {
                if (character == null || character.Submarine == null)
                {
                    return new PowerContainer[0];
                }
                batteryList = character.Submarine.GetItems(true).Select(i => i.GetComponent<PowerContainer>()).Where(b => b != null);
            }
            return batteryList;
        }

        private bool IsReady(PowerContainer battery)
        {
            if (Option == "charge")
            {
                return battery.RechargeRatio >= PowerContainer.aiRechargeTargetRatio;
            }
            else
            {
                return battery.RechargeRatio <= 0;
            }
        }

        protected override AIObjective ObjectiveConstructor(PowerContainer battery) 
            => new AIObjectiveOperateItem(battery, character, objectiveManager, Option, false, priorityModifier: PriorityModifier)
            {
                IsLoop = false,
                completionCondition = () => IsReady(battery)
            };

        protected override void OnObjectiveCompleted(AIObjective objective, PowerContainer target) 
            => HumanAIController.RemoveTargets<AIObjectiveChargeBatteries, PowerContainer>(character, target);
    }
}
