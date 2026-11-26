using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    public class CharacterAbilityGroupEffect : CharacterAbilityGroup
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

        /// <summary>
        /// Same as <see cref="CheckAbilityGroup"/>, but without side effects: only the abilities that just modify the ability object are applied, and this doesn't count as a trigger.
        /// </summary>
        public void PreviewAbilityGroup(AbilityObject abilityObject)
        {
            if (!IsActive) { return; }

            if (IsOverTriggerCount) { return; }

            List<CharacterAbility> abilities = IsApplicable(abilityObject) ? characterAbilities : fallbackAbilities;

            foreach (CharacterAbility characterAbility in abilities)
            {
                if (characterAbility.OnlyModifiesAbilityObject && characterAbility.IsViable())
                {
                    characterAbility.ApplyAbilityEffect(abilityObject);
                }
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