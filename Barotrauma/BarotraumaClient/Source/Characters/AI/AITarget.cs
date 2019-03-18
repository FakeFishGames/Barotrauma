using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class AITarget
    {
        public static bool ShowAITargets;

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!ShowAITargets) return;

            var rangeSprite = GUI.SubmarineIcon;

            if (soundRange > 0.0f)
            {
                rangeSprite.Draw(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                    Color.Cyan * 0.1f, rangeSprite.Origin,
                    0.0f, soundRange / rangeSprite.size.X);
                rangeSprite.Draw(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                    Color.Cyan, rangeSprite.Origin,
                    0.0f, 1.0f);
            }

            if (sightRange > 0.0f)
            {
                rangeSprite.Draw(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                    Color.Orange * 0.1f, rangeSprite.Origin,
                    0.0f, sightRange / rangeSprite.size.X);
                rangeSprite.Draw(spriteBatch,
                    new Vector2(WorldPosition.X, -WorldPosition.Y),
                    Color.Orange, rangeSprite.Origin,
                    0.0f, 1.0f);
            }
        }
    }
}
