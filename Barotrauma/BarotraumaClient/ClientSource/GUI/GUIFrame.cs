using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace Barotrauma
{
    public class GUIFrame : GUIComponent
    {
        public float OutlineThickness { get; set; }

        public GUIFrame(RectTransform rectT, string style = "", Color? color = null) : base(style, rectT)
        {
            Enabled = true;
            if (color.HasValue)
            {
                this.color = color.Value;
            }
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Color currColor = GetColor(State);

            if (sprites == null || !sprites.Any(s => s.Value.Any())) GUI.DrawRectangle(spriteBatch, Rect, currColor * (currColor.A/255.0f), true);
            base.Draw(spriteBatch);

            if (OutlineColor != Color.Transparent)
            {
                GUI.DrawRectangle(spriteBatch, Rect, OutlineColor, false, thickness: OutlineThickness);
            }
        }
    }
}
