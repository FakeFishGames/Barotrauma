using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityIncreaseSkill : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private readonly string skillIdentifier;
        private readonly float skillIncrease;

        public CharacterAbilityIncreaseSkill(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            skillIdentifier = abilityElement.GetAttributeString("skillidentifier", "").ToLowerInvariant();
            skillIncrease = abilityElement.GetAttributeFloat("skillincrease", 0f);

            if (string.IsNullOrEmpty(skillIdentifier))
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
            if (skillIdentifier.Equals("random"))
            {
                var skill = character.Info?.Job?.Skills?.GetRandom();
                if (skill == null) { return; }
                character.Info?.IncreaseSkillLevel(skill.Identifier, skillIncrease, character.Position + Vector2.UnitY * 175.0f);
            }
            else
            {
                character.Info?.IncreaseSkillLevel(skillIdentifier, skillIncrease, character.Position + Vector2.UnitY * 175.0f);
            }

        }
    }
}
