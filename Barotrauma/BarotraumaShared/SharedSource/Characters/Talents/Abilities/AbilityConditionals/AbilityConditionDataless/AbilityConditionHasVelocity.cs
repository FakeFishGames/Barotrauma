namespace Barotrauma.Abilities
{
    class AbilityConditionHasVelocity : AbilityConditionDataless
    {
        private readonly float velocity;

        public AbilityConditionHasVelocity(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            velocity = conditionElement.GetAttributeFloat("velocity", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.AnimController.Collider.LinearVelocity.LengthSquared() > velocity * velocity;
        }
    }
}
