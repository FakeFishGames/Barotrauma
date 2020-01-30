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

        private Vector2 worldPosition;

        public Vector2 WorldPosition
        {
            get
            {
                return worldPosition;
            }
            set
            {
                worldPosition = value;
                if (parentSub != null)
                {                 
                    worldPosition -= parentSub.DrawPosition;
                }
            }
        }

        public Vector2 Size;

        private Submarine parentSub;

        public HUDProgressBar(Vector2 worldPosition, Submarine parentSubmarine = null)
            : this(worldPosition, parentSubmarine, GUI.Style.Red, GUI.Style.Green)
        {
        }

        public HUDProgressBar(Vector2 worldPosition, Submarine parentSubmarine, Color emptyColor, Color fullColor)
        {
            this.emptyColor = emptyColor;
            this.fullColor = fullColor;
            
            parentSub = parentSubmarine;

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

            Vector2 pos = new Vector2(WorldPosition.X - Size.X / 2, WorldPosition.Y + Size.Y / 2);

            if (parentSub != null)
            {
                pos += parentSub.DrawPosition;
            }

            pos = cam.WorldToScreen(pos);
            
            GUI.DrawProgressBar(spriteBatch,
                new Vector2(pos.X, -pos.Y),
                Size, progress, 
                Color.Lerp(emptyColor, fullColor, progress) * a,
                Color.White * a * 0.8f);
        }
    }
}
