using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public void ServerRead(ClientNetObject type, NetBuffer msg, Client c)
        {
            List<Item> prevItems = new List<Item>(Items);
            ushort[] newItemIDs = new ushort[capacity];

            for (int i = 0; i < capacity; i++)
            {
                newItemIDs[i] = msg.ReadUInt16();
            }

            if (this is CharacterInventory)
            {
                if (Owner == null || !(Owner is Character)) return;
                if (!((CharacterInventory)this).AccessibleWhenAlive && !((Character)Owner).IsDead) return;
            }

            if (c == null || c.Character == null || !c.Character.CanAccessInventory(this))
            {
                return;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (newItemIDs[i] == 0 || (Entity.FindEntityByID(newItemIDs[i]) as Item != Items[i]))
                {
                    if (Items[i] != null) Items[i].Drop();
                    System.Diagnostics.Debug.Assert(Items[i] == null);
                }
            }


            for (int i = 0; i < capacity; i++)
            {
                if (newItemIDs[i] > 0)
                {
                    var item = Entity.FindEntityByID(newItemIDs[i]) as Item;
                    if (item == null || item == Items[i]) continue;

                    if (GameMain.Server != null)
                    {
                        if (!item.CanClientAccess(c)) continue;
                    }
                    TryPutItem(item, i, true, true, c.Character, false);
                }
            }

            CreateNetworkEvent();

            foreach (Item item in Items.Distinct())
            {
                if (item == null) continue;
                if (!prevItems.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        GameServer.Log(c.Character.LogName + " picked up " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(c.Character.LogName + " placed " + item.Name + " in " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
            foreach (Item item in prevItems.Distinct())
            {
                if (item == null) continue;
                if (!Items.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        GameServer.Log(c.Character.LogName + " dropped " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(c.Character.LogName + " removed " + item.Name + " from " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            SharedWrite(msg, extraData);
        }
    }
}
