using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class ConnectionPanel : ItemComponent, IServerSerializable, IClientSerializable
    {
        public override void UpdateHUD(Character character)
        {
            if (character != Character.Controlled || character != user) return;
            
            if (HighlightedWire != null)
            {
                HighlightedWire.Item.IsHighlighted = true;
                if (HighlightedWire.Connections[0] != null && HighlightedWire.Connections[0].Item != null) HighlightedWire.Connections[0].Item.IsHighlighted = true;
                if (HighlightedWire.Connections[1] != null && HighlightedWire.Connections[1].Item != null) HighlightedWire.Connections[1].Item.IsHighlighted = true;
            }
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            if (character != Character.Controlled || character != user) return;

            HighlightedWire = null;
            Connection.DrawConnections(spriteBatch, this, character);
        }


        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            if (GameMain.Client.MidRoundSyncing)
            {
                //delay reading the state until midround syncing is done
                //because some of the wires connected to the panel may not exist yet
                int bitsToRead = Connections.Count * Connection.MaxLinked * 16;
                StartDelayedCorrection(type, msg.ExtractBits(bitsToRead), sendingTime, waitForMidRoundSync: true);
            }
            else
            {
                ApplyRemoteState(msg);
            }
        }

        private void ApplyRemoteState(NetBuffer msg)
        {
            List<Wire> prevWires = Connections.SelectMany(c => Array.FindAll(c.Wires, w => w != null)).ToList();
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

                    Item wireItem = Entity.FindEntityByID(wireId) as Item;
                    if (wireItem == null) continue;

                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent == null) continue;

                    newWires.Add(wireComponent);

                    connection.Wires[i] = wireComponent;
                    wireComponent.Connect(connection, false);
                }
            }

            foreach (Wire wire in prevWires)
            {
                if (wire.Connections[0] == null && wire.Connections[1] == null)
                {
                    wire.Item.Drop(null);
                }
                //wires that are not in anyone's inventory (i.e. not currently being rewired) can never be connected to only one connection
                // -> someone must have dropped the wire from the connection panel
                else if (wire.Item.ParentInventory == null &&
                    (wire.Connections[0] != null ^ wire.Connections[1] != null))
                {
                    wire.Item.Drop(null);
                }
            }
        }
    }
}
