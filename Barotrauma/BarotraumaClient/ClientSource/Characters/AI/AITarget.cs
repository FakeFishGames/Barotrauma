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
            float thickness = 1 / Screen.Selected.Cam.Zoom;

            float offset = MathUtils.VectorToAngle(new Vector2(sectorDir.X, -sectorDir.Y)) - (sectorRad / 2f);
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

                if (sectorRad < MathHelper.TwoPi)
                {
                    spriteBatch.DrawSector(pos, SoundRange, sectorRad, 100, color, offset: offset, thickness: thickness);
                }
                else
                {
                    spriteBatch.DrawCircle(pos, SoundRange, 100, color, thickness: thickness);
                }
                spriteBatch.DrawCircle(pos, 3, 8, color, thickness: 2 / Screen.Selected.Cam.Zoom);
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
                    // disable the indicators for structures and hulls, because they clutter the debug view
                    return;
                }
                if (sectorRad < MathHelper.TwoPi)
                {
                    spriteBatch.DrawSector(pos, SightRange, sectorRad, 100, color, offset: offset, thickness: thickness);
                }
                else
                {
                    spriteBatch.DrawCircle(pos, SightRange, 100, color, thickness: thickness);
                }
                ShapeExtensions.DrawCircle(spriteBatch, pos, 6, 8, color, thickness: 2 / Screen.Selected.Cam.Zoom);
                GUI.DrawLine(spriteBatch, pos, pos + Vector2.UnitY * SightRange, color, width: (int)(1 / Screen.Selected.Cam.Zoom) + 1);
            }
        }
    }
}
