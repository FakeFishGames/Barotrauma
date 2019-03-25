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
                Color color;
                if (Entity is Character)
                {
                    color = Color.Yellow;
                }
                else if (Entity is Item)
                {
                    color = Color.Orange;
                }
                else
                {
                    color = Color.OrangeRed;
                }
                ShapeExtensions.DrawCircle(spriteBatch, pos, SoundRange, 100, color, thickness: 1 / Screen.Selected.Cam.Zoom);
                ShapeExtensions.DrawCircle(spriteBatch, pos, 3, 8, color, thickness: 2 / Screen.Selected.Cam.Zoom);
            }
            if (sightRange > 0.0f)
            {
                Color color;
                if (Entity is Character)
                {
                    color = Color.CornflowerBlue;
                }
                else if (Entity is Item)
                {
                    color = Color.CadetBlue;
                }
                else
                {
                    color = Color.WhiteSmoke;
                }
                ShapeExtensions.DrawCircle(spriteBatch, pos, SightRange, 100, color, thickness: 1 / Screen.Selected.Cam.Zoom);
                ShapeExtensions.DrawCircle(spriteBatch, pos, 6, 8, color, thickness: 2 / Screen.Selected.Cam.Zoom);
            }
        }
    }
}
