using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveFightIntruders : AIObjectiveLoop<Character>
    {
        public override string DebugTag => "fight intruders";

        public AIObjectiveFightIntruders(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier) { }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFightIntruders && otherObjective.Option == Option;

        protected override void FindTargets()
        {
            if (Option == null) { return; }
            base.FindTargets();
            if (targets.None() && objectiveManager.CurrentOrder == this)
            {
                character.Speak(TextManager.Get("DialogNoEnemies"), null, 3.0f, "noenemies", 30.0f);
            }
        }

        protected override bool Filter(Character target)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (HumanAIController == null || HumanAIController.IsFriendly(target)) { return false; }
            if (target.Submarine != character.Submarine) { return false; }
            if (target.CurrentHull == null && character.Submarine != null) { return false; }
            return true;
        }
        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override AIObjective ObjectiveConstructor(Character target) => new AIObjectiveCombat(character, target, AIObjectiveCombat.CombatMode.Offensive, objectiveManager, PriorityModifier) { useCoolDown = false };
        protected override float TargetEvaluation()
        {
            // TODO: sorting criteria
            return 90;
        }
    }
}
