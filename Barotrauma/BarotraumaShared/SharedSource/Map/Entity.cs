using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Barotrauma.IO;
using System.Linq;
using System.Text;
using Barotrauma.Networking;

namespace Barotrauma
{
    abstract class Entity : ISpatialEntity
    {
        public const ushort NullEntityID = 0;
        public const ushort EntitySpawnerID = ushort.MaxValue;
        public const ushort RespawnManagerID = ushort.MaxValue - 1;
        public const ushort DummyID = ushort.MaxValue - 2;

        public const ushort ReservedIDStart = ushort.MaxValue - 3;

        public const ushort MaxEntityCount = ushort.MaxValue - 4; //ushort.MaxValue - 4 because the 4 values above are reserved values

        private static readonly Dictionary<ushort, Entity> dictionary = new Dictionary<ushort, Entity>();
        public static IReadOnlyCollection<Entity> GetEntities()
        {
            return dictionary.Values;
        }

        public static int EntityCount => dictionary.Count;

        public static EntitySpawner Spawner;

        protected AITarget aiTarget;

        public bool Removed { get; private set; }

        public bool IdFreed { get; private set; }

        /// <summary>
        /// Unique, but non-persistent identifier.
        /// Stays the same if the entities are created in the exactly same order, but doesn't persist e.g. between the rounds.
        /// </summary>
        public readonly ushort ID;

        public virtual Vector2 SimPosition => Vector2.Zero;

        public virtual Vector2 Position => Vector2.Zero;

        public virtual Vector2 WorldPosition => Submarine == null ? Position : Submarine.Position + Position;

        public virtual Vector2 DrawPosition => Submarine == null ? Position : Submarine.DrawPosition + Position;

        public Submarine Submarine { get; set; }

        public AITarget AiTarget => aiTarget;

        public bool InDetectable
        {
            get
            {
                if (aiTarget != null)
                {
                    return aiTarget.InDetectable;
                }
                return false;
            }
            set
            {
                if (aiTarget != null)
                {
                    aiTarget.InDetectable = value;
                }
            }
        }

        public double SpawnTime => spawnTime;
        private readonly double spawnTime;

        private static UInt64 creationCounter = 0;
        private readonly static object creationCounterMutex = new object();
        
        public readonly string CreationStackTrace;
        public readonly UInt64 CreationIndex;
        public string ErrorLine
            => $"-   {ID}: {this} ({Submarine?.Info?.Name ?? "[null]"} {Submarine?.ID ?? 0}) {CreationStackTrace}";
        
        public Entity(Submarine submarine, ushort id)
        {
            this.Submarine = submarine;
            spawnTime = Timing.TotalTime;

            if (dictionary.Count >= MaxEntityCount)
            {
                Dictionary<Identifier, int> entityCounts = new Dictionary<Identifier, int>();
                foreach (var entity in dictionary)
                {
                    if (entity.Value is MapEntity me)
                    {
                        if (entityCounts.ContainsKey(me.Prefab.Identifier))
                        {
                            entityCounts[me.Prefab.Identifier]++;
                        }
                        else
                        {
                            entityCounts[me.Prefab.Identifier] = 1;
                        }
                    }
                }
                string errorMsg = $"Maximum amount of entities ({MaxEntityCount}) exceeded! Largest numbers of entities: " +
                  string.Join(", ", entityCounts.OrderByDescending(kvp => kvp.Value).Take(10).Select(kvp => $"{kvp.Key}: {kvp.Value}"));
                throw new Exception(errorMsg);
            }

            //give a unique ID
            ID = DetermineID(id, submarine);

            if (dictionary.ContainsKey(ID))
            {
                throw new Exception($"ID {ID} is taken by {dictionary[ID]}");
            }

            dictionary.Add(ID, this);

            CreationStackTrace = "";
#if DEBUG
            var st = new StackTrace(skipFrames: 2, fNeedFileInfo: true);
            var frames = st.GetFrames();
            int frameCount = 0;
            foreach (var frame in frames)
            {
                string fileName = frame.GetFileName();
                if ((fileName?.Contains("BarotraumaClient") ?? false) || (fileName?.Contains("BarotraumaServer") ?? false)) { break; }

                fileName = Path.GetFileNameWithoutExtension(fileName);
                int fileLineNumber = frame.GetFileLineNumber();
                
                if (fileName.IsNullOrEmpty() || fileLineNumber <= 0) { continue; }
                
                CreationStackTrace += $"{fileName}@{fileLineNumber}; ";
            }
#endif
            #warning TODO: consider removing this mutex, entity creation probably shouldn't be multithreaded
            lock (creationCounterMutex)
            {
                CreationIndex = creationCounter;
                creationCounter++;
            }
        }

        protected virtual ushort DetermineID(ushort id, Submarine submarine)
        {
            return id != NullEntityID
                ? id
                : FindFreeId(submarine == null ? (ushort)1 : submarine.IdOffset);
        }

        private static ushort FindFreeId(ushort idOffset)
        {
            if (dictionary.Count >= MaxEntityCount)
            {
                throw new Exception($"Maximum amount of entities ({MaxEntityCount}) reached!");
            }

            ushort id = idOffset;
            while (id < ReservedIDStart)
            {
                if (!dictionary.ContainsKey(id)) { break; }
                id++;
            };
            return id;
        }
        
        /// <summary>
        /// Finds a contiguous block of free IDs of at least the given size
        /// </summary>
        /// <returns>The first ID in the found block, or zero if none are found</returns>
        public static int FindFreeIdBlock(int minBlockSize)
        {
            int currentBlockSize = 0;
            for (int i = 1; i < ReservedIDStart; i++)
            {
                if (dictionary.ContainsKey((ushort)i))
                {
                    currentBlockSize = 0;
                }
                else
                {
                    currentBlockSize++;
                    if (currentBlockSize >= minBlockSize)
                    {
                        return i - (currentBlockSize-1);
                    }
                }
            }
            return 0;
        }

        /// <summary>
        /// Find an entity based on the ID
        /// </summary>
        public static Entity FindEntityByID(ushort ID)
        {
            Entity matchingEntity;
            dictionary.TryGetValue(ID, out matchingEntity);

            return matchingEntity;
        }

        public static void RemoveAll()
        {
            List<Entity> list = new List<Entity>(dictionary.Values);
            foreach (Entity e in list)
            {
                try
                {
                    e.Remove();
                }
                catch (Exception exception)
                {
                    DebugConsole.ThrowError($"Error while removing entity \"{e}\"", exception);
                    GameAnalyticsManager.AddErrorEventOnce(
                        $"Entity.RemoveAll:Exception{e}",
                        GameAnalyticsManager.ErrorSeverity.Error,
                        $"Error while removing entity \"{e} ({exception.Message})\n{exception.StackTrace.CleanupStackTrace()}");
                }
            }
            StringBuilder errorMsg = new StringBuilder();
            if (dictionary.Count > 0)
            {
                errorMsg.AppendLine("Some entities were not removed in Entity.RemoveAll:");
                foreach (Entity e in dictionary.Values)
                {
                    errorMsg.AppendLine(" - " + e.ToString() + "(ID " + e.ID + ")");
                }
            }
            if (Item.ItemList.Count > 0)
            {
                errorMsg.AppendLine("Some items were not removed in Entity.RemoveAll:");
                foreach (Item item in Item.ItemList)
                {
                    errorMsg.AppendLine(" - " + item.Name + "(ID " + item.ID + ")");
                }

                var items = new List<Item>(Item.ItemList);
                foreach (Item item in items)
                {
                    try
                    {
                        item.Remove();
                    }
                    catch (Exception exception)
                    {
                        DebugConsole.ThrowError($"Error while removing item \"{item}\"", exception);
                    }
                }
                Item.ItemList.Clear();
            }
            if (Character.CharacterList.Count > 0)
            {
                errorMsg.AppendLine("Some characters were not removed in Entity.RemoveAll:");
                foreach (Character character in Character.CharacterList)
                {
                    errorMsg.AppendLine(" - " + character.Name + "(ID " + character.ID + ")");
                }

                var characters = new List<Character>(Character.CharacterList);
                foreach (Character character in characters)
                {
                    try
                    {
                        character.Remove();
                    }
                    catch (Exception exception)
                    {
                        DebugConsole.ThrowError($"Error while removing character \"{character}\"", exception);
                    }
                }
                Character.CharacterList.Clear();
            }

            if (!string.IsNullOrEmpty(errorMsg.ToString()))
            {
                foreach (string errorLine in errorMsg.ToString().Split('\n'))
                {
                    DebugConsole.ThrowError(errorLine);
                }
                GameAnalyticsManager.AddErrorEventOnce("Entity.RemoveAll", GameAnalyticsManager.ErrorSeverity.Error, errorMsg.ToString());
            }

            dictionary.Clear();
            Hull.EntityGrids.Clear();
            Spawner?.Reset();
            Items.Components.Projectile.ResetSpreadCounter();
        }

        /// <summary>
        /// Removes the entity from the entity dictionary and frees up the ID it was using.
        /// </summary>
        public void FreeID()
        {
            if (IdFreed) { return; }
            DebugConsole.Log($"Removing entity {ToString()} ({ID}) from entity dictionary.");
            if (!dictionary.TryGetValue(ID, out Entity existingEntity))
            {
                DebugConsole.ThrowError($"Entity {ToString()} ({ID}) not present in entity dictionary.");
                GameAnalyticsManager.AddErrorEventOnce(
                    $"Entity.FreeID:EntityNotFound{ID}",
                    GameAnalyticsManager.ErrorSeverity.Error,
                    $"Entity {ToString()} ({ID}) not present in entity dictionary.\n{Environment.StackTrace.CleanupStackTrace()}");
            }
            else if (existingEntity != this)
            {
                DebugConsole.ThrowError($"Entity ID mismatch in entity dictionary. Entity {existingEntity} had the ID {ID} (expecting {ToString()})");
                GameAnalyticsManager.AddErrorEventOnce("Entity.FreeID:EntityMismatch" + ID,
                    GameAnalyticsManager.ErrorSeverity.Error,
                    $"Entity ID mismatch in entity dictionary. Entity {existingEntity} had the ID {ID} (expecting {ToString()})");
            }
            else
            {
                dictionary.Remove(ID);
            }
            IdFreed = true;
        }

        public virtual void Remove()
        {
            FreeID();
            Removed = true;
        }

        public static void DumpIds(int count, string filename)
        {
            List<Entity> entities = dictionary.Values.OrderByDescending(e => e.ID).ToList();

            count = Math.Min(entities.Count, count);

            List<string> lines = new List<string>();
            for (int i = 0; i < count; i++)
            {
                lines.Add($"{entities[i].ID}: {entities[i]}");
                DebugConsole.ThrowError($"{entities[i].ID}: {entities[i]}");
            }

            if (!string.IsNullOrWhiteSpace(filename))
            {
                File.WriteAllLines(filename, lines);
            }
        }
    }
}
