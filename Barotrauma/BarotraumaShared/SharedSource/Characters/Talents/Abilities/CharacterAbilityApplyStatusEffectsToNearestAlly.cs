using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToNearestAlly : CharacterAbilityApplyStatusEffects
    {
        protected float squaredMaxDistance;
        public CharacterAbilityApplyStatusEffectsToNearestAlly(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            squaredMaxDistance = MathF.Pow(abilityElement.GetAttributeFloat("maxdistance", float.MaxValue), 2);
        }

        protected override void ApplyEffect()
        {
            Character closestCharacter = null;
            float closestDistance = float.MaxValue;

            foreach (Character crewCharacter in Character.GetFriendlyCrew(Character))
            {
                if (crewCharacter != Character && Vector2.DistanceSquared(Character.WorldPosition, crewCharacter.WorldPosition) is float tempDistance && tempDistance < closestDistance)
                {
                    closestCharacter = crewCharacter;
                    closestDistance = tempDistance;
                }
            }

            if (closestDistance < squaredMaxDistance)
            {
                ApplyEffectSpecific(closestCharacter);
            }
        }
    }
}
