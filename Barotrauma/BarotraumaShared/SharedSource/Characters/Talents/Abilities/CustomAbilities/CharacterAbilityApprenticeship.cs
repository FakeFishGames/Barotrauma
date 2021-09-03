using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApprenticeship : CharacterAbility
    {
        public CharacterAbilityApprenticeship(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        protected override void ApplyEffect(object abilityData)
        {
            if (abilityData is AbilityStringCharacter abilityStringCharacter && abilityStringCharacter.Character != Character)
            {
                Character.Info?.IncreaseSkillLevel(abilityStringCharacter.String, 1.0f, abilityStringCharacter.Character.Position + Vector2.UnitY * 175.0f);
            }
        }
    }
}
