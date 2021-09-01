using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIObjectiveLoop<PowerContainer>
    {
        public override string Identifier { get; set; } = "charge batteries";
        public override bool AllowAutomaticItemUnequipping => true;
        private IEnumerable<PowerContainer> batteryList;

        public AIObjectiveChargeBatteries(Character character, AIObjectiveManager objectiveManager, string option, float priorityModifier) 
            : base(character, objectiveManager, priorityModifier, option) { }

        protected override bool Filter(PowerContainer battery)
        {
            if (battery == null) { return false; }
            var item = battery.Item;
            if (item.IgnoreByAI(character)) { return false; }
            if (!item.IsInteractable(character)) { return false; }
            if (item.Submarine == null) { return false; }
            if (item.CurrentHull == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null)
            {
                if (!character.Submarine.IsConnectedTo(item.Submarine)) { return false; }
            }
            if (item.ConditionPercentage <= 0) { return false; }
            if (Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            if (IsReady(battery)) { return false; }
            return true;
        }

        protected override float TargetEvaluation()
        {
            if (Targets.None()) { return 0; }
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
            if (battery.HasBeenTuned && character.IsDismissed) { return true; }
            if (Option == "charge")
            {
                return battery.RechargeRatio >= PowerContainer.aiRechargeTargetRatio;
            }
            else
            {
                return battery.RechargeRatio <= 0;
            }
        }

        protected override AIObjective ObjectiveConstructor(PowerContainer battery) =>
            new AIObjectiveOperateItem(battery, character, objectiveManager, Option, false, priorityModifier: PriorityModifier)
            {
                IsLoop = false,
                Override = !character.IsDismissed,
                completionCondition = () => IsReady(battery)
            };

        protected override void OnObjectiveCompleted(AIObjective objective, PowerContainer target) 
            => HumanAIController.RemoveTargets<AIObjectiveChargeBatteries, PowerContainer>(character, target);
    }
}
