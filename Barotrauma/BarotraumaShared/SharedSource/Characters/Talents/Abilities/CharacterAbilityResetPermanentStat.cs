
namespace Barotrauma.Abilities
{
    class CharacterAbilityResetPermanentStat : CharacterAbility
    {
        private readonly Identifier statIdentifier;
        public override bool AppliesEffectOnIntervalUpdate => true;
        public override bool AllowClientSimulation => true;

        public CharacterAbilityResetPermanentStat(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statIdentifier = abilityElement.GetAttributeIdentifier("statidentifier", Identifier.Empty);
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
