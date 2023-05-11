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
        private MedicalClinicUI? ui => campaign?.CampaignUI?.MedicalClinic;

        public enum RequestResult
        {
            Undecided,
            Success,
            CharacterInfoMissing,
            CharacterNotFound,
            Timeout
        }

        public readonly record struct RequestAction<T>(Action<T> Callback, DateTimeOffset Timeout);
        public readonly record struct AfflictionRequest(RequestResult Result, ImmutableArray<NetAffliction> Afflictions);
        public readonly record struct PendingRequest(RequestResult Result, NetCollection<NetCrewMember> CrewMembers);
        public readonly record struct CallbackOnlyRequest(RequestResult Result);
        public readonly record struct HealRequest(RequestResult Result, HealRequestResult HealResult);

        private readonly List<RequestAction<AfflictionRequest>> afflictionRequests = new List<RequestAction<AfflictionRequest>>();
        private readonly List<RequestAction<PendingRequest>> pendingHealRequests = new List<RequestAction<PendingRequest>>();
        private readonly List<RequestAction<CallbackOnlyRequest>> clearAllRequests = new List<RequestAction<CallbackOnlyRequest>>();
        private readonly List<RequestAction<HealRequest>> healAllRequests = new List<RequestAction<HealRequest>>();
        private readonly List<RequestAction<CallbackOnlyRequest>> addRequests = new List<RequestAction<CallbackOnlyRequest>>();
        private readonly List<RequestAction<CallbackOnlyRequest>> removeRequests = new List<RequestAction<CallbackOnlyRequest>>();

        private static readonly LeakyBucket requestBucket = new(RateLimitExpiry / (float)RateLimitMaxRequests, 10);

        public bool RequestAfflictions(CharacterInfo info, Action<AfflictionRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
#if DEBUG && LINUX
                if (Screen.Selected is TestScreen)
                {
                    onReceived.Invoke(new AfflictionRequest(RequestResult.Success, TestAfflictions.ToImmutableArray()));
                    return true;
                }
#endif

                if (info is not { Character.CharacterHealth: { } health })
                {
                    onReceived.Invoke(new AfflictionRequest(RequestResult.CharacterInfoMissing, ImmutableArray<NetAffliction>.Empty));
                    return true;
                }

                ImmutableArray<NetAffliction> pendingAfflictions = GetAllAfflictions(health);
                onReceived.Invoke(new AfflictionRequest(RequestResult.Success, pendingAfflictions));
                return true;
            }

            return requestBucket.TryEnqueue(() =>
            {
                afflictionRequests.Add(new RequestAction<AfflictionRequest>(onReceived, GetTimeout()));
                SendAfflictionRequest(info);
            });
        }

        public void RequestLatestPending(Action<PendingRequest> onReceived)
        {
            // no need to worry about syncing when there's only one pair of eyes capable of looking at the UI
            if (GameMain.IsSingleplayer) { return; }

            requestBucket.TryEnqueue(() =>
            {
                pendingHealRequests.Add(new RequestAction<PendingRequest>(onReceived, GetTimeout()));
                SendPendingRequest();
            });
        }

        public void Update(float deltaTime)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            UpdateQueue(afflictionRequests, now, onTimeout: static callback => { callback(new AfflictionRequest(RequestResult.Timeout, ImmutableArray<NetAffliction>.Empty)); });
            UpdateQueue(pendingHealRequests, now, onTimeout: static callback => { callback(new PendingRequest(RequestResult.Timeout, NetCollection<NetCrewMember>.Empty)); });
            UpdateQueue(healAllRequests, now, onTimeout: static callback => { callback(new HealRequest(RequestResult.Timeout, HealRequestResult.Unknown)); });
            UpdateQueue(clearAllRequests, now, onTimeout: CallbackOnlyTimeout);
            UpdateQueue(addRequests, now, onTimeout: CallbackOnlyTimeout);
            UpdateQueue(removeRequests, now, onTimeout: CallbackOnlyTimeout);
            requestBucket.Update(deltaTime);

            static void CallbackOnlyTimeout(Action<CallbackOnlyRequest> callback) { callback(new CallbackOnlyRequest(RequestResult.Timeout)); }
        }

        public bool IsAfflictionPending(NetCrewMember character, NetAffliction affliction)
        {
            foreach (NetCrewMember crewMember in PendingHeals)
            {
                if (!crewMember.CharacterEquals(character)) { continue; }

                return crewMember.Afflictions.Any(a => a.AfflictionEquals(affliction));
            }

            return false;
        }

        private static bool TryDequeue<T>(List<RequestAction<T>> requestQueue, out Action<T> result)
        {
            RequestAction<T>? first = requestQueue.FirstOrNull();
            if (first is not { } action)
            {
                result = static _ => { };
                return false;
            }

            requestQueue.Remove(action);
            result = action.Callback;
            return true;
        }

        private static void UpdateQueue<T>(List<RequestAction<T>> requestQueue, DateTimeOffset now, Action<Action<T>> onTimeout)
        {
            HashSet<RequestAction<T>>? removals = null;
            foreach (RequestAction<T> action in requestQueue)
            {
                if (action.Timeout < now)
                {
                    onTimeout.Invoke(action.Callback);

                    removals ??= new HashSet<RequestAction<T>>();
                    removals.Add(action);
                }
            }

            if (removals is null) { return; }

            foreach (RequestAction<T> action in removals)
            {
                requestQueue.Remove(action);
            }
        }

        private void OnMoneyChanged(WalletChangedEvent e)
        {
            if (e.Wallet.IsOwnWallet) { OnUpdate?.Invoke(); }
        }

        // if you have more than 5000 ping there are probably more important things to worry about but hey just in case
        private static DateTimeOffset GetTimeout() => DateTimeOffset.Now.AddSeconds(5).AddMilliseconds(GetPing());

        private static int GetPing()
        {
            if (GameMain.IsSingleplayer || GameMain.Client?.Name is not { } ownName || GameMain.NetworkMember?.ConnectedClients is not { } clients) { return 0; }

            return (from client in clients where client.Name == ownName select client.Ping).FirstOrDefault();
        }

        public bool TreatAllButtonAction(Action<CallbackOnlyRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                AddEverythingToPending();
                onReceived(new CallbackOnlyRequest(RequestResult.Success));
                OnUpdate?.Invoke();
                return true;
            }

            return requestBucket.TryEnqueue(() =>
            {
                addRequests.Add(new RequestAction<CallbackOnlyRequest>(onReceived, GetTimeout()));
                ClientSend(null, NetworkHeader.ADD_EVERYTHING_TO_PENDING, DeliveryMethod.Reliable);
            });
        }


        public bool HealAllButtonAction(Action<HealRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                HealRequestResult result = HealAllPending();
                onReceived(new HealRequest(RequestResult.Success, HealAllPending()));
                if (result == HealRequestResult.Success)
                {
                    OnUpdate?.Invoke();
                }

                return true;
            }

            if (campaign?.CampaignUI?.MedicalClinic is { } openedUi)
            {
                openedUi.ClosePopup();
            }

            return requestBucket.TryEnqueue(() =>
            {
                healAllRequests.Add(new RequestAction<HealRequest>(onReceived, GetTimeout()));
                ClientSend(null, NetworkHeader.HEAL_PENDING, DeliveryMethod.Reliable);
            });
        }

        public bool ClearAllButtonAction(Action<CallbackOnlyRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                ClearPendingHeals();
                onReceived(new CallbackOnlyRequest(RequestResult.Success));
                OnUpdate?.Invoke();
                return true;
            }

            return requestBucket.TryEnqueue(() =>
            {
                clearAllRequests.Add(new RequestAction<CallbackOnlyRequest>(onReceived, GetTimeout()));
                ClientSend(null, NetworkHeader.CLEAR_PENDING, DeliveryMethod.Reliable);
            });
        }

        private void ClearRequestReceived()
        {
            ClearPendingHeals();
            if (TryDequeue(clearAllRequests, out var callback))
            {
                callback(new CallbackOnlyRequest(RequestResult.Success));
            }
            OnUpdate?.Invoke();
        }

        private void HealRequestReceived(IReadMessage inc)
        {
            NetHealRequest request = INetSerializableStruct.Read<NetHealRequest>(inc);

            if (request.Result == HealRequestResult.Success)
            {
                HealAllPending(force: true);
            }

            if (TryDequeue(healAllRequests, out var callback))
            {
                callback(new HealRequest(RequestResult.Success, request.Result));
            }

            OnUpdate?.Invoke();
        }

        public bool AddPendingButtonAction(NetCrewMember crewMember, Action<CallbackOnlyRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                InsertPendingCrewMember(crewMember);
                onReceived(new CallbackOnlyRequest(RequestResult.Success));
                OnUpdate?.Invoke();
                return true;
            }

            return requestBucket.TryEnqueue(() =>
            {
                addRequests.Add(new RequestAction<CallbackOnlyRequest>(onReceived, GetTimeout()));
                ClientSend(crewMember, NetworkHeader.ADD_PENDING, DeliveryMethod.Reliable);
            });
        }

        public bool RemovePendingButtonAction(NetCrewMember crewMember, NetAffliction affliction, Action<CallbackOnlyRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                RemovePendingAffliction(crewMember, affliction);
                onReceived(new CallbackOnlyRequest(RequestResult.Success));
                OnUpdate?.Invoke();
                return true;
            }

            INetSerializableStruct removedAffliction = new NetRemovedAffliction
            {
                CrewMember = crewMember,
                Affliction = affliction
            };

            return requestBucket.TryEnqueue(() =>
            {
                removeRequests.Add(new RequestAction<CallbackOnlyRequest>(onReceived, GetTimeout()));
                ClientSend(removedAffliction, NetworkHeader.REMOVE_PENDING, DeliveryMethod.Reliable);
            });
        }

        private void NewAdditionReceived(IReadMessage inc, MessageFlag flag)
        {
            var crewMembers = INetSerializableStruct.Read<NetCollection<NetCrewMember>>(inc);
            foreach (var crewMember in crewMembers)
            {
                InsertPendingCrewMember(crewMember);
            }
            if (flag == MessageFlag.Response && TryDequeue(addRequests, out var callback))
            {
                callback(new CallbackOnlyRequest(RequestResult.Success));
            }
            OnUpdate?.Invoke();
        }

        private void NewRemovalReceived(IReadMessage inc, MessageFlag flag)
        {
            NetRemovedAffliction removed = INetSerializableStruct.Read<NetRemovedAffliction>(inc);
            RemovePendingAffliction(removed.CrewMember, removed.Affliction);
            if (flag == MessageFlag.Response && TryDequeue(removeRequests, out var callback))
            {
                callback(new CallbackOnlyRequest(RequestResult.Success));
            }
            OnUpdate?.Invoke();
        }

        private static void SendAfflictionRequest(CharacterInfo info)
        {
            INetSerializableStruct crewMember = new NetCrewMember(info);

            ClientSend(crewMember, NetworkHeader.REQUEST_AFFLICTIONS, DeliveryMethod.Unreliable);
        }

        private static void SendPendingRequest()
        {
            ClientSend(null, NetworkHeader.REQUEST_PENDING, DeliveryMethod.Reliable);
        }

        private void AfflictionRequestReceived(IReadMessage inc)
        {
            NetCrewMember crewMember = INetSerializableStruct.Read<NetCrewMember>(inc);
            if (TryDequeue(afflictionRequests, out var callback))
            {
                RequestResult result = crewMember.CharacterInfoID is 0 ? RequestResult.CharacterNotFound : RequestResult.Success;
                callback(new AfflictionRequest(result, crewMember.Afflictions.ToImmutableArray()));
            }
        }

        private void AfflictionUpdateReceived(IReadMessage inc)
        {
            NetCrewMember crewMember = INetSerializableStruct.Read<NetCrewMember>(inc);
            ui?.UpdateAfflictions(crewMember);
        }

        private void PendingRequestReceived(IReadMessage inc)
        {
            var pendingCrew = INetSerializableStruct.Read<NetCollection<NetCrewMember>>(inc);
            if (TryDequeue(pendingHealRequests, out var callback))
            {
                callback(new PendingRequest(RequestResult.Success, pendingCrew));
            }
        }

        public static void SendUnsubscribeRequest() => ClientSend(null,
            header: NetworkHeader.UNSUBSCRIBE_ME,
            deliveryMethod: DeliveryMethod.Reliable);

        private static IWriteMessage StartSending()
        {
            IWriteMessage writeMessage = new WriteOnlyMessage();
            writeMessage.WriteByte((byte)ClientPacketHeader.MEDICAL);
            return writeMessage;
        }

        private static void ClientSend(INetSerializableStruct? netStruct, NetworkHeader header, DeliveryMethod deliveryMethod)
        {
            IWriteMessage msg = StartSending();
            msg.WriteByte((byte)header);
            netStruct?.Write(msg);
            GameMain.Client?.ClientPeer?.Send(msg, deliveryMethod);
        }

        public void ClientRead(IReadMessage inc)
        {
            NetworkHeader header = (NetworkHeader)inc.ReadByte();
            MessageFlag flag = (MessageFlag)inc.ReadByte();

            switch (header)
            {
                case NetworkHeader.REQUEST_AFFLICTIONS:
                    AfflictionRequestReceived(inc);
                    break;
                case NetworkHeader.AFFLICTION_UPDATE:
                    AfflictionUpdateReceived(inc);
                    break;
                case NetworkHeader.REQUEST_PENDING:
                    PendingRequestReceived(inc);
                    break;
                case NetworkHeader.ADD_PENDING:
                    NewAdditionReceived(inc, flag);
                    break;
                case NetworkHeader.REMOVE_PENDING:
                    NewRemovalReceived(inc, flag);
                    break;
                case NetworkHeader.HEAL_PENDING:
                    HealRequestReceived(inc);
                    break;
                case NetworkHeader.CLEAR_PENDING:
                    ClearRequestReceived();
                    break;
            }
        }
    }
}