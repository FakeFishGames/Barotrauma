using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityMultitasker : CharacterAbility
    {
        private string lastSkillIdentifier;

        public CharacterAbilityMultitasker(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilitySkillIdentifier)?.SkillIdentifier is string skillIdentifier)
            {
                if (skillIdentifier != lastSkillIdentifier)
                {
                    lastSkillIdentifier = skillIdentifier;
                    Character.Info?.IncreaseSkillLevel(skillIdentifier, 1.0f, gainedFromAbility: true);
                }
            }
        }
    }
}
