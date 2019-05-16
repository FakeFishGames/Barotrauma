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

        protected override bool Filter(Character target)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (target == character) { return false; }
            if (target.Submarine != character.Submarine) { return false; }
            if (target.CurrentHull == null && character.Submarine != null) { return false; }
            if (target.Vitality / target.MaxVitality > VitalityThreshold) { return false; }
            if (HumanAIController == null || !HumanAIController.IsFriendly(target)) { return false; }
            return true;
        }

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override AIObjective ObjectiveConstructor(Character target) => new AIObjectiveRescue(character, target, objectiveManager, PriorityModifier);

        protected override float TargetEvaluation()
        {
            // TODO: sorting criteria
            return 100;
        }
    }
}
