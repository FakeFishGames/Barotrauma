using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;
using System;

namespace Barotrauma
{
    class ItemSpawner : IServerSerializable
    {
        public UInt32 NetStateID
        {
            get;
            private set;
        }

        class ItemSpawnInfo
        {
            public readonly ItemPrefab Prefab;

            public readonly Vector2 Position;
            public readonly Inventory Inventory;
            public readonly Submarine Submarine;

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 worldPosition)
            {
                Prefab = prefab;
                Position = worldPosition;
            }

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 position, Submarine sub)
            {
                Prefab = prefab;
                Position = position;
                Submarine = sub;
            }
            
            public ItemSpawnInfo(ItemPrefab prefab, Inventory inventory)
            {
                Prefab = prefab;
                Inventory = inventory;
            }
        }

        private readonly Queue<ItemSpawnInfo> spawnQueue;


        public List<Item> spawnItems = new List<Item>();


        public ItemSpawner()
        {
            spawnQueue = new Queue<ItemSpawnInfo>();
        }

        public void QueueItem(ItemPrefab itemPrefab, Vector2 worldPosition, bool isNetworkMessage = false)
        {
            //clients aren't allowed to spawn new items unless the server says so
            if (!isNetworkMessage && GameMain.Client != null) return;
            
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, worldPosition));
        }

        public void QueueItem(ItemPrefab itemPrefab, Vector2 position, Submarine sub, bool isNetworkMessage = false)
        {
            //clients aren't allowed to spawn new items unless the server says so
            if (!isNetworkMessage && GameMain.Client != null) return;

            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, position, sub));
        }

        public void QueueItem(ItemPrefab itemPrefab, Inventory inventory, bool isNetworkMessage = false)
        {
            //clients aren't allowed to spawn new items unless the server says so
            if (!isNetworkMessage && GameMain.Client != null) return;

            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, inventory));
        }

        public void Update()
        {
            if (!spawnQueue.Any()) return;

            List<Item> items = new List<Item>();
            //List<Inventory> inventories = new List<Inventory>();

            while (spawnQueue.Count>0)
            {
                var itemInfo = spawnQueue.Dequeue();

                Item spawnedItem = null;

                if (itemInfo.Inventory != null)
                {
                    spawnedItem = new Item(itemInfo.Prefab, Vector2.Zero, null);
                    itemInfo.Inventory.TryPutItem(spawnedItem, spawnedItem.AllowedSlots);
                }
                else
                {
                    spawnedItem = new Item(itemInfo.Prefab, itemInfo.Position, itemInfo.Submarine);
                }

                AddToSpawnedList(spawnedItem);
                items.Add(spawnedItem);
            }

            //if (GameMain.Server != null) GameMain.Server.SendItemSpawnMessage(items);
        }

        public void AddToSpawnedList(List<Item> items)
        {
            foreach (Item item in items)
            {
                AddToSpawnedList(item);
            }
        }

        public void AddToSpawnedList(Item item)
        {
            spawnItems.Add(item);
            NetStateID = (UInt32)spawnItems.Count;
        }

        public void ServerWrite(Lidgren.Network.NetOutgoingMessage message, Client client)
        {
            if (GameMain.Server == null) return;

            //skip items that the client already knows about
            List<Item> items = spawnItems.Skip((int)client.lastRecvItemSpawnID).ToList();

            message.Write((UInt32)spawnItems.Count);

            message.Write((byte)items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                message.Write(items[i].Prefab.Name);
                message.Write(items[i].ID);

                if (items[i].ParentInventory == null || items[i].ParentInventory.Owner == null)
                {
                    message.Write((ushort)0);

                    message.Write(items[i].Position.X);
                    message.Write(items[i].Position.Y);
                    message.Write(items[i].Submarine != null ? items[i].Submarine.ID : (ushort)0);
                }
                else
                {
                    message.Write(items[i].ParentInventory.Owner.ID);

                    int index = items[i].ParentInventory.FindIndex(items[i]);
                    message.Write(index < 0 ? (byte)255 : (byte)index);
                }

                if (items[i].Name == "ID Card")
                {
                    message.Write(items[i].Tags);
                }
            }
        }

        public void ClientRead(Lidgren.Network.NetIncomingMessage message)
        {
            if (GameMain.Server != null) return;

            UInt32 ID = message.ReadUInt32();
            
            var itemCount = message.ReadByte();
            for (int i = 0; i < itemCount; i++)
            {
                string itemName = message.ReadString();
                ushort itemId   = message.ReadUInt16();

                ushort inventoryId = message.ReadUInt16();

                Vector2 pos = Vector2.Zero;
                Submarine sub = null;
                int inventorySlotIndex = -1;
                
                if (inventoryId > 0)
                {
                    inventorySlotIndex = message.ReadByte();
                }
                else
                {
                    pos = new Vector2(message.ReadSingle(), message.ReadSingle());

                    ushort subID = message.ReadUInt16();
                    if (subID > 0)
                    {
                        sub = Submarine.Loaded.Find(s => s.ID == subID);
                    }
                }

                string tags = "";
                if (itemName == "ID Card")
                {
                    tags = message.ReadString();
                }
                                
                if (ID - itemCount + i < NetStateID) continue;

                //----------------------------------------

                var prefab = MapEntityPrefab.list.Find(me => me.Name == itemName);
                if (prefab == null) continue;

                var itemPrefab = prefab as ItemPrefab;
                if (itemPrefab == null) continue;

                Inventory inventory = null;

                var inventoryOwner = Entity.FindEntityByID(inventoryId);
                if (inventoryOwner != null)
                {
                    if (inventoryOwner is Character)
                    {
                        inventory = (inventoryOwner as Character).Inventory;
                    }
                    else if (inventoryOwner is Item)
                    {
                        var containers = (inventoryOwner as Item).GetComponents<Items.Components.ItemContainer>();
                        if (containers!=null && containers.Any())
                        {
                            inventory = containers.Last().Inventory;
                        }
                    }
                }                

                var item = new Item(itemPrefab, pos, sub);                

                item.ID = itemId;
                if (sub != null)
                {
                    item.CurrentHull = Hull.FindHull(pos + sub.Position, null, true);
                    item.Submarine = item.CurrentHull == null ? null : item.CurrentHull.Submarine;
                }

                if (!string.IsNullOrEmpty(tags)) item.Tags = tags;

                if (inventory != null)
                {
                    if (inventorySlotIndex >= 0 && inventorySlotIndex < 255 &&
                        inventory.TryPutItem(item, inventorySlotIndex, false))
                    {
                        continue;
                    }
                    inventory.TryPutItem(item, item.AllowedSlots);
                }
            }

            NetStateID = Math.Max(ID, NetStateID);
        }

        public void Clear()
        {
            NetStateID = 0;

            spawnQueue.Clear();
            spawnItems.Clear();
        }
    }

    class ItemRemover : IServerSerializable
    {
        public UInt32 NetStateID
        {
            get;
            private set;
        }

        private readonly Queue<Item> removeQueue;
        
        public List<Item> removedItems = new List<Item>();

        public ItemRemover()
        {
            removeQueue = new Queue<Item>();
        }

        public void QueueItem(Item item, bool isNetworkMessage = false)
        {
            if (!isNetworkMessage && GameMain.Client != null)
            {
                //clients aren't allowed to remove items unless the server says so
                return;
            }

            removeQueue.Enqueue(item);
        }

        public void Update()
        {
            if (!removeQueue.Any()) return;

            List<Item> items = new List<Item>();

            while (removeQueue.Count > 0)
            {
                var item = removeQueue.Dequeue();
                removedItems.Add(item);

                item.Remove();

                items.Add(item);
            }

            //if (GameMain.Server != null) GameMain.Server.SendItemRemoveMessage(items);
        }

        public void ServerWrite(Lidgren.Network.NetOutgoingMessage message, Client client)
        {
            //message.Write((byte)items.Count);
            //foreach (Item item in items)
            //{
            //    message.Write(item.ID);
            //}
        }

        public void ClientRead(Lidgren.Network.NetIncomingMessage message)
        {
            var itemCount = message.ReadByte();
            for (int i = 0; i<itemCount; i++)
            {
                ushort itemId = message.ReadUInt16();

                var item = MapEntity.FindEntityByID(itemId) as Item;
                if (item == null) continue;

                item.Remove();
            }
        }

        public void Clear()
        {
            NetStateID = 0;

            removeQueue.Clear();
            removedItems.Clear();
        }
    }
}
