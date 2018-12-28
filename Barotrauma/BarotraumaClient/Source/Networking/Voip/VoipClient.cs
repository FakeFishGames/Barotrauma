using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class VoipClient : IDisposable
    {
        private GameClient gameClient;
        private NetClient netClient;
        private DateTime lastSendTime;
        private List<VoipQueue> queues;

        public VoipClient(GameClient gClient,NetClient nClient)
        {
            gameClient = gClient;
            netClient = nClient;
            
            queues = new List<VoipQueue>();
            
            lastSendTime = DateTime.Now;
        }

        public void RegisterQueue(VoipQueue queue)
        {
            if (queue == VoipCapture.Instance) return;
            if (!queues.Contains(queue)) queues.Add(queue);
        }

        public void UnregisterQueue(VoipQueue queue)
        {
            if (queues.Contains(queue)) queues.Remove(queue);
        }

        public void SendToServer()
        {
            if (GameMain.Config.VoiceSetting == GameSettings.VoiceMode.Disabled)
            {
                VoipCapture.Instance?.Dispose();
                return;
            }
            else
            {
                if (VoipCapture.Instance == null) VoipCapture.Create();
            }

            if (DateTime.Now >= lastSendTime + VoipConfig.SEND_INTERVAL)
            {
                NetOutgoingMessage msg = netClient.CreateMessage();

                msg.Write((byte)ClientPacketHeader.VOICE);
                msg.Write((byte)VoipCapture.Instance.QueueID);
                VoipCapture.Instance.Write(msg);

                netClient.SendMessage(msg, NetDeliveryMethod.Unreliable);

                lastSendTime = DateTime.Now;
            }
        }

        public void Read(NetBuffer msg)
        {
            byte queueId = msg.ReadByte();
            VoipQueue queue = queues.Find(q => q.QueueID == queueId);
            if (queue!=null)
            {
                queue.Read(msg);
            }
            else
            {
                DebugConsole.ThrowError("Couldn't find VoipQueue with id " + queueId.ToString()+"!");
                return;
            }
        }

        public void Dispose()
        {
            VoipCapture.Instance?.Dispose();
        }
    }
}
