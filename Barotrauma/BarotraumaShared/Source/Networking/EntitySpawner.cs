using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        const int MaxEntitiesPerWrite = 10;

        private enum SpawnableType { Item, Character };
        
        interface IEntitySpawnInfo
        {
            Entity Spawn();
        }

        class ItemSpawnInfo : IEntitySpawnInfo
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

            public Entity Spawn()
            {                
                Item spawnedItem = null;

                if (Inventory != null)
                {
                    spawnedItem = new Item(Prefab, Vector2.Zero, null);
                    Inventory.TryPutItem(spawnedItem, null, spawnedItem.AllowedSlots);
                }
                else
                {
                    spawnedItem = new Item(Prefab, Position, Submarine);
                }

                return spawnedItem;
            }
        }

        private readonly Queue<IEntitySpawnInfo> spawnQueue;
        private readonly Queue<Entity> removeQueue;

        class SpawnOrRemove
        {
            public readonly Entity Entity;

            public readonly bool Remove = false;

            public SpawnOrRemove(Entity entity, bool remove)
            {
                Entity = entity;
                Remove = remove;
            }
        }
        
        public EntitySpawner()
            : base(null)
        {
            spawnQueue = new Queue<IEntitySpawnInfo>();
            removeQueue = new Queue<Entity>();
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Vector2 worldPosition)
        {
            if (GameMain.Client != null) return;
            
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, worldPosition));
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Vector2 position, Submarine sub)
        {
            if (GameMain.Client != null) return;

            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, position, sub));
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Inventory inventory)
        {
            if (GameMain.Client != null) return;

            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, inventory));
        }

        public void AddToRemoveQueue(Entity entity)
        {
            if (GameMain.Client != null) return;
            if (removeQueue.Contains(entity) || entity.Removed) return;

            removeQueue.Enqueue(entity);
        }

        public void AddToRemoveQueue(Item item)
        {
            if (GameMain.Client != null) return;
            if (removeQueue.Contains(item) || item.Removed) return;

            removeQueue.Enqueue(item);
            if (item.ContainedItems == null) return;
            foreach (Item containedItem in item.ContainedItems)
            {
                if (containedItem != null) AddToRemoveQueue(containedItem);
            }
        }

        public void CreateNetworkEvent(Entity entity, bool remove)
        {
            if (GameMain.Server != null && entity != null)
            {
                GameMain.Server.CreateEntityEvent(this, new object[] { new SpawnOrRemove(entity, remove) });
            }
        }

        public void Update()
        {
            if (GameMain.Client != null) return;
            
            while (spawnQueue.Count>0)
            {
                var entitySpawnInfo = spawnQueue.Dequeue();

                var spawnedEntity = entitySpawnInfo.Spawn();
                if (spawnedEntity != null)
                {
                    CreateNetworkEvent(spawnedEntity, false);
                }
            }

            while (removeQueue.Count > 0)
            {
                var removedEntity = removeQueue.Dequeue();

                if (GameMain.Server != null)
                {
                    CreateNetworkEvent(removedEntity, true);
                }

                removedEntity.Remove();
            }
        }

        public void ServerWrite(Lidgren.Network.NetBuffer message, Client client, object[] extraData = null)
        {
            if (GameMain.Server == null) return;

            SpawnOrRemove entities = (SpawnOrRemove)extraData[0];
            
            message.Write(entities.Remove);

            if (entities.Remove)
            {
                message.Write(entities.Entity.ID);
            }
            else
            {
                if (entities.Entity is Item)
                {
                    message.Write((byte)SpawnableType.Item);
                    ((Item)entities.Entity).WriteSpawnData(message);
                }
                else if (entities.Entity is Character)
                {
                    message.Write((byte)SpawnableType.Character);
                    ((Character)entities.Entity).WriteSpawnData(message);
                }
            }
        }
    }
}
