using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        public static Wire HighlightedWire;

        partial void InitProjSpecific(XElement element)
        {
            if (GuiFrame == null) return;
            new GUICustomComponent(new RectTransform(Vector2.One, GuiFrame.RectTransform), DrawConnections, null)
            {
                UserData = this
            };
        }
        
        public override void Move(Vector2 amount)
        {
            if (item.Submarine == null || item.Submarine.Loading || Screen.Selected != GameMain.SubEditorScreen) return;
            MoveConnectedWires(amount);
        }
        
        public override bool ShouldDrawHUD(Character character)
        {
            return character == Character.Controlled && character == user && character.SelectedConstruction == item;
        }
        
        public override void AddToGUIUpdateList()
        {
            GuiFrame?.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (character != Character.Controlled || character != user || character.SelectedConstruction != item) { return; }
            
            if (HighlightedWire != null)
            {
                HighlightedWire.Item.IsHighlighted = true;
                if (HighlightedWire.Connections[0] != null && HighlightedWire.Connections[0].Item != null) HighlightedWire.Connections[0].Item.IsHighlighted = true;
                if (HighlightedWire.Connections[1] != null && HighlightedWire.Connections[1].Item != null) HighlightedWire.Connections[1].Item.IsHighlighted = true;
            }
        }

        private void DrawConnections(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            if (user != Character.Controlled || user == null) { return; }

            HighlightedWire = null;
            Connection.DrawConnections(spriteBatch, this, user);

            foreach (UISprite sprite in GUI.Style.GetComponentStyle("ConnectionPanelFront").Sprites[GUIComponent.ComponentState.None])
            {
                sprite.Draw(spriteBatch, GuiFrame.Rect, Color.White, SpriteEffects.None);
            }
        }


        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (GameMain.Client.MidRoundSyncing)
            {
                //delay reading the state until midround syncing is done
                //because some of the wires connected to the panel may not exist yet
                long msgStartPos = msg.BitPosition;
                foreach (Connection connection in Connections)
                {
                    for (int i = 0; i < Connection.MaxLinked; i++)
                    {
                        msg.ReadUInt16();
                    }
                }
                ushort disconnectedWireCount = msg.ReadUInt16();
                for (int i = 0; i < disconnectedWireCount; i++)
                {
                    msg.ReadUInt16();
                }
                int msgLength = (int)(msg.BitPosition - msgStartPos);
                msg.BitPosition = (int)msgStartPos;
                StartDelayedCorrection(type, msg.ExtractBits(msgLength), sendingTime, waitForMidRoundSync: true);
            }
            else
            {
                ApplyRemoteState(msg);
            }
        }

        private void ApplyRemoteState(IReadMessage msg)
        {
            List<Wire> prevWires = Connections.SelectMany(c => c.Wires.Where(w => w != null)).ToList();
            List<Wire> newWires = new List<Wire>();

            foreach (Connection connection in Connections)
            {
                connection.ClearConnections();
            }

            foreach (Connection connection in Connections)
            {
                for (int i = 0; i < Connection.MaxLinked; i++)
                {
                    ushort wireId = msg.ReadUInt16();

                    if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }
                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent == null) { continue; }

                    newWires.Add(wireComponent);

                    connection.SetWire(i, wireComponent);
                    wireComponent.Connect(connection, false);
                }
            }

            List<Wire> previousDisconnectedWires = new List<Wire>(DisconnectedWires);
            DisconnectedWires.Clear();
            ushort disconnectedWireCount = msg.ReadUInt16();
            for (int i = 0; i < disconnectedWireCount; i++)
            {
                ushort wireId = msg.ReadUInt16();
                if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }
                Wire wireComponent = wireItem.GetComponent<Wire>();
                if (wireComponent == null) { continue; }
                DisconnectedWires.Add(wireComponent);
            }

            foreach (Wire wire in prevWires)
            {
                bool connected = wire.Connections[0] != null || wire.Connections[1] != null;
                if (!connected)
                {
                    foreach (Item item in Item.ItemList)
                    {
                        var connectionPanel = item.GetComponent<ConnectionPanel>();
                        if (connectionPanel != null && connectionPanel.DisconnectedWires.Contains(wire))
                        {
                            connected = true;
                            break;
                        }
                    }
                }
                if (wire.Item.ParentInventory == null && !connected)
                {
                    wire.Item.Drop(null);
                }
            }

            foreach (Wire disconnectedWire in previousDisconnectedWires)
            {
                if (disconnectedWire.Connections[0] == null &&
                    disconnectedWire.Connections[1] == null &&
                    !DisconnectedWires.Contains(disconnectedWire))
                {
                    disconnectedWire.Item.Drop(dropper: null);
                }
            }
        }
    }
}
