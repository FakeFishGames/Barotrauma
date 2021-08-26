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
            if (abilityData is (string skillIdentifier, Character character) && character != Character)
            {
                character.Info?.IncreaseSkillLevel(skillIdentifier, 1.0f, character.Position + Vector2.UnitY * 175.0f);
            }
        }
    }
}
