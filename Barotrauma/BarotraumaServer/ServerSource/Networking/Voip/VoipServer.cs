using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    class VoipServer
    {
        private readonly ServerPeer netServer;
        private readonly List<VoipQueue> queues;
        private readonly Dictionary<VoipQueue,DateTime> lastSendTime;

        public VoipServer(ServerPeer server)
        {
            netServer = server;
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
                if (sender == null) { return; }

                foreach (Client recipient in clients)
                {
                    if (recipient == sender) { continue; }

                    if (!CanReceive(sender, recipient, out float distanceFactor)) { continue; }

                    IWriteMessage msg = new WriteOnlyMessage();

                    msg.WriteByte((byte)ServerPacketHeader.VOICE);
                    msg.WriteByte((byte)queue.QueueID);
                    msg.WriteRangedSingle(distanceFactor, 0.0f, 1.0f, 8);
                    queue.Write(msg);
                    
                    netServer.Send(msg, recipient.Connection, DeliveryMethod.Unreliable);
                }
            }
        }

        private static bool CanReceive(Client sender, Client recipient, out float distanceFactor)
        {
            if (Screen.Selected != GameMain.GameScreen) 
            {
                distanceFactor = 0.0f;
                return true; 
            }

            distanceFactor = 0.0f;

            //no-one can hear muted players
            if (sender.Muted) { return false; }

            bool recipientSpectating = recipient.Character == null || recipient.Character.IsDead;
            bool senderSpectating = sender.Character == null || sender.Character.IsDead;

            //non-spectators cannot hear spectators, and spectators can always hear spectators
            if (senderSpectating)
            {
                return recipientSpectating;
            }

            //sender can't speak
            if (sender.Character != null && sender.Character.SpeechImpediment >= 100.0f) { return false; }

            //check if the message can be sent via radio
            WifiComponent recipientRadio = null;
            if (!sender.VoipQueue.ForceLocal &&
                ChatMessage.CanUseRadio(sender.Character, out WifiComponent senderRadio) &&
                (recipientSpectating || ChatMessage.CanUseRadio(recipient.Character, out recipientRadio)))
            {
                if (recipientSpectating)
                {
                    if (recipient.SpectatePos == null) { return true; }
                    distanceFactor = MathHelper.Clamp(Vector2.Distance(sender.Character.WorldPosition, recipient.SpectatePos.Value) / senderRadio.Range, 0.0f, 1.0f);
                    return distanceFactor < 1.0f;
                }
                else if (recipientRadio != null && recipientRadio.CanReceive(senderRadio))
                {
                    distanceFactor = MathHelper.Clamp(Vector2.Distance(sender.Character.WorldPosition, recipient.Character.WorldPosition) / senderRadio.Range, 0.0f, 1.0f);
                    return true;
                }
            }

            if (recipientSpectating)
            {
                if (recipient.SpectatePos == null) { return true; }
                distanceFactor = MathHelper.Clamp(Vector2.Distance(sender.Character.WorldPosition, recipient.SpectatePos.Value) / ChatMessage.SpeakRange, 0.0f, 1.0f);
                return distanceFactor < 1.0f;
            }
            else
            {
                //otherwise do a distance check
                float garbleAmount = ChatMessage.GetGarbleAmount(recipient.Character, sender.Character, ChatMessage.SpeakRange);
                distanceFactor = garbleAmount;
                return garbleAmount < 1.0f;
            }
        }
    }
}
