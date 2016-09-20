using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    class HUDProgressBar
    {
        private float progress;

        public float Progress
        {
            get { return progress; }
            set { progress = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        public float FadeTimer;

        private Color fullColor, emptyColor;

        public Vector2 WorldPosition;

        public Vector2 Size;

        public HUDProgressBar(Vector2 worldPosition)
            : this(worldPosition, Color.Red, Color.Green)
        {
        }

        public HUDProgressBar(Vector2 worldPosition, Color emptyColor, Color fullColor)
        {
            this.emptyColor = emptyColor;
            this.fullColor = fullColor;

            WorldPosition = worldPosition;

            Size = new Vector2(100.0f, 20.0f);

            FadeTimer = 1.0f;
        }

        public void Update(float deltatime)
        {
            FadeTimer -= deltatime;
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam)
        {
            float a = Math.Min(FadeTimer, 1.0f);

            Vector2 pos = cam.WorldToScreen(
                new Vector2(WorldPosition.X - Size.X / 2, WorldPosition.Y + Size.Y / 2));

            pos.Y = -pos.Y;
            
            GUI.DrawProgressBar(spriteBatch,
                pos,
                Size, progress, 
                Color.Lerp(emptyColor, fullColor, progress) * a,
                Color.White * a * 0.8f);
        }
    }
}
