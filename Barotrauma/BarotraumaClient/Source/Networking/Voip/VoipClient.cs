using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class VoipClient
    {
        private GameClient gameClient;
        private NetClient netClient;
        private DateTime lastSendTime;
        private VoipCapture capture;

        public VoipClient(GameClient gClient,NetClient nClient)
        {
            gameClient = gClient;

            capture = new VoipCapture(gClient.ID);
            
            lastSendTime = DateTime.Now;
        }

        public void Write()
        {
            if (DateTime.Now >= lastSendTime + VoipConfig.SEND_INTERVAL)
            {
                NetOutgoingMessage msg = netClient.CreateMessage();

                msg.Write((byte)ClientPacketHeader.VOICE);
                msg.Write((UInt16)capture.QueueID);
                capture.Write(msg);

                netClient.SendMessage(msg, NetDeliveryMethod.Unreliable);
            }
        }
    }
}
