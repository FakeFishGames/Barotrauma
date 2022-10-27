using System;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasItem : AbilityConditionDataless
    {
        private readonly string[] tags;
        readonly bool requireAll;

        public AbilityConditionHasItem(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            tags = conditionElement.GetAttributeStringArray("tags", Array.Empty<string>());
            requireAll = conditionElement.GetAttributeBool("requireall", false);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (tags.None())
            {
                return character.GetEquippedItem(null) != null;
            }

            if (requireAll)
            {
                foreach (string tag in tags)
                {
                    if (character.GetEquippedItem(tag) == null) { return false; }
                }
                return true;
            }
            else
            {
                foreach (string tag in tags)
                {
                    if (character.GetEquippedItem(tag) != null) { return true; }
                }
                return false;
            }
        }
    }
}
