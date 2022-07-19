using System;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasItem : AbilityConditionDataless
    {
        // not used for anything atm, will be used for clown subclass
        private readonly string[] tags;
        private InvSlotType? invSlotType;
        bool requireAll;

        private List<Item> items = new List<Item>();

        public AbilityConditionHasItem(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            tags = conditionElement.GetAttributeStringArray("tags", Array.Empty<string>(), convertToLowerInvariant: true);
            requireAll = conditionElement.GetAttributeBool("requireall", false);
            //this.invSlotType = invSlotType;
        }

        protected override bool MatchesConditionSpecific()
        {
            items.Clear();
            if (tags.Any())
            {
                foreach (string tag in tags)
                {
                    // there is a better method, should use that instead
                    if (character.GetEquippedItem(tag, invSlotType) is Item foundItem)
                    {
                        items.Add(foundItem);
                    }
                }

            }
            else
            {
                if (character.GetEquippedItem(null, invSlotType) is Item foundItem)
                {
                    items.Add(foundItem);
                }
            }

            if (requireAll)
            {
                return (items.Count >= tags.Count());
            }
            else
            {
                return items.Any();
            }
        }
    }
}
