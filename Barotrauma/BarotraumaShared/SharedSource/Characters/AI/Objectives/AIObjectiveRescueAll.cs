using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRescueAll : AIObjectiveLoop<Character>
    {
        public override string Identifier { get; set; } = "rescue all";
        public override bool ForceRun => true;
        public override bool InverseTargetEvaluation => true;
        public override bool AllowOutsideSubmarine => true;
        public override bool AllowInAnySub => true;

        private const float vitalityThreshold = 75;
        private const float vitalityThresholdForOrders = 90;
        public static float GetVitalityThreshold(AIObjectiveManager manager, Character character, Character target)
        {
            if (manager == null)
            {
                return vitalityThreshold;
            }
            else
            {
                // When targeting player characters, always treat them when ordered, else use the threshold so that minor/non-severe damage is ignored.
                // If we ignore any damage when the player orders a bot to do healings, it's observed to cause confusion among the players.
                // On the other hand, if the bots too eagerly heal characters when it's not necessary, it's inefficient and can feel frustrating, because it can't be controlled.
                return character == target || manager.HasOrder<AIObjectiveRescueAll>() ? (target.IsPlayer ? 100 : vitalityThresholdForOrders) : vitalityThreshold;
            }
        }
        
        public AIObjectiveRescueAll(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Character target) => IsValidTarget(target, character);

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override float TargetEvaluation()
        {
            if (Targets.None()) { return 100; }
            if (!objectiveManager.IsOrder(this))
            {
                if (!character.IsMedic && HumanAIController.IsTrueForAnyCrewMember(c => c != HumanAIController && c.Character.IsMedic && !c.Character.IsUnconscious))
                {
                    // Don't do anything if there's a medic on board and we are not a medic
                    return 100;
                }
            }
            float worstCondition = Targets.Min(t => GetVitalityFactor(t));
            if (Targets.Contains(character))
            {
                if (character.Bleeding > 10)
                {
                    // Enforce the highest priority when bleeding out.
                    worstCondition = 0;
                }
                // Boost the priority when wounded.
                worstCondition /= 2;
            }
            return worstCondition;
        }

        public static float GetVitalityFactor(Character character)
        {
            float vitality = character.HealthPercentage - (character.Bleeding * 2) - character.Bloodloss + Math.Min(character.Oxygen, 0);
            vitality -= character.CharacterHealth.GetAfflictionStrength("paralysis");
            return Math.Clamp(vitality, 0, 100);
        }

        protected override AIObjective ObjectiveConstructor(Character target)
            => new AIObjectiveRescue(character, target, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
            => HumanAIController.RemoveTargets<AIObjectiveRescueAll, Character>(character, target);

        public static bool IsValidTarget(Character target, Character character)
        {
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (target.IsInstigator) { return false; }
            if (!HumanAIController.IsFriendly(character, target, onlySameTeam: true)) { return false; }
            if (character.AIController is HumanAIController humanAI)
            {
                if (GetVitalityFactor(target) >= GetVitalityThreshold(humanAI.ObjectiveManager, character, target) ||
                    target.CharacterHealth.GetAllAfflictions().All(a => a.Strength < a.Prefab.TreatmentThreshold)) 
                {
                    return false; 
                }
                if (!humanAI.ObjectiveManager.HasOrder<AIObjectiveRescueAll>())
                {
                    if (!character.IsMedic && target != character)
                    {
                        // Don't allow to treat others autonomously
                        return false;
                    }
                    // Ignore unsafe hulls, unless ordered
                    if (humanAI.UnsafeHulls.Contains(target.CurrentHull))
                    {
                        return false;
                    }
                }
            }
            else
            {
                if (GetVitalityFactor(target) >= vitalityThreshold) { return false; }
            }
            if (target.Submarine == null || character.Submarine == null) { return false; }
            // Don't allow going into another sub, unless it's connected and of the same team and type.
            if (!character.Submarine.IsEntityFoundOnThisSub(target.CurrentHull, includingConnectedSubs: true)) { return false; }
            if (target != character &&!target.IsPlayer && HumanAIController.IsActive(target) && target.AIController is HumanAIController targetAI)
            {
                // Ignore all concious targets that are currently fighting, fleeing, fixing, or treating characters
                if (targetAI.ObjectiveManager.HasActiveObjective<AIObjectiveCombat>() ||
                    targetAI.ObjectiveManager.HasActiveObjective<AIObjectiveFindSafety>() ||
                    targetAI.ObjectiveManager.HasActiveObjective<AIObjectiveRescue>() ||
                    targetAI.ObjectiveManager.HasActiveObjective<AIObjectiveFixLeak>())
                {
                    return false;
                }
            }
            // Don't go into rooms that have enemies
            if (Character.CharacterList.Any(c => c.CurrentHull == target.CurrentHull && !HumanAIController.IsFriendly(character, c) && HumanAIController.IsActive(c))) { return false; }
            return true;
        }
    }
}
