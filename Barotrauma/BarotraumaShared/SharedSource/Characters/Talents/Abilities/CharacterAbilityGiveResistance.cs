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
                DebugConsole.ThrowError("Error in CharacterAbilityGiveResistance - resistance identifier not set.",
                    contentPackage: abilityElement.ContentPackage);
            }

            // NOTE: The resistance value is a multiplier here, so 1.0 == 0% resistance
            if (MathUtils.NearlyEqual(multiplier, 1))
            {
                DebugConsole.AddWarning($"Possible error in talent {CharacterTalent.DebugIdentifier} - multiplier set to 1, which will do nothing.",
                    contentPackage: abilityElement.ContentPackage);
            }
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            TalentResistanceIdentifier identifier = new(resistanceId, CharacterTalent.Prefab.Identifier);
            Character.ChangeAbilityResistance(identifier, multiplier);
        }
    }
}
