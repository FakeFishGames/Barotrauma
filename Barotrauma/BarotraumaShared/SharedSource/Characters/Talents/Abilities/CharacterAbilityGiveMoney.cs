using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveMoney : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private int amount;

        public CharacterAbilityGiveMoney(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            amount = abilityElement.GetAttributeInt("amount", 0);
        }

        protected override void ApplyEffect()
        {
            Character.GiveMoney(amount);
        }
    }
}
