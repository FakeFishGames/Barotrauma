using System;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal readonly struct WalletChangedEvent
    {
        public readonly Wallet Wallet;
        public readonly WalletInfo Info;
        public readonly WalletChangedData ChangedData;

        public WalletChangedEvent(Wallet wallet, WalletChangedData changedData, WalletInfo info)
        {
            Wallet = wallet;
            Info = info;
            ChangedData = changedData;
        }
    }

    [NetworkSerialize]
    internal struct WalletInfo : INetSerializableStruct
    {
        public int RewardDistribution;
        public int Balance;
    }

    internal struct NetWalletUpdate : INetSerializableStruct
    {
        [NetworkSerialize(ArrayMaxSize = NetConfig.MaxPlayers + 1)]
        public NetWalletTransaction[] Transactions;
    }

    [NetworkSerialize]
    internal struct NetWalletTransfer : INetSerializableStruct
    {
        public Option<ushort> Sender;
        public Option<ushort> Receiver;
        public int Amount;
    }

    internal struct NetWalletSalaryUpdate : INetSerializableStruct
    {
        [NetworkSerialize]
        public ushort Target;

        [NetworkSerialize(MinValueInt = 0, MaxValueInt = 100)]
        public int NewRewardDistribution;
    }

    [NetworkSerialize]
    internal struct WalletChangedData : INetSerializableStruct
    {
        public Option<int> RewardDistributionChanged;
        public Option<int> BalanceChanged;

        public WalletChangedData MergeInto(WalletChangedData other)
        {
            other.BalanceChanged = AddOptionalInt(other.BalanceChanged, BalanceChanged);
            other.RewardDistributionChanged = AddOptionalInt(other.RewardDistributionChanged, RewardDistributionChanged);
            return other;

            static Option<int> AddOptionalInt(Option<int> a, Option<int> b)
            {
                return a switch
                {
                    Some<int> some1 => b switch
                    {
                        Some<int> some2 => Option<int>.Some(some1.Value + some2.Value),
                        None<int> _ => Option<int>.Some(some1.Value),
                        _ => throw new ArgumentOutOfRangeException(nameof(b))
                    },
                    None<int> _ => b switch
                    {
                        Some<int> some1 => Option<int>.Some(some1.Value),
                        None<int> _ => Option<int>.None(),
                        _ => throw new ArgumentOutOfRangeException(nameof(b))
                    },
                    _ => throw new ArgumentOutOfRangeException(nameof(a))
                };
            }
        }
    }

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

        public Wallet() { }

        public Wallet(XElement element)
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

        public void SetRewardDistrubiton(int value)
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

        partial void SettingsChanged(Option<int> balanceChanged, Option<int> rewardChanged);

        private static int ClampBalance(int value) => Math.Clamp(value, 0, CampaignMode.MaxMoney);
        private static int ClampRewardDistribution(int value) => Math.Clamp(value, 0, 100);
    }
}