namespace Barotrauma.Abilities
{
    class AbilityConditionInWater : AbilityConditionDataless
    {
        public AbilityConditionInWater(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific()
        {
            return character.InWater;
        }
    }
}
