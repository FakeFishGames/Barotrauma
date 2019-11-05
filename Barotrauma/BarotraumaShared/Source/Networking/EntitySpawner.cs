using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
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
            public readonly float Condition;

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 worldPosition, float? condition = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = worldPosition;
                Condition = condition ?? prefab.Health;
            }

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 position, Submarine sub, float? condition = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = position;
                Submarine = sub;
                Condition = condition ?? prefab.Health;
            }
            
            public ItemSpawnInfo(ItemPrefab prefab, Inventory inventory, float? condition = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Inventory = inventory;
                Condition = condition ?? prefab.Health;
            }

            public Entity Spawn()
            {                
                if (Prefab == null)
                {
                    return null;
                }
                Item spawnedItem;
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

        class CharacterSpawnInfo : IEntitySpawnInfo
        {
            public readonly string identifier;

            public readonly Vector2 Position;
            public readonly Submarine Submarine;

            private readonly Action<Character> onSpawn;

            public CharacterSpawnInfo(string identifier, Vector2 worldPosition, Action<Character> onSpawn = null)
            {
                this.identifier = identifier ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = worldPosition;
                this.onSpawn = onSpawn;
            }

            public CharacterSpawnInfo(string identifier, Vector2 position, Submarine sub, Action<Character> onSpawn = null)
            {
                this.identifier = identifier ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = position;
                Submarine = sub;
                this.onSpawn = onSpawn;
            }

            public Entity Spawn()
            {
                var character = string.IsNullOrEmpty(identifier) ? null : 
                    Character.Create(identifier,
                    Submarine == null ? Position : Submarine.Position + Position,
                    ToolBox.RandomSeed(8), createNetworkEvent: false);
                onSpawn?.Invoke(character);
                return character;
            }
        }

        private readonly Queue<IEntitySpawnInfo> spawnQueue;
        private readonly Queue<Entity> removeQueue;

        public class SpawnOrRemove
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

        public override string ToString()
        {
            return "EntitySpawner";
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Vector2 worldPosition, float? condition = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue1:ItemPrefabNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, worldPosition, condition));
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Vector2 position, Submarine sub, float? condition = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue2:ItemPrefabNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, position, sub, condition));
        }

        public void AddToSpawnQueue(ItemPrefab itemPrefab, Inventory inventory, float? condition = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue3:ItemPrefabNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, inventory, condition));
        }

        public void AddToSpawnQueue(string speciesName, Vector2 worldPosition, Action<Character> onSpawn = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (string.IsNullOrEmpty(speciesName))
            {
                string errorMsg = "Attempted to add an empty/null species name to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue4:SpeciesNameNullOrEmpty", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new CharacterSpawnInfo(speciesName, worldPosition, onSpawn));
        }

        public void AddToSpawnQueue(string speciesName, Vector2 position, Submarine sub, Action<Character> onSpawn = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (string.IsNullOrEmpty(speciesName))
            {
                string errorMsg = "Attempted to add an empty/null species name to entity spawn queue.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue5:SpeciesNameNullOrEmpty", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new CharacterSpawnInfo(speciesName, position, sub, onSpawn));
        }

        public void AddToRemoveQueue(Entity entity)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (removeQueue.Contains(entity) || entity.Removed || entity == null) { return; }
            if (entity is Character)
            {
                Character character = entity as Character;
#if SERVER
                if (GameMain.Server != null)
                {
                    Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
                    if (client != null) GameMain.Server.SetClientCharacter(client, null);
                }
#endif
            }            

            removeQueue.Enqueue(entity);
        }

        public void AddToRemoveQueue(Item item)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (removeQueue.Contains(item) || item.Removed) { return; }

            removeQueue.Enqueue(item);
            if (item.ContainedItems == null) return;
            foreach (Item containedItem in item.ContainedItems)
            {
                if (containedItem != null) AddToRemoveQueue(containedItem);
            }
        }

        public void Update()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            while (spawnQueue.Count > 0)
            {
                var entitySpawnInfo = spawnQueue.Dequeue();

                var spawnedEntity = entitySpawnInfo.Spawn();
                if (spawnedEntity != null)
                {
                    CreateNetworkEventProjSpecific(spawnedEntity, false);
                    if (spawnedEntity is Item)
                    {
                        ((Item)spawnedEntity).Condition = ((ItemSpawnInfo)entitySpawnInfo).Condition;
                    }
                }
            }

            while (removeQueue.Count > 0)
            {
                var removedEntity = removeQueue.Dequeue();
                if (removedEntity is Item item)
                {
                    item.SendPendingNetworkUpdates();
                }
                CreateNetworkEventProjSpecific(removedEntity, true);
                removedEntity.Remove();
            }
        }

        partial void CreateNetworkEventProjSpecific(Entity entity, bool remove);

        public void Reset()
        {
            removeQueue.Clear();
            spawnQueue.Clear();
        }
    }
}
