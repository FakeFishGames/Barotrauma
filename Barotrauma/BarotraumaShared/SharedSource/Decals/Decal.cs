using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Decal
    {
        public readonly DecalPrefab Prefab;
        private Vector2 position;

        private float fadeTimer;

        public readonly Sprite Sprite;

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

        public float BaseAlpha
        {
            get;
            set;
        } = 1.0f;

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
                    + clippedSourceRect.Size.ToVector2() / 2 * Scale
                    + hull.Rect.Location.ToVector2();
                if (hull.Submarine != null) { worldPos += hull.Submarine.DrawPosition; }
                return worldPos;
            }
        }

        public Vector2 CenterPosition
        {
            get;
            private set;
        }

        public Vector2 NonClampedPosition
        {
            get;
            private set;
        }

        public int SpriteIndex
        {
            get;
            private set;
        }

        private readonly HashSet<BackgroundSection> affectedSections;

        private readonly Hull hull;

        public readonly float Scale;

        private Rectangle clippedSourceRect;

        private bool cleaned = false;

        public Decal(DecalPrefab prefab, float scale, Vector2 worldPosition, Hull hull, int? spriteIndex = null)
        {
            Prefab = prefab;

            this.hull = hull;

            //transform to hull-relative coordinates so we don't have to worry about the hull moving
            NonClampedPosition = position = worldPosition - hull.WorldRect.Location.ToVector2();

            Vector2 drawPos = position + hull.Rect.Location.ToVector2();

            SpriteIndex = spriteIndex ?? Rand.Range(0, prefab.Sprites.Count, Rand.RandSync.Unsynced);
            Sprite = prefab.Sprites[SpriteIndex];
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

            CenterPosition = position;

            position -= new Vector2(Sprite.size.X / 2 * scale - overFlowAmount.X, -Sprite.size.Y / 2 * scale + overFlowAmount.Y);

            this.Scale = scale;
            
            foreach (BackgroundSection section in hull.GetBackgroundSectionsViaContaining(new Rectangle((int)position.X, (int)position.Y - drawRect.Height, drawRect.Width, drawRect.Height)))
            {
                affectedSections ??= new HashSet<BackgroundSection>();
                affectedSections.Add(section);
            }
        }

        public void Update(float deltaTime)
        {
            fadeTimer += deltaTime;
        }

        public void ForceRefreshFadeTimer(float val)
        {
            cleaned = false;
            fadeTimer = val;
        }

        public void StopFadeIn()
        {
            Color *= GetAlpha();
            fadeTimer = Prefab.FadeInTime;
        }

        public bool AffectsSection(BackgroundSection section)
        {
            return affectedSections != null && affectedSections.Contains(section);
        }

        public void Clean(float val)
        {
            cleaned = true;
            float sizeModifier = MathHelper.Clamp(Sprite.size.X * Sprite.size.Y * Scale / 10000, 1.0f, 25.0f);
            BaseAlpha -= val * -1 / sizeModifier;
        }

        private float GetAlpha()
        {
            if (fadeTimer < Prefab.FadeInTime && !cleaned)
            {
                return BaseAlpha * fadeTimer / Prefab.FadeInTime;
            }
            else if (cleaned || fadeTimer > Prefab.LifeTime - Prefab.FadeOutTime)
            {
                return BaseAlpha * Math.Min((Prefab.LifeTime - fadeTimer) / Prefab.FadeOutTime, 1.0f);
            }
            return BaseAlpha;
        }
    }
}
