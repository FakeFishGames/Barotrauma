using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class WifiComponent : ItemComponent
    {
        private static readonly List<WifiComponent> list = new List<WifiComponent>();

        const int ChannelMemorySize = 10;

        private float range;

        private int channel;
        
        private float chatMsgCooldown;

        private string prevSignal;

        private int[] channelMemory = new int[ChannelMemorySize];

        [Serialize(Character.TeamType.None, true, description: "WiFi components can only communicate with components that have the same Team ID.", alwaysUseInstanceValues: true)]
        public Character.TeamType TeamID { get; set; }

        [Editable, Serialize(20000.0f, false, description: "How close the recipient has to be to receive a signal from this WiFi component.", alwaysUseInstanceValues: true)]
        public float Range
        {
            get { return range; }
            set
            {
                range = Math.Max(value, 0.0f);
#if CLIENT
                item.ResetCachedVisibleSize();
#endif
            }
        }

        [InGameEditable, Serialize(0, true, description: "WiFi components can only communicate with components that use the same channel.", alwaysUseInstanceValues: true)]
        public int Channel
        {
            get { return channel; }
            set
            {
                channel = MathHelper.Clamp(value, 0, 10000);
            }
        }


        [Editable, Serialize(false, true, description: "Can the component communicate with wifi components in another team's submarine (e.g. enemy sub in Combat missions, respawn shuttle). Needs to be enabled on both the component transmitting the signal and the component receiving it.", alwaysUseInstanceValues: true)]
        public bool AllowCrossTeamCommunication
        {
            get;
            set;
        }

        [Editable, Serialize(false, false, description: "If enabled, any signals received from another chat-linked wifi component are displayed " +
            "as chat messages in the chatbox of the player holding the item.", alwaysUseInstanceValues: true)]
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
            channelMemory = element.GetAttributeIntArray("channelmemory", new int[ChannelMemorySize]);
        }

        public override void OnItemLoaded()
        {
            if (channelMemory.All(m => m == 0))
            {
                for (int i = 0; i < channelMemory.Length; i++)
                {
                    channelMemory[i] = i;
                }
            }
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

            if (sender.TeamID != TeamID && !AllowCrossTeamCommunication)
            {
                return false;
            }            

            if (Vector2.DistanceSquared(item.WorldPosition, sender.item.WorldPosition) > sender.range * sender.range) { return false; }

            return HasRequiredContainedItems(user: null, addMessage: false);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            chatMsgCooldown -= deltaTime;
            if (chatMsgCooldown <= 0.0f)
            {
                IsActive = false;
            }
        }

        public int GetChannelMemory(int index)
        {
            if (index < 0 || index >= ChannelMemorySize)
            {
                return 0;
            }
            return channelMemory[index];
        }

        public void SetChannelMemory(int index, int value)
        {
            if (index < 0 || index >= ChannelMemorySize)
            {
                return;
            }
            channelMemory[index] = MathHelper.Clamp(value, 0, 10000);
        }

        public void TransmitSignal([NotNull] Signal signal, bool sentFromChat)
        {
            var senderComponent = signal.source?.GetComponent<WifiComponent>();
            if (senderComponent != null && !CanReceive(senderComponent)) { return; }

            bool chatMsgSent = false;

            var receivers = GetReceiversInRange();
            foreach (WifiComponent wifiComp in receivers)
            {
                if (sentFromChat && !wifiComp.LinkToChat) { continue; }

                //signal strength diminishes by distance
                float sentSignalStrength = signal.strength *
                    MathHelper.Clamp(1.0f - (Vector2.Distance(item.WorldPosition, wifiComp.item.WorldPosition) / wifiComp.range), 0.0f, 1.0f);
                Signal s = new Signal(signal.value, signal.stepsTaken, sender: signal.sender, source: signal.source,
                                      power: 0.0f, strength: sentSignalStrength);
                wifiComp.item.SendSignal(s, "signal_out");
                
                if (signal.source != null)
                {
                    foreach (Item receiverItem in wifiComp.item.LastSentSignalRecipients)
                    {
                        if (!signal.source.LastSentSignalRecipients.Contains(receiverItem))
                        {
                            signal.source.LastSentSignalRecipients.Add(receiverItem);
                        }
                    }
                }

                if (DiscardDuplicateChatMessages && signal.value == prevSignal) { continue; }

                //create a chat message
                if (LinkToChat && wifiComp.LinkToChat && chatMsgCooldown <= 0.0f && !sentFromChat)
                {
                    if (wifiComp.item.ParentInventory != null &&
                        wifiComp.item.ParentInventory.Owner != null)
                    {
                        string chatMsg = signal.value;
                        if (senderComponent != null)
                        {
                            chatMsg = ChatMessage.ApplyDistanceEffect(chatMsg, 1.0f - sentSignalStrength);
                        }
                        if (chatMsg.Length > ChatMessage.MaxLength) { chatMsg = chatMsg.Substring(0, ChatMessage.MaxLength); }
                        if (string.IsNullOrEmpty(chatMsg)) { continue; }

#if CLIENT
                        if (wifiComp.item.ParentInventory.Owner == Character.Controlled)
                        {
                            if (GameMain.Client == null)
                            {
                                GameMain.GameSession?.CrewManager?.AddSinglePlayerChatMessage(signal.source?.Name ?? "", signal.value, ChatMessageType.Radio, sender: null);
                            }
                        }
#elif SERVER
                        if (GameMain.Server != null)
                        {
                            Client recipientClient = GameMain.Server.ConnectedClients.Find(c => c.Character == wifiComp.item.ParentInventory.Owner);
                            if (recipientClient != null)
                            {
                                GameMain.Server.SendDirectChatMessage(
                                    ChatMessage.Create(signal.source?.Name ?? "", chatMsg, ChatMessageType.Radio, null), recipientClient);
                            }
                        }
#endif
                        chatMsgSent = true;
                    }
                }
            }
            if (chatMsgSent) 
            { 
                chatMsgCooldown = MinChatMessageInterval;
                IsActive = true;
            }

            prevSignal = signal.value;
        }
                
        public override void ReceiveSignal([NotNull] Signal signal)
        {
            if (signal.connection == null) { return; }

            switch (signal.connection.Name)
            {
                case "signal_in":
                    TransmitSignal(signal, false);
                    break;
                case "set_channel":
                    if (int.TryParse(signal.value, out int newChannel))
                    {
                        Channel = newChannel;
                    }
                    break;
                case "set_range":
                    if (float.TryParse(signal.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float newRange))
                    {
                        Range = newRange;
                    }
                    break;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            list.Remove(this);
        }

        public override XElement Save(XElement parentElement)
        {
            var element = base.Save(parentElement);
            element.Add(new XAttribute("channelmemory", string.Join(',', channelMemory)));
            return element;
        }
    }
}
