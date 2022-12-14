#nullable enable
using System.Collections.Immutable;

namespace Barotrauma.Abilities;

internal sealed class AbilityConditionHoldingItem : AbilityConditionDataless
{
    private readonly ImmutableHashSet<Identifier> tags;

    public AbilityConditionHoldingItem(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
    {
        tags = conditionElement.GetAttributeIdentifierImmutableHashSet("tags", ImmutableHashSet<Identifier>.Empty);
    }

    protected override bool MatchesConditionSpecific()
    {
        if (tags.Count is 0)
        {
            return HasItemInHand(character, null);
        }

        foreach (Identifier tag in tags)
        {
            if (HasItemInHand(character, tag)) { return true; }
        }

        return false;

        static bool HasItemInHand(Character character, Identifier? tagOrIdentifier) =>
            character.GetEquippedItem(tagOrIdentifier?.Value, InvSlotType.RightHand) is not null ||
            character.GetEquippedItem(tagOrIdentifier?.Value, InvSlotType.LeftHand) is not null;

    }
}