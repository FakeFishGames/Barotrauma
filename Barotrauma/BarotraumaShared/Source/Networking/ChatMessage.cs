using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    public enum ChatMessageType
    {
        Default, Error, Dead, Server, Radio, Private, Console, MessageBox, Order, ServerLog
    }

    partial class ChatMessage
    {
        public const int MaxLength = 150;

        public const int MaxMessagesPerPacket = 10;

        public const float SpeakRange = 2000.0f;
        
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
                    if (translatedText == null || translatedText.Length == 0)
                    {
                        translatedText = TextManager.GetServerMessage(Text);
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

        public readonly Character Sender;

        public readonly string SenderName;

        public Color Color
        {
            get { return MessageColor[(int)Type]; }
        }

        public string TextWithSender
        {
            get
            {
                return string.IsNullOrWhiteSpace(SenderName) ? TranslatedText : SenderName + ": " + TranslatedText;
            }
        }

        public static UInt16 LastID = 0;

        public UInt16 NetStateID
        {
            get;
            set;
        }

        protected ChatMessage(string senderName, string text, ChatMessageType type, Character sender)
        {
            Text = text;
            Type = type;

            Sender = sender;

            SenderName = senderName;
        }        

        public static ChatMessage Create(string senderName, string text, ChatMessageType type, Character sender)
        {
            return new ChatMessage(senderName, text, type, sender);
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

        public static float GetGarbleAmount(Entity listener, Entity sender, float range, float obstructionmult = 2.0f)
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
            if (sourceHull != listenerHull)
            {
                if (Submarine.CheckVisibility(listener.SimPosition, sender.SimPosition) != null) dist = (dist + 100f) * obstructionmult;
            }
            if (dist > range) { return 1.0f; }

            return dist / range;
        }

        public string ApplyDistanceEffect(Character listener)
        {
            if (Sender == null) return Text;

            return ApplyDistanceEffect(listener, Sender, Text, SpeakRange);
        }

        public static string ApplyDistanceEffect(Entity listener, Entity sender, string text, float range, float obstructionmult = 2.0f)
        {
            return ApplyDistanceEffect(text, GetGarbleAmount(listener, sender, range, obstructionmult));
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
            if (sender == null) return "";

            switch (type)
            {
                case ChatMessageType.Default:
                    if (receiver != null && !receiver.IsDead)
                    {
                        return ApplyDistanceEffect(receiver, sender, message, SpeakRange * (1.0f - sender.SpeechImpediment / 100.0f), 3.0f);
                    }
                    break;
                case ChatMessageType.Radio:
                case ChatMessageType.Order:
                    if (receiver != null && !receiver.IsDead)
                    {
                        var receiverItem = receiver.Inventory?.Items.FirstOrDefault(i => i?.GetComponent<WifiComponent>() != null);
                        //character doesn't have a radio -> don't send
                        if (receiverItem == null || !receiver.HasEquippedItem(receiverItem)) return "";

                        var senderItem = sender.Inventory?.Items.FirstOrDefault(i => i?.GetComponent<WifiComponent>() != null);
                        if (senderItem == null || !sender.HasEquippedItem(senderItem)) return "";

                        var receiverRadio = receiverItem.GetComponent<WifiComponent>();
                        var senderRadio = senderItem.GetComponent<WifiComponent>();

                        if (!receiverRadio.CanReceive(senderRadio)) return "";

                        string msg = ApplyDistanceEffect(receiverItem, senderItem, message, senderRadio.Range);
                        if (sender.SpeechImpediment > 0.0f)
                        {
                            //speech impediment doesn't reduce the range when using a radio, but adds extra garbling
                            msg = ApplyDistanceEffect(msg, sender.SpeechImpediment / 100.0f);
                        }
                        return msg;
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

        public static bool CanUseRadio(Character sender)
        {
            return CanUseRadio(sender, out _);
        }

        public static bool CanUseRadio(Character sender, out WifiComponent radio)
        {
            radio = null;
            if (sender == null) { return false; }
            var senderItem = sender.Inventory.Items.FirstOrDefault(i => i?.GetComponent<WifiComponent>() != null);
            if (senderItem == null) { return false; }
            radio = senderItem.GetComponent<WifiComponent>();
            return sender.HasEquippedItem(senderItem) && radio.CanTransmit();
        }
    }
}
