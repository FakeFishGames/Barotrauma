﻿using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class GUIMessage
    {
        private ColoredText coloredText;
        private Vector2 pos;

        private float lifeTime;

        private Vector2 size;

        public readonly bool WorldSpace;

        public string Text
        {
            get { return coloredText.Text; }
        }

        public Color Color
        {
            get { return coloredText.Color; }
        }

        public Vector2 Pos
        {
            get { return pos; }
            set { pos = value; }
        }

        public Vector2 Velocity
        {
            get;
            private set;
        }

        public Vector2 Size
        {
            get { return size; }
        }
        
        public Vector2 Origin;

        public float Timer;

        public float LifeTime
        {
            get { return lifeTime; }
        }

        public GUIFont Font
        {
            get;
            private set;
        }
        
        public Submarine Submarine
        {
            get;
            private set;
        }

        public Vector2 DrawPos
        {
            get
            {
                return Submarine == null ? Pos : Pos + Submarine.DrawPosition;
            }
        }

        public GUIMessage(string text, Color color, float lifeTime, GUIFont font = null)
        {
            coloredText = new ColoredText(text, color, false, false);
            this.lifeTime = lifeTime;
            Timer = lifeTime;

            size = font.MeasureString(text);
            Origin = new Vector2(0, size.Y * 0.5f);

            Font = font;
        }

        public GUIMessage(string text, Color color, Vector2 position, Vector2 velocity, float lifeTime, Alignment textAlignment = Alignment.Center, GUIFont font = null, Submarine sub = null)
        {
            coloredText = new ColoredText(text, color, false, false);
            WorldSpace = true;
            pos = position;
            Timer = lifeTime;
            Velocity = velocity;
            this.lifeTime = lifeTime;

            Font = font;

            size = font.MeasureString(text);

            Origin = new Vector2((int)(0.5f * size.X), (int)(0.5f * size.Y));
            if (textAlignment.HasFlag(Alignment.Left))
                Origin.X -= size.X * 0.5f;

            if (textAlignment.HasFlag(Alignment.Right))
                Origin.X += size.X * 0.5f;

            if (textAlignment.HasFlag(Alignment.Top))
                Origin.Y -= size.Y * 0.5f;

            if (textAlignment.HasFlag(Alignment.Bottom))
                Origin.Y += size.Y * 0.5f;

            Submarine = sub;
        }
    }
}
