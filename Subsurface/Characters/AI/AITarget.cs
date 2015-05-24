using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Subsurface
{
    class AITarget
    {
        public static List<AITarget> list = new List<AITarget>();


        protected float soundRange;
        protected float sightRange;

        public Entity entity;

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

        public Vector2 Position
        {
            get { return entity.SimPosition; }
        }

        public AITarget(Entity e)
        {
            entity = e;
            list.Add(this);
        }

        public void Remove()
        {
            list.Remove(this);
        }

    }
}
