using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static Wire HighlightedWire;

        public List<Connection> Connections;

        Character user;

        public ConnectionPanel(Item item, XElement element)
            : base(item, element)
        {
            Connections = new List<Connection>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":                        
                        Connections.Add(new Connection(subElement, item));
                        break;
                    case "output":
                        Connections.Add(new Connection(subElement, item));
                        break;
                }
            }

            IsActive = true;
        }

        public override void OnMapLoaded()
        {
            foreach (Connection c in Connections)
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
            //attaching wires to items with a body is not allowed
            //(signal items remove their bodies when attached to a wall)
            if (item.body != null)
            {
                return false;
            }

            user = picker;
            IsActive = true;
            return true;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || character!=user) return false;

            var powered = item.GetComponent<Powered>();
            if (powered != null)
            {
                if (powered.Voltage < 0.1f) return false;
            }

            float degreeOfSuccess = DegreeOfSuccess(character);
            if (Rand.Range(0.0f, 50.0f) < degreeOfSuccess) return false;

            character.SetStun(5.0f);

            item.ApplyStatusEffects(ActionType.OnFailure, 1.0f, character);

            return true;
        }

        public override void Load(XElement element)
        {
            base.Load(element);
                        
            List<Connection> loadedConnections = new List<Connection>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":
                        loadedConnections.Add(new Connection(subElement, item));
                        break;
                    case "output":
                        loadedConnections.Add(new Connection(subElement, item));
                        break;
                }
            }
            
            for (int i = 0; i<loadedConnections.Count && i<Connections.Count; i++)
            {
                loadedConnections[i].wireId.CopyTo(Connections[i].wireId, 0);
            }
        }

        protected override void RemoveComponentSpecific()
        {
            foreach (Connection c in Connections)
            {
                foreach (Wire wire in c.Wires)
                {
                    if (wire == null) continue;

                    if (wire.OtherConnection(c) == null) //wire not connected to anything else
                    {
                        wire.Item.Drop(null);
                    }
                    else
                    {
                        wire.RemoveConnection(item);
                    }
                }
            }
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            foreach (Connection connection in Connections)
            {
                Wire[] wires = Array.FindAll(connection.Wires, w => w != null);
                msg.WriteRangedInteger(0, Connection.MaxLinked, wires.Length);
                for (int i = 0; i < wires.Length; i++)
                {
                    msg.Write(wires[i].Item.ID);
                }
            }
        }

        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            int[] wireCounts = new int[Connections.Count];
            Wire[,] wires = new Wire[Connections.Count, Connection.MaxLinked];

            //read wire IDs for each connection
            for (int i = 0; i < Connections.Count; i++)
            {
                wireCounts[i] = msg.ReadRangedInteger(0, Connection.MaxLinked);
                for (int j = 0; j < wireCounts[i]; j++)
                {
                    ushort wireId = msg.ReadUInt16();

                    Item wireItem = MapEntity.FindEntityByID(wireId) as Item;
                    if (wireItem == null) continue;

                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent != null)
                    {
                        wires[i, j] = wireComponent;
                    }
                }
            }

            item.CreateServerEvent<ConnectionPanel>(this);

            //check if the character can access this connectionpanel 
            //and all the wires they're trying to connect
            if (!item.CanClientAccess(c)) return;
            foreach (Wire wire in wires)
            {
                if (wire != null)
                {
                    //wire not found in any of the connections yet (client is trying to connect a new wire)
                    //  -> we need to check if the client has access to it
                    if (!Connections.Any(connection => connection.Wires.Contains(wire)))
                    {
                        if (!wire.Item.CanClientAccess(c)) return;
                    }
                }
            }

            Networking.GameServer.Log(item.Name + " rewired by " + c.Character.Name, ServerLog.MessageType.ItemInteraction);

            //update the connections
            for (int i = 0; i < Connections.Count; i++)
            {
                Connections[i].ClearConnections();

                for (int j = 0; j < wireCounts[i]; j++)
                {
                    if (wires[i, j] == null) continue;

                    Connections[i].Wires[j] = wires[i,j];
                    wires[i, j].Connect(Connections[i], true);

                    var otherConnection = Connections[i].Wires[j].OtherConnection(Connections[i]);

                    Networking.GameServer.Log(
                        item.Name + " (" + Connections[i].Name + ") -> " +
                        (otherConnection == null ? "none" : otherConnection.Item.Name + " (" + (otherConnection.Name) + ")"), ServerLog.MessageType.ItemInteraction);
                }
            }
            
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            ClientWrite(msg, extraData);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            foreach (Connection connection in Connections)
            {
                connection.ClearConnections();
                int wireCount = msg.ReadRangedInteger(0, Connection.MaxLinked);
                for (int i = 0; i < wireCount; i++)
                {
                    ushort wireId = msg.ReadUInt16();

                    Item wireItem = MapEntity.FindEntityByID(wireId) as Item;
                    if (wireItem == null) continue;

                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent == null) continue;

                    connection.Wires[i] = wireComponent;
                    wireComponent.Connect(connection, false);
                }
            }
        }        
    }
}
