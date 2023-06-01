#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal sealed partial class MedicalClinic
    {
        private const int RateLimitMaxRequests = 20,
                          RateLimitExpiry = 5;

        public enum NetworkHeader
        {
            REQUEST_AFFLICTIONS,
            AFFLICTION_UPDATE,
            UNSUBSCRIBE_ME,
            REQUEST_PENDING,
            ADD_PENDING,
            REMOVE_PENDING,
            CLEAR_PENDING,
            HEAL_PENDING,
            ADD_EVERYTHING_TO_PENDING
        }

        public enum AfflictionSeverity
        {
            Low,
            Medium,
            High
        }

        public enum MessageFlag
        {
            Response, // responding to your request
            Announce // responding to someone else's request
        }

        public enum HealRequestResult
        {
            Unknown, // everything is not ok
            Success, // everything ok
            InsufficientFunds, // not enough money
            Refused // the outpost has refused to provide medical assistance
        }

        [NetworkSerialize]
        public readonly record struct NetHealRequest(HealRequestResult Result) : INetSerializableStruct;

        [NetworkSerialize]
        public readonly record struct NetRemovedAffliction(NetCrewMember CrewMember, NetAffliction Affliction) : INetSerializableStruct;

        public struct NetAffliction : INetSerializableStruct
        {
            [NetworkSerialize]
            public Identifier Identifier;

            [NetworkSerialize]
            public ushort Strength;

            [NetworkSerialize]
            public int VitalityDecrease;

            [NetworkSerialize]
            public ushort Price;

            public void SetAffliction(Affliction affliction, CharacterHealth characterHealth)
            {
                Identifier = affliction.Identifier;
                Strength = (ushort)Math.Ceiling(affliction.Strength);
                Price = (ushort)(affliction.Prefab.BaseHealCost + Strength * affliction.Prefab.HealCostMultiplier);
                VitalityDecrease = (int)affliction.GetVitalityDecrease(characterHealth);
            }

            private AfflictionPrefab? cachedPrefab;

            public AfflictionPrefab? Prefab
            {
                get
                {
                    if (cachedPrefab is { } cached) { return cached; }

                    foreach (AfflictionPrefab prefab in AfflictionPrefab.List)
                    {
                        if (prefab.Identifier == Identifier)
                        {
                            cachedPrefab = prefab;
                            return prefab;
                        }
                    }

                    return null;
                }
                set
                {
                    cachedPrefab = value;
                    Identifier = value?.Identifier ?? Identifier.Empty;
                    Strength = 0;
                    Price = 0;
                }
            }

            public readonly bool AfflictionEquals(AfflictionPrefab prefab)
            {
                return prefab.Identifier == Identifier;
            }

            public readonly bool AfflictionEquals(NetAffliction affliction)
            {
                return affliction.Identifier == Identifier;
            }
        }

        public record struct NetCrewMember : INetSerializableStruct
        {
            [NetworkSerialize]
            public int CharacterInfoID;

            [NetworkSerialize]
            public ImmutableArray<NetAffliction> Afflictions;

            public NetCrewMember(CharacterInfo info)
            {
                CharacterInfoID = info.GetIdentifierUsingOriginalName();
                Afflictions = ImmutableArray<NetAffliction>.Empty;
            }

            public NetCrewMember(CharacterInfo info, ImmutableArray<NetAffliction> afflictions): this(info)
            {
                Afflictions = afflictions;
            }

            public readonly CharacterInfo? FindCharacterInfo(ImmutableArray<CharacterInfo> crew)
            {
                foreach (CharacterInfo info in crew)
                {
                    if (info.GetIdentifierUsingOriginalName() == CharacterInfoID)
                    {
                        return info;
                    }
                }

                return null;
            }

            public readonly bool CharacterEquals(NetCrewMember crewMember)
            {
                return crewMember.CharacterInfoID == CharacterInfoID;
            }
        }

        public readonly List<NetCrewMember> PendingHeals = new List<NetCrewMember>();

        public Action? OnUpdate;

        private readonly CampaignMode? campaign;

        public MedicalClinic(CampaignMode campaign)
        {
            this.campaign = campaign;
#if CLIENT
            campaign.OnMoneyChanged.RegisterOverwriteExisting(nameof(MedicalClinic).ToIdentifier(), OnMoneyChanged);
#endif
        }

        private static bool IsOutpostInCombat()
        {
            if (Level.Loaded is not { Type: LevelData.LevelType.Outpost }) { return false; }

            IEnumerable<Character> crew = GetCrewCharacters().Where(static c => c.Character != null).Select(static c => c.Character).ToImmutableHashSet();

            foreach (Character npc in Character.CharacterList.Where(static c => c.TeamID == CharacterTeamType.FriendlyNPC))
            {
                bool isInCombatWithCrew = !npc.IsInstigator && npc.AIController is HumanAIController { ObjectiveManager: { CurrentObjective: AIObjectiveCombat combatObjective } } && crew.Contains(combatObjective.Enemy);
                if (isInCombatWithCrew) { return true; }
            }

            return false;
        }

        private HealRequestResult HealAllPending(bool force = false, Client? client = null)
        {
            int totalCost = GetTotalCost();
            if (!force)
            {
                if (IsOutpostInCombat()) { return HealRequestResult.Refused; }
                if (!(campaign?.TryPurchase(client, totalCost) ?? false)) { return HealRequestResult.InsufficientFunds; }
            }

            ImmutableArray<CharacterInfo> crew = GetCrewCharacters();
            foreach (NetCrewMember crewMember in PendingHeals)
            {
                CharacterInfo? targetCharacter = crewMember.FindCharacterInfo(crew);
                if (!(targetCharacter?.Character is { CharacterHealth: { } health })) { continue; }

                foreach (NetAffliction affliction in crewMember.Afflictions)
                {
                    health.ReduceAfflictionOnAllLimbs(affliction.Identifier, affliction.Prefab?.MaxStrength ?? affliction.Strength);
                }
            }

            ClearPendingHeals();

            return HealRequestResult.Success;
        }

        private void ClearPendingHeals()
        {
            PendingHeals.Clear();
        }

        private void AddEverythingToPending()
        {
            foreach (CharacterInfo info in GetCrewCharacters())
            {
                if (info.Character?.CharacterHealth is not { } health) { continue; }

                var afflictions = GetAllAfflictions(health);

                if (afflictions.Length is 0) { continue; }

                InsertPendingCrewMember(new NetCrewMember(info, afflictions));
            }
        }

        private void RemovePendingAffliction(NetCrewMember crewMember, NetAffliction affliction)
        {
            foreach (NetCrewMember listMember in PendingHeals.ToList())
            {
                PendingHeals.Remove(listMember);
                NetCrewMember pendingMember = listMember;

                if (pendingMember.CharacterEquals(crewMember))
                {
                    List<NetAffliction> newAfflictions = new List<NetAffliction>();
                    foreach (NetAffliction pendingAffliction in pendingMember.Afflictions)
                    {
                        if (pendingAffliction.AfflictionEquals(affliction)) { continue; }

                        newAfflictions.Add(pendingAffliction);
                    }

                    pendingMember.Afflictions = newAfflictions.ToImmutableArray();
                }

                if (!pendingMember.Afflictions.Any()) { continue; }

                PendingHeals.Add(pendingMember);
            }
        }

        private void InsertPendingCrewMember(NetCrewMember crewMember)
        {
            if (PendingHeals.FirstOrNull(m => m.CharacterEquals(crewMember)) is { } foundHeal)
            {
                PendingHeals.Remove(foundHeal);
            }

            PendingHeals.Add(crewMember);
        }

        public static bool IsHealable(Affliction affliction)
        {
            return affliction.Prefab.HealableInMedicalClinic && affliction.Strength > GetShowTreshold(affliction);
            static float GetShowTreshold(Affliction affliction) => Math.Max(0, Math.Min(affliction.Prefab.ShowIconToOthersThreshold, affliction.Prefab.ShowInHealthScannerThreshold));
        }

        private ImmutableArray<NetAffliction> GetAllAfflictions(CharacterHealth health)
        {
            IEnumerable<Affliction> rawAfflictions = health.GetAllAfflictions().Where(IsHealable);

            List<NetAffliction> afflictions = new List<NetAffliction>();

            foreach (Affliction affliction in rawAfflictions)
            {
                NetAffliction newAffliction;
                if (afflictions.FirstOrNull(netAffliction => netAffliction.AfflictionEquals(affliction.Prefab)) is { } foundAffliction)
                {
                    afflictions.Remove(foundAffliction);
                    foundAffliction.Strength += (ushort)affliction.Strength;
                    foundAffliction.Price += (ushort)GetAdjustedPrice(GetHealPrice(affliction));
                    newAffliction = foundAffliction;
                }
                else
                {
                    newAffliction = new NetAffliction();
                    newAffliction.SetAffliction(affliction, health);
                    newAffliction.Price = (ushort)GetAdjustedPrice(newAffliction.Price);
                }

                afflictions.Add(newAffliction);
            }

            return afflictions.ToImmutableArray();

            static int GetHealPrice(Affliction affliction) => (int)(affliction.Prefab.BaseHealCost + (affliction.Prefab.HealCostMultiplier * affliction.Strength));
        }

        public static void OnAfflictionCountChanged(Character character) =>
            GameMain.GameSession?.Campaign?.MedicalClinic?.OnAfflictionCountChangedPrivate(character);

        private void OnAfflictionCountChangedPrivate(Character character)
        {
            if (character is not { CharacterHealth: { } health, Info: { } info }) { return; }

            ImmutableArray<NetAffliction> afflictions = GetAllAfflictions(health);

#if CLIENT
            if (GameMain.NetworkMember is null)
            {
                ui?.UpdateAfflictions(new NetCrewMember(info, afflictions));
            }

            ui?.UpdateCrewPanel();
#elif SERVER
            foreach (AfflictionSubscriber sub in afflictionSubscribers.ToList())
            {
                if (sub.Expiry < DateTimeOffset.Now)
                {
                    afflictionSubscribers.Remove(sub);
                    continue;
                }

                if (sub.Target == info)
                {
                    ServerSend(new NetCrewMember(info, afflictions),
                        header: NetworkHeader.AFFLICTION_UPDATE,
                        deliveryMethod: DeliveryMethod.Unreliable,
                        targetClient: sub.Subscriber);
                }
            }
#endif
        }

        public int GetTotalCost() => PendingHeals.SelectMany(static h => h.Afflictions).Aggregate(0, static (current, affliction) => current + affliction.Price);

        private int GetAdjustedPrice(int price) => campaign?.Map?.CurrentLocation is { Type: { HasOutpost: true } } currentLocation ? currentLocation.GetAdjustedHealCost(price) : int.MaxValue;

        public int GetBalance() => campaign?.GetBalance() ?? 0;

        public static ImmutableArray<CharacterInfo> GetCrewCharacters()
        {
#if DEBUG && CLIENT
            if (Screen.Selected is TestScreen)
            {
                return TestInfos.ToImmutableArray();
            }
#endif

            return Character.CharacterList.Where(static c => c.Info != null && c.TeamID == CharacterTeamType.Team1).Select(static c => c.Info).ToImmutableArray();
        }

#if DEBUG && CLIENT
        private static readonly CharacterInfo[] TestInfos =
        {
            new CharacterInfo(CharacterPrefab.HumanSpeciesName),
            new CharacterInfo(CharacterPrefab.HumanSpeciesName),
            new CharacterInfo(CharacterPrefab.HumanSpeciesName),
            new CharacterInfo(CharacterPrefab.HumanSpeciesName),
            new CharacterInfo(CharacterPrefab.HumanSpeciesName),
            new CharacterInfo(CharacterPrefab.HumanSpeciesName),
            new CharacterInfo(CharacterPrefab.HumanSpeciesName)
        };

        private static readonly NetAffliction[] TestAfflictions =
        {
            new NetAffliction { Identifier = "internaldamage".ToIdentifier(), Strength = 80, Price = 10 },
            new NetAffliction { Identifier = "blunttrauma".ToIdentifier(), Strength = 50, Price = 10 },
            new NetAffliction { Identifier = "lacerations".ToIdentifier(), Strength = 20, Price = 10 },
            new NetAffliction { Identifier = AfflictionPrefab.DamageType, Strength = 10, Price = 10 }
        };
#endif
    }
}