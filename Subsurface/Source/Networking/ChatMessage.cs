using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Text;

namespace Barotrauma.Networking
{
    enum ChatMessageType
    {
        Default, Error, Dead, Server, Radio, Private
    }    

    class ChatMessage
    {
        public const int MaxLength = 150;

        public const int MaxMessagesPerPacket = 10;

        public const float SpeakRange = 2000.0f;
        
        public static Color[] MessageColor = 
        {
            new Color(125, 140, 153),   //default
            new Color(204, 74, 78),     //error
            new Color(63, 72, 204),     //dead
            new Color(157, 225, 160),   //server
            new Color(238, 208, 0),     //radio
            new Color(228, 199, 27)     //private
        };
        
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

        public static UInt16 LastID = 0;

        public UInt16 NetStateID
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

        public static void ServerRead(NetIncomingMessage msg, Client c)
        {
            UInt16 ID = msg.ReadUInt16();
            string txt = msg.ReadString();
            if (txt == null) txt = "";

            if (!NetIdUtils.IdMoreRecent(ID, c.lastSentChatMsgID)) return;

            c.lastSentChatMsgID = ID;

            if (txt.Length > MaxLength)
            {
                txt = txt.Substring(0, MaxLength);
            }

            c.lastSentChatMessages.Add(txt);
            if (c.lastSentChatMessages.Count > 10)
            {
                c.lastSentChatMessages.RemoveRange(0, c.lastSentChatMessages.Count-10);
            }
            
            float similarity = 0.0f;
            for (int i = 0; i < c.lastSentChatMessages.Count; i++)
            {
                float closeFactor = 1.0f / (c.lastSentChatMessages.Count - i);
                int levenshteinDist = ToolBox.LevenshteinDistance(txt, c.lastSentChatMessages[i]);
                similarity += Math.Max((txt.Length - levenshteinDist) / (float)txt.Length * closeFactor, 0.0f);
            }

            if (similarity + c.ChatSpamSpeed > 5.0f)
            {
                c.ChatSpamCount++;

                if (c.ChatSpamCount > 3)
                {
                    //kick for spamming too much
                    GameMain.Server.KickClient(c);
                }
                else
                {
                    ChatMessage denyMsg = ChatMessage.Create("", "You have been blocked by the spam filter. Try again after 10 seconds.", ChatMessageType.Server, null);
                    c.ChatSpamTimer = 10.0f;
                    GameMain.Server.SendChatMessage(denyMsg, c);
                }
                return;
            }

            c.ChatSpamSpeed += similarity + 0.5f;

            if (c.ChatSpamTimer > 0.0f)
            {
                ChatMessage denyMsg = ChatMessage.Create("", "You have been blocked by the spam filter. Try again after 10 seconds.", ChatMessageType.Server, null);
                c.ChatSpamTimer = 10.0f;
                GameMain.Server.SendChatMessage(denyMsg, c);
                return;
            }

            GameMain.Server.SendChatMessage(txt, null, c);
        }

        public void ServerWrite(NetOutgoingMessage msg, Client c)
        {
            msg.Write((byte)ServerNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)Type);
            msg.Write(Text);

            msg.Write(Sender != null && c.inGame);
            if (Sender != null && c.inGame)
            {
                msg.Write(Sender.ID);
            }
            else
            {
                msg.Write(SenderName);
            }
        }

        public static void ClientRead(NetIncomingMessage msg)
        {
            UInt16 ID = msg.ReadUInt16();
            ChatMessageType type = (ChatMessageType)msg.ReadByte();           
            string txt = msg.ReadString();

            string senderName = "";
            Character senderCharacter = null;
            bool hasSenderCharacter = msg.ReadBoolean();
            if (hasSenderCharacter)
            {
                senderCharacter = Entity.FindEntityByID(msg.ReadUInt16()) as Character;
                if (senderCharacter != null)
                {
                    senderName = senderCharacter.Name;
                }
            }
            else
            {
                senderName = msg.ReadString();
            }

            if (NetIdUtils.IdMoreRecent(ID, LastID))
            {
                GameMain.Client.AddChatMessage(txt, type, senderName, senderCharacter);
                LastID = ID;
            }
        }
    }
}
