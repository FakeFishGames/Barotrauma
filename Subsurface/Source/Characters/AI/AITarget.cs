using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class AITarget
    {
        public static List<AITarget> List = new List<AITarget>();

        public Entity Entity;

        protected float soundRange;
        protected float sightRange;
        
        public float SoundRange
        {
            get 
            {
                return soundRange;
            }
            set { soundRange = value; }
        }

        public float SightRange
        {
            get { return sightRange; }
            set { sightRange = value; }
        }

        public Vector2 WorldPosition
        {
            get { return Entity.WorldPosition; }
        }

        public Vector2 SimPosition
        {
            get { return Entity.SimPosition; }
        }

        public AITarget(Entity e)
        {
            Entity = e;
            List.Add(this);
        }

        public void Remove()
        {
            List.Remove(this);
        }

    }
}
