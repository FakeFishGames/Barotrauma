using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRescueAll : AIObjectiveLoop<Character>
    {
        public override string DebugTag => "rescue all";
        public override bool ForceRun => true;
        public override bool IgnoreUnsafeHulls => true;

        private const float vitalityThreshold = 80;
        private const float vitalityThresholdForOrders = 95;
        public static float GetVitalityThreshold(AIObjectiveManager manager)
        {
            if (manager == null)
            {
                return vitalityThreshold;
            }
            else
            {
                return manager.CurrentOrder is AIObjectiveRescueAll ? vitalityThresholdForOrders : vitalityThreshold;
            }
        }
        
        public AIObjectiveRescueAll(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Character target) => IsValidTarget(target, character);

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override float TargetEvaluation() => Targets.Max(t => GetVitalityFactor(t));

        public static float GetVitalityFactor(Character character) => Math.Min(character.HealthPercentage - character.Bleeding - character.Bloodloss - Math.Min(character.Oxygen, 0), 100);

        protected override AIObjective ObjectiveConstructor(Character target)
            => new AIObjectiveRescue(character, target, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
            => HumanAIController.RemoveTargets<AIObjectiveRescueAll, Character>(character, target);

        public static bool IsValidTarget(Character target, Character character)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (!HumanAIController.IsFriendly(character, target)) { return false; }
            if (character.AIController is HumanAIController humanAI)
            {
                if (GetVitalityFactor(target) > GetVitalityThreshold(humanAI.ObjectiveManager)) { return false; }
            }
            else
            {
                if (GetVitalityFactor(target) > vitalityThreshold) { return false; }
            }
            if (target.Submarine == null) { return false; }
            if (target.Submarine.TeamID != character.Submarine.TeamID) { return false; }
            if (target.CurrentHull == null) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(target.CurrentHull, true)) { return false; }
            // Don't go into rooms that have enemies
            if (Character.CharacterList.Any(c => c.CurrentHull == target.CurrentHull && !c.IsDead && !c.Removed && !c.IsUnconscious && !HumanAIController.IsFriendly(character, c))) { return false; }
            return true;
        }
    }
}
