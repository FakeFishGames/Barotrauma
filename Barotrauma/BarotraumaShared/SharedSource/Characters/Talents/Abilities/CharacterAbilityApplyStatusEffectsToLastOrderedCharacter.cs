using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToLastOrderedCharacter : CharacterAbilityApplyStatusEffects
    {
        public CharacterAbilityApplyStatusEffectsToLastOrderedCharacter(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        protected override void ApplyEffect()
        {
            if (IsViableTarget(Character.LastOrderedCharacter))
            {
                ApplyEffectSpecific(Character.LastOrderedCharacter);
            }
            if (Character.HasAbilityFlag(AbilityFlags.AllowSecondOrderedTarget) && IsViableTarget(Character.SecondLastOrderedCharacter))
            {
                ApplyEffectSpecific(Character.SecondLastOrderedCharacter);
            }
        }

        private bool IsViableTarget(Character targetCharacter)
        {
            if (targetCharacter == null || targetCharacter.Removed) { return false; }
            if (targetCharacter == Character) { return false; }
            return true;
        }
    }
}
