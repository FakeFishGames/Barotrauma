using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        public List<Connection> Connections;

        private Character user;

        /// <summary>
        /// Wires that have been disconnected from the panel, but not removed completely (visible at the bottom of the connection panel).
        /// </summary>
        public readonly HashSet<Wire> DisconnectedWires = new HashSet<Wire>();

        private List<ushort> disconnectedWireIds;

        [Serialize(false, true), Editable(ToolTip = "Locked connection panels cannot be rewired in-game.")]
        public bool Locked
        {
            get;
            set;
        }

        //connection panels can't be deactivated
        public override bool IsActive
        {
            get { return true; }
            set { /*do nothing*/ }
        }

        public ConnectionPanel(Item item, XElement element)
            : base(item, element)
        {
            Connections = new List<Connection>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":                        
                        Connections.Add(new Connection(subElement, this));
                        break;
                    case "output":
                        Connections.Add(new Connection(subElement, this));
                        break;
                }
            }

            IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void OnMapLoaded()
        {
            foreach (Connection c in Connections)
            {
                c.ConnectLinked();
            }

            if (disconnectedWireIds != null)
            {
                foreach (ushort disconnectedWireId in disconnectedWireIds)
                {
                    if (!(Entity.FindEntityByID(disconnectedWireId) is Item wireItem)) { continue; }
                    Wire wire = wireItem.GetComponent<Wire>();
                    if (wire != null)
                    {
                        DisconnectedWires.Add(wire);
                    }
                }
            }
        }

        public override void OnItemLoaded()
        {
            if (item.body != null)
            {
                var holdable = item.GetComponent<Holdable>();
                if (holdable == null || !holdable.Attachable)
                {
                    DebugConsole.ThrowError("Item \"" + item.Name + "\" has a ConnectionPanel component," +
                        " but cannot be wired because it has an active physics body that cannot be attached to a wall." +
                        " Remove the physics body or add a Holdable component with the Attachable attribute set to true.");
                }
            }
        }

        public void MoveConnectedWires(Vector2 amount)
        {
            Vector2 wireNodeOffset = item.Submarine == null ? Vector2.Zero : item.Submarine.HiddenSubPosition + amount;
            foreach (Connection c in Connections)
            {
                foreach (Wire wire in c.Wires)
                {
                    if (wire == null) continue;
#if CLIENT
                    if (wire.Item.IsSelected) continue;
#endif
                    var wireNodes = wire.GetNodes();
                    if (wireNodes.Count == 0) continue;

                    if (Submarine.RectContains(item.Rect, wireNodes[0] + wireNodeOffset))
                    {
                        wire.MoveNode(0, amount);
                    }
                    else if (Submarine.RectContains(item.Rect, wireNodes[wireNodes.Count - 1] + wireNodeOffset))
                    {
                        wire.MoveNode(wireNodes.Count - 1, amount);
                    }
                }
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            UpdateProjSpecific(deltaTime);

            if (user == null || user.SelectedConstruction != item)
            {
                user = null;
                return;
            }

            if (!user.Enabled || !HasRequiredItems(user, addMessage: false)) { return; }

            user.AnimController.UpdateUseItem(true, item.WorldPosition + new Vector2(0.0f, 100.0f) * (((float)Timing.TotalTime / 10.0f) % 0.1f));
        }

        partial void UpdateProjSpecific(float deltaTime);

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
            if (character == null || character != user) return false;

            var powered = item.GetComponent<Powered>();
            if (powered != null)
            {
                if (powered.Voltage < 0.1f) return false;
            }

            float degreeOfSuccess = DegreeOfSuccess(character);
            if (Rand.Range(0.0f, 0.5f) < degreeOfSuccess) return false;

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
                        loadedConnections.Add(new Connection(subElement, this));
                        break;
                    case "output":
                        loadedConnections.Add(new Connection(subElement, this));
                        break;
                }
            }

            for (int i = 0; i < loadedConnections.Count && i < Connections.Count; i++)
            {
                loadedConnections[i].wireId.CopyTo(Connections[i].wireId, 0);
            }

            disconnectedWireIds = element.GetAttributeUshortArray("disconnectedwires", new ushort[0]).ToList();
        }

        public override XElement Save(XElement parentElement)
        {
            XElement componentElement = base.Save(parentElement);

            foreach (Connection c in Connections)
            {
                c.Save(componentElement);
            }

            if (DisconnectedWires.Count > 0)
            {
                componentElement.Add(new XAttribute("disconnectedwires", string.Join(",", DisconnectedWires.Select(w => w.Item.ID))));
            }

            return componentElement;
        }

        protected override void ShallowRemoveComponentSpecific()
        {
            //do nothing
        }

        protected override void RemoveComponentSpecific()
        {
            foreach (Wire wire in DisconnectedWires.ToList())
            {
                if (wire.OtherConnection(null) == null) //wire not connected to anything else
                {
                    wire.Item.Drop(null);
                }
            }

            DisconnectedWires.Clear();
            foreach (Connection c in Connections)
            {
                foreach (Wire wire in c.Wires)
                {
                    if (wire == null) { continue; }

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

#if CLIENT
            rewireSoundChannel?.FadeOutAndDispose();
            rewireSoundChannel = null;
#endif
        }


        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            foreach (Connection connection in Connections)
            {
                foreach (Wire wire in connection.Wires)
                {
                    msg.Write(wire?.Item == null ? (ushort)0 : wire.Item.ID);
                }
            }

            msg.Write((ushort)DisconnectedWires.Count());
            foreach (Wire disconnectedWire in DisconnectedWires)
            {
                msg.Write(disconnectedWire.Item.ID);
            }
        }
    }
}
