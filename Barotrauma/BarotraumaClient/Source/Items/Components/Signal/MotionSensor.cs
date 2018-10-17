using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class MotionSensor : IDrawableComponent
    {
        public void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (!editing || !MapEntity.SelectedList.Contains(item)) return;

            Vector2 pos = new Vector2(item.DrawPosition.X, -item.DrawPosition.Y);
            GUI.DrawRectangle(spriteBatch, pos - new Vector2(rangeX, rangeY) / 2, new Vector2(rangeX, rangeY), Color.Cyan * 0.5f, isFilled: false, thickness: 2);
        }
    }
}

