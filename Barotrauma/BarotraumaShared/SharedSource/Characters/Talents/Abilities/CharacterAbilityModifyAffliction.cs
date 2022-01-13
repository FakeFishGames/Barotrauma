using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyAffliction : CharacterAbility
    {
        private readonly string[] afflictionIdentifiers;

        private readonly float addedMultiplier;

        public CharacterAbilityModifyAffliction(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            afflictionIdentifiers = abilityElement.GetAttributeStringArray("afflictionidentifiers", new string[0], convertToLowerInvariant: true);
            addedMultiplier = abilityElement.GetAttributeFloat("addedmultiplier", 0f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityAffliction)?.Affliction is Affliction affliction)
            {
                foreach (string afflictionIdentifier in afflictionIdentifiers)
                {
                    if (affliction.Identifier == afflictionIdentifier)
                    {
                        affliction.Strength *= 1 + addedMultiplier;
                    }
                }
            }
            else
            {
                LogAbilityObjectMismatch();
            }
        }
    }
}
