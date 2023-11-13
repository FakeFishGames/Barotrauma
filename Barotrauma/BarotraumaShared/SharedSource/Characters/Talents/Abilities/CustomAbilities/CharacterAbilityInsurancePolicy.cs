namespace Barotrauma.Abilities
{
    class CharacterAbilityInsurancePolicy : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private readonly int moneyPerMission;

        public CharacterAbilityInsurancePolicy(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            moneyPerMission = abilityElement.GetAttributeInt("moneypermission", 0);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (Character?.Info is CharacterInfo info && GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                int totalAmount = moneyPerMission * info.MissionsCompletedSinceDeath;
                campaign.Bank.Give(totalAmount);
                GameAnalyticsManager.AddMoneyGainedEvent(totalAmount, GameAnalyticsManager.MoneySource.Ability, CharacterTalent.Prefab.Identifier.Value);
            }
        }
    }
}
