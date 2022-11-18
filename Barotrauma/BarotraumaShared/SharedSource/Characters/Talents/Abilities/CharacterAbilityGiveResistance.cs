namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveResistance : CharacterAbility
    {
        private readonly Identifier resistanceId;
        private readonly float multiplier;

        public CharacterAbilityGiveResistance(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeIdentifier("resistanceid", abilityElement.GetAttributeIdentifier("resistance", Identifier.Empty));
            multiplier = abilityElement.GetAttributeFloat("multiplier", 1f);

            if (resistanceId.IsEmpty)
            {
                DebugConsole.ThrowError("Error in CharacterAbilityGiveResistance - resistance identifier not set.");
            }
            if (MathUtils.NearlyEqual(multiplier, 1))
            {
                DebugConsole.AddWarning($"Possible error in talent {CharacterTalent.DebugIdentifier} - multiplier set to 1, which will do nothing.");
            }

        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            TalentResistanceIdentifier identifier = new(resistanceId, CharacterTalent.Prefab.Identifier);
            Character.ChangeAbilityResistance(identifier, multiplier);
        }
    }
}
