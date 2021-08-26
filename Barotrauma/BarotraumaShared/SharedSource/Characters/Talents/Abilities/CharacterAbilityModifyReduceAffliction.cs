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

        protected override void ApplyEffect(object abilityData)
        {
            if (abilityData is (Affliction affliction, float reduceAmount))
            {
                affliction.Strength -= addedAmountMultiplier * reduceAmount;
            }
            else
            {
                LogAbilityDataMismatch();
            }
        }
    }
}
