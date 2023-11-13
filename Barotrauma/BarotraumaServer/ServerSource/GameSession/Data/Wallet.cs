using System.Collections.Generic;

namespace Barotrauma
{
    internal partial class Wallet
    {
        private readonly Queue<WalletChangedData> transactions = new Queue<WalletChangedData>();

        public bool ShouldForceUpdate;

        partial void SettingsChanged(Option<int> balanceChanged, Option<int> rewardChanged)
        {
            transactions.Enqueue(new WalletChangedData
            {
                BalanceChanged = balanceChanged,
                RewardDistributionChanged =  rewardChanged
            });
        }

        /// <summary>
        /// Forces the server to sync the state of the wallet regardless if the balance/reward has changed
        /// </summary>
        public void ForceUpdate()
        {
            SettingsChanged(balanceChanged: Option<int>.Some(0), rewardChanged: Option<int>.None());
            ShouldForceUpdate = true;
        }

        public bool HasTransactions() => transactions.Count > 0;

        public NetWalletTransaction DequeueAndMergeTransactions(ushort id)
        {
            Option<ushort> targetCharacterID = id == Entity.NullEntityID ? Option<ushort>.None() : Option<ushort>.Some(id);

            WalletChangedData changedData = new WalletChangedData
            {
                BalanceChanged = Option<int>.None(),
                RewardDistributionChanged = Option<int>.None()
            };

            while (transactions.TryDequeue(out WalletChangedData transactionOut))
            {
                changedData = changedData.MergeInto(transactionOut);
            }

            return new NetWalletTransaction
            {
                CharacterID = targetCharacterID,
                ChangedData = changedData,
                Info = CreateWalletInfo()
            };
        }
    }
}