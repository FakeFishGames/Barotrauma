using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class MotionSensor : IDrawableComponent
    {
        public Vector2 DrawSize
        {
            get { return new Vector2(rangeX, rangeY) * 2.0f; }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing, float itemDepth = -1)
        {
            if (!editing || !MapEntity.SelectedList.Contains(item)) { return; }

            Vector2 pos = item.WorldPosition + TransformedDetectOffset;
            pos.Y = -pos.Y;
            GUI.DrawRectangle(spriteBatch, pos - new Vector2(rangeX, rangeY), new Vector2(rangeX, rangeY) * 2.0f, Color.Cyan * 0.5f, isFilled: false, thickness: 2);
        }
    }
}

