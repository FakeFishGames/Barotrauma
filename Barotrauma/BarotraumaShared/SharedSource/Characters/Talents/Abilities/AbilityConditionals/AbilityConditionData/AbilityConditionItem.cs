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

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            ItemPrefab item = null;
            if (abilityData is Item tempItem)
            {
                item = tempItem.Prefab;
            }
            // this and other instances of this type of casting will be refactored
            else if (abilityData is (ItemPrefab itemPrefab, object _))
            {
                item = itemPrefab;
            }

            if (item != null)
            {
                if (!string.IsNullOrEmpty(identifier))
                {
                    if (item.Identifier != identifier)
                    {
                        return false;
                    }
                }

                return tags.Any(t => item.Tags.Any(p => t == p));
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(Item));
                return false;
            }
        }
    }
}
