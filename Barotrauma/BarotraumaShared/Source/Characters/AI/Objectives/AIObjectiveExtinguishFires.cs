using System.Linq;
using System.Collections.Generic;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveExtinguishFires : AIObjectiveLoop<Hull>
    {
        public override string DebugTag => "extinguish fires";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;

        public AIObjectiveExtinguishFires(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        protected override void FindTargets()
        {
            base.FindTargets();
            if (targets.None() && objectiveManager.CurrentOrder == this)
            {
                character.Speak(TextManager.Get("DialogNoFire"), null, 3.0f, "nofire", 30.0f);
            }
        }

        protected override bool Filter(Hull target)
        {
            if (target.FireSources.None()) { return false; }
            if (target.Submarine == null) { return false; }
            if (target.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(target, true)) { return false; }
            return true;
        }

        protected override float TargetEvaluation() => (objectiveManager.CurrentObjective == this || objectiveManager.CurrentOrder == this) ? 100 : targets.Sum(t => GetFireSeverity(t));

        public static float GetFireSeverity(Hull hull) => hull.FireSources.Sum(fs => fs.Size.X);

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveExtinguishFires;
        protected override IEnumerable<Hull> GetList() => Hull.hullList;

        protected override AIObjective ObjectiveConstructor(Hull target) => new AIObjectiveExtinguishFire(character, target, objectiveManager, PriorityModifier);
    }
}
