using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Particles
{
    class Decal
    {
        public readonly DecalPrefab Prefab;
        public Vector2 Position;

        public readonly Sprite Sprite;

        private float fadeTimer;

        public Color Color
        {
            get { return Prefab.Color; }
        }

        public float FadeTimer
        {
            get { return fadeTimer; }
        }

        public float LifeTime
        {
            get { return Prefab.LifeTime; }
        }

        private float scale;

        private Rectangle clippedSourceRect;

        public Decal(DecalPrefab prefab, float scale, Vector2 worldPosition, Hull hull)
        {
            Prefab = prefab;

            //transform to hull-relative coordinates so we don't have to worry about the hull moving
            Position = worldPosition - hull.WorldRect.Location.ToVector2();

            Vector2 drawPos = Position + hull.Rect.Location.ToVector2();

            Sprite = prefab.Sprites[Rand.Range(0, prefab.Sprites.Count, Rand.RandSync.Unsynced)];

            Rectangle drawRect = new Rectangle(
                (int)(drawPos.X - Sprite.size.X / 2 * scale),
                (int)(drawPos.Y + Sprite.size.Y / 2 * scale),
                (int)(Sprite.size.X * scale),
                (int)(Sprite.size.Y * scale));

            Rectangle overFlowAmount = new Rectangle(
                (int)Math.Max(hull.Rect.X - drawRect.X, 0.0f),
                (int)Math.Max(drawRect.Y - hull.Rect.Y, 0.0f),
                (int)Math.Max(drawRect.Right - hull.Rect.Right, 0.0f),
                (int)Math.Max((hull.Rect.Y - hull.Rect.Height) - (drawRect.Y - drawRect.Height), 0.0f));           

            clippedSourceRect = new Rectangle(
                Sprite.SourceRect.X + (int)(overFlowAmount.X / scale),
                Sprite.SourceRect.Y + (int)(overFlowAmount.Y / scale),
                Sprite.SourceRect.Width - (int)((overFlowAmount.X + overFlowAmount.Width) / scale),
                Sprite.SourceRect.Height - (int)((overFlowAmount.Y + overFlowAmount.Height) / scale));

            Position -= new Vector2(Sprite.size.X / 2 * scale - overFlowAmount.X, -Sprite.size.Y / 2 * scale + overFlowAmount.Y);

            this.scale = scale;
        }

        public void Update(float deltaTime)
        {
            fadeTimer += deltaTime;
        }

        public void Draw(SpriteBatch spriteBatch, Hull hull)
        {
            Vector2 drawPos = Position + hull.Rect.Location.ToVector2();
            drawPos += hull.Submarine.DrawPosition;
            drawPos.Y = -drawPos.Y;

            float a = 1.0f;
            if (fadeTimer > Prefab.LifeTime - Prefab.FadeTime)
            {
                a = (Prefab.LifeTime - fadeTimer) / Prefab.FadeTime;
            }
            
            spriteBatch.Draw(Sprite.Texture, drawPos, clippedSourceRect, Color * a, 0, Vector2.Zero , scale, SpriteEffects.None, 1);
        }
    }
}
