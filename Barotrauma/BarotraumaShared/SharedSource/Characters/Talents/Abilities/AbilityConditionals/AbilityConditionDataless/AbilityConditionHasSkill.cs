namespace Barotrauma.Abilities
{
    class AbilityConditionHasSkill : AbilityConditionDataless
    {
        private readonly Identifier skillIdentifier;
        private readonly float minValue;

        public AbilityConditionHasSkill(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            skillIdentifier = conditionElement.GetAttributeIdentifier("skillidentifier", Identifier.Empty);
            minValue = conditionElement.GetAttributeFloat("minvalue", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.GetSkillLevel(skillIdentifier) >= minValue;
        }
    }
}
