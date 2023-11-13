#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionCharacterUnconcious : AbilityConditionData
    {
        public AbilityConditionCharacterUnconcious(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is not IAbilityCharacter targetCharacter) { return false; }

            return targetCharacter.Character.IsUnconscious;
        }
    }
}