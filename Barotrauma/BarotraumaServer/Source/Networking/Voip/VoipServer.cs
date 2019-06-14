using Barotrauma.Items.Components;
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
            foreach (VoipQueue queue in queues)
            {
                if (queue.LastReadTime < DateTime.Now - VoipConfig.SEND_INTERVAL) { continue; }

                if (lastSendTime.ContainsKey(queue))
                {
                    if ((lastSendTime[queue] + VoipConfig.SEND_INTERVAL) > DateTime.Now) { continue; }
                    lastSendTime[queue] = DateTime.Now;
                }
                else
                {
                    lastSendTime.Add(queue, DateTime.Now);
                }

                Client sender = clients.Find(c => c.VoipQueue == queue);

                foreach (Client recipient in clients)
                {
                    if (recipient == sender) { continue; }

                    if (!CanReceive(sender, recipient)) { continue; }

                    NetOutgoingMessage msg = netServer.CreateMessage();

                    msg.Write((byte)ServerPacketHeader.VOICE);
                    msg.Write((byte)queue.QueueID);
                    queue.Write(msg);

                    GameMain.Server.CompressOutgoingMessage(msg);

                    netServer.SendMessage(msg, recipient.Connection, NetDeliveryMethod.Unreliable);
                }
            }
        }

        private bool CanReceive(Client sender, Client recipient)
        {
            if (Screen.Selected != GameMain.GameScreen) { return true; }

            //no-one can hear muted players
            if (sender.Muted) { return false; }

            bool recipientSpectating = recipient.Character == null || recipient.Character.IsDead;
            bool senderSpectating = sender.Character == null || sender.Character.IsDead;

            //TODO: only allow spectators to hear the voice chat if close enough to the speaker?

            //non-spectators cannot hear spectators
            if (senderSpectating && !recipientSpectating) { return false; }

            //both spectating, no need to do radio/distance checks
            if (recipientSpectating && senderSpectating) { return true; }

            //sender can't speak
            if (sender.Character != null && sender.Character.SpeechImpediment >= 100.0f) { return false; }

            //check if the message can be sent via radio
            if (ChatMessage.CanUseRadio(sender.Character, out WifiComponent senderRadio) && 
                ChatMessage.CanUseRadio(recipient.Character, out WifiComponent recipientRadio))
            {
                if (recipientRadio.CanReceive(senderRadio)) { return true; }
            }

            //otherwise do a distance check
            return ChatMessage.GetGarbleAmount(recipient.Character, sender.Character, ChatMessage.SpeakRange) < 1.0f;
        }
    }
}
