using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRescueAll : AIObjectiveLoop<Character>
    {
        public override string DebugTag => "rescue all";
        public override bool ForceRun => true;

        private const float vitalityThreshold = 0.85f;
        private const float vitalityThresholdForOrders = 0.95f;
        public static float GetVitalityThreshold(AIObjectiveManager manager) => manager.CurrentOrder is AIObjectiveRescueAll ? vitalityThresholdForOrders : vitalityThreshold;
        
        public AIObjectiveRescueAll(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier) { }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRescueAll;

        protected override bool Filter(Character target) => IsValidTarget(target, character);

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override AIObjective ObjectiveConstructor(Character target) => new AIObjectiveRescue(character, target, objectiveManager, PriorityModifier);

        protected override float TargetEvaluation() => Targets.Max(t => GetVitalityFactor(t)) * 100;

        public static float GetVitalityFactor(Character character) => (character.MaxVitality - character.Vitality) / character.MaxVitality;

        public static bool IsValidTarget(Character target, Character character)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (target == character) { return false; } // TODO: enable healing self
            if (!HumanAIController.IsFriendly(character, target)) { return false; }
            if (!(character.AIController is HumanAIController humanAI)) { return false; }
            if (target.Vitality / target.MaxVitality > GetVitalityThreshold(humanAI.ObjectiveManager)) { return false; }
            if (target.Submarine == null) { return false; }
            if (target.Submarine.TeamID != character.Submarine.TeamID) { return false; }
            if (target.CurrentHull == null) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(target.CurrentHull, true)) { return false; }
            return true;
        }
    }
}
