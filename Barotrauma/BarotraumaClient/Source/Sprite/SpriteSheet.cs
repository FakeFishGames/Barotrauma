using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class SpriteSheet : Sprite
    {
        public override void Draw(SpriteBatch spriteBatch, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = default(float?))
        {
            if (texture == null) return;

            spriteBatch.Draw(texture, pos + offset, sourceRects[0], color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }

        public void Draw(SpriteBatch spriteBatch, int spriteIndex, Vector2 pos, Color color, Vector2 origin, float rotate, Vector2 scale, SpriteEffects spriteEffect = SpriteEffects.None, float? depth = default(float?))
        {
            if (texture == null) return;

            spriteBatch.Draw(texture, pos + offset, sourceRects[MathHelper.Clamp(spriteIndex, 0, sourceRects.Length - 1)], color, rotation + rotate, origin, scale, spriteEffect, depth == null ? this.depth : (float)depth);
        }
    }
}
