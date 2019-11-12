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
        protected override float IgnoreListClearInterval => 30;
        public override bool IgnoreUnsafeHulls => true;

        public AIObjectiveFightIntruders(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Character target) => IsValidTarget(target, character);

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override float TargetEvaluation()
        {
            // TODO: sorting criteria
            return 100;
        }

        protected override AIObjective ObjectiveConstructor(Character target) 
            => new AIObjectiveCombat(character, target, AIObjectiveCombat.CombatMode.Offensive, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
            => HumanAIController.RemoveTargets<AIObjectiveFightIntruders, Character>(character, target);

        public static bool IsValidTarget(Character target, Character character)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (target == character) { return false; }
            if (HumanAIController.IsFriendly(character, target)) { return false; }
            if (target.Submarine == null) { return false; }
            if (target.Submarine.TeamID != character.TeamID) { return false; }
            if (target.CurrentHull == null) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(target.CurrentHull, true)) { return false; }
            return true;
        }
    }
}
