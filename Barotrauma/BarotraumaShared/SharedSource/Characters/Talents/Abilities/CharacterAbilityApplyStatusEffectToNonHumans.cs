#nullable enable

using Microsoft.Xna.Framework;

namespace Barotrauma.Abilities
{
    internal sealed class CharacterAbilityApplyStatusEffectToNonHumans : CharacterAbilityApplyStatusEffects
    {
        private readonly float maxDistance;

        public CharacterAbilityApplyStatusEffectToNonHumans(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            maxDistance = abilityElement.GetAttributeFloat("maxdistance", float.MaxValue);
        }

        protected override void ApplyEffect()
        {
            foreach (Character character in Character.CharacterList)
            {
                if (character.IsHuman) { continue; }

                if (maxDistance < float.MaxValue)
                {
                    if (Vector2.DistanceSquared(character.WorldPosition, Character.WorldPosition) > maxDistance * maxDistance) { continue; }
                }
                ApplyEffectSpecific(character);
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffect();
        }
    }
}