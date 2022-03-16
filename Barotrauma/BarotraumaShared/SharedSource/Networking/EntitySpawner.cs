using Barotrauma.Extensions; 
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks.ServerList;

namespace Barotrauma
{
    partial class EntitySpawner : Entity, IServerSerializable
    {
        private enum SpawnableType { Item, Character };
        
        public interface IEntitySpawnInfo
        {
            Entity Spawn();
            void OnSpawned(Entity entity);
        }

        public class ItemSpawnInfo : IEntitySpawnInfo
        {
            public readonly ItemPrefab Prefab;

            public readonly Vector2 Position;
            public readonly Inventory Inventory;
            public readonly Submarine Submarine;
            public readonly float Condition;
            public readonly int Quality;

            public bool SpawnIfInventoryFull = true;
            public bool IgnoreLimbSlots = false;
            public InvSlotType Slot = InvSlotType.None;

            private readonly Action<Item> onSpawned;

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 worldPosition, Action<Item> onSpawned, float? condition = null, int? quality = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = worldPosition;
                Condition = condition ?? prefab.Health;
                Quality = quality ?? 0;
                this.onSpawned = onSpawned;
            }

            public ItemSpawnInfo(ItemPrefab prefab, Vector2 position, Submarine sub, Action<Item> onSpawned, float? condition = null, int? quality = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = position;
                Submarine = sub;
                Condition = condition ?? prefab.Health;
                Quality = quality ?? 0;
                this.onSpawned = onSpawned;
            }
            
            public ItemSpawnInfo(ItemPrefab prefab, Inventory inventory, Action<Item> onSpawned, float? condition = null, int? quality = null)
            {
                Prefab = prefab ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Inventory = inventory;
                Condition = condition ?? prefab.Health;
                Quality = quality ?? 0;
                this.onSpawned = onSpawned;
            }

            public Entity Spawn()
            {                
                if (Prefab == null)
                {
                    return null;
                }
                Item spawnedItem;
                if (Inventory?.Owner != null)
                {
                    if (!SpawnIfInventoryFull && !Inventory.CanBePut(Prefab))
                    {
                        return null;
                    }
                    spawnedItem = new Item(Prefab, Vector2.Zero, null)
                    {
                        Condition = Condition,
                        Quality = Quality
                    };
                    var slot = Slot != InvSlotType.None ? Slot.ToEnumerable() : spawnedItem.AllowedSlots;
                    if (!Inventory.Owner.Removed && !Inventory.TryPutItem(spawnedItem, null, slot))
                    {
                        if (IgnoreLimbSlots)
                        {
                            for (int i = 0; i < Inventory.Capacity; i++)
                            {
                                if (Inventory.GetItemAt(i) == null)
                                {
                                    Inventory.ForceToSlot(spawnedItem, i);
                                    break;
                                }
                            }
                        }
                        spawnedItem.SetTransform(FarseerPhysics.ConvertUnits.ToSimUnits(Inventory.Owner?.WorldPosition ?? Vector2.Zero), spawnedItem.body?.Rotation ?? 0.0f, findNewHull: false);
                    }
                }
                else
                {
                    spawnedItem = new Item(Prefab, Position, Submarine)
                    {
                        Condition = Condition,
                        Quality = Quality
                    };
                }
                return spawnedItem;
            }

            public void OnSpawned(Entity spawnedItem)
            {
                if (!(spawnedItem is Item item)) { throw new ArgumentException($"The entity passed to ItemSpawnInfo.OnSpawned must be an Item (value was {spawnedItem?.ToString() ?? "null"})."); }
                onSpawned?.Invoke(item);
            }
        }

        class CharacterSpawnInfo : IEntitySpawnInfo
        {
            public readonly Identifier Identifier;
            public readonly CharacterInfo CharacterInfo;

            public readonly Vector2 Position;
            public readonly Submarine Submarine;

            private readonly Action<Character> onSpawned;

            public CharacterSpawnInfo(Identifier identifier, Vector2 worldPosition, Action<Character> onSpawn = null)
            {
                this.Identifier = identifier;
                if (identifier.IsEmpty) { throw new ArgumentException($"{nameof(CharacterSpawnInfo)} identifier cannot be null."); }
                Position = worldPosition;
                this.onSpawned = onSpawn;
            }

            public CharacterSpawnInfo(Identifier identifier, Vector2 position, Submarine sub, Action<Character> onSpawn = null)
            {
                this.Identifier = identifier;
                if (identifier.IsEmpty) { throw new ArgumentException($"{nameof(CharacterSpawnInfo)} identifier cannot be null."); }
                Position = position;
                Submarine = sub;
                this.onSpawned = onSpawn;
            }

            public CharacterSpawnInfo(Identifier identifier, Vector2 position, CharacterInfo characterInfo, Action<Character> onSpawn = null) : this (identifier, position, onSpawn)
            {
                CharacterInfo = characterInfo;
            }

            public Entity Spawn()
            {
                var character = Identifier.IsEmpty ? null :
                    Character.Create(Identifier,
                    Submarine == null ? Position : Submarine.Position + Position,
                    ToolBox.RandomSeed(8), CharacterInfo, createNetworkEvent: false);
                return character;
            }

            public void OnSpawned(Entity spawnedCharacter)
            {
                if (!(spawnedCharacter is Character character)) { throw new ArgumentException($"The entity passed to CharacterSpawnInfo.OnSpawned must be a Character (value was {spawnedCharacter?.ToString() ?? "null"})."); }
                onSpawned?.Invoke(character);
            }
        }

        class SubmarineSpawnInfo : IEntitySpawnInfo
        {
            public readonly string Name;

            public readonly Vector2 Position;

            private readonly Action<Character> onSpawned;

            public SubmarineSpawnInfo(string name, Vector2 worldPosition, Action<Character> onSpawn = null)
            {
                this.Name = name ?? throw new ArgumentException("ItemSpawnInfo prefab cannot be null.");
                Position = worldPosition;
                this.onSpawned = onSpawn;
            }


            public Entity Spawn()
            {
                var submarine = string.IsNullOrEmpty(Name) ? null :
                    new Submarine(SubmarineInfo.SavedSubmarines.First(s => s.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)));
                return submarine;
            }

            public void OnSpawned(Entity spawnedCharacter)
            {
                if (!(spawnedCharacter is Character character)) { throw new ArgumentException($"The entity passed to CharacterSpawnInfo.OnSpawned must be a Character (value was {spawnedCharacter?.ToString() ?? "null"})."); }
                onSpawned?.Invoke(character);
            }
        }

        private readonly Queue<IEntitySpawnInfo> spawnQueue;
        private readonly Queue<Entity> removeQueue;

        public abstract class SpawnOrRemove : NetEntityEvent.IData
        {
            public readonly Entity Entity;
            public UInt16 ID => Entity.ID;
            
            public readonly UInt16 InventoryID;

            public readonly byte ItemContainerIndex;
            public readonly int SlotIndex;

            public override string ToString()
            {
                return
                    "(" +
                    ((Entity as MapEntity)?.Name ?? "[NULL]") +
                    $", {ID}, {InventoryID}, {SlotIndex})";
            }

            protected SpawnOrRemove(Entity entity)
            {
                Entity = entity;
                if (!(entity is Item { ParentInventory: { Owner: { } } } item)) { return; }

                InventoryID = item.ParentInventory.Owner.ID;
                SlotIndex = item.ParentInventory.FindIndex(item);
                //find the index of the ItemContainer this item is inside to get the item to
                //spawn in the correct inventory in multi-inventory items like fabricators
                if (item.Container == null) { return; }

                foreach (ItemComponent component in item.Container.Components)
                {
                    if (component is ItemContainer container &&
                        container.Inventory == item.ParentInventory)
                    {
                        ItemContainerIndex = (byte)item.Container.GetComponentIndex(component);
                        break;
                    }
                }
            }
        }

        public sealed class SpawnEntity : SpawnOrRemove
        {
            public SpawnEntity(Entity entity) : base(entity) { }
            public override string ToString()
                => $"Spawn {base.ToString()}";
        }
        
        public sealed class RemoveEntity : SpawnOrRemove
        {
            public RemoveEntity(Entity entity) : base(entity) { }
            public override string ToString()
                => $"Remove {base.ToString()}";
        }
        
        public EntitySpawner()
            : base(null, Entity.EntitySpawnerID)
        {
            spawnQueue = new Queue<IEntitySpawnInfo>();
            removeQueue = new Queue<Entity>();
        }

        public override string ToString()
        {
            return "EntitySpawner";
        }

        public void AddItemToSpawnQueue(ItemPrefab itemPrefab, Vector2 worldPosition, float? condition = null, int? quality = null, Action<Item> onSpawned = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue1:ItemPrefabNull", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, worldPosition, onSpawned, condition, quality));
        }

        public void AddItemToSpawnQueue(ItemPrefab itemPrefab, Vector2 position, Submarine sub, float? condition = null, int? quality = null, Action<Item> onSpawned = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue2:ItemPrefabNull", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, position, sub, onSpawned, condition, quality));
        }

        public void AddItemToSpawnQueue(ItemPrefab itemPrefab, Inventory inventory, float? condition = null, int? quality = null, Action<Item> onSpawned = null, bool spawnIfInventoryFull = true, bool ignoreLimbSlots = false, InvSlotType slot = InvSlotType.None)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (itemPrefab == null)
            {
                string errorMsg = "Attempted to add a null item to entity spawn queue.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue3:ItemPrefabNull", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new ItemSpawnInfo(itemPrefab, inventory, onSpawned, condition, quality) 
            { 
                SpawnIfInventoryFull = spawnIfInventoryFull, 
                IgnoreLimbSlots = ignoreLimbSlots,
                Slot = slot
            });
        }

        public void AddCharacterToSpawnQueue(Identifier speciesName, Vector2 worldPosition, Action<Character> onSpawn = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (speciesName.IsEmpty)
            {
                string errorMsg = "Attempted to add an empty/null species name to entity spawn queue.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue4:SpeciesNameNullOrEmpty", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new CharacterSpawnInfo(speciesName, worldPosition, onSpawn));
        }

        public void AddCharacterToSpawnQueue(Identifier speciesName, Vector2 position, Submarine sub, Action<Character> onSpawn = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (speciesName.IsEmpty)
            {
                string errorMsg = "Attempted to add an empty/null species name to entity spawn queue.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue5:SpeciesNameNullOrEmpty", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new CharacterSpawnInfo(speciesName, position, sub, onSpawn));
        }

        public void AddCharacterToSpawnQueue(Identifier speciesName, Vector2 worldPosition, CharacterInfo characterInfo, Action<Character> onSpawn = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (speciesName.IsEmpty)
            {
                string errorMsg = "Attempted to add an empty/null species name to entity spawn queue.\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("EntitySpawner.AddToSpawnQueue4:SpeciesNameNullOrEmpty", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return;
            }
            spawnQueue.Enqueue(new CharacterSpawnInfo(speciesName, worldPosition, characterInfo, onSpawn));
        }

        public void AddEntityToRemoveQueue(Entity entity)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (removeQueue.Contains(entity) || entity.Removed || entity == null || entity.IdFreed) { return; }
            if (entity is Item item) { AddItemToRemoveQueue(item); return; }
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

        public void AddItemToRemoveQueue(Item item)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (removeQueue.Contains(item) || item.Removed) { return; }

            removeQueue.Enqueue(item);
            var containedItems = item.OwnInventory?.AllItems;
            if (containedItems == null) { return; }
            foreach (Item containedItem in containedItems)
            {
                if (containedItem != null)
                {
                    AddItemToRemoveQueue(containedItem);
                }
            }
        }

        /// <summary>
        /// Are there any entities in the spawn queue that match the given predicate
        /// </summary>
        public bool IsInSpawnQueue(Predicate<IEntitySpawnInfo> predicate)
        {
            return spawnQueue.Any(s => predicate(s));
        }

        /// <summary>
        /// How many entities in the spawn queue match the given predicate
        /// </summary>
        public int CountSpawnQueue(Predicate<IEntitySpawnInfo> predicate)
        {
            return spawnQueue.Count(s => predicate(s));
        }

        public bool IsInRemoveQueue(Entity entity)
        {
            return removeQueue.Contains(entity);
        }

        public void Update(bool createNetworkEvents = true)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            while (spawnQueue.Count > 0)
            {
                var entitySpawnInfo = spawnQueue.Dequeue();

                var spawnedEntity = entitySpawnInfo.Spawn();
                if (spawnedEntity == null) { continue; }

                if (createNetworkEvents) 
                { 
                    CreateNetworkEventProjSpecific(new SpawnEntity(spawnedEntity)); 
                }
                entitySpawnInfo.OnSpawned(spawnedEntity);
            }

            while (removeQueue.Count > 0)
            {
                var removedEntity = removeQueue.Dequeue();
                if (removedEntity is Item item)
                {
                    item.SendPendingNetworkUpdates();
                }
                if (createNetworkEvents)
                {
                    CreateNetworkEventProjSpecific(new RemoveEntity(removedEntity));
                }
                removedEntity.Remove();
            }
        }

        partial void CreateNetworkEventProjSpecific(SpawnOrRemove spawnOrRemove);

        public void Reset()
        {
            removeQueue.Clear();
            spawnQueue.Clear();
        }
    }
}
