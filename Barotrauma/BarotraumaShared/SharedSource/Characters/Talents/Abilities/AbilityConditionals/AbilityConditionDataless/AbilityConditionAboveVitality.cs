namespace Barotrauma.Abilities
{
    class AbilityConditionAboveVitality : AbilityConditionDataless
    {
        private readonly float vitalityPercentage;

        public AbilityConditionAboveVitality(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            vitalityPercentage = conditionElement.GetAttributeFloat("vitalitypercentage", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.Vitality / character.MaxVitality > vitalityPercentage;
        }
    }
}
