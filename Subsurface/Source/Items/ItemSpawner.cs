using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class ItemSpawner
    {
        private Queue<Pair<ItemPrefab, object>> spawnQueue;

        public ItemSpawner()
        {
            spawnQueue = new Queue<Pair<ItemPrefab, object>>();
        }

        public void QueueItem(ItemPrefab itemPrefab, Vector2 position)
        {
            var itemInfo = new Pair<ItemPrefab, object>();
            itemInfo.First = itemPrefab;
            itemInfo.Second = position;

            spawnQueue.Enqueue(itemInfo);
        }

        public void QueueItem(ItemPrefab itemPrefab, Inventory inventory)
        {
            var itemInfo = new Pair<ItemPrefab, object>();
            itemInfo.First = itemPrefab;
            itemInfo.Second = inventory;

            spawnQueue.Enqueue(itemInfo);
        }

        public void Update()
        {
            while (spawnQueue.Count>0)
            {
                var itemInfo = spawnQueue.Dequeue();

                if (itemInfo.Second is Vector2)
                {
                    new Item(itemInfo.First, (Vector2)itemInfo.Second - Submarine.HiddenSubPosition, null);
                }
                else if (itemInfo.Second is Inventory)
                {

                    var item = new Item(itemInfo.First, Vector2.Zero, null);

                    var inventory = itemInfo.Second as Inventory;
                    inventory.TryPutItem(item, null, false);
                }
                //!!!!!!!!!!!!!!!!!!!!!!
                
            }
        }
    }

    class ItemRemover
    {
        private Queue<Item> removeQueue;

        public ItemRemover()
        {
            removeQueue = new Queue<Item>();
        }

        public void QueueItem(Item item)
        {
            removeQueue.Enqueue(item);
        }

        public void Update()
        {
            while (removeQueue.Count > 0)
            {
                var item = removeQueue.Dequeue();

                item.Remove();
            }
        }
    }
}
