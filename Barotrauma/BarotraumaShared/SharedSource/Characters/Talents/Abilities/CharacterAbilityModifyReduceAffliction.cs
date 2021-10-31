using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyReduceAffliction : CharacterAbility
    {
        float addedAmountMultiplier;

        public CharacterAbilityModifyReduceAffliction(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedAmountMultiplier = abilityElement.GetAttributeFloat("addedamountmultiplier", 0f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is AbilityValueAffliction afflictionReduceAmount)
            {
                afflictionReduceAmount.Affliction.Strength -= addedAmountMultiplier * afflictionReduceAmount.Value;
            }
            else
            {
                LogabilityObjectMismatch();
            }
        }
    }
}
