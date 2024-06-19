using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveRescueAll : AIObjectiveLoop<Character>
    {
        public override Identifier Identifier { get; set; } = "rescue all".ToIdentifier();
        public override bool ForceRun => true;
        public override bool InverseTargetPriority => true;
        protected override bool AllowOutsideSubmarine => true;
        protected override bool AllowInAnySub => true;

        private readonly HashSet<Character> charactersWithMinorInjuries = new HashSet<Character>();

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
                return character == target || manager.HasOrder<AIObjectiveRescueAll>() ? vitalityThresholdForOrders : vitalityThreshold;
            }
        }

        public AIObjectiveRescueAll(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier) { }

        protected override bool IsValidTarget(Character target)
        {
            if (!IsValidTarget(target, character, out bool ignoredasMinorWounds))
            {
                if (ignoredasMinorWounds)
                {
                    //the target might be at a low enough health to be considered a valid target,
                    //but if all afflictions are below treatment thresholds, the bot won't (and shouldn't) treat them
                    // -> make the bot speak to make it clear the bot intentionally ignores very minor injuries
                    if (character.IsOnPlayerTeam && target != character && !charactersWithMinorInjuries.Contains(target))
                    {
                        // But only speak about targets when we are not already actively treating, in which case we should be speaking about the current target.
                        if (objectiveManager.GetFirstActiveObjective<AIObjectiveRescue>() == null)
                        {
                            charactersWithMinorInjuries.Add(target);
                            character.Speak(TextManager.GetWithVariable("dialogignoreminorinjuries", "[targetname]", target.Name).Value,
                                delay: 1.0f,
                                identifier: $"notreatableafflictions{target.Name}".ToIdentifier(),
                                minDurationBetweenSimilar: 10.0f);
                        }
                    }
                }
                return false;
            }
            return true;
        }

        protected override IEnumerable<Character> GetList() => Character.CharacterList;

        protected override float GetTargetPriority()
        {
            if (Targets.None()) { return 100; }
            if (!objectiveManager.IsOrder(this))
            {
                if (!character.IsMedic && HumanAIController.IsTrueForAnyCrewMember(c => c != character && c.IsMedic, onlyActive: true, onlyConnectedSubs: true))
                {
                    // Don't do anything if there's a medic on board actively treating and we are not a medic
                    return 100;
                }
            }
            float worstCondition = Targets.Min(t => GetVitalityFactor(t));
            if (Targets.Contains(character))
            {
                // Targeting self -> higher prio
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
            float vitality = 100;
            vitality -= character.Bleeding * 2;
            vitality += Math.Min(character.Oxygen, 0);
            foreach (Affliction affliction in GetTreatableAfflictions(character, ignoreTreatmentThreshold: true))
            {
                float strength = character.CharacterHealth.GetPredictedStrength(affliction, predictFutureDuration: 10.0f);
                vitality -= affliction.GetVitalityDecrease(character.CharacterHealth, strength) / character.MaxVitality * 100;
                if (affliction.Prefab.AfflictionType == AfflictionPrefab.ParalysisType)
                {
                    vitality -= affliction.Strength;
                }
                else if (affliction.Prefab.AfflictionType == AfflictionPrefab.PoisonType)
                {
                    vitality -= affliction.Strength;
                }
            }
            return Math.Clamp(vitality, 0, 100);
        }

        public static IEnumerable<Affliction> GetTreatableAfflictions(Character character, bool ignoreTreatmentThreshold)
        {
            var allAfflictions = character.CharacterHealth.GetAllAfflictions();
            foreach (Affliction affliction in allAfflictions)
            {
                if (affliction.Prefab.IsBuff) { continue; }
                if (!affliction.Prefab.HasTreatments) { continue; }
                if (!ignoreTreatmentThreshold)
                {
                    //other afflictions of the same type increase the "treatability"
                    // e.g. we might want to ignore burns below 5%, but not if the character has them on all limbs
                    float totalAfflictionStrength = character.CharacterHealth.GetTotalAdjustedAfflictionStrength(affliction);
                    if (totalAfflictionStrength < affliction.Prefab.TreatmentThreshold) { continue; }
                }
                if (allAfflictions.Any(otherAffliction => affliction.Prefab.IgnoreTreatmentIfAfflictedBy.Contains(otherAffliction.Identifier))) { continue; }
                yield return affliction;
            }
        }

        protected override AIObjective ObjectiveConstructor(Character target)
            => new AIObjectiveRescue(character, target, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Character target)
            => HumanAIController.RemoveTargets<AIObjectiveRescueAll, Character>(character, target);

        public static bool IsValidTarget(Character target, Character character, out bool ignoredAsMinorWounds)
        {
            ignoredAsMinorWounds = false;
            if (target == null || target.IsDead || target.Removed) { return false; }
            if (target.IsInstigator) { return false; }
            if (target.IsPet) { return false; }
            if (!HumanAIController.IsFriendly(character, target, onlySameTeam: true)) { return false; }
            bool isBelowTreatmentThreshold;
            float vitalityFactor;
            if (character.AIController is HumanAIController humanAI)
            {
                if (!IsValidTargetForAI(target, humanAI)) { return false; }
                vitalityFactor = GetVitalityFactor(target);
                isBelowTreatmentThreshold = vitalityFactor < GetVitalityThreshold(humanAI.ObjectiveManager, character, target);
            }
            else
            {
                vitalityFactor = GetVitalityFactor(target);
                isBelowTreatmentThreshold = vitalityFactor < vitalityThreshold;
            }
            bool hasTreatableAfflictions = GetTreatableAfflictions(target, ignoreTreatmentThreshold: false).Any();
            bool isValidTarget = isBelowTreatmentThreshold && hasTreatableAfflictions;
            if (!isValidTarget)
            {
                ignoredAsMinorWounds = hasTreatableAfflictions || vitalityFactor < 100;
            }
            return isValidTarget;
        }

        private static bool IsValidTargetForAI(Character target, HumanAIController humanAI)
        {
            Character character = humanAI.Character;
            if (!humanAI.ObjectiveManager.HasOrder<AIObjectiveRescueAll>())
            {
                if (!character.IsMedic && target != character)
                {
                    // Don't allow to treat others autonomously, unless we are a medic
                    return false;
                }
                // Ignore unsafe hulls, unless ordered
                if (humanAI.UnsafeHulls.Contains(target.CurrentHull))
                {
                    return false;
                }
            }
            if (character.Submarine != null)
            {
                // Don't allow going into another sub, unless it's connected and of the same team and type.
                if (!character.Submarine.IsEntityFoundOnThisSub(target.CurrentHull, includingConnectedSubs: true)) { return false; }
            }
            else if (target.Submarine != null)
            {
                // We are outside, but the target is inside.
                return false;
            }
            if (target != character && target.IsBot && HumanAIController.IsActive(target) && target.AIController is HumanAIController targetAI)
            {
                // Ignore all concious targets that are currently fighting, fleeing, or treating characters
                if (targetAI.ObjectiveManager.HasActiveObjective<AIObjectiveCombat>() ||
                    targetAI.ObjectiveManager.HasActiveObjective<AIObjectiveFindSafety>() ||
                    targetAI.ObjectiveManager.HasActiveObjective<AIObjectiveRescue>())
                {
                    return false;
                }
            }
            if (target.CurrentHull != null)
            {
                // Don't go into rooms that have enemies
                if (Character.CharacterList.Any(c => c.CurrentHull == target.CurrentHull && !HumanAIController.IsFriendly(character, c) && HumanAIController.IsActive(c))) { return false; }
            }
            return character.GetDamageDoneByAttacker(target) <= 0;
        }
    }
}
