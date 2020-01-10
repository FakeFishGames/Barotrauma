using Microsoft.Xna.Framework;

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

        public ScalableFont Font
        {
            get;
            private set;
        }
        
        public GUIMessage(string text, Color color, float lifeTime, ScalableFont font = null)
        {
            coloredText = new ColoredText(text, color, false);
            this.lifeTime = lifeTime;
            Timer = lifeTime;

            size = font.MeasureString(text);
            Origin = new Vector2(0, size.Y * 0.5f);

            Font = font;
        }

        public GUIMessage(string text, Color color, Vector2 worldPosition, Vector2 velocity, float lifeTime, Alignment textAlignment = Alignment.Center, ScalableFont font = null)
        {
            coloredText = new ColoredText(text, color, false);
            WorldSpace = true;
            pos = worldPosition;
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
        }
    }
}
