using Microsoft.Xna.Framework;
using System.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    class AIObjectiveFixLeaks : AIObjectiveLoop<Gap>
    {
        public override string DebugTag => "fix leaks";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool AllowInAnySub => true;
        private Hull PrioritizedHull { get; set; }

        public AIObjectiveFixLeaks(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1, Hull prioritizedHull = null) : base(character, objectiveManager, priorityModifier)
        {
            PrioritizedHull = prioritizedHull;
        }

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
            int totalLeaks = Targets.Count();
            if (totalLeaks == 0) { return 0; }
            int otherFixers = HumanAIController.CountCrew(c => c != HumanAIController && c.ObjectiveManager.IsCurrentObjective<AIObjectiveFixLeaks>() && !c.Character.IsIncapacitated, onlyBots: true);
            bool anyFixers = otherFixers > 0;
            if (objectiveManager.CurrentOrder == this)
            {
                float ratio = anyFixers ? totalLeaks / (float)otherFixers : 1;
                return Targets.Sum(t => GetLeakSeverity(t)) * ratio;
            }
            else
            {
                int secondaryLeaks = Targets.Count(l => l.IsRoomToRoom);
                int leaks = totalLeaks - secondaryLeaks;
                float ratio = leaks == 0 ? 1 : anyFixers ? leaks / otherFixers : 1;
                if (anyFixers && (ratio <= 1 || otherFixers > 5 || otherFixers / (float)HumanAIController.CountCrew(onlyBots: true) > 0.75f))
                {
                    // Enough fixers
                    return 0;
                }
                return Targets.Sum(t => GetLeakSeverity(t)) * ratio;
            }
        }

        protected override IEnumerable<Gap> GetList() => Gap.GapList;
        protected override AIObjective ObjectiveConstructor(Gap gap) 
            => new AIObjectiveFixLeak(gap, character, objectiveManager, priorityModifier: PriorityModifier, isPriority: gap.FlowTargetHull == PrioritizedHull);

        protected override void OnObjectiveCompleted(AIObjective objective, Gap target)
            => HumanAIController.RemoveTargets<AIObjectiveFixLeaks, Gap>(character, target);

        public static bool IsValidTarget(Gap gap, Character character)
        {
            if (gap == null) { return false; }
            // Don't fix a leak on a wall section set to be ignored
            if (gap.ConnectedWall?.Sections?.Any(s => s.gap == gap && s.IgnoreByAI) ?? false) { return false; } 
            if (gap.ConnectedWall == null || gap.ConnectedDoor != null || gap.Open <= 0 || gap.linkedTo.All(l => l == null)) { return false; }
            if (gap.Submarine == null || character.Submarine == null) { return false; }
            // Don't allow going into another sub, unless it's connected and of the same team and type.
            if (!character.Submarine.IsEntityFoundOnThisSub(gap, includingConnectedSubs: true)) { return false; }
            return true;
        }
    }
}
