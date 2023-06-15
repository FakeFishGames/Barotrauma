using Barotrauma.Abilities;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class CharacterTalent
    {
        public Character Character { get; }
        public string DebugIdentifier { get; }

        public readonly TalentPrefab Prefab;

        public bool AddedThisRound = true;

        private readonly Dictionary<AbilityEffectType, List<CharacterAbilityGroupEffect>> characterAbilityGroupEffectDictionary = new Dictionary<AbilityEffectType, List<CharacterAbilityGroupEffect>>();

        private readonly List<CharacterAbilityGroupInterval> characterAbilityGroupIntervals = new List<CharacterAbilityGroupInterval>();

        // works functionally but a missing recipe is not represented on GUI side. this might be better placed in the character class itself, though it might be fine here as well
        public List<Identifier> UnlockedRecipes { get; } = new List<Identifier>();
        public List<Identifier> UnlockedStoreItems { get; } = new List<Identifier>();

        public CharacterTalent(TalentPrefab talentPrefab, Character character)
        {
            Character = character;

            Prefab = talentPrefab;
            var element = talentPrefab.ConfigElement;
            DebugIdentifier = talentPrefab.OriginalName;

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "abilitygroupeffect":
                        LoadAbilityGroupEffect(subElement);
                        break;
                    case "abilitygroupinterval":
                        LoadAbilityGroupInterval(subElement);
                        break;
                    case "addedrecipe":
                        if (subElement.GetAttributeIdentifier("itemidentifier", Identifier.Empty) is { IsEmpty: false } recipeIdentifier)
                        {
                            UnlockedRecipes.Add(recipeIdentifier);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"No recipe identifier defined for talent {DebugIdentifier}");
                        }
                        break;
                    case "addedstoreitem":
                        if (subElement.GetAttributeIdentifier("itemtag", Identifier.Empty) is { IsEmpty: false } storeItemTag)
                        {
                            UnlockedStoreItems.Add(storeItemTag);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"No store item identifier defined for talent {DebugIdentifier}");
                        }
                        break;
                }
            }
        }

        public virtual void UpdateTalent(float deltaTime)
        {
            foreach (var characterAbilityGroupInterval in characterAbilityGroupIntervals)
            {
                characterAbilityGroupInterval.UpdateAbilityGroup(deltaTime);
            }
        }

        private static readonly HashSet<Identifier> checkedNonStackableTalents = new();

        /// <summary>
        /// Checks talents for a given AbilityObject taking into account non-stackable talents.
        /// </summary>
        public static void CheckTalentsForCrew(IEnumerable<Character> crew, AbilityEffectType type, AbilityObject abilityObject)
        {
            checkedNonStackableTalents.Clear();
            foreach (Character character in crew)
            {
                foreach (CharacterTalent characterTalent in character.CharacterTalents)
                {
                    if (!characterTalent.Prefab.AbilityEffectsStackWithSameTalent)
                    {
                        if (checkedNonStackableTalents.Contains(characterTalent.Prefab.Identifier)) { continue; }
                        checkedNonStackableTalents.Add(characterTalent.Prefab.Identifier);
                    }

                    characterTalent.CheckTalent(type, abilityObject);
                }
            }
        }

        public void CheckTalent(AbilityEffectType abilityEffectType, AbilityObject abilityObject)
        {
            if (characterAbilityGroupEffectDictionary.TryGetValue(abilityEffectType, out var characterAbilityGroups))
            {
                foreach (var characterAbilityGroup in characterAbilityGroups)
                {
                    characterAbilityGroup.CheckAbilityGroup(abilityObject);
                }
            }
        }

        public void ActivateTalent(bool addingFirstTime)
        {
            foreach (var characterAbilityGroups in characterAbilityGroupEffectDictionary.Values)
            {
                foreach (var characterAbilityGroup in characterAbilityGroups)
                {
                    characterAbilityGroup.ActivateAbilityGroup(addingFirstTime);
                }
            }
        }

        // XML logic
        private void LoadAbilityGroupInterval(ContentXElement abilityGroup)
        {
            characterAbilityGroupIntervals.Add(new CharacterAbilityGroupInterval(AbilityEffectType.Undefined, this, abilityGroup));
        }

        private void LoadAbilityGroupEffect(ContentXElement abilityGroup)
        {
            AbilityEffectType abilityEffectType = ParseAbilityEffectType(this, abilityGroup.GetAttributeString("abilityeffecttype", "none"));
            AddAbilityGroupEffect(new CharacterAbilityGroupEffect(abilityEffectType, this, abilityGroup), abilityEffectType);
        }

        public void AddAbilityGroupEffect(CharacterAbilityGroupEffect characterAbilityGroup, AbilityEffectType abilityEffectType = AbilityEffectType.None)
        {
            if (characterAbilityGroupEffectDictionary.TryGetValue(abilityEffectType, out var characterAbilityList))
            {
                characterAbilityList.Add(characterAbilityGroup);
            }
            else
            {
                List<CharacterAbilityGroupEffect> characterAbilityGroups = new List<CharacterAbilityGroupEffect>();
                characterAbilityGroups.Add(characterAbilityGroup);
                characterAbilityGroupEffectDictionary.Add(abilityEffectType, characterAbilityGroups);
            }
        }

        public static AbilityEffectType ParseAbilityEffectType(CharacterTalent characterTalent, string abilityEffectTypeString)
        {
            if (!Enum.TryParse(abilityEffectTypeString, true, out AbilityEffectType abilityEffectType))
            {
                DebugConsole.ThrowError("Invalid ability effect type \"" + abilityEffectTypeString + "\" in CharacterTalent (" + characterTalent.DebugIdentifier + ")");
            }
            if (abilityEffectType == AbilityEffectType.Undefined)
            {
                DebugConsole.ThrowError("Ability effect type not defined in CharacterTalent (" + characterTalent.DebugIdentifier + ")");
            }

            return abilityEffectType;
        }
    }
}
