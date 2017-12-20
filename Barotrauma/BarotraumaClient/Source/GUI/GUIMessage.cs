using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class GUIMessage
    {
        private ColoredText coloredText;
        private Vector2 pos;

        private float lifeTime;

        private Vector2 size;

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

        public Vector2 Size
        {
            get { return size; }
        }

        public Vector2 Origin;

        public float LifeTime
        {
            get { return lifeTime; }
            set { lifeTime = value; }
        }

        public Alignment Alignment
        {
            get;
            private set;
        }

        public bool Centered;

        public GUIMessage(string text, Color color, Vector2 position, float lifeTime, Alignment textAlignment, bool centered)
        {
            coloredText = new ColoredText(text, color, false);
            pos = position;
            this.lifeTime = lifeTime;
            this.Alignment = textAlignment;

            if (textAlignment.HasFlag(Alignment.Left))
                Origin.X += size.X * 0.5f;

            if (textAlignment.HasFlag(Alignment.Right))
                Origin.X -= size.X * 0.5f;

            if (textAlignment.HasFlag(Alignment.Top))
                Origin.Y += size.Y * 0.5f;

            if (textAlignment.HasFlag(Alignment.Bottom))
                Origin.Y -= size.Y * 0.5f;

            if (centered)
            {
                Origin = new Vector2((int)(0.5f * size.X), (int)(0.5f * size.Y));
            }
            else
            {
                size = GUI.Font.MeasureString(text);
            }
        }
    }
}
