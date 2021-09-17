using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionItem : AbilityConditionData
    {
        private readonly string[] identifiers;
        private readonly string[] tags;

        public AbilityConditionItem(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            identifiers = conditionElement.GetAttributeStringArray("identifiers", Array.Empty<string>(), convertToLowerInvariant: true);
            tags = conditionElement.GetAttributeStringArray("tags", Array.Empty<string>(), convertToLowerInvariant: true);
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            ItemPrefab itemPrefab = null;
            if ((abilityObject as IAbilityItemPrefab)?.ItemPrefab is ItemPrefab abilityItemPrefab) 
            {
                itemPrefab = abilityItemPrefab;
            }
            else if ((abilityObject as IAbilityItem)?.Item is Item abilityItem)
            {
                itemPrefab = abilityItem.Prefab;
            }

            if (itemPrefab != null)
            {
                if (identifiers.Any())
                {
                    if (!identifiers.Any(t => itemPrefab.Identifier == t))
                    {
                        return false;
                    }
                }

                return !tags.Any() || tags.Any(t => itemPrefab.Tags.Any(p => t == p));
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityItemPrefab));
                return false;
            }
        }
    }
}
