#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionCharacterUnconcious : AbilityConditionCharacter
    {
        public AbilityConditionCharacterUnconcious(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesCharacter(Character character)
        {
            return character is { IsUnconscious: true };
        }
    }
}