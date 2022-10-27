#nullable enable
using System;
using System.Collections.Immutable;
using Microsoft.Xna.Framework;

namespace Barotrauma.Abilities;

internal sealed class AbilityConditionNearbyCharacterCount : AbilityConditionDataless
{
    private readonly float distance;
    private readonly int count;
    private readonly ImmutableHashSet<TargetType> targetTypes;

    public AbilityConditionNearbyCharacterCount(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
    {
        distance = conditionElement.GetAttributeFloat("distance", 10f);
        count = conditionElement.GetAttributeInt("count", 1);
        targetTypes = ParseTargetTypes(conditionElement.GetAttributeStringArray("targettypes", Array.Empty<string>(), convertToLowerInvariant: true)).ToImmutableHashSet();
    }

    protected override bool MatchesConditionSpecific()
    {
        int amountNeeded = count;
        foreach (Character otherCharacter in Character.CharacterList)
        {
            if (character.Submarine != otherCharacter.Submarine) { continue; }
            if (!IsViableTarget(targetTypes, otherCharacter)) { return false; }

            if (Vector2.DistanceSquared(character.WorldPosition, otherCharacter.WorldPosition) < distance * distance)
            {
                amountNeeded--;

                if (amountNeeded <= 0) { return true; }
            }
        }

        return false;
    }
}