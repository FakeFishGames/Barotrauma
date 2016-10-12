using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;
using System;

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
        
        private List<Entity> spawnedEntities = new List<Entity>();
        
        public EntitySpawner()
        {
            spawnQueue = new Queue<IEntitySpawnInfo>();
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
            //List<Inventory> inventories = new List<Inventory>();

            while (spawnQueue.Count>0)
            {
                var entitySpawnInfo = spawnQueue.Dequeue();

                var spawnedEntity = entitySpawnInfo.Spawn();

                if (spawnedEntity!= null) AddToSpawnedList(spawnedEntity);
            }

            //if (GameMain.Server != null) GameMain.Server.SendItemSpawnMessage(items);
        }

        public void AddToSpawnedList(Entity entity)
        {
            if (GameMain.Server == null) return;
            if (entity == null) return;

            spawnedEntities.Add(entity);
            NetStateID = (UInt32)spawnedEntities.Count;
        }

        public void ServerWrite(Lidgren.Network.NetOutgoingMessage message, Client client)
        {
            if (GameMain.Server == null) return;

            //skip items that the client already knows about
            List<Entity> entities = spawnedEntities.Skip((int)client.lastRecvEntitySpawnID).ToList();

            message.Write((UInt32)spawnedEntities.Count);

            message.Write((byte)entities.Count);
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i] is Item)
                {
                    message.Write((byte)SpawnableType.Item);
                    ((Item)entities[i]).WriteSpawnData(message);
                }
                else if (entities[i] is Character)
                {
                    message.Write((byte)SpawnableType.Character);
                    ((Character)entities[i]).WriteSpawnData(message);
                }
            }
        }

        public void ClientRead(Lidgren.Network.NetIncomingMessage message)
        {
            if (GameMain.Server != null) return;

            UInt32 ID = message.ReadUInt32();
            
            var entityCount = message.ReadByte();
            for (int i = 0; i < entityCount; i++)
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

            NetStateID = Math.Max(ID, NetStateID);
        }


        public void Clear()
        {
            NetStateID = 0;

            spawnQueue.Clear();
            spawnedEntities.Clear();
        }
    }

    //todo: turn into a generic EntityRemover class + sync
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
