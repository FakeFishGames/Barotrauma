namespace Barotrauma.Abilities
{
    class AbilityConditionCrouched : AbilityConditionDataless
    {

        public AbilityConditionCrouched(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.AnimController is HumanoidAnimController humanoidAnimController && humanoidAnimController.Crouching;
        }
    }
}
