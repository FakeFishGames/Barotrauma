using System;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal readonly struct WalletChangedEvent
    {
        public readonly Option<Character> Owner;
        public readonly Wallet Wallet;
        public readonly WalletInfo Info;
        public readonly WalletChangedData ChangedData;

        public WalletChangedEvent(Wallet wallet, WalletChangedData changedData, WalletInfo info)
        {
            Wallet = wallet;
            Info = info;
            ChangedData = changedData;
            Owner = wallet.Owner;
        }
    }

    [NetworkSerialize]
    internal struct WalletInfo : INetSerializableStruct
    {
        public int RewardDistribution;
        public int Balance;
    }

    /// <summary>
    /// Network message for the server to update wallet values to clients
    /// </summary>
    internal struct NetWalletUpdate : INetSerializableStruct
    {
        [NetworkSerialize(ArrayMaxSize = 256)]
        public NetWalletTransaction[] Transactions;
    }

    /// <summary>
    /// Network message for the client to transfer money between wallets
    /// </summary>
    [NetworkSerialize]
    internal struct NetWalletTransfer : INetSerializableStruct
    {
        public Option<ushort> Sender;
        public Option<ushort> Receiver;
        public int Amount;
    }

    /// <summary>
    /// Network message for the client to set the salary of someone
    /// </summary>
    internal struct NetWalletSetSalaryUpdate : INetSerializableStruct
    {
        [NetworkSerialize]
        public ushort Target;

        [NetworkSerialize(MinValueInt = 0, MaxValueInt = 100)]
        public int NewRewardDistribution;
    }

    /// <summary>
    /// Represents the difference in balance and salary when a wallet gets updated
    /// Not really used right now but could be used for notifications when receiving funds similar to how talents do it
    /// </summary>
    [NetworkSerialize]
    internal struct WalletChangedData : INetSerializableStruct
    {
        public Option<int> RewardDistributionChanged;
        public Option<int> BalanceChanged;

        public readonly WalletChangedData MergeInto(WalletChangedData other)
        {
            other.BalanceChanged = AddOptionalInt(other.BalanceChanged, BalanceChanged);
            other.RewardDistributionChanged = AddOptionalInt(other.RewardDistributionChanged, RewardDistributionChanged);

            other.BalanceChanged = TurnToNoneIfZero(other.BalanceChanged);
            other.RewardDistributionChanged = TurnToNoneIfZero(other.RewardDistributionChanged);
            return other;

            static Option<int> AddOptionalInt(Option<int> a, Option<int> b)
            {
                bool hasValue1 = a.TryUnwrap(out var value1);
                bool hasValue2 = b.TryUnwrap(out var value2);
                return hasValue1
                    ? hasValue2
                        ? Option.Some(value1 + value2)
                        : Option.Some(value1)
                    : hasValue2
                        ? Option.Some(value2)
                        : Option.None;
            }

            static Option<int> TurnToNoneIfZero(Option<int> option)
            {
                return option.Bind(i => i == 0 ? Option.None : Option.Some(i));
            }
        }
    }

    /// <summary>
    /// Represents an update that changed the amount of money or salary of the wallet
    /// </summary>
    [NetworkSerialize]
    internal struct NetWalletTransaction : INetSerializableStruct
    {
        public Option<ushort> CharacterID;
        public WalletChangedData ChangedData;
        public WalletInfo Info;
    }

    // ReSharper disable ValueParameterNotUsed
    internal sealed class InvalidWallet : Wallet
    {
        public InvalidWallet(): base(Option<Character>.None()) { }

        public override int Balance
        {
            get => 0;
            set => new InvalidOperationException("Tried to set the balance on an invalid wallet");
        }

        public override int RewardDistribution
        {
            get => 0;
            set => new InvalidOperationException("Tried to set the reward distribution on an invalid wallet");
        }
    }

    internal partial class Wallet
    {
        public static readonly Wallet Invalid = new InvalidWallet();

        public const string LowerCaseSaveElementName = "wallet";

        private const string AttributeNameBalance = "balance",
                             AttrubuteNameRewardDistribution = "rewarddistribution",
                             SaveElementName = "Wallet";

        public readonly Option<Character> Owner;

        private int balance;

        public virtual int Balance
        {
            get => balance;
            set => balance = ClampBalance(value);
        }

        private int rewardDistribution;

        public virtual int RewardDistribution
        {
            get => rewardDistribution;
            set => rewardDistribution = ClampRewardDistribution(value);
        }

        public Wallet(Option<Character> owner)
        {
            Owner = owner;
        }

        public Wallet(Option<Character> owner, XElement element): this(owner)
        {
            balance = ClampBalance(element.GetAttributeInt(AttributeNameBalance, 0));
            rewardDistribution = ClampBalance(element.GetAttributeInt(AttrubuteNameRewardDistribution, 0));
        }

        public XElement Save()
        {
            XElement element = new XElement(SaveElementName, new XAttribute(AttributeNameBalance, Balance), new XAttribute(AttrubuteNameRewardDistribution, RewardDistribution));
            return element;
        }

        public bool TryDeduct(int price)
        {
            if (!CanAfford(price)) { return false; }

            Deduct(price);
            return true;
        }

        public bool CanAfford(int price) => Balance >= price;
        public void Refund(int price) => Give(price);

        public void Give(int amount)
        {
            Balance += amount;
            SettingsChanged(balanceChanged: Option<int>.Some(amount), rewardChanged: Option<int>.None());
        }

        public void Deduct(int price)
        {
            Balance -= price;
            SettingsChanged(balanceChanged: Option<int>.Some(-price), rewardChanged: Option<int>.None());
        }

        public void SetRewardDistribution(int value)
        {
            int oldValue = RewardDistribution;
            RewardDistribution = value;
            SettingsChanged(balanceChanged: Option<int>.None(), rewardChanged: Option<int>.Some(RewardDistribution - oldValue));
        }

        public WalletInfo CreateWalletInfo()
        {
            return new WalletInfo
            {
                Balance = Balance,
                RewardDistribution = RewardDistribution
            };
        }

        public string GetOwnerLogName()
            => Owner.TryUnwrap(out var character) ? character.Name : "the bank";

        partial void SettingsChanged(Option<int> balanceChanged, Option<int> rewardChanged);

        private static int ClampBalance(int value) => Math.Clamp(value, 0, CampaignMode.MaxMoney);
        private static int ClampRewardDistribution(int value) => Math.Clamp(value, 0, 100);
    }
}