using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    class VoipServer
    {
        private NetServer netServer;
        private List<VoipQueue> queues;
        private Dictionary<VoipQueue,DateTime> lastSendTime;

        public VoipServer(NetServer server)
        {
            this.netServer = server;
            queues = new List<VoipQueue>();
            lastSendTime = new Dictionary<VoipQueue, DateTime>();
        }

        public void RegisterQueue(VoipQueue queue)
        {
            if (!queues.Contains(queue)) queues.Add(queue);
        }

        public void UnregisterQueue(VoipQueue queue)
        {
            if (queues.Contains(queue)) queues.Remove(queue);
        }

        public void SendToClients(List<Client> clients)
        {
            foreach (VoipQueue queue in queues) {
                if (queue.LastReadTime < DateTime.Now - VoipConfig.SEND_INTERVAL) continue;

                if (lastSendTime.ContainsKey(queue))
                {
                    if ((lastSendTime[queue] + VoipConfig.SEND_INTERVAL) > DateTime.Now) continue;
                    lastSendTime[queue] = DateTime.Now;
                }
                else
                {
                    lastSendTime.Add(queue, DateTime.Now);
                }

                Client currClient = clients.Find(c => c.VoipQueue == queue);

                foreach (Client client in clients)
                {
                    if (client == currClient) continue;
                    
                    if (Screen.Selected == GameMain.GameScreen)
                    {
                        if (client.Character == null || client.Character.IsDead) //client is spectating
                        {
                            if (currClient.Character != null && !currClient.Character.IsDead) //currClient is not spectating
                            {
                                continue;
                            }
                        }
                        else //client is not spectating
                        {
                            if (currClient.Character == null || client.Character.IsDead) //currClient is spectating
                            {
                                continue;
                            }
                            else if (Vector2.Distance(currClient.Character.Position,client.Character.Position)>500.0f) //clients are too far away from each other
                            {
                                //TODO: account for radio
                                continue;
                            }
                        }
                    }

                    NetOutgoingMessage msg = netServer.CreateMessage();

                    msg.Write((byte)ServerPacketHeader.VOICE);
                    msg.Write((byte)queue.QueueID);
                    queue.Write(msg);

                    GameMain.Server.CompressOutgoingMessage(msg);

                    netServer.SendMessage(msg, client.Connection, NetDeliveryMethod.Unreliable);
                }
            }
        }
    }
}
