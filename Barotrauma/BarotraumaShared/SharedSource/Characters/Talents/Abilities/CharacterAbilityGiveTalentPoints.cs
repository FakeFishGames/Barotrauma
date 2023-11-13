namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveTalentPoints : CharacterAbility
    {
        private readonly int amount;

        public CharacterAbilityGiveTalentPoints(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            amount = abilityElement.GetAttributeInt("amount", 0); 
            if (amount == 0)
            {
                DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}, amount of talent points to give is 0.");
            }
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (addingFirstTime && Character.Info != null)
            {
                Character.Info.AdditionalTalentPoints += amount;
            }
        }
    }
}
