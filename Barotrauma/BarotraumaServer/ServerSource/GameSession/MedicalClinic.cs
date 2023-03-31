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
        // allow 10 requests per 5 seconds, announce to chat if the limit is reached
        private readonly RateLimiter rateLimiter = new(
            maxRequests: 10,
            expiryInSeconds: 5,
            punishmentRules: (RateLimitAction.OnLimitReached, RateLimitPunishment.Announce));

        private readonly record struct AfflictionSubscriber(Client Subscriber, CharacterInfo Target, DateTimeOffset Expiry);

        private readonly List<AfflictionSubscriber> afflictionSubscribers = new();

        public void ServerRead(IReadMessage inc, Client sender)
        {
            NetworkHeader header = (NetworkHeader)inc.ReadByte();

            switch (header)
            {
                case NetworkHeader.ADD_EVERYTHING_TO_PENDING:
                    ProcessAddEverything(sender);
                    break;
                case NetworkHeader.UNSUBSCRIBE_ME:
                    RemoveClientSubscription(sender);
                    break;
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
            if (rateLimiter.IsLimitReached(client)) { return; }

            NetCrewMember newCrewMember = INetSerializableStruct.Read<NetCrewMember>(inc);
            InsertPendingCrewMember(newCrewMember);
            ServerSend(new NetCollection<NetCrewMember>(newCrewMember), NetworkHeader.ADD_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessAddEverything(Client client)
        {
            if (rateLimiter.IsLimitReached(client)) { return; }
            AddEverythingToPending();
            ServerSend(PendingHeals.ToNetCollection(), NetworkHeader.ADD_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void RemoveClientSubscription(Client client)
        {
            foreach (AfflictionSubscriber sub in afflictionSubscribers.ToList())
            {
                if (sub.Subscriber == client || sub.Expiry < DateTimeOffset.Now)
                {
                    afflictionSubscribers.Remove(sub);
                }
            }
        }

        private void ProcessNewRemoval(IReadMessage inc, Client client)
        {
            if (rateLimiter.IsLimitReached(client)) { return; }

            NetRemovedAffliction removed = INetSerializableStruct.Read<NetRemovedAffliction>(inc);
            RemovePendingAffliction(removed.CrewMember, removed.Affliction);
            ServerSend(removed, NetworkHeader.REMOVE_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessRequestedPending(Client client)
        {
            if (rateLimiter.IsLimitReached(client)) { return; }

            ServerSend(PendingHeals.ToNetCollection(), NetworkHeader.REQUEST_PENDING, DeliveryMethod.Reliable, targetClient: client);
        }

        private void ProcessHealing(Client client)
        {
            if (rateLimiter.IsLimitReached(client)) { return; }

            HealRequestResult result = HealAllPending(client: client);
            ServerSend(new NetHealRequest { Result = result }, NetworkHeader.HEAL_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessClearing(Client client)
        {
            if (rateLimiter.IsLimitReached(client)) { return; }

            if (!PendingHeals.Any()) { return; }

            ClearPendingHeals();
            ServerSend(null, NetworkHeader.CLEAR_PENDING, DeliveryMethod.Reliable, reponseClient: client);
        }

        private void ProcessRequestedAfflictions(IReadMessage inc, Client client)
        {
            if (rateLimiter.IsLimitReached(client)) { return; }

            NetCrewMember crewMember = INetSerializableStruct.Read<NetCrewMember>(inc);

            CharacterInfo? foundInfo = crewMember.FindCharacterInfo(GetCrewCharacters());

            ImmutableArray<NetAffliction> pendingAfflictions = ImmutableArray<NetAffliction>.Empty;
            int infoId = 0;

            if (foundInfo is { Character.CharacterHealth: { } health })
            {
                pendingAfflictions = GetAllAfflictions(health);
                infoId = foundInfo.GetIdentifierUsingOriginalName();
            }

            INetSerializableStruct writeCrewMember = new NetCrewMember
            {
                CharacterInfoID = infoId,
                Afflictions = pendingAfflictions
            };

            if (foundInfo is not null)
            {
                RemoveClientSubscription(client);

                // the client subscribes to the afflictions of the crew member for the next minute
                afflictionSubscribers.Add(new AfflictionSubscriber(client, foundInfo, DateTimeOffset.Now.AddMinutes(1)));
            }

            ServerSend(writeCrewMember, NetworkHeader.REQUEST_AFFLICTIONS, DeliveryMethod.Unreliable, client);
        }

        private IWriteMessage StartSending()
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.WriteByte((byte)ServerPacketHeader.MEDICAL);
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
                msg.WriteByte((byte)header);
                msg.WriteByte((byte)flag);
                netStruct?.Write(msg);
                GameMain.Server.ServerPeer.Send(msg, c.Connection, deliveryMethod);
            }
        }
    }
}