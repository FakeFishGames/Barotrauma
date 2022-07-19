using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGainSimultaneousSkill : CharacterAbility
    {
        private readonly Identifier skillIdentifier;
        private readonly bool ignoreAbilitySkillGain;

        public CharacterAbilityGainSimultaneousSkill(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            skillIdentifier = abilityElement.GetAttributeIdentifier("skillidentifier", "");
            ignoreAbilitySkillGain = abilityElement.GetAttributeBool("ignoreabilityskillgain", true);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is AbilitySkillGain abilitySkillGain)
            {
                if (ignoreAbilitySkillGain && abilitySkillGain.GainedFromAbility) { return; }
                Character.Info?.IncreaseSkillLevel(skillIdentifier, abilitySkillGain.Value, gainedFromAbility: true);
            }
            else
            {
                LogAbilityObjectMismatch();
            }
        }
    }
}
