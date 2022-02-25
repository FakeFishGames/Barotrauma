using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

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
            IEnumerable<Character> chosenCharacters = Character.GetFriendlyCrew(Character).Where(c => allowSelf || c != Character);

            foreach (Character character in chosenCharacters)
            {
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
