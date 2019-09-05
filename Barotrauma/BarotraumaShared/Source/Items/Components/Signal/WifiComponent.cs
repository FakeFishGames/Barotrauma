using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent : ItemComponent
    {
        private static List<WifiComponent> list = new List<WifiComponent>();

        private float range;

        private int channel;
        
        private float chatMsgCooldown;

        private string prevSignal;

        [Serialize(Character.TeamType.None, false)]
        public Character.TeamType TeamID { get; set; }

        [Serialize(20000.0f, false)]
        public float Range
        {
            get { return range; }
            set { range = Math.Max(value, 0.0f); }
        }

        [InGameEditable, Serialize(1, true)]
        public int Channel
        {
            get { return channel; }
            set
            {
                channel = MathHelper.Clamp(value, 0, 10000);
            }
        }

        [Editable, Serialize(false, false, description: "If enabled, any signals received from another chat-linked wifi component are displayed " +
            "as chat messages in the chatbox of the player holding the item.")]
        public bool LinkToChat
        {
            get;
            set;
        }

        [Editable, Serialize(1.0f, true, description: "How many seconds have to pass between signals for a message to be displayed in the chatbox. " +
            "Setting this to a very low value is not recommended, because it may cause an excessive amount of chat messages to be created " +
            "if there are chat-linked wifi components that transmit a continuous signal.")]
        public float MinChatMessageInterval
        {
            get;
            set;
        }

        [Editable, Serialize(false, true, description: "If set to true, the component will only create chat messages when the received signal changes.")]
        public bool DiscardDuplicateChatMessages
        {
            get;
            set;
        }

        public WifiComponent(Item item, XElement element)
            : base (item, element)
        {
            list.Add(this);
            IsActive = true;
        }

        public bool CanTransmit()
        {
            return HasRequiredContainedItems(user: null, addMessage: false);
        }
        
        public IEnumerable<WifiComponent> GetReceiversInRange()
        {
            return list.Where(w => w != this && w.CanReceive(this));
        }

        public bool CanReceive(WifiComponent sender)
        {
            if (sender == null || sender.channel != channel) { return false; }
            if (sender.TeamID == Character.TeamType.Team1 && TeamID == Character.TeamType.Team2) { return false; }
            if (sender.TeamID == Character.TeamType.Team2 && TeamID == Character.TeamType.Team1) { return false; }

            if (Vector2.DistanceSquared(item.WorldPosition, sender.item.WorldPosition) > sender.range * sender.range) { return false; }

            return HasRequiredContainedItems(user: null, addMessage: false);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            chatMsgCooldown -= deltaTime;
        }

        public void TransmitSignal(int stepsTaken, string signal, Item source, Character sender, bool sendToChat, float signalStrength = 1.0f)
        {
            var senderComponent = source?.GetComponent<WifiComponent>();
            if (senderComponent != null && !CanReceive(senderComponent)) return;

            bool chatMsgSent = false;

            var receivers = GetReceiversInRange();
            foreach (WifiComponent wifiComp in receivers)
            {
                //signal strength diminishes by distance
                float sentSignalStrength = signalStrength *
                    MathHelper.Clamp(1.0f - (Vector2.Distance(item.WorldPosition, wifiComp.item.WorldPosition) / wifiComp.range), 0.0f, 1.0f);
                wifiComp.item.SendSignal(stepsTaken, signal, "signal_out", sender, 0, source, sentSignalStrength);
                
                if (source != null)
                {
                    foreach (Item receiverItem in wifiComp.item.LastSentSignalRecipients)
                    {
                        if (!source.LastSentSignalRecipients.Contains(receiverItem))
                        {
                            source.LastSentSignalRecipients.Add(receiverItem);
                        }
                    }
                }                

                if (DiscardDuplicateChatMessages && signal == prevSignal) continue;

                if (LinkToChat && wifiComp.LinkToChat && chatMsgCooldown <= 0.0f && sendToChat)
                {
                    if (wifiComp.item.ParentInventory != null &&
                        wifiComp.item.ParentInventory.Owner != null &&
                        GameMain.NetworkMember != null)
                    {
                        string chatMsg = signal;
                        if (senderComponent != null)
                        {
                            chatMsg = ChatMessage.ApplyDistanceEffect(chatMsg, 1.0f - sentSignalStrength);
                        }
                        if (chatMsg.Length > ChatMessage.MaxLength) chatMsg = chatMsg.Substring(0, ChatMessage.MaxLength);
                        if (string.IsNullOrEmpty(chatMsg)) continue;

#if CLIENT
                        if (wifiComp.item.ParentInventory.Owner == Character.Controlled)
                        {
                            if (GameMain.Client == null)
                                GameMain.NetworkMember.AddChatMessage(signal, ChatMessageType.Radio, source == null ? "" : source.Name);
                        }
#endif

#if SERVER
                        if (GameMain.Server != null)
                        {
                            Client recipientClient = GameMain.Server.ConnectedClients.Find(c => c.Character == wifiComp.item.ParentInventory.Owner);
                            if (recipientClient != null)
                            {
                                GameMain.Server.SendDirectChatMessage(
                                    ChatMessage.Create(source == null ? "" : source.Name, chatMsg, ChatMessageType.Radio, null), recipientClient);
                            }
                        }
#endif
                        chatMsgSent = true;
                    }
                }
            }
            if (chatMsgSent) chatMsgCooldown = MinChatMessageInterval;

            prevSignal = signal;
        }
                
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power = 0.0f, float signalStrength = 1.0f)
        {
            if (connection == null || connection.Name != "signal_in") return;
            TransmitSignal(stepsTaken, signal, source, sender, true, signalStrength);
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            list.Remove(this);
        }
    }
}
