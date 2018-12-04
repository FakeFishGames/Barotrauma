using Lidgren.Network;
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
                if (lastSendTime.ContainsKey(queue))
                {
                    if ((lastSendTime[queue] + VoipConfig.SEND_INTERVAL) > DateTime.Now) continue;
                    lastSendTime[queue] = DateTime.Now;
                }
                else
                {
                    lastSendTime.Add(queue, DateTime.Now);
                }

                foreach (Client client in clients)
                {
                    if (client.VoipQueue == queue) continue;
                    //TODO: use character states to determine whether to send or not
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
