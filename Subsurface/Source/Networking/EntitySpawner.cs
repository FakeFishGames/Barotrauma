using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class EntitySpawner : IServerSerializable
    {
        private enum SpawnableType { Item, Character };

        public UInt32 NetStateID
        {
            get;
            private set;
        }

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
                    Inventory.TryPutItem(spawnedItem, spawnedItem.AllowedSlots);
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
        
        private List<SpawnOrRemove> spawnHistory = new List<SpawnOrRemove>();
        
        public EntitySpawner()
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

        public void AddToRemoveQueue(Item item)
        {
            if (GameMain.Client != null) return;

            removeQueue.Enqueue(item);
        }

        public void Update()
        {
            if (GameMain.Client != null) return;

            if (!spawnQueue.Any()) return;

            while (spawnQueue.Count>0)
            {
                var entitySpawnInfo = spawnQueue.Dequeue();

                var spawnedEntity = entitySpawnInfo.Spawn();
                if (spawnedEntity != null) AddToSpawnedList(spawnedEntity);
            }

            while (removeQueue.Count > 0)
            {
                var entity = removeQueue.Dequeue();
                spawnHistory.Add(new SpawnOrRemove(entity, true));

                entity.Remove();
                NetStateID = (UInt32)spawnHistory.Count;
            }
        }

        public void AddToSpawnedList(Entity entity)
        {
            if (GameMain.Server == null) return;
            if (entity == null) return;

            spawnHistory.Add(new SpawnOrRemove(entity, false));

            NetStateID = (UInt32)spawnHistory.Count;
        }

        public void ServerWrite(Lidgren.Network.NetOutgoingMessage message, Client client)
        {
            if (GameMain.Server == null) return;

            //skip items that the client already knows about
            List<SpawnOrRemove> entities = spawnHistory.Skip((int)client.lastRecvEntitySpawnID).ToList();

            message.Write((UInt32)spawnHistory.Count);

            message.Write((UInt16)entities.Count);
            for (int i = 0; i < entities.Count; i++)
            {
                message.Write(entities[i].Remove);

                if (entities[i].Remove)
                {
                    message.Write(entities[i].Entity.ID);
                }
                else
                {
                    if (entities[i].Entity is Item)
                    {
                        message.Write((byte)SpawnableType.Item);
                        ((Item)entities[i].Entity).WriteSpawnData(message);
                    }
                    else if (entities[i].Entity is Character)
                    {
                        message.Write((byte)SpawnableType.Character);
                        ((Character)entities[i].Entity).WriteSpawnData(message);
                    }
                }
            }
        }

        public void ClientRead(Lidgren.Network.NetIncomingMessage message, float sendingTime)
        {
            if (GameMain.Server != null) return;

            UInt32 ID = message.ReadUInt32();
            
            var entityCount = message.ReadUInt16();
            for (int i = 0; i < entityCount; i++)
            {
                bool remove = message.ReadBoolean();

                if (remove)
                {
                    ushort entityId = message.ReadUInt16();

                    var entity = Entity.FindEntityByID(entityId);
                    if (entity == null || ID - entityCount + i < NetStateID) continue; //already removed

                    entity.Remove();
                }
                else
                {
                    switch (message.ReadByte())
                    {
                        case (byte)SpawnableType.Item:
                            Item.ReadSpawnData(message, ID - entityCount + i >= NetStateID);
                            break;
                        case (byte)SpawnableType.Character:
                            Character.ReadSpawnData(message, ID - entityCount + i >= NetStateID);
                            break;
                        default:
                            DebugConsole.ThrowError("Received invalid entity spawn message (unknown spawnable type)");
                            break;
                    }
                }
            }

            NetStateID = Math.Max(ID, NetStateID);
        }


        public void Clear()
        {
            NetStateID = 0;

            spawnQueue.Clear();
            removeQueue.Clear();
            spawnHistory.Clear();
        }
    }
}
