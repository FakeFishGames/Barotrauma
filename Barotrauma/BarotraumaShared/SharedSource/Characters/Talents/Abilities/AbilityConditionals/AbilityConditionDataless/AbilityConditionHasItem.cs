using Barotrauma.Extensions;
using System;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasItem : AbilityConditionDataless
    {
        private readonly Identifier[] tags;
        readonly bool requireAll;

        public AbilityConditionHasItem(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            tags = conditionElement.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>());
            requireAll = conditionElement.GetAttributeBool("requireall", false);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (tags.None())
            {
                return character.GetEquippedItem(Identifier.Empty) != null;
            }

            if (requireAll)
            {
                foreach (Identifier tag in tags)
                {
                    if (character.GetEquippedItem(tag) == null) { return false; }
                }
                return true;
            }
            else
            {
                foreach (Identifier tag in tags)
                {
                    if (character.GetEquippedItem(tag) != null) { return true; }
                }
                return false;
            }
        }
    }
}
