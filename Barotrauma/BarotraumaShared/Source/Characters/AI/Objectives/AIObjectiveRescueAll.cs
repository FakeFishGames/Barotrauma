using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRescueAll : AIObjective
    {
        //only treat characters whose vitality is below this (0.8 = 80% of max vitality)
        public const float VitalityThreshold = 0.8f;

        private List<Character> rescueTargets;
        
        public AIObjectiveRescueAll(Character character)
            : base (character, "")
        {
            rescueTargets = new List<Character>();
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return true;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            GetRescueTargets();
            if (!rescueTargets.Any()) { return 0.0f; }
            
            if (objectiveManager.CurrentObjective == this)
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
                c != character &&
                !c.IsDead &&
                c.Vitality / c.MaxVitality < VitalityThreshold);
        }

        protected override void Act(float deltaTime)
        {
            foreach (Character target in rescueTargets)
            {
                AddSubObjective(new AIObjectiveRescue(character, target));
            }
        }

        public override bool IsCompleted()
        {
            return false;
        }        
    }
}
