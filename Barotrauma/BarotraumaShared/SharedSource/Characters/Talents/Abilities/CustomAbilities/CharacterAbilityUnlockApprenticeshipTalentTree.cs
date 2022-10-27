#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Extensions;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityUnlockApprenticeshipTalentTree : CharacterAbility
    {
        public CharacterAbilityUnlockApprenticeshipTalentTree(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement) { }

        public override void InitializeAbility(bool addingFirstTime)
        {
            JobPrefab? apprentice = CharacterAbilityApplyStatusEffectsToApprenticeship.GetApprenticeJob(Character, JobPrefab.Prefabs.ToImmutableHashSet());
            if (apprentice is null)
            {
                DebugConsole.ThrowError($"{nameof(CharacterAbilityUnlockApprenticeshipTalentTree)}: Could not find apprentice job for character {Character.Name}");
                return;
            }

            if (!TalentTree.JobTalentTrees.TryGet(apprentice.Identifier, out TalentTree? talentTree)) { return; }

            HashSet<ImmutableHashSet<Identifier>> talentsTrees = new HashSet<ImmutableHashSet<Identifier>>();
            foreach (TalentSubTree subTree in talentTree.TalentSubTrees)
            {
                if (subTree.Type != TalentTreeType.Specialization) { continue; }
                talentsTrees.Add(subTree.AllTalentIdentifiers);
            }

            ImmutableHashSet<Identifier> selectedTalentTree = talentsTrees.GetRandomUnsynced();

            foreach (Identifier identifier in selectedTalentTree)
            {
                if (Character.HasTalent(identifier)) { continue; }
                if (Character.GiveTalent(identifier))
                {
                    Character.Info.AdditionalTalentPoints++;
                }
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffect();
        }
    }
}