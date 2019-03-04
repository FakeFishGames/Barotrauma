using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;
using System.Collections.Generic;

namespace Barotrauma
{
    class AIObjectiveFixLeaks : AIMultiObjective<Gap>
    {
        public override string DebugTag => "fix leaks";

        public AIObjectiveFixLeaks(Character character) : base (character, "") { }

        public override float GetPriority(AIObjectiveManager objectiveManager)
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
            foreach (Gap gap in Gap.GapList)
            {
                if (ignoreList.Contains(gap)) { continue; }
                if (gap.ConnectedWall == null) { continue; }
                // Door
                if (gap.ConnectedDoor != null || gap.Open <= 0.0f) { continue; }
                // Not linked to a hull -> ignore
                if (gap.linkedTo.All(l => l == null)) { continue; }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(gap, true)) { continue; }
                    if (!targets.Contains(gap))
                    {
                        targets.Add(gap);
                    }
                }
            }
            targets.Sort((x, y) => GetGapFixPriority(y).CompareTo(GetGapFixPriority(x)));
        }

        protected override void CreateObjectives()
        {
            foreach (var gap in targets)
            {
                if (!objectives.TryGetValue(gap, out AIObjective objective))
                {
                    objective = new AIObjectiveFixLeak(gap, character);
                    objectives.Add(gap, objective);
                    AddSubObjective(objective);
                }
            }
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

        protected override float Average(Gap gap) => gap.Open;

        protected override IEnumerable<Gap> GetList() => Gap.GapList;
    }
}
