using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToAllies : CharacterAbilityApplyStatusEffects
    {
        private readonly bool allowSelf;

        public CharacterAbilityApplyStatusEffectsToAllies(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            allowSelf = abilityElement.GetAttributeBool("allowself", true);
        }


        protected override void ApplyEffect()
        {
            IEnumerable<Character> chosenCharacters = Character.GetFriendlyCrew(Character).Where(c => allowSelf || c != Character);

            foreach (Character character in chosenCharacters)
            {
                ApplyEffectSpecific(character);
            }
        }

    }
}
