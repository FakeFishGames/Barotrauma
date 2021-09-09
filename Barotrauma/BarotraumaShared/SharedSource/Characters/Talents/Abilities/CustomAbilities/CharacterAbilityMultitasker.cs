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
            if ((abilityObject as IAbilityString)?.String is string skillIdentifier)
            {
                if (skillIdentifier != lastSkillIdentifier)
                {
                    lastSkillIdentifier = skillIdentifier;
                    Character.Info?.IncreaseSkillLevel(skillIdentifier, 1.0f, Character.Position + Vector2.UnitY * 175.0f);
                }
            }
        }
    }
}
