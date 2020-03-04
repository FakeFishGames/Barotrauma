using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    partial class AITarget
    {
        public static bool ShowAITargets;

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!ShowAITargets) { return; }
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
                GUI.DrawLine(spriteBatch, pos, pos + Vector2.UnitY * SoundRange, color, width: (int)(1 / Screen.Selected.Cam.Zoom) + 1);
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
                    //color = Color.WhiteSmoke;
                    // disable the indicators for structures, because they clutter the debug view
                    return;
                }
                ShapeExtensions.DrawCircle(spriteBatch, pos, SightRange, 100, color, thickness: 1 / Screen.Selected.Cam.Zoom);
                ShapeExtensions.DrawCircle(spriteBatch, pos, 6, 8, color, thickness: 2 / Screen.Selected.Cam.Zoom);
                GUI.DrawLine(spriteBatch, pos, pos + Vector2.UnitY * SightRange, color, width: (int)(1 / Screen.Selected.Cam.Zoom) + 1);
            }
        }
    }
}
