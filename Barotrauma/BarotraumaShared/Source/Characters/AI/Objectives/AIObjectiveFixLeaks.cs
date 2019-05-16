using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;
using System.Collections.Generic;

namespace Barotrauma
{
    class AIObjectiveFixLeaks : AIObjectiveLoop<Gap>
    {
        public override string DebugTag => "fix leaks";
        public override bool KeepDivingGearOn => true;
        public override bool ForceRun => true;

        public AIObjectiveFixLeaks(Character character) : base (character, "") { }

        protected override void FindTargets()
        {
            if (character.Submarine == null) { return 0; }
            if (targets.None()) { return 0; }
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return MathHelper.Lerp(0, AIObjectiveManager.OrderPriority, targets.Average(t => Average(t)));
        }

        protected override void FindTargets()
        {
            base.FindTargets();
            targets.Sort((x, y) => GetGapFixPriority(y).CompareTo(GetGapFixPriority(x)));
        }

        protected override bool Filter(Gap gap)
        {
            bool ignore = ignoreList.Contains(gap) || gap.ConnectedWall == null || gap.ConnectedDoor != null || gap.Open <= 0 || gap.linkedTo.All(l => l == null);
            if (!ignore)
            {
                if (gap.Submarine == null) { ignore = true; }
                else if (gap.Submarine.TeamID != character.TeamID) { ignore = true; }
                else if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(gap, true)) { ignore = true; }
            }
            return ignore;
        }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFixLeaks;
        protected override float TargetEvaluation() => targets.Max(t => GetLeakSeverity(t));
        protected override IEnumerable<Gap> GetList() => Gap.GapList;
        protected override AIObjective ObjectiveConstructor(Gap gap) => new AIObjectiveFixLeak(gap, character, objectiveManager, PriorityModifier);

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFixLeaks;
        protected override float Average(Gap gap) => gap.Open;
        protected override IEnumerable<Gap> GetList() => Gap.GapList;
        protected override AIObjective ObjectiveConstructor(Gap gap) => new AIObjectiveFixLeak(gap, character);
    }
}
