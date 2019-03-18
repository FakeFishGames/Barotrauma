using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Particles
{
    class Decal
    {
        public readonly DecalPrefab Prefab;
        private Vector2 position;

        public readonly Sprite Sprite;

        private float fadeTimer;
        
        public float FadeTimer
        {
            get { return fadeTimer; }
            set { fadeTimer = MathHelper.Clamp(value, 0.0f, LifeTime); }
        }

        public float FadeInTime
        {
            get { return Prefab.FadeInTime; }
        }

        public float FadeOutTime
        {
            get { return Prefab.FadeOutTime; }
        }

        public float LifeTime
        {
            get { return Prefab.LifeTime; }
        }
        
        public Color Color
        {
            get;
            set;
        }

        public Vector2 WorldPosition
        {
            get
            {
                Vector2 worldPos = position
                    + clippedSourceRect.Size.ToVector2() / 2 * scale
                    + hull.Rect.Location.ToVector2();
                if (hull.Submarine != null) { worldPos += hull.Submarine.DrawPosition; }
                return worldPos;
            }
        }

        private Hull hull;

        private float scale;

        private Rectangle clippedSourceRect;

        public Decal(DecalPrefab prefab, float scale, Vector2 worldPosition, Hull hull)
        {
            Prefab = prefab;

            this.hull = hull;

            //transform to hull-relative coordinates so we don't have to worry about the hull moving
            position = worldPosition - hull.WorldRect.Location.ToVector2();

            Vector2 drawPos = position + hull.Rect.Location.ToVector2();

            Sprite = prefab.Sprites[Rand.Range(0, prefab.Sprites.Count, Rand.RandSync.Unsynced)];
            Color = prefab.Color;

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

            position -= new Vector2(Sprite.size.X / 2 * scale - overFlowAmount.X, -Sprite.size.Y / 2 * scale + overFlowAmount.Y);

            this.scale = scale;
        }

        public void Update(float deltaTime)
        {
            fadeTimer += deltaTime;
        }

        public void StopFadeIn()
        {
            Color *= GetAlpha();
            fadeTimer = Prefab.FadeInTime;
        }

        public void Draw(SpriteBatch spriteBatch, Hull hull, float depth)
        {
            Vector2 drawPos = position + hull.Rect.Location.ToVector2();
            if (hull.Submarine != null) { drawPos += hull.Submarine.DrawPosition; }
            drawPos.Y = -drawPos.Y;
            
            spriteBatch.Draw(Sprite.Texture, drawPos, clippedSourceRect, Color * GetAlpha(), 0, Vector2.Zero, scale, SpriteEffects.None, depth);
        }

        private float GetAlpha()
        {
            if (fadeTimer < Prefab.FadeInTime)
            {
                return fadeTimer / Prefab.FadeInTime;
            }
            else if (fadeTimer > Prefab.LifeTime - Prefab.FadeOutTime)
            {
                return (Prefab.LifeTime - fadeTimer) / Prefab.FadeOutTime;
            }
            return 1.0f;
        }
    }
}
