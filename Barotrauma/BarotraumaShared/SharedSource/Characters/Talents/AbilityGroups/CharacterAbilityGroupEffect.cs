using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGroupEffect : CharacterAbilityGroup
    {
        public CharacterAbilityGroupEffect(AbilityEffectType abilityEffectType, CharacterTalent characterTalent, XElement abilityElementGroup) : 
            base(abilityEffectType, characterTalent, abilityElementGroup) { }

        public void CheckAbilityGroup(AbilityObject abilityObject)
        {
            if (!IsActive) { return; }
            if (IsApplicable(abilityObject))
            {
                foreach (var characterAbility in characterAbilities)
                {
                    if (characterAbility.IsViable())
                    {
                        characterAbility.ApplyAbilityEffect(abilityObject);
                    }
                }
            }
        }

        private bool IsApplicable(AbilityObject abilityObject)
        {
            if (timesTriggered >= maxTriggerCount) { return false; }
            return abilityConditions.All(c => c.MatchesCondition(abilityObject));
        }
    }
}
