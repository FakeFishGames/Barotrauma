using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma.Lights
{
    class LightSource
    {
        private static Texture2D lightTexture;

        public List<ConvexHull> hullsInRange;

        private Color color;

        private float range;

        private Texture2D texture;

        public Sprite LightSprite;

        public Entity Submarine;

        //what was the range of the light when HullsInRange were last updated
        private float prevHullUpdateRange;

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

        public Vector2 WorldPosition
        {
            get { return (Submarine == null) ? position : position + Submarine.Position; }
        }

        public static Texture2D LightTexture
        {
            get
            {
                if (lightTexture == null)
                {
                    lightTexture = TextureLoader.FromFile("Content/Lights/light.png");
                }

                return lightTexture;
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

                range = MathHelper.Clamp(value, 0.0f, 2048.0f);
                if (Math.Abs(prevHullUpdateRange - range)<5.0f) return;
                
                UpdateHullsInRange();
                prevHullUpdateRange = range;
            }
        }

        public LightSource(Vector2 position, float range, Color color, Submarine submarine)
        {
            hullsInRange = new List<ConvexHull>();

            this.Submarine = submarine;

            this.position = position;
            this.range = range;
            this.color = color;

            texture = LightTexture;

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
            Vector2 center = new Vector2(LightTexture.Width / 2, LightTexture.Height / 2);
            float scale = range / (lightTexture.Width / 2.0f);
            spriteBatch.Draw(lightTexture, new Vector2(WorldPosition.X, -WorldPosition.Y), null, color, 0, center, scale, SpriteEffects.None, 1);

            if (LightSprite != null)
            {
                LightSprite.Draw(spriteBatch, new Vector2(WorldPosition.X, -WorldPosition.Y), Color);
            } 
        }

        public void Remove()
        {
            GameMain.LightManager.RemoveLight(this);
        }
    }
}
