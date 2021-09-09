using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionItem : AbilityConditionData
    {
        private readonly string identifier;
        private readonly string[] tags;

        public AbilityConditionItem(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            identifier = conditionElement.GetAttributeString("identifier", string.Empty).ToLowerInvariant();
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
                if (!string.IsNullOrEmpty(identifier))
                {
                    if (itemPrefab.Identifier != identifier)
                    {
                        return false;
                    }
                }

                return tags.Any(t => itemPrefab.Tags.Any(p => t == p));
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityItemPrefab));
                return false;
            }
        }
    }
}
