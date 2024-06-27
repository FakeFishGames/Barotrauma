using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFightIntruders : AIObjectiveLoop<Character>
    {
        public override Identifier Identifier { get; set; } = "fight intruders".ToIdentifier();
        protected override float IgnoreListClearInterval => 30;
        public override bool IgnoreUnsafeHulls => true;
        protected override float TargetUpdateTimeMultiplier => 0.2f;
        public bool TargetCharactersInOtherSubs { get; init; }

        protected override bool AllowInAnySub => TargetCharactersInOtherSubs;

        public AIObjectiveFightIntruders(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier) { }

        protected override bool IsValidTarget(Character target) => IsValidTarget(target, character, TargetCharactersInOtherSubs);

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override float GetTargetPriority()
        {
            if (Targets.None()) { return 0; }
            if (!character.IsOnPlayerTeam && !character.IsOriginallyOnPlayerTeam) { return 100; }
            if (character.IsSecurity) { return 100; }
            if (objectiveManager.IsOrder(this)) { return 100; }
            // If there's any security officers onboard, leave fighting for them.
            return HumanAIController.IsTrueForAnyCrewMember(c => c.IsSecurity, onlyActive: true, onlyConnectedSubs: true) ? 0 : 100;
        }

        protected override AIObjective ObjectiveConstructor(Character target)
        {
            AIObjectiveCombat.CombatMode combatMode = AIObjectiveCombat.CombatMode.Offensive;
            if (character.IsOnPlayerTeam && target is { IsEscorted: true })
            {
                // Try to arrest escorted characters, instead of killing them.
                combatMode = AIObjectiveCombat.CombatMode.Arrest;
            }
            var combatObjective = new AIObjectiveCombat(character, target, combatMode, objectiveManager, PriorityModifier);
            if (character.TeamID == CharacterTeamType.FriendlyNPC && target.TeamID == CharacterTeamType.Team1 && GameMain.GameSession?.GameMode is CampaignMode { CurrentLocation.IsFactionHostile: true })
            {
                combatObjective.holdFireCondition = () =>
                {
                    //hold fire while the enemy is in the airlock (except if they've attacked us)
                    if (character.GetDamageDoneByAttacker(target) > 0.0f) { return false; }
                    return target.CurrentHull == null || target.CurrentHull.OutpostModuleTags.Any(t => t == "airlock");
                };
                character.Speak(TextManager.Get("dialogenteroutpostwarning").Value, null, Rand.Range(0.5f, 1.0f), "leaveoutpostwarning".ToIdentifier(), 30.0f);
            }
            return combatObjective;
        }

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
            => HumanAIController.RemoveTargets<AIObjectiveFightIntruders, Character>(character, target);

        public static bool IsValidTarget(Character target, Character character, bool targetCharactersInOtherSubs)
        {
            if (target == null || target.Removed) { return false; }
            if (target.IsDead) { return false; }
            if (target.IsUnconscious && target.Params.Health.ConstantHealthRegeneration <= 0.0f) { return false; }
            if (target == character) { return false; }
            if (target.Submarine == null) { return false; }
            if (character.Submarine == null) { return false; }
            if (target.CurrentHull == null) { return false; }
            if (HumanAIController.IsFriendly(character, target)) { return false; }
            if (!character.Submarine.IsConnectedTo(target.Submarine)) { return false; }
            if (!targetCharactersInOtherSubs)
            {
                if (character.Submarine.TeamID != target.Submarine.TeamID && character.OriginalTeamID != target.Submarine.TeamID)
                {
                    return false;
                }
            }
            if (target.HasAbilityFlag(AbilityFlags.IgnoredByEnemyAI)) { return false; }
            if (target.IsHandcuffed && target.IsKnockedDown) { return false; }
            if (EnemyAIController.IsLatchedToSomeoneElse(target, character)) { return false; }
            return true;
        }
    }
}
