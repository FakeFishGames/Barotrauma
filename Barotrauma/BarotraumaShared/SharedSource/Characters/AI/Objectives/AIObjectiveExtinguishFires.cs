using System.Linq;
using System.Collections.Generic;
using Barotrauma.Extensions;
using System;

namespace Barotrauma
{
    class AIObjectiveExtinguishFires : AIObjectiveLoop<Hull>
    {
        public override string DebugTag => "extinguish fires";
        public override bool ForceRun => true;
        public override bool IgnoreUnsafeHulls => true;

        public AIObjectiveExtinguishFires(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Hull hull) => IsValidTarget(hull, character);

        protected override float TargetEvaluation() => objectiveManager.CurrentObjective == this ? 100 : Targets.Sum(t => GetFireSeverity(t));

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
            if (hull.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(hull, true)) { return false; }
            return true;
        }
    }
}
