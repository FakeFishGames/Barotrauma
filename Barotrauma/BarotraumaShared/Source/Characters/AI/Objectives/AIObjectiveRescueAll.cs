using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRescueAll : AIObjectiveLoop<Character>
    {
        public override string DebugTag => "rescue all";

        //only treat characters whose vitality is below this (0.8 = 80% of max vitality)
        public const float VitalityThreshold = 0.8f;
        
        public AIObjectiveRescueAll(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier) { }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRescueAll;

        protected override void FindTargets()
        {
            base.FindTargets();
            if (targets.None() && objectiveManager.CurrentOrder == this)
            {
                character.Speak(TextManager.Get("DialogNoRescueTargets"), null, 3.0f, "norescuetargets", 30.0f);
            }
        }

        protected override bool Filter(Character target) => IsValidTarget(target, character);

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override AIObjective ObjectiveConstructor(Character target) => new AIObjectiveRescue(character, target, objectiveManager, PriorityModifier);

        protected override float TargetEvaluation() => GetVitalityFactor(character) * 100;

        public static float GetVitalityFactor(Character character) => (character.MaxVitality - character.Vitality) / character.MaxVitality;

        public static bool IsValidTarget(Character target, Character character)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (!HumanAIController.IsFriendly(character, target)) { return false; }
            if (target.Vitality / target.MaxVitality > VitalityThreshold) { return false; }
            if (target.Submarine == null) { return false; }
            if (target.Submarine.TeamID != character.Submarine.TeamID) { return false; }
            if (target.CurrentHull == null) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(target.CurrentHull, true)) { return false; }
            return true;
        }
    }
}
