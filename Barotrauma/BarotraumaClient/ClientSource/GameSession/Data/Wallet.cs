namespace Barotrauma
{
    internal partial class Wallet
    {
        public bool IsOwnWallet =>
            GameMain.GameSession?.Campaign switch
            {
                null => false,
                SinglePlayerCampaign spCampaign => this == spCampaign.Bank,
                MultiPlayerCampaign mpCampaign => this == mpCampaign.PersonalWallet,
                _ => false
            };

        partial void SettingsChanged(Option<int> balanceChanged, Option<int> rewardChanged)
        {
            if (Owner.TryUnwrap(out var character))
            {
                if (!character.IsPlayer) { return; }
            }

            CampaignMode campaign = GameMain.GameSession?.Campaign;
            WalletChangedData data = new WalletChangedData
            {
                BalanceChanged = balanceChanged,
                RewardDistributionChanged = rewardChanged,
            };

            campaign?.OnMoneyChanged.Invoke(new WalletChangedEvent(this, data, CreateWalletInfo()));
        }
    }
}