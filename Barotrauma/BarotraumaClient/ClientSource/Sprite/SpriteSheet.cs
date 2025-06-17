﻿using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class SpriteSheet : Sprite
    {
        public override void Draw(ISpriteBatch spriteBatch, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = default(float?))
        {
            if (texture == null) return;

            spriteBatch.Draw(texture, pos + offset, sourceRects[0], color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }

        public void Draw(ISpriteBatch spriteBatch, int spriteIndex, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = default(float?))
        {
            if (texture == null) return;

            spriteBatch.Draw(texture, pos + offset, sourceRects[MathHelper.Clamp(spriteIndex, 0, sourceRects.Length - 1)], color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }

        /// <summary>
        /// When this spritesheet is used for an animation, returns the current spriteIndex based on the given animation speed.
        /// </summary>
        /// <param name="animationSpeed">Animation speed in frames per second</param>
        /// <param name="animatePaused">Should the animation run when paused? Defaults to false.</param>
        public int GetAnimatedSpriteIndex(float animationSpeed, bool animatePaused = false)
        {
            return (int)(Math.Floor((animatePaused ? Timing.TotalTime : Timing.TotalTimeUnpaused) * animationSpeed) % FrameCount);
        }
    }
}
