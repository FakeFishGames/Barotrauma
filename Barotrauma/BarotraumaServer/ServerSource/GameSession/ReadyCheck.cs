#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    internal partial class ReadyCheck
    {
        private static List<Client> ActivePlayers => GameMain.Server.ConnectedClients.Where(c => c != null && !c.Spectating && c.InGame).ToList();

        public void InitializeReadyCheck(string author, Client? sender = null)
        {
            foreach (Client client in ActivePlayers)
            {
                if (client != null && !client.Spectating)
                {
                    IWriteMessage msg = new WriteOnlyMessage();
                    msg.Write((byte) ServerPacketHeader.READY_CHECK);
                    msg.Write((byte) ReadyCheckState.Start);
                    msg.Write(new DateTimeOffset(startTime).ToUnixTimeSeconds());
                    msg.Write(new DateTimeOffset(endTime).ToUnixTimeSeconds());
                    msg.Write(author);

                    if (sender != null)
                    {
                        msg.Write(true);
                        msg.Write(sender.ID);
                    }
                    else
                    {
                        msg.Write(false);
                    }

                    msg.Write((ushort) ActivePlayers.Count);
                    foreach (byte clientId in Clients.Keys)
                    {
                        msg.Write(clientId);
                    }

                    GameMain.Server.ServerPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
                }
            }
        }

        private void UpdateReadyCheck(byte otherClient, ReadyStatus state)
        {
            if (Clients.All(pair => pair.Value != ReadyStatus.Unanswered))
            {
                EndReadyCheck();
                return;
            }

            foreach (Client client in ActivePlayers)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ServerPacketHeader.READY_CHECK);
                msg.Write((byte)ReadyCheckState.Update);
                msg.Write((byte)state);
                msg.Write(otherClient);
                GameMain.Server.ServerPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        partial void EndReadyCheck()
        {
            if (IsFinished) { return; }
            IsFinished = true;
            foreach (Client client in ActivePlayers)
            {
                if (client != null && !client.Spectating)
                {
                    IWriteMessage msg = new WriteOnlyMessage();
                    msg.Write((byte) ServerPacketHeader.READY_CHECK);
                    msg.Write((byte) ReadyCheckState.End);
                    msg.Write((ushort) Clients.Count);
                    foreach (var (id, state) in Clients)
                    {
                        msg.Write(id);
                        msg.Write((byte) state);
                    }

                    GameMain.Server.ServerPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
                }
            }
        }

        public static void ServerRead(IReadMessage inc, Client client)
        {
            ReadyCheckState state = (ReadyCheckState) inc.ReadByte();
            ReadyCheck? readyCheck = GameMain.GameSession?.CrewManager?.ActiveReadyCheck;

            switch (state)
            {
                case ReadyCheckState.Start when readyCheck == null:
                    StartReadyCheck(client.Name, client);
                    break;
                case ReadyCheckState.Update when readyCheck != null:

                    ReadyStatus status = (ReadyStatus) inc.ReadByte();
                    if (!readyCheck.Clients.ContainsKey(client.ID)) { return; }

                    readyCheck.Clients[client.ID] = status;
                    readyCheck.UpdateReadyCheck(client.ID, status);
                    break;
            }
        }

        public static void StartReadyCheck(string author, Client? sender = null)
        {
            if (GameMain.GameSession?.CrewManager == null || GameMain.GameSession.CrewManager.ActiveReadyCheck != null) { return; }

            List<Client> connectedClients = GameMain.Server.ConnectedClients;
            ReadyCheck newReadyCheck = new ReadyCheck(connectedClients.Where(c => !c.Spectating).Select(c => c.ID).ToList(), 30);
            GameMain.GameSession.CrewManager.ActiveReadyCheck = newReadyCheck;
            newReadyCheck.InitializeReadyCheck(author, sender);
        }
    }
}