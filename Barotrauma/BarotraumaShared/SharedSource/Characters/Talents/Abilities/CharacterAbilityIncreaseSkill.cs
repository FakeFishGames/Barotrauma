using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityIncreaseSkill : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private string skillIdentifier;
        private float skillIncrease;

        public CharacterAbilityIncreaseSkill(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            skillIdentifier = abilityElement.GetAttributeString("skillidentifier", "").ToLowerInvariant();
            skillIncrease = abilityElement.GetAttributeFloat("skillincrease", 0f);
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
            character.Info?.IncreaseSkillLevel(skillIdentifier, skillIncrease, character.Position + Vector2.UnitY * 175.0f);
        }
    }
}
