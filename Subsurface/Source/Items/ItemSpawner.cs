using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class ItemSpawner
    {
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
                    itemInfo.Inventory.TryPutItem(spawnedItem, spawnedItem.AllowedSlots, false);
                }
                else
                {
                    spawnedItem = new Item(itemInfo.Prefab, itemInfo.Position, itemInfo.Submarine);
                }

                AddToSpawnedList(spawnedItem);
                items.Add(spawnedItem);
            }
            
        }

        public void AddToSpawnedList(Item item)
        {
            spawnItems.Add(item);
        }

        public void Clear()
        {
            spawnQueue.Clear();
            spawnItems.Clear();
        }
    }

    class ItemRemover
    {
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
            
        }
        
        public void Clear()
        {
            removeQueue.Clear();
            removedItems.Clear();
        }
    }
}
