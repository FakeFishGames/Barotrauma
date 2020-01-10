using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class Entity : ISpatialEntity
    {
        public const ushort NullEntityID = 0;
        public const ushort EntitySpawnerID = ushort.MaxValue;

        private static Dictionary<ushort, Entity> dictionary = new Dictionary<ushort, Entity>();
        public static List<Entity> GetEntityList()
        {
            return dictionary.Values.ToList();
        }

        public static EntitySpawner Spawner;

        private ushort id;

        protected AITarget aiTarget;

        private bool idFreed;

        public virtual bool Removed
        {
            get;
            private set;
        }

        public bool IdFreed
        {
            get { return idFreed; }
        }

        public ushort ID
        {
            get 
            {                                
                return id;             
            }
            set
            {
                if (this is EntitySpawner) { return; }
                if (value == NullEntityID)
                {
                    DebugConsole.ThrowError("Cannot set the ID of an entity to " + NullEntityID +
                        "! The value is reserved for entity events referring to a non-existent (e.g. removed) entity.\n" + Environment.StackTrace);
                    return;
                }
                if (value == EntitySpawnerID)
                {
                    DebugConsole.ThrowError("Cannot set the ID of an entity to " + EntitySpawnerID +
                        "! The value is reserved for EntitySpawner.\n" + Environment.StackTrace);
                    return;
                }

                if (dictionary.TryGetValue(id, out Entity thisEntity) && thisEntity == this)
                {
                    dictionary.Remove(id);
                }
                //if there's already an entity with the same ID, give it the old ID of this one
                if (dictionary.TryGetValue(value, out Entity existingEntity))
                {
                    DebugConsole.Log(existingEntity + " had the same ID as " + this + " (" + value + ")");
                    dictionary.Remove(value);
                    dictionary.Add(id, existingEntity);
                    existingEntity.id = id;
                    DebugConsole.Log("The id of " + existingEntity + " is now " + id);
                    DebugConsole.Log("The id of " + this + " is now " + value);
                }

                id = value;
                idFreed = false;
                dictionary.Add(id, this);
            }
        }

        public virtual Vector2 SimPosition
        {
            get { return Vector2.Zero; }
        }
        
        public virtual Vector2 Position
        {
            get { return Vector2.Zero; }
        }

        public virtual Vector2 WorldPosition
        {
            get { return Submarine == null ? Position : Submarine.Position + Position; }
        }

        public virtual Vector2 DrawPosition
        {
            get { return Submarine == null ? Position : Submarine.DrawPosition + Position; }
        }

        public Submarine Submarine
        {
            get;
            set;
        }

        public AITarget AiTarget
        {
            get { return aiTarget; }
        }

        public double SpawnTime
        {
            get { return spawnTime;  }
        }

        private readonly double spawnTime;

        public Entity(Submarine submarine)
        {
            this.Submarine = submarine;
            spawnTime = Timing.TotalTime;

            //give a unique ID
            id = this is EntitySpawner ?
                EntitySpawnerID :
                FindFreeID(submarine == null ? (ushort)1 : submarine.IdOffset);
            
            dictionary.Add(id, this);
        }

        public static ushort FindFreeID(ushort idOffset = 0)
        {
            //ushort.MaxValue - 2 because 0 and ushort.MaxValue are reserved values
            if (dictionary.Count >= ushort.MaxValue - 2)
            {
                throw new Exception("Maximum amount of entities (" + (ushort.MaxValue - 1) + ") reached!");
            }

            idOffset = Math.Max(idOffset, (ushort)1);
            bool IDfound;
            ushort id = idOffset;
            do
            {
                id += 1;
                IDfound = dictionary.ContainsKey(id);
            } while (IDfound);
            return id;
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
                    DebugConsole.ThrowError("Error while removing entity \"" + e.ToString() + "\"", exception);
                    GameAnalyticsManager.AddErrorEventOnce(
                        "Entity.RemoveAll:Exception" + e.ToString(),
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Error while removing entity \"" + e.ToString() + " (" + exception.Message + ")\n" + exception.StackTrace);
                }
            }
            StringBuilder errorMsg = new StringBuilder();
            if (dictionary.Count > 0)
            {
                errorMsg.AppendLine("Some entities were not removed in Entity.RemoveAll:");
                foreach (Entity e in dictionary.Values)
                {
                    errorMsg.AppendLine(" - " + e.ToString() + "(ID " + e.id + ")");
                }
            }
            if (Item.ItemList.Count > 0)
            {
                errorMsg.AppendLine("Some items were not removed in Entity.RemoveAll:");
                foreach (Item item in Item.ItemList)
                {
                    errorMsg.AppendLine(" - " + item.Name + "(ID " + item.id + ")");
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
                        DebugConsole.ThrowError("Error while removing item \"" + item.ToString() + "\"", exception);
                    }
                }
                Item.ItemList.Clear();
            }
            if (Character.CharacterList.Count > 0)
            {
                errorMsg.AppendLine("Some characters were not removed in Entity.RemoveAll:");
                foreach (Character character in Character.CharacterList)
                {
                    errorMsg.AppendLine(" - " + character.Name + "(ID " + character.id + ")");
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
                        DebugConsole.ThrowError("Error while removing character \"" + character.ToString() + "\"", exception);
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
                GameAnalyticsManager.AddErrorEventOnce("Entity.RemoveAll", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg.ToString());
            }

            dictionary.Clear();
            Hull.EntityGrids.Clear();
            Spawner?.Reset();
        }

        /// <summary>
        /// Removes the entity from the entity dictionary and frees up the ID it was using.
        /// </summary>
        public void FreeID()
        {
            DebugConsole.Log("Removing entity " + ToString() + " (" + ID + ") from entity dictionary.");
            if (!dictionary.TryGetValue(ID, out Entity existingEntity))
            {
                DebugConsole.Log("Entity " + ToString() + " (" + ID + ") not present in entity dictionary.");
                GameAnalyticsManager.AddErrorEventOnce(
                    "Entity.FreeID:EntityNotFound" + ID,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Entity " + ToString() + " (" + ID + ") not present in entity dictionary.\n" + Environment.StackTrace);
            }
            else if (existingEntity != this)
            {
                DebugConsole.Log("Entity ID mismatch in entity dictionary. Entity " + existingEntity + " had the ID " + ID + " (expecting " + ToString() + ")");
                GameAnalyticsManager.AddErrorEventOnce("Entity.FreeID:EntityMismatch" + ID,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Entity ID mismatch in entity dictionary. Entity " + existingEntity + " had the ID " + ID + " (expecting " + ToString() + ")");

                foreach (var keyValuePair in dictionary.Where(kvp => kvp.Value == this).ToList())
                {
                    dictionary.Remove(keyValuePair.Key);
                }
            }

            dictionary.Remove(ID);
            idFreed = true;
        }

        public virtual void Remove()
        {
            if (!idFreed) FreeID();
            Removed = true;
        }

        public static void DumpIds(int count)
        {
            List<Entity> entities = dictionary.Values.OrderByDescending(e => e.id).ToList();

            count = Math.Min(entities.Count, count);

            for (int i = 0; i < count; i++)
            {
                DebugConsole.ThrowError(entities[i].id + ": " + entities[i].ToString());
            }
        }
    }
}
