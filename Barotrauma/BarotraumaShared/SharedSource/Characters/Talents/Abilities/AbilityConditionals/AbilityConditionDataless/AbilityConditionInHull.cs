namespace Barotrauma.Abilities
{
    class AbilityConditionInHull : AbilityConditionDataless
    {
        public AbilityConditionInHull(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific()
        {
            return character.CurrentHull != null;
        }
    }
}
