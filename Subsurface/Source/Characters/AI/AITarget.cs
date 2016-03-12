using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class AITarget
    {
        public static bool ShowAITargets;

        public static List<AITarget> List = new List<AITarget>();

        public Entity Entity;

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
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!ShowAITargets) return;

            var rangeSprite = GUI.SubmarineIcon;

            if (soundRange > 0.0f)
                rangeSprite.Draw(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                    Color.Cyan * 0.1f, rangeSprite.Origin,
                    0.0f, soundRange / rangeSprite.size.X);

            if (sightRange > 0.0f)
                rangeSprite.Draw(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                    Color.Orange * 0.1f, rangeSprite.Origin,
                    0.0f, sightRange / rangeSprite.size.X);
        }

    }
}
