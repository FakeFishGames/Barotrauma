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
            var pos = new Vector2(WorldPosition.X, -WorldPosition.Y);
            if (soundRange > 0.0f)
            {
                ShapeExtensions.DrawCircle(spriteBatch, pos, SoundRange, 100, Color.CornflowerBlue, thickness: 1 / Screen.Selected.Cam.Zoom);
            }
            if (sightRange > 0.0f)
            {
                ShapeExtensions.DrawCircle(spriteBatch, pos, SightRange, 100, Color.Orange, thickness: 1 / Screen.Selected.Cam.Zoom);
            }
        }
    }
}
