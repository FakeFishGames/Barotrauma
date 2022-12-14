#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityApplyStatusEffectsToApprenticeship : CharacterAbilityApplyStatusEffects
    {
        private readonly bool invert;
        private readonly ImmutableHashSet<JobPrefab> jobPrefabList = JobPrefab.Prefabs.ToImmutableHashSet();

        public CharacterAbilityApplyStatusEffectsToApprenticeship(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            invert = abilityElement.GetAttributeBool("invert", false);
        }

        protected override void ApplyEffect()
        {
            ApplyEffectSpecific(Character);
            JobPrefab? apprenticeJob = GetApprenticeJob(Character, jobPrefabList);
            if (apprenticeJob is null)
            {
                DebugConsole.ThrowError($"{nameof(CharacterAbilityUnlockApprenticeshipTalentTree)}: Could not find apprentice job for character {Character.Name}");
                return;
            }

            foreach (Character character in GameSession.GetSessionCrewCharacters(CharacterType.Both))
            {
                JobPrefab? characterJob = character.Info?.Job?.Prefab;
                if (characterJob is null) { continue; }

                switch (characterJob.Identifier == apprenticeJob.Identifier)
                {
                    case true when invert:
                        continue;
                    case false when !invert:
                        continue;
                }

                ApplyEffectSpecific(character);
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffect();
        }

        public static JobPrefab? GetApprenticeJob(Character character, IReadOnlyCollection<JobPrefab> jobList)
        {
            foreach (JobPrefab prefab in jobList)
            {
                if (character.Info.GetSavedStatValue(StatTypes.Apprenticeship, prefab.Identifier) > 0)
                {
                    return prefab;
                }
            }

            return null;
        }
    }
}