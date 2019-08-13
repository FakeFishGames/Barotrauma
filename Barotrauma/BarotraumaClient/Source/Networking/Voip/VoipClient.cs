using Barotrauma.Sounds;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Barotrauma.Items.Components;

namespace Barotrauma.Networking
{
    class VoipClient : IDisposable
    {
        private GameClient gameClient;
        private ClientPeer netClient;
        private DateTime lastSendTime;
        private List<VoipQueue> queues;

        private UInt16 storedBufferID = 0;

        public VoipClient(GameClient gClient,ClientPeer nClient)
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
                if (VoipCapture.Instance != null)
                {
                    storedBufferID = VoipCapture.Instance.LatestBufferID;
                    VoipCapture.Instance.Dispose();
                }
                return;
            }
            else
            {
                if (VoipCapture.Instance == null) VoipCapture.Create(GameMain.Config.VoiceCaptureDevice, storedBufferID);
                if (VoipCapture.Instance == null || VoipCapture.Instance.EnqueuedTotalLength <= 0) return;
            }

            if (DateTime.Now >= lastSendTime + VoipConfig.SEND_INTERVAL)
            {
                IWriteMessage msg = new WriteOnlyMessage();

                msg.Write((byte)ClientPacketHeader.VOICE);
                msg.Write((byte)VoipCapture.Instance.QueueID);
                VoipCapture.Instance.Write(msg);

                netClient.Send(msg, DeliveryMethod.Unreliable);

                lastSendTime = DateTime.Now;
            }
        }

        public void Read(IReadMessage msg)
        {
            byte queueId = msg.ReadByte();
            VoipQueue queue = queues.Find(q => q.QueueID == queueId);

            if (queue == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Couldn't find VoipQueue with id " + queueId.ToString() + "!", Color.Red);
#endif
                return;
            }

            if (queue.Read(msg))
            {
                Client client = gameClient.ConnectedClients.Find(c => c.VoipQueue == queue);
                if (client.Muted || client.MutedLocally) { return; }

                if (client.VoipSound == null)
                {
                    DebugConsole.Log("Recreating voipsound " + queueId);
                    client.VoipSound = new VoipSound(GameMain.SoundManager, client.VoipQueue);
                }

                if (client.Character != null && !client.Character.IsDead && client.Character.SpeechImpediment <= 100.0f)
                {
                    var messageType = ChatMessage.CanUseRadio(client.Character, out WifiComponent radio) ? ChatMessageType.Radio : ChatMessageType.Default;
                    client.Character.ShowSpeechBubble(1.25f, ChatMessage.MessageColor[(int)messageType]);

                    client.VoipSound.UseRadioFilter = messageType == ChatMessageType.Radio;
                    if (client.VoipSound.UseRadioFilter)
                    {
                        client.VoipSound.SetRange(radio.Range * 0.8f, radio.Range);
                    }
                    else
                    {
                        client.VoipSound.SetRange(ChatMessage.SpeakRange * 0.4f, ChatMessage.SpeakRange);
                    }
                    if (!client.VoipSound.UseRadioFilter && Character.Controlled != null)
                    {
                        client.VoipSound.UseMuffleFilter = SoundPlayer.ShouldMuffleSound(Character.Controlled, client.Character.WorldPosition, ChatMessage.SpeakRange, client.Character.CurrentHull);
                    }
                }

                GameMain.NetLobbyScreen.SetPlayerSpeaking(client);
                GameMain.GameSession?.CrewManager?.SetClientSpeaking(client);

                if (client.Character != null)
                {
                    Vector3 clientPos = new Vector3(client.Character.WorldPosition.X, client.Character.WorldPosition.Y, 0.0f);
                    Vector3 listenerPos = GameMain.SoundManager.ListenerPosition;
                    float attenuationDist = client.VoipSound.Near * 1.125f;
                    if (Vector3.DistanceSquared(clientPos, listenerPos) < attenuationDist * attenuationDist)
                    {
                        GameMain.SoundManager.VoipAttenuatedGain = 0.2f;
                    }
                }
            }
        }

        public void Dispose()
        {
            VoipCapture.Instance?.Dispose();
        }
    }
}
