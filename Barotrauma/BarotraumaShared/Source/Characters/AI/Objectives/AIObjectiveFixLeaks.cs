using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFixLeaks : AIMultiObjective<Gap>
    {
        public override string DebugTag => "fix leaks";
        private AIObjectiveFindDivingGear findDivingGear;
        private bool cannotFindDivingGear;

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
            targets.Clear();
            foreach (Gap gap in Gap.GapList)
            {
                if (ignoreList.Contains(gap)) { continue; }
                if (cannotFindDivingGear && gap.IsRoomToRoom) { continue; }
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

        protected override void Act(float deltaTime)
        {
            foreach (var objective in objectives)
            {
                if (!objective.Value.CanBeCompleted)
                {
                    ignoreList.Add(objective.Key);
                }
            }
            SyncRemovedObjectives(objectives, targets);
            if (targets.None())
            {
                FindTargets();
            }
            if (!cannotFindDivingGear && targets.Any(g => g.IsRoomToRoom))
            {
                if (findDivingGear == null)
                {
                    findDivingGear = new AIObjectiveFindDivingGear(character, true);
                    AddSubObjective(findDivingGear);
                }
                else if (!findDivingGear.CanBeCompleted)
                {
                    cannotFindDivingGear = true;
                }
            }
            else if (findDivingGear == null || findDivingGear.IsCompleted() || cannotFindDivingGear)
            {
                if (objectives.None())
                {
                    CreateObjectives();
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
    }
}
