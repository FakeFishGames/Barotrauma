using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyValue : CharacterAbility
    {
        private readonly float addedValue;
        private readonly float multiplyValue;

        public CharacterAbilityModifyValue(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedValue = abilityElement.GetAttributeFloat("addedvalue", 0f);
            multiplyValue = abilityElement.GetAttributeFloat("multiplyvalue", 1f);
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
