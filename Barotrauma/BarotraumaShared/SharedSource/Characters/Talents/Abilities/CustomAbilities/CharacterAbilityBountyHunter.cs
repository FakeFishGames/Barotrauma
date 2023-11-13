namespace Barotrauma.Abilities
{
    class CharacterAbilityBountyHunter : CharacterAbility
    {
        private readonly float vitalityPercentage;

        public CharacterAbilityBountyHunter(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            vitalityPercentage = abilityElement.GetAttributeFloat("vitalitypercentage", 0f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character character)
            {
                int totalAmount = (int)(vitalityPercentage * character.MaxVitality);
                Character.GiveMoney(totalAmount);
                GameAnalyticsManager.AddMoneyGainedEvent(totalAmount, GameAnalyticsManager.MoneySource.Ability, CharacterTalent.Prefab.Identifier.Value);
            }
        }
    }
}
