using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class AITarget
    {
        public static List<AITarget> List = new List<AITarget>();

        public Entity Entity
        {
            get;
            private set;
        }

        private float soundRange;
        private float sightRange;
        
        public float SoundRange
        {
            get { return soundRange; }
            set { soundRange = Math.Max(value, 0.0f); }
        }

        public float SightRange
        {
            get { return sightRange; }
            set { sightRange = Math.Max(value, 0.0f); }
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
            Entity = null;
        }
    }
}
