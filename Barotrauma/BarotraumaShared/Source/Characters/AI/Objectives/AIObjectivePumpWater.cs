using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class AIObjectivePumpWater : AIObjectiveLoop<Pump>
    {
        public override string DebugTag => "pump water";
        public override bool KeepDivingGearOn => true;
        private readonly IEnumerable<Pump> pumpList;

        public AIObjectivePumpWater(Character character, string option, float priorityModifier = 1) : base(character, option, priorityModifier)
        {
            pumpList = character.Submarine.GetItems(true).Select(i => i.GetComponent<Pump>()).Where(p => p != null);
        }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectivePumpWater && otherObjective.Option == Option;

        //availablePumps = allPumps.Where(p => !p.Item.HasTag("ballast") && p.Item.Connections.None(c => c.IsPower && p.Item.GetConnectedComponentsRecursive<Steering>(c).None())).ToList();
        protected override void FindTargets()
        {
            if (option == null) { return; }
            base.FindTargets();
        }

        protected override bool Filter(Pump pump)
        {
            if (pump.Item.HasTag("ballast")) { return false; }
            if (pump.Item.Submarine == null) { return false; }
            if (pump.Item.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(pump.Item, true)) { return false; }
            if (option == "stoppumping")
            {
                if (!pump.IsActive || pump.FlowPercentage == 0.0f) { return false; }
            }
            else
            {
                if (!pump.Item.InWater) { return false; }
                if (pump.IsActive && pump.FlowPercentage <= -90.0f) { return false; }
            }
            return true;
        }
        protected override IEnumerable<Pump> GetList() => pumpList;
        protected override AIObjective ObjectiveConstructor(Pump pump) => new AIObjectiveOperateItem(pump, character, Option, false) { IsLoop = true };
        protected override float TargetEvaluation() => targets.Max(t => MathHelper.Lerp(100, 0, t.CurrFlow / t.MaxFlow));
    }
}
