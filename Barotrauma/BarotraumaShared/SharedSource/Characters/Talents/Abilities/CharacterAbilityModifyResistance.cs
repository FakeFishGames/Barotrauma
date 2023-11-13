namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyResistance : CharacterAbility
    {
        private readonly Identifier resistanceId;
        private readonly float multiplier;
        bool lastState;
        public override bool AllowClientSimulation => true;

        // should probably be split to different classes
        public CharacterAbilityModifyResistance(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeIdentifier("resistanceid", abilityElement.GetAttributeIdentifier("resistance", Identifier.Empty));
            multiplier = abilityElement.GetAttributeFloat("multiplier", 1f);

            if (resistanceId.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier} - resistance identifier not set in {nameof(CharacterAbilityModifyResistance)}.");
            }
            if (MathUtils.NearlyEqual(multiplier, 1.0f))
            {
                DebugConsole.AddWarning($"Possible error in talent {CharacterTalent.DebugIdentifier} - resistance set to 1, which will do nothing.");
            }
        }

        public override void UpdateCharacterAbility(bool conditionsMatched, float timeSinceLastUpdate)
        {
            if (conditionsMatched != lastState)
            {
                TalentResistanceIdentifier identifier = new(resistanceId, CharacterTalent.Prefab.Identifier);
                if (conditionsMatched)
                {
                    Character.ChangeAbilityResistance(identifier, multiplier);
                }
                else
                {
                    Character.RemoveAbilityResistance(identifier);
                }
                lastState = conditionsMatched;
            }
        }
    }
}
