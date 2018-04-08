using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class Entity
    {
        private static Dictionary<ushort, Entity> dictionary = new Dictionary<ushort, Entity>();
        public static List<Entity> GetEntityList()
        {
            return dictionary.Values.ToList();
        }

        public static EntitySpawner Spawner;

        private ushort id;

        protected AITarget aiTarget;

        public virtual bool Removed
        {
            get;
            private set;
        }

        public ushort ID
        {
            get 
            {                                
                return id;             
            }
            set 
            {
                Entity thisEntity;
                if (dictionary.TryGetValue(id, out thisEntity) && thisEntity == this)
                {
                    dictionary.Remove(id);
                }
                //if there's already an entity with the same ID, give it the old ID of this one
                Entity existingEntity;
                if (dictionary.TryGetValue(value, out existingEntity))
                {
                    System.Diagnostics.Debug.WriteLine(existingEntity+" had the same ID as "+this);
                    dictionary.Remove(value);
                    dictionary.Add(id, existingEntity);
                    existingEntity.id = id;
                }

                id = value;                             
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

        public Entity(Submarine submarine)
        {
            this.Submarine = submarine;

            //give  an unique ID
            bool IDfound;
            id = submarine == null ? (ushort)1 : submarine.IdOffset;
            do
            {
                id += 1;
                IDfound = dictionary.ContainsKey(id);
            } while (IDfound);

            dictionary.Add(id, this);
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
                }
            }
            if (dictionary.Count > 0)
            {
                DebugConsole.ThrowError("Some entities were not removed in Entity.RemoveAll:");
                foreach (Entity e in dictionary.Values)
                {
                    DebugConsole.ThrowError(" - " + e.ToString() + "(ID " + e.id + ")");
                }
            }
            dictionary.Clear();

            Hull.EntityGrids.Clear();
        }

        public virtual void Remove()
        {
            dictionary.Remove(ID);
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
