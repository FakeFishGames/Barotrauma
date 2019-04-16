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

        private float GetGapFixPriority(Gap gap)
        {
            if (gap == null) return 0.0f;

            //larger gap -> higher priority
            float gapPriority = (gap.IsHorizontal ? gap.Rect.Width : gap.Rect.Height) * gap.Open;

            //prioritize gaps that are close
            gapPriority /= Math.Max(Vector2.Distance(character.WorldPosition, gap.WorldPosition), 1.0f);

            //gaps to outside are much higher priority
            if (!gap.IsRoomToRoom) gapPriority *= 10.0f;

            return gapPriority;

        }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFixLeaks;
        protected override float Average(Gap gap) => gap.Open * 100;
        protected override IEnumerable<Gap> GetList() => Gap.GapList;
        protected override AIObjective ObjectiveConstructor(Gap gap) => new AIObjectiveFixLeak(gap, character);
    }
}
