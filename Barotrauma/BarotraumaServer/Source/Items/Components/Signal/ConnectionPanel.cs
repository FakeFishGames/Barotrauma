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
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            List<Wire>[] wires = new List<Wire>[Connections.Count];

            //read wire IDs for each connection
            for (int i = 0; i < Connections.Count; i++)
            {
                wires[i] = new List<Wire>();
                for (int j = 0; j < Connection.MaxLinked; j++)
                {
                    ushort wireId = msg.ReadUInt16();

                    if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }

                    Wire wireComponent = wireItem.GetComponent<Wire>();
                    if (wireComponent != null)
                    {
                        wires[i].Add(wireComponent);
                    }
                }
            }

            List<Wire> clientSideDisconnectedWires = new List<Wire>();
            ushort disconnectedWireCount = msg.ReadUInt16();
            for (int i = 0; i < disconnectedWireCount; i++)
            {
                ushort wireId = msg.ReadUInt16();
                if (!(Entity.FindEntityByID(wireId) is Item wireItem)) { continue; }
                Wire wireComponent = wireItem.GetComponent<Wire>();
                if (wireComponent == null) { continue; }
                clientSideDisconnectedWires.Add(wireComponent);
            }

            //don't allow rewiring locked panels
            if (Locked || !GameMain.NetworkMember.ServerSettings.AllowRewiring) { return; }

            item.CreateServerEvent(this);

            //check if the character can access this connectionpanel 
            //and all the wires they're trying to connect
            if (!item.CanClientAccess(c)) { return; }
            for (int i = 0; i < Connections.Count; i++)
            {
                foreach (Wire wire in wires[i])
                {
                    //wire not found in any of the connections yet (client is trying to connect a new wire)
                    //  -> we need to check if the client has access to it
                    if (!Connections.Any(connection => connection.Wires.Contains(wire)) && !DisconnectedWires.Contains(wire))
                    {
                        if (!wire.Item.CanClientAccess(c)) { return; }
                    }
                }
            }

            if (!CheckCharacterSuccess(c.Character))
            {
                GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnFailure, this, c.Character.ID });
                return;
            }

            //go through existing wire links
            for (int i = 0; i < Connections.Count; i++)
            {
                int j = -1;
                foreach (Wire existingWire in Connections[i].Wires)
                {
                    j++;
                    if (existingWire == null) { continue; }

                    //existing wire not in the list of new wires -> disconnect it
                    if (!wires[i].Contains(existingWire))
                    {
                        if (existingWire.Locked)
                        {
                            //this should not be possible unless the client is running a modified version of the game
                            GameServer.Log(c.Character.LogName + " attempted to disconnect a locked wire from " +
                                Connections[i].Item.Name + " (" + Connections[i].Name + ")", ServerLog.MessageType.Error);
                            continue;
                        }

                        existingWire.RemoveConnection(item);
                        item.GetComponent<ConnectionPanel>()?.DisconnectedWires.Add(existingWire);

                        if (!wires.Any(w => w.Contains(existingWire)))
                        {
                            GameMain.Server.KarmaManager.OnWireDisconnected(c.Character, existingWire);
                        }

                        if (existingWire.Connections[0] == null && existingWire.Connections[1] == null)
                        {
                            GameServer.Log(c.Character.LogName + " disconnected a wire from " +
                                Connections[i].Item.Name + " (" + Connections[i].Name + ")", ServerLog.MessageType.Wiring);

                            if (!clientSideDisconnectedWires.Contains(existingWire))
                            {
                                existingWire.Item.Drop(c.Character);
                            }
                        }
                        else if (existingWire.Connections[0] != null)
                        {
                            GameServer.Log(c.Character.LogName + " disconnected a wire from " +
                                Connections[i].Item.Name + " (" + Connections[i].Name + ") to " + existingWire.Connections[0].Item.Name + " (" + existingWire.Connections[0].Name + ")", ServerLog.MessageType.Wiring);

                            //wires that are not in anyone's inventory (i.e. not currently being rewired) 
                            //can never be connected to only one connection
                            // -> the client must have dropped the wire from the connection panel
                            /*if (existingWire.Item.ParentInventory == null && !wires.Any(w => w.Contains(existingWire)))
                            {
                                //let other clients know the item was also disconnected from the other connection
                                existingWire.Connections[0].Item.CreateServerEvent(existingWire.Connections[0].Item.GetComponent<ConnectionPanel>());
                                existingWire.Item.Drop(c.Character);
                            }*/
                        }
                        else if (existingWire.Connections[1] != null)
                        {
                            GameServer.Log(c.Character.LogName + " disconnected a wire from " +
                                Connections[i].Item.Name + " (" + Connections[i].Name + ") to " + existingWire.Connections[1].Item.Name + " (" + existingWire.Connections[1].Name + ")", ServerLog.MessageType.Wiring);

                            /*if (existingWire.Item.ParentInventory == null && !wires.Any(w => w.Contains(existingWire)))
                            {
                                //let other clients know the item was also disconnected from the other connection
                                existingWire.Connections[1].Item.CreateServerEvent(existingWire.Connections[1].Item.GetComponent<ConnectionPanel>());
                                existingWire.Item.Drop(c.Character);
                            }*/
                        }

                        Connections[i].SetWire(j, null);
                    }
                }
            }

            foreach (Wire disconnectedWire in DisconnectedWires.ToList())
            {
                if (disconnectedWire.Connections[0] == null && 
                    disconnectedWire.Connections[1] == null &&
                    !clientSideDisconnectedWires.Contains(disconnectedWire))
                {
                    disconnectedWire.Item.Drop(c.Character);
                    GameServer.Log(c.Character.LogName + " dropped " + disconnectedWire.Name, ServerLog.MessageType.Inventory);
                }
            }

            //go through new wires
            for (int i = 0; i < Connections.Count; i++)
            {
                foreach (Wire newWire in wires[i])
                {
                    //already connected, no need to do anything
                    if (Connections[i].Wires.Contains(newWire)) { continue; }

                    Connections[i].TryAddLink(newWire);
                    newWire.Connect(Connections[i], true, true);

                    var otherConnection = newWire.OtherConnection(Connections[i]);

                    if (otherConnection == null)
                    {
                        GameServer.Log(c.Character.LogName + " connected a wire to " +
                            Connections[i].Item.Name + " (" + Connections[i].Name + ")",
                            ServerLog.MessageType.Wiring);
                    }
                    else
                    {
                        GameServer.Log(c.Character.LogName + " connected a wire from " +
                            Connections[i].Item.Name + " (" + Connections[i].Name + ") to " +
                            (otherConnection == null ? "none" : otherConnection.Item.Name + " (" + (otherConnection.Name) + ")"),
                            ServerLog.MessageType.Wiring);
                    }
                }
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(user == null ? (ushort)0 : user.ID);
            ClientWrite(msg, extraData);
        }
    }
}
