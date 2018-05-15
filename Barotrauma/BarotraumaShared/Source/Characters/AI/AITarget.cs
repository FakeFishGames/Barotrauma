using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

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
            set { soundRange = Math.Max(value, MinSoundRange); }
        }

        public float SightRange
        {
            get { return sightRange; }
            set { sightRange = Math.Max(value, MinSightRange); }
        }

        public float MinSoundRange, MinSightRange;

        public Vector2 WorldPosition
        {
            get
            {
                if (Entity == null || Entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace);
#endif
                    return Vector2.Zero;
                }

                return Entity.WorldPosition;
            }
        }

        public Vector2 SimPosition
        {
            get
            {
                if (Entity == null || Entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace);
#endif
                    return Vector2.Zero;
                }

                return Entity.SimPosition;
            }
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
