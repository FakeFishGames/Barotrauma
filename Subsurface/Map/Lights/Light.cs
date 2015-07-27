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

        private Color color;

        private float range;

        private Texture2D texture;

        public Vector2 Position;

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
            }
        }

        public LightSource(Vector2 position, float range, Color color)
        {
            Position = position;
            this.range = range;
            this.color = color;

            if (lightTexture == null)
            {
                lightTexture = Game1.TextureLoader.FromFile("Content/Lights/light.png");
            }

            texture = lightTexture;

            Game1.LightManager.AddLight(this);
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 center = new Vector2(lightTexture.Width / 2, lightTexture.Height / 2);
            float scale = range / ((float)lightTexture.Width / 2.0f);
            spriteBatch.Draw(lightTexture, new Vector2(Position.X, -Position.Y), null, color, 0, center, scale, SpriteEffects.None, 1);
        }

        public void Remove()
        {
            Game1.LightManager.RemoveLight(this);
        }
    }
}
