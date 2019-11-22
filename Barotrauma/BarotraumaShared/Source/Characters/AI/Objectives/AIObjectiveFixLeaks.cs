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
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool IgnoreUnsafeHulls => true;

        public AIObjectiveFixLeaks(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Gap gap) => IsValidTarget(gap, character);

        public static float GetLeakSeverity(Gap leak)
        {
            if (leak == null) { return 0; }
            float sizeFactor = MathHelper.Lerp(1, 10, MathUtils.InverseLerp(0, 200, leak.Size));
            float severity = sizeFactor * leak.Open;
            if (!leak.IsRoomToRoom)
            {
                severity *= 10;
                // If there is a leak in the outer walls, the severity cannot be lower than 10, no matter how small the leak
                return MathHelper.Clamp(severity, 10, 100);
            }
            else
            {
                return MathHelper.Min(severity, 100);
            }
        }

        protected override float TargetEvaluation()
        {
            int otherFixers = HumanAIController.CountCrew(c => c != HumanAIController && c.ObjectiveManager.IsCurrentObjective<AIObjectiveFixLeaks>());
            int leaks = Targets.Count;
            bool anyFixers = otherFixers > 0;
            float ratio = anyFixers ? leaks / otherFixers : 1;
            if (objectiveManager.CurrentOrder == this)
            {
                return Targets.Sum(t => GetLeakSeverity(t)) * ratio;
            }
            else
            {
                if (anyFixers && (ratio <= 1 || otherFixers > 5 || otherFixers / HumanAIController.CountCrew() > 0.75f))
                {
                    // Enough fixers
                    return 0;
                }
                return Targets.Sum(t => GetLeakSeverity(t)) * ratio;
            }
        }

        protected override IEnumerable<Gap> GetList() => Gap.GapList;
        protected override AIObjective ObjectiveConstructor(Gap gap) 
            => new AIObjectiveFixLeak(gap, character, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Gap target)
            => HumanAIController.RemoveTargets<AIObjectiveFixLeaks, Gap>(character, target);

        public static bool IsValidTarget(Gap gap, Character character)
        {
            if (gap == null) { return false; }
            if (gap.ConnectedWall == null || gap.ConnectedDoor != null || gap.Open <= 0 || gap.linkedTo.All(l => l == null)) { return false; }
            if (gap.Submarine == null) { return false; }
            if (gap.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(gap, true)) { return false; }
            return true;
        }
    }
}
