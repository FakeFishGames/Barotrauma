using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityRevive : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        public CharacterAbilityRevive(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        private void ApplyEffectSpecific()
        {
            Character.Revive(removeAllAfflictions: false);
        }

        protected override void ApplyEffect()
        {
            ApplyEffectSpecific();
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffectSpecific();
        }
    }
}
