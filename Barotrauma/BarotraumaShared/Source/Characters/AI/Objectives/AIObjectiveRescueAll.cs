using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRescueAll : AIObjective
    {
        public override string DebugTag => "rescue all";

        public override bool KeepDivingGearOn => true;

        //only treat characters whose vitality is below this (0.8 = 80% of max vitality)
        public const float VitalityThreshold = 0.8f;

        private List<Character> rescueTargets;
        
        public AIObjectiveRescueAll(Character character) : base (character, "")
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

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (character.Submarine == null) { return 0; }
            GetRescueTargets();
            if (!rescueTargets.Any()) { return 0.0f; }
            
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            //if there are targets to rescue, the priority is slightly less 
            //than the priority of explicit orders given to the character
            return AIObjectiveManager.OrderPriority - 5.0f;
        }

        private void GetRescueTargets()
        {
            rescueTargets = Character.CharacterList.FindAll(c => 
                c.AIController is HumanAIController &&
                c.TeamID == character.TeamID &&
                c != character &&
                !c.IsDead &&
                c.Vitality / c.MaxVitality < VitalityThreshold);
        }

        protected override float TargetEvaluation()
        {
            // TODO: sorting criteria
            return 100;
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;
    }
}
