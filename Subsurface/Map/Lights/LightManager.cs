using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface.Lights
{
    class LightManager
    {
        public static Vector2 viewPos;

        public static bool fowEnabled = true;

        public static void DrawFow(GraphicsDevice graphics, Camera cam)
        {
            if (!fowEnabled) return;
            foreach (ConvexHull convexHull in ConvexHull.list)
            {
                convexHull.DrawShadows(graphics, cam, viewPos);
            }
        }
    }

}
