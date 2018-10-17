using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class MotionSensor : IDrawableComponent
    {
        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (!editing || !MapEntity.SelectedList.Contains(item)) return;

            Vector2 pos = item.WorldPosition + detectOffset;
            pos.Y = -pos.Y;
            GUI.DrawRectangle(spriteBatch, pos - new Vector2(rangeX, rangeY), new Vector2(rangeX, rangeY) * 2.0f, Color.Cyan * 0.5f, isFilled: false, thickness: 2);
        }
    }
}

