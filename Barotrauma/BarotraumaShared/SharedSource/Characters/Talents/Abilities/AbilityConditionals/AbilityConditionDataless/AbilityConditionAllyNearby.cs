using System;
using Microsoft.Xna.Framework;

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionAllyNearby : AbilityConditionDataless
    {
        private enum NearbyCharacterTruthy
        {
            OneCharacterMatches,
            NoCharacterMatches
        }

        private readonly NearbyCharacterTruthy truthyWhen;
        private readonly float distance;

        public AbilityConditionAllyNearby(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            truthyWhen = conditionElement.GetAttributeEnum("truthywhen", NearbyCharacterTruthy.OneCharacterMatches);
            distance = conditionElement.GetAttributeFloat("distance", 10f);
        }

        protected override bool MatchesConditionSpecific()
        {
            bool trueCondition = truthyWhen switch
            {
                NearbyCharacterTruthy.OneCharacterMatches => true,
                NearbyCharacterTruthy.NoCharacterMatches => false,
                _ => throw new ArgumentOutOfRangeException(nameof(truthyWhen))
            };

            foreach (Character ally in Character.GetFriendlyCrew(character))
            {
                if (ally == character) { continue; }

                float distanceToCharacter = Vector2.DistanceSquared(ally.WorldPosition, character.WorldPosition);

                if (distanceToCharacter < distance * distance)
                {
                    return trueCondition;
                }
            }

            return !trueCondition;
        }
    }
}