namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyValue : CharacterAbility
    {
        private readonly float addedValue;
        private readonly float multiplyValue;

        public CharacterAbilityModifyValue(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedValue = abilityElement.GetAttributeFloat("addedvalue", 0f);
            multiplyValue = abilityElement.GetAttributeFloat("multiplyvalue", 1f);
            if (MathUtils.NearlyEqual(addedValue, 0.0f) && MathUtils.NearlyEqual(multiplyValue, 1.0f))
            {
                DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}, {nameof(CharacterAbilityModifyValue)} - added value is 0 and multiplier is 1, the ability will do nothing.");
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityValue abilityValue)
            {
                abilityValue.Value += addedValue;
                abilityValue.Value *= multiplyValue;
            }
        }
    }
}
