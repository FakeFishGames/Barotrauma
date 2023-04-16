namespace Barotrauma.Abilities
{
    class CharacterAbilityGainSimultaneousSkill : CharacterAbility
    {
        private readonly Identifier skillIdentifier;

        private readonly bool ignoreAbilitySkillGain,
                              targetAllies;

        public CharacterAbilityGainSimultaneousSkill(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            skillIdentifier = abilityElement.GetAttributeIdentifier("skillidentifier", "");
            ignoreAbilitySkillGain = abilityElement.GetAttributeBool("ignoreabilityskillgain", true);
            targetAllies = abilityElement.GetAttributeBool("targetallies", false);
            if (skillIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}: skill identifier not defined.");
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is AbilitySkillGain abilitySkillGain)
            {
                if (ignoreAbilitySkillGain && abilitySkillGain.GainedFromAbility) { return; }
                Identifier identifier = skillIdentifier == "inherit" ? abilitySkillGain.SkillIdentifier : skillIdentifier;
                if (targetAllies)
                {
                    foreach (Character otherCharacter in Character.GetFriendlyCrew(Character))
                    {
                        if (otherCharacter == Character) { continue; }
                        otherCharacter.Info?.IncreaseSkillLevel(identifier, abilitySkillGain.Value, gainedFromAbility: true);
                    }
                }
                else
                {
                    Character.Info?.IncreaseSkillLevel(identifier, abilitySkillGain.Value, gainedFromAbility: true);
                }
            }
            else
            {
                LogAbilityObjectMismatch();
            }
        }
    }
}
