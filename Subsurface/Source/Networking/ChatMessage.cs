using Barotrauma.Items.Components;
using Barotrauma.Networking.ReliableMessages;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{

    enum ChatMessageType
    {
        Default, Error, Dead, Server, Radio
    }    

    class ChatMessage
    {
        public const float SpeakRange = 2000.0f;

        public static Color[] MessageColor = { Color.White, Color.Red, new Color(63, 72, 204), Color.LightGreen, Color.Yellow };
        
        public readonly string Text;

        public ChatMessageType Type;

        public readonly Character Sender;

        public readonly string SenderName;
        
        public Color Color
        {
            get { return MessageColor[(int)Type]; }
        }

        public string TextWithSender
        {
            get;
            private set;
        }

        private ChatMessage(string senderName, string text, ChatMessageType type, Character sender)
        {
            Text = text;
            Type = type;

            Sender = sender;

            SenderName = senderName;

            TextWithSender = string.IsNullOrWhiteSpace(senderName) ? text : senderName + ": " + text;
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

        public string ApplyDistanceEffect(Character listener)
        {
            if (Sender == null) return Text;

            return ApplyDistanceEffect(listener, Sender, Text, SpeakRange);
        }

        public static string ApplyDistanceEffect(Entity listener, Entity Sender, string text, float range)
        {
            if (listener.WorldPosition == Sender.WorldPosition) return text;

            float dist = Vector2.Distance(listener.WorldPosition, Sender.WorldPosition);
            if (dist > range) return "";

            if (Submarine.CheckVisibility(listener.SimPosition, Sender.SimPosition) != null) dist *= 2.0f;
            if (dist > range) return "";

            float garbleAmount = dist / range;
            if (garbleAmount < 0.5f) return text;

            int startIndex = Math.Max(text.IndexOf(':') + 1, 1);

            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                sb.Append((i>startIndex && Rand.Range(0.0f, 1.0f) < garbleAmount) ? '-' : text[i]);
            }

            return sb.ToString();
        }
        
        public void WriteNetworkMessage(NetOutgoingMessage msg)
        {
            msg.WriteRangedInteger(0, Enum.GetValues(typeof(ChatMessageType)).Length, (byte)Type);
            if (GameMain.Server != null)
            {
                msg.Write(Sender == null ? (ushort)0 : Sender.ID);
                msg.Write(SenderName);
            }

            msg.Write(Text);  
        }

        public static ChatMessage ReadNetworkMessage(NetBuffer msg)
        {
            ChatMessageType type = (ChatMessageType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(ChatMessageType)).Length);
            string senderName="";
            Character character = null;
            if (GameMain.Server == null)
            {
                ushort senderId = msg.ReadUInt16();
                character = Entity.FindEntityByID(senderId) as Character;
                senderName = msg.ReadString();
            }
            else
            {
                NetIncomingMessage inc = msg as NetIncomingMessage;
                if (inc == null) return null;
                Client sender = GameMain.Server.ConnectedClients.Find(x => x.Connection == inc.SenderConnection);
                if (sender == null) return null;
                character = sender.Character;
                if (character != null)
                {
                    senderName = character.Name;
                }
                else
                {
                    senderName = sender.name;
                }
            }
            string text = msg.ReadString();

            return new ChatMessage(senderName, text, type, character);
        }
    }
}
