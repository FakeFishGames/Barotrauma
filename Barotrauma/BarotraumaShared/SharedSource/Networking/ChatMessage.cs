﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    public enum ChatMessageType
    {
        Default = 0,
        Error = 1,
        Dead = 2,
        Server = 3,
        Radio = 4,
        Private = 5,
        Console = 6,
        MessageBox = 7,
        Order = 8,
        ServerLog = 9,
        ServerMessageBox = 10,
        ServerMessageBoxInGame = 11
    }

    public enum PlayerConnectionChangeType { None = 0, Joined = 1, Kicked = 2, Disconnected = 3, Banned = 4 }

    partial class ChatMessage
    {
        public const int MaxLength = 200;

        public const int MaxMessagesPerPacket = 10;

        public const float SpeakRange = 2000.0f;

        private static readonly string dateTimeFormatLongTimePattern = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;

        public static Color[] MessageColor = 
        {
            new Color(190, 198, 205),   //default
            new Color(204, 74, 78),     //error
            new Color(136, 177, 255),     //dead
            new Color(157, 225, 160),   //server
            new Color(238, 208, 0),     //radio
            new Color(64, 240, 89),     //private
            new Color(255, 255, 255),   //console
            new Color(255, 255, 255),   //messagebox
            new Color(255, 128, 0)      //order
        };

        public readonly string Text;

        private string translatedText;
        public string TranslatedText
        {
            get
            {
                if (Type.HasFlag(ChatMessageType.Server) || Type.HasFlag(ChatMessageType.Error) || Type.HasFlag(ChatMessageType.ServerLog))
                {
                    if (translatedText.IsNullOrEmpty())
                    {
                        translatedText = TextManager.GetServerMessage(Text).Value;
                    }

                    return translatedText;
                }
                else
                {
                    return Text;
                }
            }
        }

        public ChatMessageType Type;
        public PlayerConnectionChangeType ChangeType;
        public string IconStyle;

        public readonly Character Sender;
        public readonly Client SenderClient;

        public readonly string SenderName;

        private Color? customTextColor;
        public Color Color
        {
            get
            {
                if (customTextColor != null) { return customTextColor.Value; }
                int intType = (int)Type;
                if (intType < 0 || intType >= MessageColor.Length) { return Color.White; }
                return MessageColor[intType];
            }

            set
            {
                customTextColor = value;
            }
        }

        public static string GetTimeStamp()
        {
            return $"[{DateTime.Now.ToString(dateTimeFormatLongTimePattern)}] ";
        }

        public string TextWithSender
        {
            get
            {
                return string.IsNullOrWhiteSpace(SenderName) ? TranslatedText : NetworkMember.ClientLogName(SenderClient, SenderName) + ": " + TranslatedText;
            }
        }

        public static UInt16 LastID = 0;

        public UInt16 NetStateID
        {
            get;
            set;
        }

        public ChatMode ChatMode { get; set; } = ChatMode.None; 

        protected ChatMessage(string senderName, string text, ChatMessageType type, Character sender, Client client, PlayerConnectionChangeType changeType = PlayerConnectionChangeType.None, Color? textColor = null)
        {
            Text = text;
            Type = type;

            Sender = sender;
            SenderClient = client;

            SenderName = senderName;
            ChangeType = changeType;

            customTextColor = textColor;
        }

        public static ChatMessage Create(string senderName, string text, ChatMessageType type, Character sender, Client client = null, PlayerConnectionChangeType changeType = PlayerConnectionChangeType.None, Color? textColor = null)
        {
            return new ChatMessage(senderName, text, type, sender, client ?? GameMain.NetworkMember?.ConnectedClients?.Find(c => c.Character != null && c.Character == sender), changeType, textColor);
        }

        public static string GetChatMessageCommand(string message, out string messageWithoutCommand)
        {
            messageWithoutCommand = message;

            int separatorIndex = message.IndexOf(";");
            if (separatorIndex == -1) return "";

            //int colonIndex = message.IndexOf(":");

            string command = "";
            try
            {
                command = message.Substring(0, separatorIndex);
                command = command.Trim();
            }

            catch 
            {
                return command;
            }

            messageWithoutCommand = message.Substring(separatorIndex + 1, message.Length - separatorIndex - 1).TrimStart();

            return command;
        }

        /// <summary>
        /// How much messages sent by <paramref name="sender"/> should get garbled. Takes the distance between the entities and optionally the obstructions between them into account (see <paramref name="obstructionMultiplier"/>).
        /// </summary>
        /// <param name="obstructionMultiplier">Values greater than or equal to 1 cause the message to get garbled more heavily when there's some obstruction between the characters. Values smaller than 1 mean the garbling only depends on distance.</param>
        public static float GetGarbleAmount(Entity listener, Entity sender, float range, float obstructionMultiplier = 2.0f)
        {
            if (listener == null || sender == null)
            {
                return 0.0f;
            }
            if (listener.WorldPosition == sender.WorldPosition) { return 0.0f; }

            float dist = Vector2.Distance(listener.WorldPosition, sender.WorldPosition);
            if (dist > range) { return 1.0f; }

            Hull listenerHull = listener == null ? null : Hull.FindHull(listener.WorldPosition);
            Hull sourceHull = sender == null ? null : Hull.FindHull(sender.WorldPosition);
            if (sourceHull != listenerHull && obstructionMultiplier >= 1.0f)
            {
                if ((sourceHull == null || !sourceHull.GetConnectedHulls(includingThis: false, searchDepth: 2, ignoreClosedGaps: true).Contains(listenerHull)) &&
                    Submarine.CheckVisibility(listener.SimPosition, sender.SimPosition) != null) 
                { 
                    dist = (dist + 100f) * obstructionMultiplier; 
                }
            }
            if (dist > range) { return 1.0f; }

            return dist / range;
        }

        public string ApplyDistanceEffect(Character listener)
        {
            if (Sender == null) return Text;

            return ApplyDistanceEffect(listener, Sender, Text, SpeakRange);
        }

        public static string ApplyDistanceEffect(Entity listener, Entity sender, string text, float range, float obstructionMultiplier = 2.0f)
        {
            return ApplyDistanceEffect(text, GetGarbleAmount(listener, sender, range, obstructionMultiplier));
        }

        public static string ApplyDistanceEffect(string text, float garbleAmount)
        {
            if (garbleAmount < 0.3f) return text;
            if (garbleAmount >= 1.0f) return "";

            int startIndex = Math.Max(text.IndexOf(':') + 1, 1);

            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                sb.Append((i > startIndex && Rand.Range(0.0f, 1.0f) < garbleAmount) ? '-' : text[i]);
            }

            return sb.ToString();
        }

        public static string ApplyDistanceEffect(string message, ChatMessageType type, Character sender, Character receiver)
        {
            if (sender == null) { return ""; }
            float range = SpeakRange;
            if (type == ChatMessageType.Default && sender.SpeechImpediment > 0)
            {
                range *= 1.0f - sender.SpeechImpediment / 100.0f;
            }
            string spokenMsg = ApplyDistanceEffect(receiver, sender, message, range, 3.0f);

            switch (type)
            {
                case ChatMessageType.Default:
                    if (receiver != null && !receiver.IsDead)
                    {
                        return spokenMsg;
                    }
                    break;
                case ChatMessageType.Radio:
                case ChatMessageType.Order:
                    if (receiver?.Inventory != null && !receiver.IsDead)
                    {
                        foreach (Item receiverItem in receiver.Inventory.AllItems.Where(i => i.GetComponent<WifiComponent>()?.LinkToChat ?? false))
                        {
                            if (sender.Inventory == null || !receiver.HasEquippedItem(receiverItem)) { continue; }

                            foreach (Item senderItem in sender.Inventory.AllItems.Where(i => i.GetComponent<WifiComponent>()?.LinkToChat ?? false))
                            {
                                if (!sender.HasEquippedItem(senderItem)) { continue; }

                                var receiverRadio = receiverItem.GetComponent<WifiComponent>();
                                var senderRadio = senderItem.GetComponent<WifiComponent>();
                                if (!receiverRadio.CanReceive(senderRadio)) { continue; }

                                string msg = ApplyDistanceEffect(receiverItem, senderItem, message, senderRadio.Range, obstructionMultiplier: 0);
                                if (sender.SpeechImpediment > 0.0f)
                                {
                                    //speech impediment doesn't reduce the range when using a radio, but adds extra garbling
                                    msg = ApplyDistanceEffect(msg, sender.SpeechImpediment / 100.0f);
                                }
                                return msg;
                            }
                        }
                        return spokenMsg;
                    }
                    break;
            }

            return message;
        }

        public int EstimateLengthBytesClient()
        {
            int length =    1 + //(byte)ServerNetObject.CHAT_MESSAGE
                            2 + //(UInt16)NetStateID
                            Encoding.UTF8.GetBytes(Text).Length + 2;

            return length;
        }

        public static bool CanUseRadio(Character sender, bool ignoreJamming = false)
        {
            return CanUseRadio(sender, out _, ignoreJamming);
        }

        public static bool CanUseRadio(Character sender, out WifiComponent radio, bool ignoreJamming = false)
        {
            radio = null;
            if (sender?.Inventory == null || sender.Removed) { return false; }

            foreach (Item item in sender.Inventory.AllItems)
            {
                var wifiComponent = item.GetComponent<WifiComponent>();
                if (wifiComponent == null || !wifiComponent.LinkToChat || !wifiComponent.CanTransmit(ignoreJamming) || !sender.HasEquippedItem(item)) { continue; }
                if (radio == null || wifiComponent.Range > radio.Range)
                {
                    radio = wifiComponent;
                }
            }
            return radio?.Item != null;
        }
    }
}
