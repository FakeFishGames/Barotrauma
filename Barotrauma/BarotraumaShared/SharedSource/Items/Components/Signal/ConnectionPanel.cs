using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        /// <summary>
        /// Allows rewiring the connection panel despite rewiring being disabled on a server
        /// </summary>
        public bool AlwaysAllowRewiring
        {
            get { return item.Submarine?.Info.Type == SubmarineType.BeaconStation; }
        }

        [Editable, Serialize(false, true, description: "Locked connection panels cannot be rewired in-game.", alwaysUseInstanceValues: true)]
        public bool Locked
        {
            get;
            set;
        }

        //connection panels can't be deactivated externally (by signals or status effects)
        public override bool IsActive
        {
            get { return base.IsActive; }
            set { /*do nothing*/ }
        }

        public Character User
        {
            get { return user; }
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
                        Connections.Add(new Connection(subElement, this, IdRemap.DiscardId));
                        break;
                    case "output":
                        Connections.Add(new Connection(subElement, this, IdRemap.DiscardId));
                        break;
                }
            }

            base.IsActive = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        private bool linksInitialized;
        public override void OnMapLoaded()
        {
            if (linksInitialized) { return; }
            InitializeLinks();
        }

        public void InitializeLinks()
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
                        if (Item.ItemList.Any(it => it != item && (it.GetComponent<ConnectionPanel>()?.DisconnectedWires.Contains(wire) ?? false)))
                        {
                            if (wire.Item.body != null) { wire.Item.body.Enabled = false; }
                            wire.IsActive = false;
                            wire.UpdateSections();
                        }
                        DisconnectedWires.Add(wire);
                        base.IsActive = true;
                    }
                }
            }

            linksInitialized = true;
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
#if SERVER
                if (user != null) { item.CreateServerEvent(this); }
#endif
                user = null;
                if (DisconnectedWires.Count == 0) { base.IsActive = false; }
                return;
            }

            if (!user.Enabled || !HasRequiredItems(user, addMessage: false)) 
            { 
                user = null; 
                base.IsActive = false; 
                return; 
            }

            user.AnimController.UpdateUseItem(true, item.WorldPosition + new Vector2(0.0f, 100.0f) * (((float)Timing.TotalTime / 10.0f) % 0.1f));
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
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
#if SERVER
            if (user != null) { item.CreateServerEvent(this); }
#endif
            base.IsActive = true;
            return true;
        }
        
        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || character != user) { return false; }
            return true;
        }

        /// <summary>
        /// Check if the character manages to succesfully rewire the panel, and if not, apply OnFailure effects
        /// </summary>
        public bool CheckCharacterSuccess(Character character)
        {
            if (character == null) { return false; }
            //no electrocution in sub editor
            if (Screen.Selected == GameMain.SubEditorScreen) { return true; }

            var powered = item.GetComponent<Powered>();
            if (powered != null)
            {
                //unpowered panels can be rewired without a risk of electrical shock
                if (powered.Voltage < 0.1f) { return true; }
            }

            float degreeOfSuccess = DegreeOfSuccess(character);
            if (Rand.Range(0.0f, 0.5f) < degreeOfSuccess) { return true; }

            ApplyStatusEffects(ActionType.OnFailure, 1.0f, character);
            return false;
        }

        public override void Load(XElement element, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(element, usePrefabValues, idRemap);

            List<Connection> loadedConnections = new List<Connection>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "input":
                        loadedConnections.Add(new Connection(subElement, this, idRemap));
                        break;
                    case "output":
                        loadedConnections.Add(new Connection(subElement, this, idRemap));
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
            base.RemoveComponentSpecific();
            foreach (Wire wire in DisconnectedWires.ToList())
            {
                if (wire.OtherConnection(null) == null) //wire not connected to anything else
                {
#if CLIENT
                    if (SubEditorScreen.IsSubEditor())
                    {
                        wire.Item.Remove();
                    }
                    else
                    {
                        wire.Item.Drop(null);
                    }
#else
                    wire.Item.Drop(null);
#endif
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
#if CLIENT
                        if (SubEditorScreen.IsSubEditor())
                        {
                            wire.Item.Remove();
                        }
                        else
                        {
                            wire.Item.Drop(null);
                        }
#else
                        wire.Item.Drop(null);
#endif
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

        public override void ReceiveSignal([NotNull] Signal signal)
        {
            //do nothing
        }


        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
#if CLIENT
            TriggerRewiringSound();
#endif

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
