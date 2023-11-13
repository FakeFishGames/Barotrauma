namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionCharacterNotLooted : AbilityConditionData
    {
        private readonly Identifier identifier;

        public AbilityConditionCharacterNotLooted(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            identifier = conditionElement.GetAttributeIdentifier("identifier", Identifier.Empty);
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is not IAbilityCharacter ability) { return false; }

            return !ability.Character.MarkedAsLooted.Contains(identifier);
        }
    }
}