using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WifiComponent : ItemComponent
    {
        private static List<WifiComponent> list = new List<WifiComponent>();

        private float range;

        private int channel;

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

        [Editable(ToolTip = "If enabled, any signals received by the item are displayed as chat messages in the chatbox of the player holding the item."), Serialize(false, false)]
        public bool LinkToChat
        {
            get;
            set;
        }

        public WifiComponent(Item item, XElement element)
            : base (item, element)
        {

            list.Add(this);
        }

        public bool CanTransmit()
        {
            return HasRequiredContainedItems(true);
        }
        
        private List<WifiComponent> GetReceiversInRange()
        {
            return list.FindAll(w => w != this && w.CanReceive(this));
        }

        public bool CanReceive(WifiComponent sender)
        {
            if (!HasRequiredContainedItems(false)) return false;

            if (sender == null || sender.channel != channel) return false;

            return Vector2.Distance(item.WorldPosition, sender.item.WorldPosition) <= sender.Range;
        }
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item source, Character sender, float power=0.0f)
        {
            var senderComponent = source.GetComponent<WifiComponent>();
            if (senderComponent != null && !CanReceive(senderComponent)) return;

            if (LinkToChat)
            {
                if (item.ParentInventory != null && 
                    item.ParentInventory.Owner != null && 
                    item.ParentInventory.Owner == Character.Controlled &&
                    GameMain.NetworkMember != null)
                {
                    if (senderComponent != null)
                    {
                        signal = ChatMessage.ApplyDistanceEffect(item, sender, signal, senderComponent.range);
                    }

                    GameMain.NetworkMember.AddChatMessage(signal, ChatMessageType.Radio);
                }
            }

            if (connection == null) return;

            switch (connection.Name)
            {
                case "signal_in":
                    var receivers = GetReceiversInRange();

                    foreach (WifiComponent wifiComp in receivers)
                    {
                        wifiComp.item.SendSignal(stepsTaken, signal, "signal_out", sender);
                    }
                    break;
            }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();

            list.Remove(this);
        }
    }
}
