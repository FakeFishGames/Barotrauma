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

        public AIObjectiveFixLeaks(Character character, float priorityModifier = 1) : base(character, "", priorityModifier) { }

        protected override void FindTargets()
        {
            base.FindTargets();
            // Sorting should be handled by the objective manager, because the targets should be subobjectives.
            //targets.Sort((x, y) => GetLeakFixPriority(y).CompareTo(GetLeakFixPriority(x)));
            // TODO: Add a dialog when no leaks are found.
        }

        protected override bool Filter(Gap gap)
        {
            bool ignore = gap.ConnectedWall == null || gap.ConnectedDoor != null || gap.Open <= 0 || gap.linkedTo.All(l => l == null);
            if (!ignore)
            {
                if (gap.Submarine == null) { ignore = true; }
                else if (gap.Submarine.TeamID != character.TeamID) { ignore = true; }
                else if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(gap, true)) { ignore = true; }
            }
            return !ignore;
        }

        public static float GetLeakSeverity(Gap leak)
        {
            if (leak == null) { return 0; }
            float sizeFactor = MathHelper.Lerp(1, 10, MathUtils.InverseLerp(0, 200, (leak.IsHorizontal ? leak.Rect.Width : leak.Rect.Height)));
            float severity = sizeFactor * leak.Open;
            if (!leak.IsRoomToRoom) { severity *= 50; }
            return MathHelper.Min(severity, 100);
        }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFixLeaks;
        protected override float TargetEvaluation() => targets.Max(t => GetLeakSeverity(t));
        protected override IEnumerable<Gap> GetList() => Gap.GapList;
        protected override AIObjective ObjectiveConstructor(Gap gap) => new AIObjectiveFixLeak(gap, character, PriorityModifier);
    }
}
