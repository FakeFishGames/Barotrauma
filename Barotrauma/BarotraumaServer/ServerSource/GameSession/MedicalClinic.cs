#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal partial class MedicalClinic
    {
        private enum RateLimitResult
        {
            OK,
            LimitReached
        }

        private struct RateLimitInfo
        {
            public int Requests;
            public const int MaxRequests = 5;
            public DateTimeOffset Expiry;
        }

        private readonly Dictionary<Client, RateLimitInfo> rateLimits = new Dictionary<Client, RateLimitInfo>();

        public void ServerRead(IReadMessage inc, Client sender)
        {
            NetworkHeader header = (NetworkHeader)inc.ReadByte();

            switch (header)
            {
                case NetworkHeader.REQUEST_AFFLICTIONS:
                    ProcessRequestedAfflictions(inc, sender);
                    break;
                case NetworkHeader.REQUEST_PENDING:
                    ProcessRequestedPending(sender);
                    break;
                case NetworkHeader.ADD_PENDING:
                    ProcessNewAddition(inc, sender);
                    break;
                case NetworkHeader.REMOVE_PENDING:
                    ProcessNewRemoval(inc, sender);
                    break;
                case NetworkHeader.HEAL_PENDING:
                    ProcessHealing(sender);
                    break;
                case NetworkHeader.CLEAR_PENDING:
                    ProcessClearing(sender);
                    break;
            }
        }

        private void ProcessNewAddition(IReadMessage inc, Client client)
        {
            if (CheckRateLimit(client) == RateLimitResult.LimitReached) { return; }

            NetCrewMember newCrewMember = INetSerializableStruct.Read<NetCrewMember>(inc);
            InsertPendingCrewMember(newCrewMember);
            ServerSend(newCrewMember, NetworkHeader.ADD_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessNewRemoval(IReadMessage inc, Client client)
        {
            if (CheckRateLimit(client) == RateLimitResult.LimitReached) { return; }

            NetRemovedAffliction removed = INetSerializableStruct.Read<NetRemovedAffliction>(inc);
            RemovePendingAffliction(removed.CrewMember, removed.Affliction);
            ServerSend(removed, NetworkHeader.REMOVE_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessRequestedPending(Client client)
        {
            if (CheckRateLimit(client) == RateLimitResult.LimitReached) { return; }

            INetSerializableStruct writeCrewMember = new NetPendingCrew
            {
                CrewMembers = PendingHeals.ToArray()
            };

            ServerSend(writeCrewMember, NetworkHeader.REQUEST_PENDING, DeliveryMethod.Reliable, targetClient: client);
        }

        private void ProcessHealing(Client client)
        {
            if (CheckRateLimit(client) == RateLimitResult.LimitReached) { return; }

            HealRequestResult result = HealAllPending(client: client);
            ServerSend(new NetHealRequest { Result = result }, NetworkHeader.HEAL_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessClearing(Client client)
        {
            if (CheckRateLimit(client) == RateLimitResult.LimitReached) { return; }

            if (!PendingHeals.Any()) { return; }

            ClearPendingHeals();
            ServerSend(null, NetworkHeader.CLEAR_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessRequestedAfflictions(IReadMessage inc, Client client)
        {
            if (CheckRateLimit(client) == RateLimitResult.LimitReached) { return; }

            NetCrewMember crewMember = INetSerializableStruct.Read<NetCrewMember>(inc);

            CharacterInfo? foundInfo = crewMember.FindCharacterInfo(GetCrewCharacters());

            NetAffliction[] pendingAfflictions = Array.Empty<NetAffliction>();
            int infoId = 0;

            if (foundInfo is { Character: { CharacterHealth: { } health } })
            {
                pendingAfflictions = GetAllAfflictions(health);
                infoId = foundInfo.GetIdentifierUsingOriginalName();
            }

            INetSerializableStruct writeCrewMember = new NetCrewMember
            {
                CharacterInfoID = infoId,
                Afflictions = pendingAfflictions
            };

            ServerSend(writeCrewMember, NetworkHeader.REQUEST_AFFLICTIONS, DeliveryMethod.Unreliable, client);
        }

        private RateLimitResult CheckRateLimit(Client client)
        {
            if (rateLimits.TryGetValue(client, out RateLimitInfo rateLimitInfo))
            {
                if (rateLimitInfo.Expiry < DateTimeOffset.Now)
                {
                    rateLimitInfo.Expiry = DateTimeOffset.Now.AddSeconds(5);
                    rateLimitInfo.Requests = 1;
                }
                else
                {
                    if (rateLimitInfo.Requests > RateLimitInfo.MaxRequests) { return RateLimitResult.LimitReached; }

                    rateLimitInfo.Requests++;
                }

                rateLimits[client] = rateLimitInfo;
            }
            else
            {
                rateLimits.Add(client, new RateLimitInfo { Requests = 1, Expiry = DateTimeOffset.Now.AddSeconds(5) });
            }

            return RateLimitResult.OK;
        }

        private IWriteMessage StartSending()
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.MEDICAL);
            return msg;
        }

        private void ServerSend(INetSerializableStruct? netStruct, NetworkHeader header, DeliveryMethod deliveryMethod, Client? targetClient = null, Client? reponseClient = null)
        {
            if (targetClient is null)
            {
                foreach (Client c in GameMain.Server.ConnectedClients)
                {
                    SendToClient(c);
                }

                return;
            }

            SendToClient(targetClient);

            void SendToClient(Client c)
            {
                MessageFlag flag = MessageFlag.Announce;
                if (reponseClient != null && reponseClient == c)
                {
                    flag = MessageFlag.Response;
                }

                IWriteMessage msg = StartSending();
                msg.Write((byte)header);
                msg.Write((byte)flag);
                netStruct?.Write(msg);
                GameMain.Server.ServerPeer.Send(msg, c.Connection, deliveryMethod);
            }
        }
    }
}