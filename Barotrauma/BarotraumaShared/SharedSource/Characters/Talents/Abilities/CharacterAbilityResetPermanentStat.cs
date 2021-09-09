using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityResetPermanentStat : CharacterAbility
    {
        private readonly string statIdentifier;
        public override bool AppliesEffectOnIntervalUpdate => true;

        public CharacterAbilityResetPermanentStat(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statIdentifier = abilityElement.GetAttributeString("statidentifier", "").ToLowerInvariant();
        }
        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffectSpecific();
        }

        protected override void ApplyEffect()
        {
            ApplyEffectSpecific();
        }

        private void ApplyEffectSpecific()
        {
            Character?.Info.ResetSavedStatValue(statIdentifier);
        }
    }
}
