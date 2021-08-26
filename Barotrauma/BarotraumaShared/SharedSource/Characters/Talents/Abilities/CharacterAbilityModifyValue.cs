using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyValue : CharacterAbility
    {
        private float addedValue;
        private float multiplierValue;

        public CharacterAbilityModifyValue(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedValue = abilityElement.GetAttributeFloat("addedvalue", 0f);
            multiplierValue = abilityElement.GetAttributeFloat("multipliervalue", 1f);
        }

        protected override void ApplyEffect(object abilityData)
        {
            if (abilityData is AbilityValue abilityValue)
            {
                ApplyEffectSpecific(abilityValue);
            }
            else if (abilityData is (object _, AbilityValue tupleAbilityValue))
            {
                ApplyEffectSpecific(tupleAbilityValue);
            }
        }

        private void ApplyEffectSpecific(AbilityValue abilityValue)
        {
            abilityValue.Value += addedValue;
            abilityValue.Value *= multiplierValue;
        }

    }

    // this seems like a real silly way to have to pass values by reference into these same interfaces
    // if more of these are required, maybe there should be an additional set of interfaces to easily pass values by reference instead
    class AbilityValue
    {
        public float Value { get; set; }
        public AbilityValue(float value)
        {
            Value = value;
        }
    }
}
