using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    class ConnectionPanel : ItemComponent
    {        
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

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character != Character.Controlled || character != user) return;

            if (Screen.Selected != GameMain.EditMapScreen &&
                character.IsKeyHit(InputType.Select) && 
                character.SelectedConstruction==this.item) character.SelectedConstruction = null;

            Connection.DrawConnections(spriteBatch, this, character);
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            foreach (Connection c in Connections)
            {
                c.Save(componentElement);
            }

            return componentElement;
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
        
        public override bool FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetBuffer message)
        {
            foreach (Connection c in Connections)
            {
                Wire[] wires = Array.FindAll(c.Wires, w => w != null);
                message.WriteRangedInteger(0, Connection.MaxLinked, wires.Length);
                for (int i = 0 ; i < wires.Length; i++)
                {
                    message.Write(wires[i].Item.ID);
                }
            }

            return true;
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message, float sendingTime)
        {
            if (GameMain.Server != null)
            {
                var sender = GameMain.Server.ConnectedClients.Find(c => c.Connection == message.SenderConnection);
                if (sender != null)
                {
                    Networking.GameServer.Log(item.Name + " rewired by " + sender.name, Color.Orange);
                }
            }

            System.Diagnostics.Debug.WriteLine("connectionpanel update");
            foreach (Connection c in Connections)
            {
                //int wireCount = c.Wires.Length;
                c.ClearConnections();

                int wireCount = message.ReadRangedInteger(0, Connection.MaxLinked);                

                for (int i = 0; i < wireCount; i++)
                {
                    ushort wireId = message.ReadUInt16();
                        
                    Item wireItem = MapEntity.FindEntityByID(wireId) as Item;
                    if (wireItem == null) continue;

                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent == null) continue;

                    c.Wires[i] = wireComponent;
                    wireComponent.Connect(c, false);

                    var otherConnection = c.Wires[i].OtherConnection(c);
                    Networking.GameServer.Log(
                        item.Name+" ("+ c.Name + ") -> " + 
                        (otherConnection == null ? "none" : otherConnection.Item.Name+" ("+(otherConnection.Name)+")"), Color.Orange);
                }
                c.UpdateRecipients();
            } 
        }
    }
}
