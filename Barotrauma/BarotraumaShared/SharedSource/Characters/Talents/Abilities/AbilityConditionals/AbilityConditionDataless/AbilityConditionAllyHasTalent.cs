
namespace Barotrauma.Abilities
{
    class AbilityConditionAllyHasTalent : AbilityConditionDataless
    {
        private readonly Identifier talentIdentifier;

        public AbilityConditionAllyHasTalent(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            talentIdentifier = conditionElement.GetAttributeIdentifier("identifier", Identifier.Empty);
        }

        protected override bool MatchesConditionSpecific()
        {
            foreach (Character crewCharacter in Character.GetFriendlyCrew(characterTalent.Character))
            {
                if (crewCharacter.HasTalent(talentIdentifier)) { return true; }
            }
            return false;
        }
    }
}