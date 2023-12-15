﻿using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionAffliction : AbilityConditionData
    {
        private readonly string[] afflictions;
        public AbilityConditionAffliction(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            afflictions = conditionElement.GetAttributeStringArray("afflictions", new string[0], convertToLowerInvariant: true);
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityAffliction)?.Affliction is Affliction affliction)
            {
                return afflictions.Any(a => a == affliction.Identifier);
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityAttackResult));
                return false;
            }
        }
    }
}
