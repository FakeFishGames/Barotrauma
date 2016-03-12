using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ItemSpawner
    {
        private readonly Queue<Pair<ItemPrefab, object>> spawnQueue;

        public ItemSpawner()
        {
            spawnQueue = new Queue<Pair<ItemPrefab, object>>();
        }

        public void QueueItem(ItemPrefab itemPrefab, Vector2 position, bool isNetworkMessage = false)
        {
            if (!isNetworkMessage && GameMain.Client!=null)
            {
                //clients aren't allowed to spawn new items unless the server says so
                return;
            }

            var itemInfo = new Pair<ItemPrefab, object>();
            itemInfo.First = itemPrefab;
            itemInfo.Second = position;

            spawnQueue.Enqueue(itemInfo);
        }

        public void QueueItem(ItemPrefab itemPrefab, Inventory inventory, bool isNetworkMessage = false)
        {
            if (!isNetworkMessage && GameMain.Client != null)
            {
                //clients aren't allowed to spawn new items unless the server says so
                return;
            }

            var itemInfo = new Pair<ItemPrefab, object>();
            itemInfo.First = itemPrefab;
            itemInfo.Second = inventory;

            spawnQueue.Enqueue(itemInfo);
        }

        public void Update()
        {
            if (!spawnQueue.Any()) return;

            List<Item> items = new List<Item>();
            List<Inventory> inventories = new List<Inventory>();

            while (spawnQueue.Count>0)
            {
                var itemInfo = spawnQueue.Dequeue();

                if (itemInfo.Second is Vector2)
                {
                    Vector2 position = (Vector2)itemInfo.Second - Submarine.HiddenSubPosition;

                    items.Add(new Item(itemInfo.First, position, null));
                    inventories.Add(null);

                }
                else if (itemInfo.Second is Inventory)
                {
                    var item = new Item(itemInfo.First, Vector2.Zero, null);

                    var inventory = (Inventory)itemInfo.Second;
                    inventory.TryPutItem(item, null, false);

                    items.Add(item);
                    inventories.Add(inventory);
                }
            }

            if (GameMain.Server != null) GameMain.Server.SendItemSpawnMessage(items, inventories);
        }

        public void FillNetworkData(Lidgren.Network.NetBuffer message, List<Item> items, List<Inventory> inventories)
        {
            message.Write((byte)items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                message.Write(items[i].Prefab.Name);
                message.Write(items[i].ID);

                message.Write((inventories[i]==null || inventories[i].Owner == null) ? (ushort)0 : inventories[i].Owner.ID);
            }
        }

        public void ReadNetworkData(Lidgren.Network.NetBuffer message)
        {
            var itemCount = message.ReadByte();
            for (int i = 0; i < itemCount; i++)
            {
                string itemName = message.ReadString();
                ushort itemId = message.ReadUInt16();

                ushort inventoryId = message.ReadUInt16();


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

                var item = new Item(itemPrefab, Vector2.Zero, null);
                item.ID = itemId;
                if (inventory != null) inventory.TryPutItem(item, null, false);

            }
        }
    }

    class ItemRemover
    {
        private readonly Queue<Item> removeQueue;

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

                item.Remove();

                items.Add(item);
            }

            if (GameMain.Server != null) GameMain.Server.SendItemRemoveMessage(items);
        }

        public void FillNetworkData(Lidgren.Network.NetBuffer message, List<Item> items)
        {
            message.Write((byte)items.Count);
            foreach (Item item in items)
            {
                message.Write(item.ID);
            }
        }

        public void ReadNetworkData(Lidgren.Network.NetBuffer message)
        {
            var itemCount = message.ReadByte();
            for (int i = 0; i<itemCount; i++)
            {
                ushort itemId = message.ReadUInt16();

                var item = MapEntity.FindEntityByID(itemId);
                if (item == null || item is Item) continue;

                item.Remove();
            }
        }
    }
}
