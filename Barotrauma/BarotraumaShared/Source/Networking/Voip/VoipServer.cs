using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    class VoipServer
    {
        private NetServer server;
        private TimeSpan sendIntervalTimeSpan;
        private Dictionary<Client,DateTime> lastSendTime;

        public VoipServer(NetServer server)
        {
            this.server = server;
            sendIntervalTimeSpan = new TimeSpan(0,0,0,0,VoipConfig.SEND_INTERVAL_MS);
            lastSendTime = new Dictionary<Client, DateTime>();
        }
        
        public void SendToClients(List<Client> clients)
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

                NetOutgoingMessage msg = server.CreateMessage();

                msg.Write((byte)ServerPacketHeader.VOICE);

                msg.Write((byte)(clients.Count - 1));
                foreach (Client c in clients)
                {
                    if (c == client) continue;
                    msg.Write(c.ID);
                    c.voipQueue.Write(msg);
                }
            }
        }
    }

    partial class Client
    {
        public VoipConfig.VoipQueue voipQueue
        {
            get;
            private set;
        }
    }
}
