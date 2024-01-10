namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionCharacterNotLooted : AbilityConditionCharacter
    {
        private readonly Identifier identifier;

        public AbilityConditionCharacterNotLooted(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            identifier = conditionElement.GetAttributeIdentifier("identifier", Identifier.Empty);
        }

        protected override bool MatchesCharacter(Character character)
        {
            return character != null &&!character.MarkedAsLooted.Contains(identifier);
        }
    }
}