using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    class VoipServer
    {
        private NetServer netServer;
        private TimeSpan sendIntervalTimeSpan;
        private Dictionary<Client,DateTime> lastSendTime;

        public VoipServer(NetServer server)
        {
            this.netServer = server;
            sendIntervalTimeSpan = new TimeSpan(0,0,0,0,VoipConfig.SEND_INTERVAL_MS);
            lastSendTime = new Dictionary<Client, DateTime>();
        }
        
        public void SendToClients(VoipQueue queue,List<Client> clients)
        {
            foreach (Client client in clients)
            {
                if (lastSendTime.ContainsKey(client))
                {
                    if ((lastSendTime[client] + sendIntervalTimeSpan) > DateTime.Now) continue;
                    lastSendTime[client] = DateTime.Now;
                }
                else
                {
                    lastSendTime.Add(client, DateTime.Now);
                }

                NetOutgoingMessage msg = netServer.CreateMessage();

                msg.Write((byte)ServerPacketHeader.VOICE);
                queue.Write(msg);
            }
        }
    }

    partial class Client
    {
        public VoipQueue VoipQueue
        {
            get;
            private set;
        }
    }
}
