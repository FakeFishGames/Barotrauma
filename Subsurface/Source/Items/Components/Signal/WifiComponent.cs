using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class WifiComponent : ItemComponent
    {

        private static List<WifiComponent> list = new List<WifiComponent>();

        private int channel;

        [InGameEditable, HasDefaultValue(1, true)]
        public int Channel
        {
            get { return channel; }
            set
            {
                channel = MathHelper.Clamp(value, 0, 100);
            }
        }

        public WifiComponent(Item item, XElement element)
            : base (item, element)
        {

            list.Add(this);
        }
        
        public override void ReceiveSignal(int stepsTaken, string signal, Connection connection, Item sender, float power=0.0f)
        {
            //prevent an ininite loop of wificomponents sending messages between each other
            if (sender.GetComponent<WifiComponent>()!=null) return;

            switch (connection.Name)
            {
                case "signal_in":
                    foreach (WifiComponent wifiComp in list)
                    {
                        if (wifiComp == this || wifiComp.channel != channel) continue;
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
