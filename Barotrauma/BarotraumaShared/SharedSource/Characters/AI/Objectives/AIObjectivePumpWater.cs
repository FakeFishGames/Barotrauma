using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectivePumpWater : AIObjectiveLoop<Pump>
    {
        public override Identifier Identifier { get; set; } = "pump water".ToIdentifier();
        public override bool KeepDivingGearOn => true;
        public override bool AllowAutomaticItemUnequipping => true;

        private List<Pump> pumpList;

        public AIObjectivePumpWater(Character character, AIObjectiveManager objectiveManager, Identifier option, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier, option) { }

        protected override void FindTargets()
        {
            if (Option == null) { return; }
            base.FindTargets();
        }

        protected override bool Filter(Pump pump)
        {
            if (pump?.Item == null || pump.Item.Removed) { return false; }
            if (pump.Item.IgnoreByAI(character)) { return false; }
            if (!pump.Item.IsInteractable(character)) { return false; }
            if (pump.IsAutoControlled) { return false; }
            if (pump.Item.ConditionPercentage <= 0) { return false; }
            if (pump.Item.CurrentHull.FireSources.Count > 0) { return false; }
            if (character.Submarine != null)
            {
                if (!character.Submarine.IsConnectedTo(pump.Item.Submarine)) { return false; }
            }
            if (Character.CharacterList.Any(c => c.CurrentHull == pump.Item.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            if (pump.Item.IsClaimedByBallastFlora) { return false; }
            if (IsReady(pump)) { return false; }
            return true;
        }
        protected override IEnumerable<Pump> GetList()
        {
            if (pumpList == null)
            {
                if (character == null || character.Submarine == null) { return Array.Empty<Pump>(); }

                pumpList = new List<Pump>();
                foreach (Item item in character.Submarine.GetItems(true))
                {
                    var pump = item.GetComponent<Pump>();
                    if (pump == null || pump.Item.Submarine == null || pump.Item.CurrentHull == null) { continue; }
                    if (pump.Item.Submarine.TeamID != character.TeamID) { continue; }
                    if (pump.Item.HasTag("ballast")) { continue; }
                    pumpList.Add(pump);
                }
            }
            return pumpList;
        }

        protected override float TargetEvaluation()
        {
            if (Targets.None()) { return 0; }
            if (Option == "stoppumping")
            {
                return Targets.Max(t => MathHelper.Lerp(0, 100, Math.Abs(t.FlowPercentage / 100)));
            }
            else
            {
                return Targets.Max(t => MathHelper.Lerp(100, 0, Math.Abs(-t.FlowPercentage / 100)));
            }
        }

        private bool IsReady(Pump pump)
        {
            if (Option == "stoppumping")
            {
                return !pump.IsActive || MathUtils.NearlyEqual(pump.FlowPercentage, 0);
            }
            else
            {
                return !pump.Item.InWater || pump.IsActive && pump.FlowPercentage <= -99.9f;
            }
        }

        protected override AIObjective ObjectiveConstructor(Pump pump)
            => new AIObjectiveOperateItem(pump, character, objectiveManager, Option, false)
            {
                IsLoop = false,
                completionCondition = () => IsReady(pump)
            };

        protected override void OnObjectiveCompleted(AIObjective objective, Pump target)
            => HumanAIController.RemoveTargets<AIObjectivePumpWater, Pump>(character, target);
    }
}
