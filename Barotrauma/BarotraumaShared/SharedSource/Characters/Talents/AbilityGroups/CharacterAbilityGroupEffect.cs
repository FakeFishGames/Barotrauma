using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGroupEffect : CharacterAbilityGroup
    {
        public CharacterAbilityGroupEffect(CharacterTalent characterTalent, XElement abilityElementGroup) : base(characterTalent, abilityElementGroup) { }

        public void CheckAbilityGroup(object abilityData)
        {
            if (!IsActive) { return; }
            if (IsApplicable(abilityData))
            {
                foreach (var characterAbility in characterAbilities)
                {
                    if (characterAbility.IsViable())
                    {
                        characterAbility.ApplyAbilityEffect(abilityData);
                    }
                }
            }
        }

        private bool IsApplicable(object abilityData)
        {
            if (timesTriggered >= maxTriggerCount) { return false; }
            return abilityConditions.All(c => c.MatchesCondition(abilityData));
        }
    }
}
