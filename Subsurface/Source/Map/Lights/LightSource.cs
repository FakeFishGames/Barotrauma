using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface.Lights
{
    class LightSource
    {
        private static Texture2D lightTexture;

        public List<ConvexHull> hullsInRange;

        private Color color;

        private float range;

        private Texture2D texture;

        private Vector2 position;
        public Vector2 Position
        {
            get { return position; }
            set
            {
                if (position == value) return;

                position = value;
                UpdateHullsInRange();
            }
        }

        public Color Color
        {
            get { return color; }
            set { color = value; }
        }

        public float Range
        {
            get { return range; }
            set
            {
                float newRange = MathHelper.Clamp(value, 0.0f, 2048.0f);
                if (range == newRange) return;
                range = newRange;

                UpdateHullsInRange();
            }
        }

        public LightSource(Vector2 position, float range, Color color)
        {
            hullsInRange = new List<ConvexHull>();

            this.position = position;
            this.range = range;
            this.color = color;

            if (lightTexture == null)
            {
                lightTexture = TextureLoader.FromFile("Content/Lights/light.png");
            }

            texture = lightTexture;

            GameMain.LightManager.AddLight(this);
        }

        public void UpdateHullsInRange()
        {
            hullsInRange.Clear();
            if (range < 1.0f || color.A < 0.01f) return;

            foreach (ConvexHull ch in ConvexHull.list)
            {
                if (MathUtils.CircleIntersectsRectangle(position, range, ch.BoundingBox)) hullsInRange.Add(ch);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 center = new Vector2(lightTexture.Width / 2, lightTexture.Height / 2);
            float scale = range / (lightTexture.Width / 2.0f);
            spriteBatch.Draw(lightTexture, new Vector2(Position.X, -Position.Y), null, color, 0, center, scale, SpriteEffects.None, 1);
        }

        public void Remove()
        {
            GameMain.LightManager.RemoveLight(this);
        }
    }
}
