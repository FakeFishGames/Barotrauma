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
            if (abilityObject is AbilitySkillGain abilitySkillGain && !abilitySkillGain.GainedFromApprenticeship && abilitySkillGain.Character != Character)
            {
                Character.Info?.IncreaseSkillLevel(abilitySkillGain.String, 1.0f, gainedFromApprenticeship: true);
            }
        }
    }
}
