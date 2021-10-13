using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGainSimultaneousSkill : CharacterAbility
    {
        private string skillIdentifier;

        public CharacterAbilityGainSimultaneousSkill(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            skillIdentifier = abilityElement.GetAttributeString("skillidentifier", "").ToLowerInvariant();
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityValue)?.Value is float skillIncrease)
            {
                Character.Info?.IncreaseSkillLevel(skillIdentifier, skillIncrease);
            }
            else
            {
                LogabilityObjectMismatch();
            }
        }
    }
}
