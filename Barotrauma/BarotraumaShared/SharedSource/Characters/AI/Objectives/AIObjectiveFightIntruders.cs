using System.Collections.Generic;
using System.Linq;
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
            return Targets.None() ? 0 : 100;
        }

        protected override AIObjective ObjectiveConstructor(Character target)
        {
            var combatObjective = new AIObjectiveCombat(character, target, AIObjectiveCombat.CombatMode.Offensive, objectiveManager, PriorityModifier);
            if (character.TeamID == CharacterTeamType.FriendlyNPC && target.TeamID == CharacterTeamType.Team1 && GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                var reputation = campaign.Map?.CurrentLocation?.Reputation;
                if (reputation != null && reputation.NormalizedValue < Reputation.HostileThreshold)
                {
                    combatObjective.holdFireCondition = () =>
                    {
                        //hold fire while the enemy is in the airlock (except if they've attacked us)
                        if (HumanAIController.GetDamageDoneByAttacker(target) > 0.0f) { return false; }
                        return target.CurrentHull == null || target.CurrentHull.OutpostModuleTags.Any(t => t.Equals("airlock", System.StringComparison.OrdinalIgnoreCase));
                    };
                    character.Speak(TextManager.Get("dialogenteroutpostwarning"), null, Rand.Range(0.5f, 1.0f), "leaveoutpostwarning", 30.0f);
                }
            }
            return combatObjective;
        }

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
            => HumanAIController.RemoveTargets<AIObjectiveFightIntruders, Character>(character, target);

        public static bool IsValidTarget(Character target, Character character)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (target == character) { return false; }
            if (target.Submarine == null) { return false; }
            if (character.Submarine == null) { return false; }
            if (target.CurrentHull == null) { return false; }
            if (HumanAIController.IsFriendly(character, target)) { return false; }
            if (!character.Submarine.IsConnectedTo(target.Submarine)) { return false; }
            return true;
        }
    }
}
