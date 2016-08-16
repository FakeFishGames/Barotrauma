using System.Collections.Generic;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;
using System.Linq;
using System;

namespace Barotrauma
{
    class Entity
    {
        private static Dictionary<ushort, Entity> dictionary = new Dictionary<ushort, Entity>();


        private ushort id;

        protected AITarget aiTarget;
        //protected float soundRange;
        //protected float sightRange;
                
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
        /// Writes the state of the entity into the message
        /// </summary>
        /// <param name="data">some data that was saved when the networkevent was created</param>
        /// <returns>false if something went wrong when filling the message, true if the msg is ready to be sent</returns>
        public virtual bool FillNetworkData(NetworkEventType type, NetBuffer message, object data) 
        {
            return false;
        }

        /// <summary>
        /// Updates the state of the entity based on the data in the message
        /// </summary>

        /// <param name="sendingTime"></param>
        /// <param name="data"></param>
        /// <returns>false if the message is not valid (client trying to change something they're not authorized to, corrupt data, etc) and should be ignored</returns>
        public virtual bool ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data)
        {
            data = null;

            return false;
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
                e.Remove();
            }
            dictionary.Clear();
        }

        public virtual void Remove()
        {
            dictionary.Remove(ID);
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
