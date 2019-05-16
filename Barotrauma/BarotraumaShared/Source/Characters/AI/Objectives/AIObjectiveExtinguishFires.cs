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

        protected override void FindTargets()
        {
            base.FindTargets();
            if (targets.None() && objectiveManager.CurrentOrder == this)
            {
                character.Speak(TextManager.Get("DialogNoFire"), null, 3.0f, "nofire", 30.0f);
            }
        }

        protected override bool Filter(Hull hull) => IsValidTarget(hull, character);

        protected override float TargetEvaluation() => objectiveManager.CurrentObjective == this ? 100 : targets.Sum(t => GetFireSeverity(t));

        public static float GetFireSeverity(Hull hull) => hull.FireSources.Sum(fs => fs.Size.X);

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveExtinguishFires;
        protected override IEnumerable<Hull> GetList() => Hull.hullList;

        protected override AIObjective ObjectiveConstructor(Hull target) => new AIObjectiveExtinguishFire(character, target, objectiveManager, PriorityModifier);

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
