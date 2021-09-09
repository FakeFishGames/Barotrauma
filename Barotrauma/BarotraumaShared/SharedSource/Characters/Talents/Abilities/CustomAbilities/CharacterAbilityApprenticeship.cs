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

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityString)?.String is string skillIdentifier && (abilityObject as IAbilityCharacter)?.Character is Character character)
            {
                Character.Info?.IncreaseSkillLevel(skillIdentifier, 1.0f, character.Position + Vector2.UnitY * 175.0f);
            }
        }
    }
}
