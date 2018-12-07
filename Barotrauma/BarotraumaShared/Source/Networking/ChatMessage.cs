using Barotrauma.Items.Components;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    enum ChatMessageType
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

        protected ChatMessage(string senderName, string text, ChatMessageType type, Character sender)
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

        public static string ApplyDistanceEffect(Entity listener, Entity Sender, string text, float range, float obstructionmult = 2.0f)
        {
            if (listener.WorldPosition == Sender.WorldPosition) return text;

            float dist = Vector2.Distance(listener.WorldPosition, Sender.WorldPosition);
            if (dist > range) return "";

            if (Submarine.CheckVisibility(listener.SimPosition, Sender.SimPosition) != null) dist = (dist + 100f) * obstructionmult;
            if (dist > range) return "";
            
            return ApplyDistanceEffect(text, dist / range);
        }

        public static string ApplyDistanceEffect(string text, float garbleAmount)
        {
            if (garbleAmount < 0.3f) return text;
            if (garbleAmount > 1.0f) return "";

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

        public static void ServerRead(NetIncomingMessage msg, Client c)
        {
            c.KickAFKTimer = 0.0f;

            UInt16 ID = msg.ReadUInt16();
            ChatMessageType type = (ChatMessageType)msg.ReadByte();
            string txt = "";

            int orderIndex = -1;
            Character orderTargetCharacter = null;
            Entity orderTargetEntity = null;
            int orderOptionIndex = -1;
            OrderChatMessage orderMsg = null;
            if (type == ChatMessageType.Order)
            {
                orderIndex = msg.ReadByte();
                orderTargetCharacter = Entity.FindEntityByID(msg.ReadUInt16()) as Character;
                orderTargetEntity = Entity.FindEntityByID(msg.ReadUInt16()) as Entity;
                orderOptionIndex = msg.ReadByte();

                if (orderIndex < 0 || orderIndex >= Order.PrefabList.Count)
                {
                    DebugConsole.ThrowError("Invalid order message from client \"" + c.Name + "\" - order index out of bounds.");
                    return;
                }

                Order order = Order.PrefabList[orderIndex];
                string orderOption = orderOptionIndex < 0 || orderOptionIndex >= order.Options.Length ? "" : order.Options[orderOptionIndex];
                orderMsg = new OrderChatMessage(order, orderOption, orderTargetEntity, orderTargetCharacter, c.Character);
                txt = orderMsg.Text;
            }
            else
            {
                txt = msg.ReadString() ?? "";
            }

            if (!NetIdUtils.IdMoreRecent(ID, c.LastSentChatMsgID)) return;

            c.LastSentChatMsgID = ID;

            if (txt.Length > MaxLength)
            {
                txt = txt.Substring(0, MaxLength);
            }

            c.LastSentChatMessages.Add(txt);
            if (c.LastSentChatMessages.Count > 10)
            {
                c.LastSentChatMessages.RemoveRange(0, c.LastSentChatMessages.Count - 10);
            }

            float similarity = 0.0f;
            for (int i = 0; i < c.LastSentChatMessages.Count; i++)
            {
                float closeFactor = 1.0f / (c.LastSentChatMessages.Count - i);
                if (string.IsNullOrEmpty(txt))
                {
                    similarity += closeFactor;
                }
                else
                {
                    int levenshteinDist = ToolBox.LevenshteinDistance(txt, c.LastSentChatMessages[i]);
                    similarity += Math.Max((txt.Length - levenshteinDist) / (float)txt.Length * closeFactor, 0.0f);
                }                
            }

            if (similarity + c.ChatSpamSpeed > 5.0f)
            {
                c.ChatSpamCount++;

                if (c.ChatSpamCount > 3)
                {
                    //kick for spamming too much
                    GameMain.Server.KickClient(c, TextManager.Get("SpamFilterKicked"));
                }
                else
                {
                    ChatMessage denyMsg = Create("", TextManager.Get("SpamFilterBlocked"), ChatMessageType.Server, null);
                    c.ChatSpamTimer = 10.0f;
                    GameMain.Server.SendDirectChatMessage(denyMsg, c);
                }
                return;
            }

            c.ChatSpamSpeed += similarity + 0.5f;

            if (c.ChatSpamTimer > 0.0f)
            {
                ChatMessage denyMsg = Create("", TextManager.Get("SpamFilterBlocked"), ChatMessageType.Server, null);
                c.ChatSpamTimer = 10.0f;
                GameMain.Server.SendDirectChatMessage(denyMsg, c);
                return;
            }
            
            if (type == ChatMessageType.Order)
            {
                if (c.Character.SpeechImpediment >= 100.0f || c.Character.IsDead) return;

                ChatMessageType messageType = CanUseRadio(orderMsg.Sender) ? ChatMessageType.Radio : ChatMessageType.Default;
                if (orderMsg.Order.TargetAllCharacters)
                {
#if CLIENT
                    //add the order to the crewmanager only if the host is not controlling a character 
                    //OR the character is close enough to hear it
                    if (Character.Controlled == null || 
                        !string.IsNullOrEmpty(ApplyDistanceEffect(orderMsg.Text, messageType, orderMsg.Sender, Character.Controlled)))
                    {
                        GameMain.GameSession?.CrewManager?.AddOrder(
                            new Order(orderMsg.Order.Prefab, orderTargetEntity, (orderTargetEntity as Item)?.GetComponent<ItemComponent>()),
                            orderMsg.Order.Prefab.FadeOutTime);
                    }
#endif
                }
                else if (orderTargetCharacter != null)
                {
                    orderTargetCharacter.SetOrder(
                        new Order(orderMsg.Order.Prefab, orderTargetEntity, (orderTargetEntity as Item)?.GetComponent<ItemComponent>()), 
                            orderMsg.OrderOption, orderMsg.Sender);                    
                }

                GameMain.Server.SendOrderChatMessage(orderMsg);
            }
            else
            {
                GameMain.Server.SendChatMessage(txt, null, c);
            }
        }

        public int EstimateLengthBytesClient()
        {
            int length =    1 + //(byte)ServerNetObject.CHAT_MESSAGE
                            2 + //(UInt16)NetStateID
                            Encoding.UTF8.GetBytes(Text).Length + 2;

            return length;
        }

        public int EstimateLengthBytesServer(Client c)
        {
            int length =    1 + //(byte)ServerNetObject.CHAT_MESSAGE
                            2 + //(UInt16)NetStateID
                            1 + //(byte)Type
                            Encoding.UTF8.GetBytes(Text).Length + 2;

            if (Sender != null && c.InGame)
            {
                length += 2; //sender ID (UInt16)
            }
            else if (SenderName != null)
            {
                length += Encoding.UTF8.GetBytes(SenderName).Length + 2;
            }

            return length;
        }

        public static bool CanUseRadio(Character sender)
        {
            if (sender == null) return false;
            var senderItem = sender.Inventory.Items.FirstOrDefault(i => i?.GetComponent<WifiComponent>() != null);
            return senderItem != null && sender.HasEquippedItem(senderItem) && senderItem.GetComponent<WifiComponent>().CanTransmit();
        }

        public virtual void ServerWrite(NetOutgoingMessage msg, Client c)
        {
            msg.Write((byte)ServerNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)Type);
            msg.Write(Text);

            msg.Write(SenderName);
            msg.Write(Sender != null && c.InGame);
            if (Sender != null && c.InGame)
            {
                msg.Write(Sender.ID);
            }
        }

    }
}
