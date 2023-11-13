using Barotrauma.Extensions;

namespace Barotrauma.Abilities
{
    class CharacterAbilityIncreaseSkill : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private readonly Identifier skillIdentifier;
        private readonly float skillIncrease;

        public CharacterAbilityIncreaseSkill(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            skillIdentifier = abilityElement.GetAttributeIdentifier("skillidentifier", "");
            skillIncrease = abilityElement.GetAttributeFloat("skillincrease", 0f);

            if (skillIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in talent \"{characterAbilityGroup.CharacterTalent.DebugIdentifier}\" - skill identifier not defined in CharacterAbilityIncreaseSkill.");
            }
            if (MathUtils.NearlyEqual(skillIncrease, 0))
            {
                DebugConsole.AddWarning($"Possible error in talent \"{characterAbilityGroup.CharacterTalent.DebugIdentifier}\" - skill increase set to 0.");
            }
        }

        protected override void ApplyEffect()
        {
            ApplyEffectSpecific(Character);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character character)
            {
                ApplyEffectSpecific(character);
            }
            else
            {
                ApplyEffectSpecific(Character);
            }
        }

        private void ApplyEffectSpecific(Character character)
        {
            if (skillIdentifier == "random")
            {
                var skill = character.Info?.Job?.GetSkills()?.GetRandomUnsynced();
                if (skill == null) { return; }
                character.Info?.IncreaseSkillLevel(skill.Identifier, skillIncrease, gainedFromAbility: true);
            }
            else
            {
                character.Info?.IncreaseSkillLevel(skillIdentifier, skillIncrease, gainedFromAbility: true);
            }
        }
    }
}
