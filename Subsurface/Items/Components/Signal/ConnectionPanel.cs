using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
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

            isActive = true;
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character != Character.Controlled || character != user) return;
            Connection.DrawConnections(spriteBatch, this, character);
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            foreach (Connection c in connections)
            {
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

        public override bool Select(Character picker)
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
                Wire[] wires = Array.FindAll(c.Wires, w => w != null);
                message.Write((byte)wires.Length);
                for (int i = 0 ; i < c.Wires.Length; i++)
                {
                    if (c.Wires[i] == null) continue;
                    message.Write(c.Wires[i].Item.ID);
                }
            }
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            System.Diagnostics.Debug.WriteLine("connectionpanel update");
            foreach (Connection c in connections)
            {
                //int wireCount = c.Wires.Length;
                c.ClearConnections();
                try
                {
                    byte wireCount = message.ReadByte();                

                    for (int i = 0; i < wireCount; i++)
                    {
                        int wireId = message.ReadInt32();
                        
                        Item wireItem = MapEntity.FindEntityByID(wireId) as Item;
                        if (wireItem == null) continue;

                        Wire wireComponent = wireItem.GetComponent<Wire>();
                        if (wireComponent == null) continue;

                        c.Wires[i] = wireComponent;
                        wireComponent.Connect(c, false);
                    }
                }

                catch { }
            } 
        }
    }
}
