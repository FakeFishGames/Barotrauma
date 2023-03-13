
namespace Barotrauma.Abilities
{
    class AbilityConditionHasTalent : AbilityConditionDataless
    {
        private readonly Identifier talentIdentifier;

        public AbilityConditionHasTalent(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            talentIdentifier = conditionElement.GetAttributeIdentifier("identifier", Identifier.Empty);
        }

        protected override bool MatchesConditionSpecific()
        {
            bool result = character.HasTalent(talentIdentifier);
            return result;
        }
    }
}