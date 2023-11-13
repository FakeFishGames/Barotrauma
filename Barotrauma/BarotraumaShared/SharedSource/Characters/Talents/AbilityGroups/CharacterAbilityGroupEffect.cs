using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGroupEffect : CharacterAbilityGroup
    {
        public CharacterAbilityGroupEffect(AbilityEffectType abilityEffectType, CharacterTalent characterTalent, ContentXElement abilityElementGroup) :
            base(abilityEffectType, characterTalent, abilityElementGroup) { }

        public void CheckAbilityGroup(AbilityObject abilityObject)
        {
            if (!IsActive) { return; }

            if (IsOverTriggerCount) { return; }

            List<CharacterAbility> abilities = IsApplicable(abilityObject) ? characterAbilities : fallbackAbilities;

            foreach (CharacterAbility characterAbility in abilities)
            {
                if (characterAbility.IsViable())
                {
                    characterAbility.ApplyAbilityEffect(abilityObject);
                }
            }

            if (abilities.Count > 0)
            {
                timesTriggered++;
            }
        }

        private bool IsOverTriggerCount => timesTriggered >= maxTriggerCount;

        private bool IsApplicable(AbilityObject abilityObject)
        {
            foreach (var abilityCondition in abilityConditions)
            {
                if (!abilityCondition.MatchesCondition(abilityObject))
                {
                    return false;
                }
            }

            return true;
        }
    }
}