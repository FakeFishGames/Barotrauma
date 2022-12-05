using Barotrauma.Extensions;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionItem : AbilityConditionData
    {
        private readonly ImmutableArray<Identifier> identifiers;
        private readonly ImmutableArray<Identifier> tags;
        private readonly MapEntityCategory category = MapEntityCategory.None;

        public AbilityConditionItem(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            identifiers = conditionElement.GetAttributeIdentifierArray("identifiers", Array.Empty<Identifier>()).ToImmutableArray();
            tags = conditionElement.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToImmutableArray();
            category = conditionElement.GetAttributeEnum("category", MapEntityCategory.None);

            if (identifiers.None() && tags.None() && category == MapEntityCategory.None)
            {
                DebugConsole.ThrowError($"Error in talent \"{characterTalent}\". No identifiers, tags or category defined.");
            }
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
                if (category != MapEntityCategory.None)
                {
                    if (!itemPrefab.Category.HasFlag(category)) { return false; }
                }

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
