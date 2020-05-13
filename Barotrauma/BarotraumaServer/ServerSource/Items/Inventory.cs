using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Inventory : IServerSerializable, IClientSerializable
    {
        public void ServerRead(ClientNetObject type, IReadMessage msg, Client c)
        {
            List<Item> prevItems = new List<Item>(Items);

            byte itemCount = msg.ReadByte();
            ushort[] newItemIDs = new ushort[itemCount];            
            for (int i = 0; i < itemCount; i++)
            {
                newItemIDs[i] = msg.ReadUInt16();
            }
            
            if (c == null || c.Character == null) return;

            bool accessible = c.Character.CanAccessInventory(this);
            if (this is CharacterInventory && accessible)
            {
                if (Owner == null || !(Owner is Character))
                {
                    accessible = false;
                }
                else if (!((CharacterInventory)this).AccessibleWhenAlive && !((Character)Owner).IsDead)
                {
                    accessible = false;
                }
            }

            if (!accessible)
            {
                //create a network event to correct the client's inventory state
                //otherwise they may have an item in their inventory they shouldn't have been able to pick up,
                //and receiving an event for that inventory later will cause the item to be dropped
                CreateNetworkEvent();
                for (int i = 0; i < capacity; i++)
                {
                    if (!(Entity.FindEntityByID(newItemIDs[i]) is Item item)) { continue; }
                    item.PositionUpdateInterval = 0.0f;
                    if (item.ParentInventory != null && item.ParentInventory != this)
                    {
                        item.ParentInventory.CreateNetworkEvent();
                    }
                }
                return;
            }
            
            List<Inventory> prevItemInventories = new List<Inventory>(Items.Select(i => i?.ParentInventory));

            for (int i = 0; i < capacity; i++)
            {
                Item newItem = newItemIDs[i] == 0 ? null : Entity.FindEntityByID(newItemIDs[i]) as Item;
                prevItemInventories.Add(newItem?.ParentInventory);

                if (newItemIDs[i] == 0 || (newItem != Items[i]))
                {
                    if (Items[i] != null)
                    {
                        Item droppedItem = Items[i];
                        Entity prevOwner = Owner;
                        droppedItem.Drop(null);

                        var previousInventory = prevOwner switch
                        {
                            Item itemInventory => (itemInventory.FindParentInventory(inventory => inventory is CharacterInventory) as CharacterInventory),
                            Character character => character.Inventory,
                            _ => null
                        };

                        if (previousInventory != null && previousInventory != c.Character?.Inventory)
                        {
                            GameMain.Server?.KarmaManager.OnItemTakenFromPlayer(previousInventory, c, droppedItem);
                        }
                        
                        if (droppedItem.body != null && prevOwner != null)
                        {
                            droppedItem.body.SetTransform(prevOwner.SimPosition, 0.0f);
                        }
                    }
                    System.Diagnostics.Debug.Assert(Items[i] == null);
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (newItemIDs[i] > 0)
                {
                    if (!(Entity.FindEntityByID(newItemIDs[i]) is Item item) || item == Items[i]) { continue; }

                    if (GameMain.Server != null)
                    {
                        var holdable = item.GetComponent<Holdable>();
                        if (holdable != null && !holdable.CanBeDeattached()) { continue; }

                        if (!prevItems.Contains(item) && !item.CanClientAccess(c))
                        {
                            if (item.body != null && !c.PendingPositionUpdates.Contains(item))
                            {
                                c.PendingPositionUpdates.Enqueue(item);
                            }
                            item.PositionUpdateInterval = 0.0f;                            
                            continue;
                        }
                    }
                    TryPutItem(item, i, true, true, c.Character, false);
                    for (int j = 0; j < capacity; j++)
                    {
                        if (Items[j] == item && newItemIDs[j] != item.ID)
                        {
                            Items[j] = null;
                        }
                    }
                }
            }

            CreateNetworkEvent();
            foreach (Inventory prevInventory in prevItemInventories.Distinct())
            {
                if (prevInventory != this) { prevInventory?.CreateNetworkEvent(); }
            }

            foreach (Item item in Items.Distinct())
            {
                if (item == null) { continue; }
                if (!prevItems.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " picked up " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " placed " + item.Name + " in " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
            foreach (Item item in prevItems.Distinct())
            {
                if (item == null) { continue; }
                if (!Items.Contains(item))
                {
                    if (Owner == c.Character)
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " dropped " + item.Name, ServerLog.MessageType.Inventory);
                    }
                    else
                    {
                        GameServer.Log(GameServer.CharacterLogName(c.Character) + " removed " + item.Name + " from " + Owner, ServerLog.MessageType.Inventory);
                    }
                }
            }
        }

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            SharedWrite(msg, extraData);
        }
    }
}
