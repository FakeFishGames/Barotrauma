using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface.Lights
{
    class LightManager
    {
        public static Vector2 ViewPos;

        public static bool FowEnabled = true;

        public static void DrawFow(GraphicsDevice graphics, Camera cam)
        {
            if (!FowEnabled) return;
            foreach (ConvexHull convexHull in ConvexHull.list)
            {
                convexHull.DrawShadows(graphics, cam, ViewPos);
            }
        }
    }

}
