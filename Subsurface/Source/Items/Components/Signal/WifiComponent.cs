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

        [HasDefaultValue(20000.0f, false)]
        public float Range
        {
            get { return range; }
            set { range = Math.Max(value, 0.0f); }
        }

        [InGameEditable, HasDefaultValue(1, true)]
        public int Channel
        {
            get { return channel; }
            set
            {
                channel = MathHelper.Clamp(value, 0, 10000);
            }
        }

        [Editable, HasDefaultValue(false, false)]
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

        public void Transmit(string signal)
        {
            if (!HasRequiredContainedItems(true)) return;

            var receivers = GetReceiversInRange();
            foreach (WifiComponent w in receivers)
            {
                var connections = w.item.Connections;

                w.ReceiveSignal(1, signal, connections == null ? null : connections.Find(c => c.Name == "signal_in"), item);
            }
        }

        private List<WifiComponent> GetReceiversInRange()
        {
            return list.FindAll(w => 
                w != this && w.channel == channel && 
                Vector2.Distance(item.WorldPosition, w.item.WorldPosition) <= Range);
        }
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            if (!HasRequiredContainedItems(false)) return;

            if (LinkToChat)
            {
                if (item.ParentInventory != null && 
                    item.ParentInventory.Owner != null && 
                    item.ParentInventory.Owner == Character.Controlled &&
                    GameMain.NetworkMember != null)
                {
                    signal = ChatMessage.ApplyDistanceEffect(item, sender, signal, range);

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
                        wifiComp.item.SendSignal(stepsTaken, signal, "signal_out");
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
