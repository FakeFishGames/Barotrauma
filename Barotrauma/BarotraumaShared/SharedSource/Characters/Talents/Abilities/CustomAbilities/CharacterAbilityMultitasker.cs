using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityMultitasker : CharacterAbility
    {
        private Identifier lastSkillIdentifier;

        public CharacterAbilityMultitasker(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilitySkillIdentifier { SkillIdentifier: Identifier skillIdentifier })
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
