#nullable enable

using System;
using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityUnlockApprenticeshipTalentTree : CharacterAbility
    {
        public override bool AllowClientSimulation => false;

        public CharacterAbilityUnlockApprenticeshipTalentTree(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement) { }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (!addingFirstTime) { return; }

            // do not run client-side in multiplayer
            if (GameMain.NetworkMember is { IsClient: true }) { return; }

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

                Character.GiveTalent(identifier);
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffect();
        }
    }
}