using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    // TODO: Ensure that this works well enough. Consider using AIObjectiveLoop class.
    class AIObjectiveRescueAll : AIObjective
    {
        public override string DebugTag => "rescue all";

        public override bool KeepDivingGearOn => true;

        //only treat characters whose vitality is below this (0.8 = 80% of max vitality)
        public const float VitalityThreshold = 0.8f;

        private List<Character> rescueTargets;
        
        public AIObjectiveRescueAll(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            rescueTargets = new List<Character>();
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return true;
        }

        public override float GetPriority()
        {
            // TODO: review
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

        protected override void Act(float deltaTime)
        {
            foreach (Character target in rescueTargets)
            {
                AddSubObjective(new AIObjectiveRescue(character, target, objectiveManager));
            }
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsLoop { get => true; set => throw new System.Exception("Trying to set the value for IsLoop from: " + System.Environment.StackTrace); }
    }
}
