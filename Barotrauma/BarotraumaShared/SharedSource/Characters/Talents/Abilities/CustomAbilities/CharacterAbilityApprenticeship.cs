using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApprenticeship : CharacterAbility
    {
        private readonly bool ignoreAbilitySkillGain;

        public CharacterAbilityApprenticeship(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            ignoreAbilitySkillGain = abilityElement.GetAttributeBool("ignoreabilityskillgain", true);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is AbilitySkillGain abilitySkillGain && abilitySkillGain.Character != Character)
            {
                if (ignoreAbilitySkillGain && abilitySkillGain.GainedFromAbility) { return; }
                Character.Info?.IncreaseSkillLevel(abilitySkillGain.SkillIdentifier, 1.0f, gainedFromAbility: true);
            }
        }
    }
}
