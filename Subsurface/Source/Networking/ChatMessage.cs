using Barotrauma.Items.Components;
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

        public static UInt32 LastID = 0;

        public UInt32 NetStateID
        {
            get;
            set;
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

        public void ClientWrite(NetOutgoingMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write(Text);
        }

        static public void ServerRead(NetIncomingMessage msg, Client c)
        {
            UInt32 ID = msg.ReadUInt32();
            string txt = msg.ReadString();
            if (c.lastSentChatMsgID < ID)
            {
                //this chat message is new to the server
                GameMain.Server.SendChatMessage(txt, null, c);
                //GameMain.Server.AddChatMessage(txt, ChatMessageType.Default, c.name);
                c.lastSentChatMsgID = ID;
            }
        }

        public void ServerWrite(NetOutgoingMessage msg, Client c)
        {
            msg.Write((byte)ServerNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)Type);
            msg.Write(SenderName);
            msg.Write(Text);
        }

        static public void ClientRead(NetIncomingMessage msg)
        {
            UInt32 ID = msg.ReadUInt32();
            ChatMessageType type = (ChatMessageType)msg.ReadByte();
            string senderName = msg.ReadString();
            string txt = msg.ReadString();
            if (ID > LastID)
            {
                GameMain.Client.AddChatMessage(txt, type, senderName);
                LastID = ID;
            }
        }
    }
}
