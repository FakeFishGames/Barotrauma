using System.Linq;
using System.Collections.Generic;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveExtinguishFires : AIObjectiveLoop<Hull>
    {
        public override string DebugTag => "extinguish fires";
        public override bool ForceRun => true;

        public AIObjectiveExtinguishFires(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Hull hull) => IsValidTarget(hull, character);

        protected override float TargetEvaluation() => Targets.Sum(t => GetFireSeverity(t));

        public static float GetFireSeverity(Hull hull) => hull.FireSources.Sum(fs => fs.Size.X);

        protected override IEnumerable<Hull> GetList() => Hull.hullList;

        protected override AIObjective ObjectiveConstructor(Hull target) 
            => new AIObjectiveExtinguishFire(character, target, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Hull target) 
            => HumanAIController.RemoveTargets<AIObjectiveExtinguishFires, Hull>(character, target);

        public static bool IsValidTarget(Hull hull, Character character)
        {
            if (hull == null) { return false; }
            if (hull.FireSources.None()) { return false; }
            if (hull.Submarine == null) { return false; }
            if (character.Submarine == null) { return false; }
            if (!character.Submarine.IsConnectedTo(hull.Submarine)) { return false; }
            if (character.AIController is HumanAIController humanAI)
            {
                if (hull.Submarine.TeamID != character.TeamID)
                {
                    if (humanAI.ObjectiveManager.IsCurrentOrder<AIObjectiveExtinguishFires>())
                    {
                        // For orders, allow targets in the current sub (for example if the bot is inside an outpost or a wreck)
                        if (hull.Submarine != character.Submarine) { return false; }
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
