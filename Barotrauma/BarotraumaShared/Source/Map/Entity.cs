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
                    System.Diagnostics.Debug.WriteLine(existingEntity + " had the same ID as " + this + " (" + value + ")");
                    DebugConsole.Log(existingEntity + " had the same ID as " + this + " (" + value + ")");
                    dictionary.Remove(value);
                    dictionary.Add(id, existingEntity);
                    existingEntity.id = id;
                    DebugConsole.Log("The id of " + existingEntity + " is now " + id);
                    DebugConsole.Log("The id of " + this + " is now " + value);
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
            if (Item.ItemList.Count > 0)
            {
                DebugConsole.ThrowError("Some items were not removed in Entity.RemoveAll:");
                foreach (Item item in Item.ItemList)
                {
                    DebugConsole.ThrowError(" - " + item.Name + "(ID " + item.id + ")");
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
                        DebugConsole.ThrowError("Error while removing entity \"" + item.ToString() + "\"", exception);
                    }
                }
                Item.ItemList.Clear();
            }
            if (Character.CharacterList.Count > 0)
            {
                DebugConsole.ThrowError("Some characters were not removed in Entity.RemoveAll:");
                foreach (Character character in Character.CharacterList)
                {
                    DebugConsole.ThrowError(" - " + character.Name + "(ID " + character.id + ")");
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
                        DebugConsole.ThrowError("Error while removing entity \"" + character.ToString() + "\"", exception);
                    }
                }
                Character.CharacterList.Clear();
            }

            dictionary.Clear();
        }

        public virtual void Remove()
        {
            DebugConsole.Log("Removing entity " + this.ToString() + " (" + ID + ") from entity dictionary.");
            Entity existingEntity;
            if (!dictionary.TryGetValue(ID, out existingEntity))
            {
                DebugConsole.Log("Entity " + this.ToString() + " (" + ID + ") not present in entity dictionary.");
            }
            else if (existingEntity != this)
            {
                DebugConsole.Log("Entity ID mismatch in entity dictionary. Entity " + existingEntity + " had the ID " + ID);
                foreach (var keyValuePair in dictionary.Where(kvp => kvp.Value == this).ToList())
                {
                    dictionary.Remove(keyValuePair.Key);
                }
            }

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
