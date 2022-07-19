namespace Barotrauma.Abilities
{
    class CharacterAbilityGroupEffect : CharacterAbilityGroup
    {
        public CharacterAbilityGroupEffect(AbilityEffectType abilityEffectType, CharacterTalent characterTalent, ContentXElement abilityElementGroup) : 
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
                timesTriggered++;
            }
        }

        private bool IsApplicable(AbilityObject abilityObject)
        {
            if (timesTriggered >= maxTriggerCount) { return false; }
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
