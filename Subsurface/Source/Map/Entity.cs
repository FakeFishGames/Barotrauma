using System.Collections.Generic;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Barotrauma.Networking;

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
            id = 1;//Rand.Int(int.MaxValue);
            do
            {
                id += 1;
                IDfound = dictionary.ContainsKey(id);
            } while (IDfound);

            dictionary.Add(id, this);
        }

        public virtual bool FillNetworkData(NetworkEventType type, NetBuffer message, object data) 
        {
            return false;
        }
        public virtual void ReadNetworkData(NetworkEventType type, NetIncomingMessage message, float sendingTime, out object data) 
        {
            data = null;
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
            List<Entity> e = new List<Entity>();

            foreach (Entity ent in dictionary.Values)
            {
                int index = 0;
                for (int i = 0; i < e.Count; i++)
                {
                    if (e[i].id < ent.id)
                    {
                        index = i;
                        break;
                    }
                }

                e.Insert(index, ent);
            }

            int c = 0;
            foreach (Entity ent in e)
            {
                if (c>count) break;

                DebugConsole.ThrowError(ent.id+": "+ent.ToString());
                c++;
            }
        }
    }
}
