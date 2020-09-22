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

        private readonly Submarine parentSub;
        public string Text
        {
            get;
            private set;
        }

        private string textTag;
        public string TextTag
        {
            get { return textTag; }
            set 
            {
                if (textTag == value) { return; }
                textTag = value;
                Text = string.IsNullOrEmpty(textTag) ? string.Empty : TextManager.Get(textTag);
            }
        }

        public HUDProgressBar(Vector2 worldPosition, string textTag, Submarine parentSubmarine = null)
            : this(worldPosition, parentSubmarine, GUI.Style.Red, GUI.Style.Green, textTag)
        {
        }

        public HUDProgressBar(Vector2 worldPosition, Submarine parentSubmarine, Color emptyColor, Color fullColor, string textTag)
        {
            this.emptyColor = emptyColor;
            this.fullColor = fullColor;
            parentSub = parentSubmarine;
            WorldPosition = worldPosition;
            Size = new Vector2(100.0f, 20.0f);
            FadeTimer = 1.0f;
            if (!string.IsNullOrEmpty(textTag))
            {
                textTag = textTag;
                Text = TextManager.Get(textTag);
            }
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
            Color color = Color.Lerp(emptyColor, fullColor, progress);
            GUI.DrawProgressBar(spriteBatch,
                new Vector2(pos.X, -pos.Y),
                Size, progress,
                color * a,
                Color.White * a * 0.8f);

            if (!string.IsNullOrEmpty(Text))
            {
                Vector2 textSize = GUI.SmallFont.MeasureString(Text);
                Vector2 textPos = new Vector2(pos.X + (Size.X - textSize.X) / 2, pos.Y - textSize.Y * 1.2f);
                GUI.DrawString(spriteBatch, textPos - Vector2.One, Text, Color.Black * a, font: GUI.SmallFont);
                GUI.DrawString(spriteBatch, textPos, Text, Color.White * a, font: GUI.SmallFont);
            }

        }
    }
}
