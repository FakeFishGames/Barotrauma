using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityAtmosMachine : CharacterAbility
    {
        private readonly float addedValue;
        private readonly float multiplyValue;
        private readonly string[] tags;
        private readonly int maxMultiplyCount;

        public CharacterAbilityAtmosMachine(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            addedValue = abilityElement.GetAttributeFloat("addedvalue", 0f);
            multiplyValue = abilityElement.GetAttributeFloat("multiplyvalue", 1f);
            tags = abilityElement.GetAttributeStringArray("tags", Array.Empty<string>(), convertToLowerInvariant: true);
            maxMultiplyCount = abilityElement.GetAttributeInt("maxmultiplycount", int.MaxValue);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityValue abilityValue)
            {
                int multiplyCount = 0;

                foreach (Item item in Item.ItemList)
                {
                    if (item.Prefab.Tags.Any(t => tags.Contains(t)))
                    {
                        multiplyCount++;
                        if (multiplyCount == maxMultiplyCount)
                        {
                            break;
                        }
                    }
                }
                abilityValue.Value += addedValue * multiplyCount;
            }
        }
    }
}
