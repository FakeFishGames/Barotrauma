using Microsoft.Xna.Framework;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToAllies : CharacterAbilityApplyStatusEffects
    {
        private readonly bool allowSelf;
        private readonly float maxDistance = float.MaxValue;

        public CharacterAbilityApplyStatusEffectsToAllies(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            allowSelf = abilityElement.GetAttributeBool("allowself", true);
            maxDistance = abilityElement.GetAttributeFloat("maxdistance", float.MaxValue);
        }


        protected override void ApplyEffect()
        {
            foreach (Character character in Character.GetFriendlyCrew(Character))
            {
                if (!allowSelf && character == Character) { continue; }
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
