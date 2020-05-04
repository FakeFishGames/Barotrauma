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

        private static Rectangle[] voiceIconSheetRects;

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
                DebugConsole.NewMessage("Couldn't find VoipQueue with id " + queueId.ToString() + "!", GUI.Style.Red);
#endif
                return;
            }

            Client client = gameClient.ConnectedClients.Find(c => c.VoipQueue == queue);
            if (queue.Read(msg, discardData: client.Muted || client.MutedLocally))
            {
                if (client.Muted || client.MutedLocally) { return; }
                if (client.VoipSound == null)
                {
                    DebugConsole.Log("Recreating voipsound " + queueId);
                    client.VoipSound = new VoipSound(client.Name, GameMain.SoundManager, client.VoipQueue);
                }

                if (client.Character != null && !client.Character.IsDead && !client.Character.Removed && client.Character.SpeechImpediment <= 100.0f)
                {
                    WifiComponent radio = null;
                    float muffler = 0f;
                    var messageType = !client.VoipQueue.ForceLocal && ChatMessage.CanUseRadio(client.Character, out radio) ? ChatMessageType.Radio : ChatMessageType.Default;
                    client.Character.ShowSpeechBubble(1.25f, ChatMessage.MessageColor[(int)messageType]);

                    client.VoipSound.UseRadioFilter = messageType == ChatMessageType.Radio;
                    if (client.VoipSound.UseRadioFilter)
                    {
                        client.VoipSound.SetRange(radio.Range * 0.8f, radio.Range);
                    }
                    else
                    {
                        client.VoipSound.Muffled = 100 - client.Character.SpeechImpediment;
                        muffler = client.VoipSound.Muffled / 100;
                        client.VoipSound.SetRange(ChatMessage.SpeakRange * (0.4f * muffler), ChatMessage.SpeakRange * muffler);
                    }
                    if (!client.VoipSound.UseRadioFilter && Character.Controlled != null)
                    {
                        if(muffler < 0.5f)
                        {
                            client.VoipSound.UseMuffleFilter = true;
                        }
                        else
                        {
                            client.VoipSound.UseMuffleFilter = SoundPlayer.ShouldMuffleSound(Character.Controlled, client.Character.WorldPosition, ChatMessage.SpeakRange, client.Character.CurrentHull);
                        }
                    }
                }

                GameMain.NetLobbyScreen?.SetPlayerSpeaking(client);
                GameMain.GameSession?.CrewManager?.SetClientSpeaking(client);

                if ((client.VoipSound.CurrentAmplitude * client.VoipSound.Gain * GameMain.SoundManager.GetCategoryGainMultiplier("voip")) > 0.1f) //TODO: might need to tweak
                {
                    if (client.Character != null && !client.Character.Removed)
                    {
                        Vector3 clientPos = new Vector3(client.Character.WorldPosition.X, client.Character.WorldPosition.Y, 0.0f);
                        Vector3 listenerPos = GameMain.SoundManager.ListenerPosition;
                        float attenuationDist = client.VoipSound.Near * 1.125f;
                        if (Vector3.DistanceSquared(clientPos, listenerPos) < attenuationDist * attenuationDist)
                        {
                            GameMain.SoundManager.VoipAttenuatedGain = 0.5f;
                        }
                    }
                    else
                    {
                        GameMain.SoundManager.VoipAttenuatedGain = 0.5f;
                    }
                }
            }
        }

        public static void UpdateVoiceIndicator(GUIImage soundIcon, float voipAmplitude, float deltaTime)
        {
            if (voiceIconSheetRects == null)
            {
                var soundIconStyle = GUI.Style.GetComponentStyle("GUISoundIcon");
                Rectangle sourceRect = soundIconStyle.Sprites.First().Value.First().Sprite.SourceRect;
                var indexPieces = soundIconStyle.Element.Attribute("sheetindices").Value.Split(';');
                voiceIconSheetRects = new Rectangle[indexPieces.Length];
                for (int i = 0; i < indexPieces.Length; i++)
                {
                    Point location = sourceRect.Location + XMLExtensions.ParsePoint(indexPieces[i].Trim()) * sourceRect.Size;
                    voiceIconSheetRects[i] = new Rectangle(location, sourceRect.Size);
                }
            }

            Pair<string, float> userdata = soundIcon.UserData as Pair<string, float>;
            userdata.Second = Math.Max(voipAmplitude, userdata.Second - deltaTime);

            if (userdata.Second <= 0.0f)
            {
                soundIcon.Visible = false;
            }
            else
            {
                soundIcon.Visible = true;
                int sheetIndex = (int)Math.Floor(userdata.Second * voiceIconSheetRects.Length);
                sheetIndex = MathHelper.Clamp(sheetIndex, 0, voiceIconSheetRects.Length - 1);
                soundIcon.SourceRect = voiceIconSheetRects[sheetIndex];
                soundIcon.OverrideState = GUIComponent.ComponentState.None;
                soundIcon.HoverColor = Color.White;
            }
        }

        public void Dispose()
        {
            VoipCapture.Instance?.Dispose();
        }
    }
}
