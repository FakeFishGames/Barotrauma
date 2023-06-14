using Barotrauma.Items.Components;
using Barotrauma.Sounds;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    class VoipClient : IDisposable
    {
        /// <summary>
        /// The "near" range of the voice chat (a percentage of either SpeakRange or radio range), further than this the volume starts to diminish
        /// </summary>
        const float RangeNear = 0.4f;

        private readonly GameClient gameClient;
        private readonly ClientPeer netClient;
        private DateTime lastSendTime;
        private readonly List<VoipQueue> queues;

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
            if (queue == VoipCapture.Instance) { return; }
            if (!queues.Contains(queue)) { queues.Add(queue); }
        }

        public void UnregisterQueue(VoipQueue queue)
        {
            if (queues.Contains(queue)) { queues.Remove(queue); }
        }

        public void SendToServer()
        {
            if (GameSettings.CurrentConfig.Audio.VoiceSetting == VoiceMode.Disabled)
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
                try
                {
                    if (VoipCapture.Instance == null) { VoipCapture.Create(GameSettings.CurrentConfig.Audio.VoiceCaptureDevice, storedBufferID); }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"VoipCature.Create failed: {e.Message} {e.StackTrace.CleanupStackTrace()}");
                    var config = GameSettings.CurrentConfig;
                    config.Audio.VoiceSetting = VoiceMode.Disabled;
                    GameSettings.SetCurrentConfig(config);
                }
                if (VoipCapture.Instance == null || VoipCapture.Instance.EnqueuedTotalLength <= 0) { return; }
            }

            if (DateTime.Now >= lastSendTime + VoipConfig.SEND_INTERVAL)
            {
                IWriteMessage msg = new WriteOnlyMessage();

                msg.WriteByte((byte)ClientPacketHeader.VOICE);
                msg.WriteByte((byte)VoipCapture.Instance.QueueID);
                VoipCapture.Instance.Write(msg);

                netClient.Send(msg, DeliveryMethod.Unreliable);

                lastSendTime = DateTime.Now;
            }
        }

        public void Read(IReadMessage msg)
        {
            byte queueId = msg.ReadByte();
            float distanceFactor = msg.ReadRangedSingle(0.0f, 1.0f, 8);
            VoipQueue queue = queues.Find(q => q.QueueID == queueId);

            if (queue == null)
            {
#if DEBUG
                DebugConsole.NewMessage("Couldn't find VoipQueue with id " + queueId.ToString() + "!", GUIStyle.Red);
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
                GameMain.SoundManager.ForceStreamUpdate();
                client.RadioNoise = 0.0f;
                if (client.Character != null && !client.Character.IsDead && !client.Character.Removed && client.Character.SpeechImpediment <= 100.0f)
                {
                    float speechImpedimentMultiplier = 1.0f - client.Character.SpeechImpediment / 100.0f;
                    bool spectating = Character.Controlled == null;
                    float rangeMultiplier = spectating ? 2.0f : 1.0f;
                    WifiComponent radio = null;
                    var messageType = 
                        !client.VoipQueue.ForceLocal && ChatMessage.CanUseRadio(client.Character, out radio) && ChatMessage.CanUseRadio(Character.Controlled) ? 
                        ChatMessageType.Radio : ChatMessageType.Default;
                    client.Character.ShowSpeechBubble(1.25f, ChatMessage.MessageColor[(int)messageType]);

                    client.VoipSound.UseRadioFilter = messageType == ChatMessageType.Radio && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters;
                    client.RadioNoise = 0.0f;
                    if (messageType == ChatMessageType.Radio)
                    {
                        client.VoipSound.SetRange(radio.Range * RangeNear * speechImpedimentMultiplier * rangeMultiplier, radio.Range * speechImpedimentMultiplier * rangeMultiplier);
                        if (distanceFactor > RangeNear && !spectating)
                        {
                            //noise starts increasing exponentially after 40% range
                            client.RadioNoise = MathF.Pow(MathUtils.InverseLerp(RangeNear, 1.0f, distanceFactor), 2);
                        }
                    }
                    else
                    {
                        client.VoipSound.SetRange(ChatMessage.SpeakRange * RangeNear * speechImpedimentMultiplier * rangeMultiplier, ChatMessage.SpeakRange * speechImpedimentMultiplier * rangeMultiplier);
                    }
                    client.VoipSound.UseMuffleFilter = 
                        messageType != ChatMessageType.Radio && Character.Controlled != null && !GameSettings.CurrentConfig.Audio.DisableVoiceChatFilters &&
                        SoundPlayer.ShouldMuffleSound(Character.Controlled, client.Character.WorldPosition, ChatMessage.SpeakRange, client.Character.CurrentHull);                 
                }

                GameMain.NetLobbyScreen?.SetPlayerSpeaking(client);
                GameMain.GameSession?.CrewManager?.SetClientSpeaking(client);

                if ((client.VoipSound.CurrentAmplitude * client.VoipSound.Gain * GameMain.SoundManager.GetCategoryGainMultiplier("voip")) > 0.1f) //TODO: might need to tweak
                {
                    if (client.Character != null && !client.Character.Removed && !client.Character.IsDead)
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
                var soundIconStyle = GUIStyle.GetComponentStyle("GUISoundIcon");
                Rectangle sourceRect = soundIconStyle.Sprites.First().Value.First().Sprite.SourceRect;
                var indexPieces = soundIconStyle.Element.GetAttribute("sheetindices").Value.Split(';');
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
