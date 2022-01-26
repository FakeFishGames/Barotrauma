#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal partial class MedicalClinic
    {
        public enum RequestResult
        {
            Undecided,
            Success,
            Error,
            Timeout
        }

        public readonly struct RequestAction<T>
        {
            public readonly Action<T> Callback;
            public readonly DateTimeOffset Timeout;

            public RequestAction(Action<T> callback, DateTimeOffset timeout)
            {
                Callback = callback;
                Timeout = timeout;
            }
        }

        public readonly struct AfflictionRequest
        {
            public readonly RequestResult Result;
            public readonly ImmutableArray<NetAffliction> Afflictions;

            public AfflictionRequest(RequestResult result, ImmutableArray<NetAffliction> afflictions)
            {
                Result = result;
                Afflictions = afflictions;
            }
        }

        public readonly struct PendingRequest
        {
            public readonly RequestResult Result;
            public readonly ImmutableArray<NetCrewMember> CrewMembers;

            public PendingRequest(RequestResult result, ImmutableArray<NetCrewMember> crewMembers)
            {
                Result = result;
                CrewMembers = crewMembers;
            }
        }

        public readonly struct CallbackOnlyRequest
        {
            public readonly RequestResult Result;

            public CallbackOnlyRequest(RequestResult result)
            {
                Result = result;
            }
        }

        public readonly struct HealRequest
        {
            public readonly RequestResult Result;
            public readonly HealRequestResult HealResult;

            public HealRequest(RequestResult result, HealRequestResult healResult)
            {
                Result = result;
                HealResult = healResult;
            }
        }

        private readonly List<RequestAction<AfflictionRequest>> afflictionRequests = new List<RequestAction<AfflictionRequest>>();
        private readonly List<RequestAction<PendingRequest>> pendingHealRequests = new List<RequestAction<PendingRequest>>();
        private readonly List<RequestAction<CallbackOnlyRequest>> clearAllRequests = new List<RequestAction<CallbackOnlyRequest>>();
        private readonly List<RequestAction<HealRequest>> healAllRequests = new List<RequestAction<HealRequest>>();
        private readonly List<RequestAction<CallbackOnlyRequest>> addRequests = new List<RequestAction<CallbackOnlyRequest>>();
        private readonly List<RequestAction<CallbackOnlyRequest>> removeRequests = new List<RequestAction<CallbackOnlyRequest>>();

        public void RequestAfflictions(CharacterInfo info, Action<AfflictionRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
#if DEBUG && LINUX
                if (Screen.Selected is TestScreen)
                {
                    onReceived.Invoke(new AfflictionRequest(RequestResult.Success, TestAfflictions.ToImmutableArray()));
                    return;
                }
#endif

                if (!(info is { Character: { CharacterHealth: { } health } }))
                {
                    onReceived.Invoke(new AfflictionRequest(RequestResult.Error, ImmutableArray<NetAffliction>.Empty));
                    return;
                }

                ImmutableArray<NetAffliction> pendingAfflictions = GetAllAfflictions(health).ToImmutableArray();
                onReceived.Invoke(new AfflictionRequest(RequestResult.Success, pendingAfflictions));
                return;
            }

            afflictionRequests.Add(new RequestAction<AfflictionRequest>(onReceived, GetTimeout()));
            SendAfflictionRequest(info);
        }

        public void RequestLatestPending(Action<PendingRequest> onReceived)
        {
            // no need to worry about syncing when there's only one pair of eyes capable of looking at the UI
            if (GameMain.IsSingleplayer) { return; }

            pendingHealRequests.Add(new RequestAction<PendingRequest>(onReceived, GetTimeout()));
            SendPendingRequest();
        }

        public void Update(float deltaTime)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            UpdateQueue(afflictionRequests, now, onTimeout: callback => { callback(new AfflictionRequest(RequestResult.Timeout, ImmutableArray<NetAffliction>.Empty)); });
            UpdateQueue(pendingHealRequests, now, onTimeout: callback => { callback(new PendingRequest(RequestResult.Timeout, ImmutableArray<NetCrewMember>.Empty)); });
            UpdateQueue(healAllRequests, now, onTimeout: callback => { callback(new HealRequest(RequestResult.Timeout, HealRequestResult.Unknown)); });
            UpdateQueue(clearAllRequests, now, onTimeout: CallbackOnlyTimeout);
            UpdateQueue(addRequests, now, onTimeout: CallbackOnlyTimeout);
            UpdateQueue(removeRequests, now, onTimeout: CallbackOnlyTimeout);

            void CallbackOnlyTimeout(Action<CallbackOnlyRequest> callback) { callback(new CallbackOnlyRequest(RequestResult.Timeout)); }
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
            if (!(first is { } action))
            {
                result = _ => { };
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

        // if you have more than 5000 ping there are probably more important things to worry about but hey just in case
        private static DateTimeOffset GetTimeout() => DateTimeOffset.Now.AddSeconds(5).AddMilliseconds(GetPing());

        private static int GetPing()
        {
            if (GameMain.IsSingleplayer || !(GameMain.Client?.Name is { } ownName) || !(GameMain.NetworkMember?.ConnectedClients is { } clients)) { return 0; }

            return (from client in clients where client.Name == ownName select client.Ping).FirstOrDefault();
        }

        public void HealAllButtonAction(Action<HealRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                HealRequestResult result = HealAllPending();
                onReceived(new HealRequest(RequestResult.Success, HealAllPending()));
                if (result == HealRequestResult.Success)
                {
                    OnUpdate?.Invoke();
                }

                return;
            }

            if (campaign?.CampaignUI?.MedicalClinic is { } ui)
            {
                ui.ClosePopup();
            }

            healAllRequests.Add(new RequestAction<HealRequest>(onReceived, GetTimeout()));
            ClientSend(null, NetworkHeader.HEAL_PENDING, DeliveryMethod.Reliable);
        }

        public void ClearAllButtonAction(Action<CallbackOnlyRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                ClearPendingHeals();
                onReceived(new CallbackOnlyRequest(RequestResult.Success));
                OnUpdate?.Invoke();
                return;
            }

            clearAllRequests.Add(new RequestAction<CallbackOnlyRequest>(onReceived, GetTimeout()));
            ClientSend(null, NetworkHeader.CLEAR_PENDING, DeliveryMethod.Reliable);
        }

        private void ClearRequstReceived()
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

        public void AddPendingButtonAction(NetCrewMember crewMember, Action<CallbackOnlyRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                InsertPendingCrewMember(crewMember);
                onReceived(new CallbackOnlyRequest(RequestResult.Success));
                OnUpdate?.Invoke();
                return;
            }

            addRequests.Add(new RequestAction<CallbackOnlyRequest>(onReceived, GetTimeout()));
            ClientSend(crewMember, NetworkHeader.ADD_PENDING, DeliveryMethod.Reliable);
        }

        public void RemovePendingButtonAction(NetCrewMember crewMember, NetAffliction affliction, Action<CallbackOnlyRequest> onReceived)
        {
            if (GameMain.IsSingleplayer)
            {
                RemovePendingAffliction(crewMember, affliction);
                onReceived(new CallbackOnlyRequest(RequestResult.Success));
                OnUpdate?.Invoke();
                return;
            }

            INetSerializableStruct removedAffliction = new NetRemovedAffliction
            {
                CrewMember = crewMember,
                Affliction = affliction
            };

            removeRequests.Add(new RequestAction<CallbackOnlyRequest>(onReceived, GetTimeout()));
            ClientSend(removedAffliction, NetworkHeader.REMOVE_PENDING, DeliveryMethod.Reliable);
        }

        private void NewAdditonReceived(IReadMessage inc, MessageFlag flag)
        {
            NetCrewMember crewMember = INetSerializableStruct.Read<NetCrewMember>(inc);
            InsertPendingCrewMember(crewMember);
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
            INetSerializableStruct crewMember = new NetCrewMember
            {
                CharacterInfo = info,
                Afflictions = Array.Empty<NetAffliction>()
            };

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
                RequestResult result = crewMember.CharacterInfoID == 0 ? RequestResult.Error : RequestResult.Success;
                callback(new AfflictionRequest(result, crewMember.Afflictions.ToImmutableArray()));
            }
        }

        private void PendingRequestReceived(IReadMessage inc)
        {
            NetPendingCrew pendingCrew = INetSerializableStruct.Read<NetPendingCrew>(inc);
            if (TryDequeue(pendingHealRequests, out var callback))
            {
                callback(new PendingRequest(RequestResult.Success, pendingCrew.CrewMembers.ToImmutableArray()));
            }
        }

        private static IWriteMessage StartSending()
        {
            IWriteMessage writeMessage = new WriteOnlyMessage();
            writeMessage.Write((byte)ClientPacketHeader.MEDICAL);
            return writeMessage;
        }

        private static void ClientSend(INetSerializableStruct? netStruct, NetworkHeader header, DeliveryMethod deliveryMethod)
        {
            IWriteMessage msg = StartSending();
            msg.Write((byte)header);
            netStruct?.Write(msg);
            GameMain.Client.ClientPeer?.Send(msg, deliveryMethod);
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
                case NetworkHeader.REQUEST_PENDING:
                    PendingRequestReceived(inc);
                    break;
                case NetworkHeader.ADD_PENDING:
                    NewAdditonReceived(inc, flag);
                    break;
                case NetworkHeader.REMOVE_PENDING:
                    NewRemovalReceived(inc, flag);
                    break;
                case NetworkHeader.HEAL_PENDING:
                    HealRequestReceived(inc);
                    break;
                case NetworkHeader.CLEAR_PENDING:
                    ClearRequstReceived();
                    break;
            }
        }
    }
}