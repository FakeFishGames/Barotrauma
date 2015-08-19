using System.Collections.Generic;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Subsurface.Networking;

namespace Subsurface
{
    class Entity
    {
        private static Dictionary<int, Entity> dictionary = new Dictionary<int, Entity>();

        private int id;

        protected AITarget aiTarget;
        //protected float soundRange;
        //protected float sightRange;
        
        public int ID
        {
            get { return id; }
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

        public AITarget AiTarget
        {
            get { return aiTarget; }
        }

        public Entity()
        {
            //give  an unique ID
            bool IDfound;
            id = 1;//Rand.Int(int.MaxValue);
            do
            {
                id += 1;
                IDfound = dictionary.ContainsKey(id);
            } while (IDfound);

            dictionary.Add(id, this);
        }

        public virtual void FillNetworkData(NetworkEventType type, NetOutgoingMessage message, object data) { }
        public virtual void ReadNetworkData(NetworkEventType type, NetIncomingMessage message) { }

        /// <summary>
        /// Find an entity based on the ID
        /// </summary>
        public static Entity FindEntityByID(int ID)
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
    }
}
