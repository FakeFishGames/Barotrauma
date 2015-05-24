using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class ConnectionPanel : ItemComponent
    {
        
        public List<Connection> connections;

        Character user;

        public ConnectionPanel(Item item, XElement element)
            : base(item, element)
        {
            connections = new List<Connection>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":                        
                        connections.Add(new Connection(subElement, item));
                        break;
                    case "output":
                        connections.Add(new Connection(subElement, item));
                        break;
                }
            }
        }

        public override void DrawHUD(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Character character)
        {
            if (user!=character) return;
            Connection.DrawConnections(spriteBatch, this, character);
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            foreach (Connection c in connections)
            {
                //XElement newElement = new XElement(c.isOutput ? "output" : "input", new XAttribute("name", c.name));

                c.Save(componentElement);
            }

            return componentElement;
        }

        public override void OnMapLoaded()
        {
            foreach (Connection c in connections)
            {
                c.ConnectLinked();
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (user != null && user.SelectedConstruction != item) user = null;
        }

        public override bool Pick(Character picker)
        {
            user = picker;
            isActive = true;
            return true;
        }

        public override void Load(XElement element)
        {
            base.Load(element);

            connections.Clear();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":
                        connections.Add(new Connection(subElement, item));
                        break;
                    case "output":
                        connections.Add(new Connection(subElement, item));
                        break;
                }
            }
        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            foreach (Connection c in connections)
            {
                int wireCount = c.wires.Length;                
                for (int i = 0 ; i < wireCount; i++)
                {
                    message.Write(c.wires[i]==null ? -1 : c.wires[i].Item.ID);
                }
            }
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            System.Diagnostics.Debug.WriteLine("connectionpanel update");
            foreach (Connection c in connections)
            {
                int wireCount = c.wires.Length;
                c.ClearConnections();

                for (int i = 0; i < wireCount; i++)
                {
                    int wireId = message.ReadInt32();
                    if (wireId == -1) continue;

                    Item wireItem = MapEntity.FindEntityByID(wireId) as Item;
                    if (wireItem == null) continue;

                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent == null) continue;

                    c.wires[i] = wireComponent;
                    wireComponent.Connect(c, false);
                }
            } 
        }
    }
}
