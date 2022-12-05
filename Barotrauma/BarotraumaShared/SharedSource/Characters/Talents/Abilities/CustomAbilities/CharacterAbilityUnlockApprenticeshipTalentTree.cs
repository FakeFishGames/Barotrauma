#nullable enable

using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityUnlockApprenticeshipTalentTree : CharacterAbility
    {
        public override bool AllowClientSimulation => false;

        public CharacterAbilityUnlockApprenticeshipTalentTree(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement) { }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (!addingFirstTime) { return; }

            JobPrefab? apprentice = CharacterAbilityApplyStatusEffectsToApprenticeship.GetApprenticeJob(Character, JobPrefab.Prefabs.ToImmutableHashSet());
            if (apprentice is null)
            {
                DebugConsole.ThrowError($"{nameof(CharacterAbilityUnlockApprenticeshipTalentTree)}: Could not find apprentice job for character {Character.Name}");
                return;
            }

            if (!TalentTree.JobTalentTrees.TryGet(apprentice.Identifier, out TalentTree? talentTree)) { return; }

            ImmutableHashSet<Character> characters = GameSession.GetSessionCrewCharacters(CharacterType.Both);

            HashSet<ImmutableHashSet<Identifier>> talentsTrees = new HashSet<ImmutableHashSet<Identifier>>();
            foreach (TalentSubTree subTree in talentTree.TalentSubTrees)
            {
                if (subTree.Type != TalentTreeType.Specialization) { continue; }

                HashSet<Identifier> identifiers = new HashSet<Identifier>();
                foreach (TalentOption option in subTree.TalentOptionStages)
                {
                    foreach (Identifier identifier in option.TalentIdentifiers)
                    {
                        if (IsShowCaseTalent(identifier, option) || TalentTree.IsTalentLocked(identifier, characters)) { continue; }

                        identifiers.Add(identifier);
                    }

                    foreach (var (_, value) in option.ShowCaseTalents)
                    {
                        var ids = value.Where(i => !TalentTree.IsTalentLocked(i, characters)).ToImmutableHashSet();
                        if (ids.Count is 0) { continue; }

                        identifiers.Add(value.GetRandomUnsynced());
                    }
                }

                talentsTrees.Add(identifiers.ToImmutableHashSet());
            }

            ImmutableHashSet<Identifier> selectedTalentTree = talentsTrees.GetRandomUnsynced();

            foreach (Identifier identifier in selectedTalentTree)
            {
                if (Character.HasTalent(identifier)) { continue; }

                Character.GiveTalent(identifier);
            }

            static bool IsShowCaseTalent(Identifier identifier, TalentOption option)
            {
                foreach (var (_, value) in option.ShowCaseTalents)
                {
                    if (value.Contains(identifier)) { return true; }
                }

                return false;
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffect();
        }
    }
}