using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    internal partial class Planter
    {
        public Vector2 DrawSize => CalculateSize();

        private Vector2 CalculateSize()
        {
            if (GrowableSeeds.All(s => s == null)) { return Vector2.Zero; }

            Point pos = item.DrawPosition.ToPoint();
            Rectangle rect = new Rectangle(pos, Point.Zero);

            for (int i = 0; i < GrowableSeeds.Length; i++)
            {
                Growable seed = GrowableSeeds[i];
                PlantSlot slot = PlantSlots.ContainsKey(i) ? PlantSlots[i] : NullSlot; 
                if (seed == null) { continue; }

                foreach (VineTile vine in seed.Vines)
                {
                    Rectangle worldRect = vine.Rect;
                    worldRect.Location += slot.Offset.ToPoint();
                    worldRect.Location += pos;
                    rect = Rectangle.Union(rect, worldRect);
                }
            }

            Vector2 result = new Vector2(MaxDistance(pos.X, rect.Left, rect.Right) * 2, MaxDistance(pos.Y, rect.Top, rect.Bottom) * 2);
            return result;

            static float MaxDistance(float origin, float x, float y)
            {
                return Math.Max(Math.Abs(origin - x), Math.Abs(origin - y));
            }
        }

        private LightComponent lightComponent;

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            for (var i = 0; i < GrowableSeeds.Length; i++)
            {
                Growable growable = GrowableSeeds[i];
                PlantSlot slot = PlantSlots.ContainsKey(i) ? PlantSlots[i] : NullSlot;
                growable?.Draw(spriteBatch, this, slot.Offset, itemDepth);
            }
        }
    }
}