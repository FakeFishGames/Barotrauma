using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class ItemSpawner
    {
        private Queue<Pair<ItemPrefab, Vector2>> spawnQueue;

        public ItemSpawner()
        {
            spawnQueue = new Queue<Pair<ItemPrefab, Vector2>>();
        }

        public void QueueItem(ItemPrefab itemPrefab, Vector2 position)
        {
            var itemInfo = new Pair<ItemPrefab, Vector2>();
            itemInfo.First = itemPrefab;
            itemInfo.Second = position;

            spawnQueue.Enqueue(itemInfo);
        }

        public void Update()
        {
            while (spawnQueue.Count>0)
            {
                var itemInfo = spawnQueue.Dequeue();

                //!!!!!!!!!!!!!!!!!!!!!!
                new Item(itemInfo.First, itemInfo.Second, null);
            }
        }
    }
}
