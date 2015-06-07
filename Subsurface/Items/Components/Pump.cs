using System.Collections.Specialized;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Pump : Powered
    {
        float flow;
        float maxFlow;

        bool flowIn;

        Hull hull1, hull2;

        [HasDefaultValue(100.0f, false)]
        public float MaxFlow
        {
            get { return maxFlow; }
            set { maxFlow = value; } 
        }

        public Pump(Item item, XElement element)
            : base(item, element)
        {
            //maxFlow = ToolBox.GetAttributeFloat(element, "maxflow", 100.0f);

            item.linkedTo.CollectionChanged += delegate(object sender, NotifyCollectionChangedEventArgs e)
            { GetHulls(); };
        }

        public override void Update(float deltaTime, Camera cam)
        {
            currPowerConsumption = powerConsumption;

            if (voltage < minVoltage) return;

            if (hull2 == null && hull1 == null) return;
            
            float powerFactor = (currPowerConsumption==0.0f) ? 1.0f : voltage;
            flow = maxFlow * powerFactor;

            float deltaVolume = flow * ((flowIn) ? 1.0f : -1.0f);
            hull1.Volume += deltaVolume;
            if (hull2 != null) hull2.Volume -= deltaVolume; 

            voltage = 0.0f;
        }

        //public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        //{
        //    int width = 300, height = 200;
        //    int x = Game1.GraphicsWidth / 2 - width / 2;
        //    int y = Game1.GraphicsHeight / 2 - height / 2;

        //    GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

        //    spriteBatch.DrawString(GUI.font, "Pumping direction: " + ((flowIn) ? "in" : "out"), new Vector2(x + 30, y + 30), Color.White);
        //    if (GUI.DrawButton(spriteBatch, new Rectangle(x + 30, y + 50, 80, 40), "TOGGLE")) flowIn = !flowIn;

        //    if (GUI.DrawButton(spriteBatch, new Rectangle(x + 30, y + 150, 100, 40), (isActive) ? "TURN OFF" : "TURN ON")) IsActive = !isActive;

        //}

        //public override bool Pick(Character activator = null)
        //{
        //    //isActive = !isActive;

        //    hull1 = null;
        //    hull2 = null;

        //    foreach (MapEntity e in item.linkedTo)
        //    {
        //        Hull hull = e as Hull;
        //        if (hull == null) continue;

        //        if (hull1 == null)
        //        {
        //            hull1 = hull;
        //        }
        //        else if (hull2 == null && hull != hull1)
        //        {
        //            hull2 = hull;
        //            break;
        //        }  
        //    }

        //    return true;
        //}

        private void GetHulls()
        {
            hull1 = null;
            hull2 = null;

            foreach (MapEntity e in item.linkedTo)
            {
                Hull hull = e as Hull;
                if (hull == null) continue;

                if (hull1 == null)
                {
                    hull1 = hull;
                }
                else if (hull2 == null && hull != hull1)
                {
                    hull2 = hull;
                    break;
                }
            }
        }

        //public override void OnMapLoaded()
        //{
        //    hull1 = null;
        //    hull2 = null;

        //    foreach (MapEntity e in item.linkedTo)
        //    {
        //        Hull hull = e as Hull;
        //        if (hull == null) continue;

        //        if (hull1 == null)
        //        {
        //            hull1 = hull;
        //        }
        //        else if (hull2 == null && hull != hull1)
        //        {
        //            hull2 = hull;
        //            break;
        //        }
        //    }
        //}

        public override void ReceiveSignal(string signal, Connection connection, Item sender)
        {
            base.ReceiveSignal(signal, connection, sender);

            if (connection.name == "toggle")
            {
                isActive = !isActive;
            }
            else if (connection.name == "set_state")
            {
                isActive = (signal != "0");
            }
        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            message.Write(flowIn);
            message.Write(isActive);
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            flowIn = message.ReadBoolean();
            isActive = message.ReadBoolean();
        }
    }
}
