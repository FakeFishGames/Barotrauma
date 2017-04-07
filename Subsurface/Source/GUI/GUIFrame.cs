using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    public class GUIFrame : GUIComponent
    {        
        public GUIFrame(Rectangle rect, string style = "", GUIComponent parent = null)
            : this(rect, null, (Alignment.Left | Alignment.Top), style, parent)
        {
        }


        public GUIFrame(Rectangle rect, Color color, string style = "", GUIComponent parent = null)
            : this(rect, color, (Alignment.Left | Alignment.Top), style, parent)
        {
        }

        public GUIFrame(Rectangle rect, Color? color, Alignment alignment, string style = "", GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;

            this.alignment = alignment;

            if (color != null) this.color = (Color)color;

            if (parent != null)
            {
                parent.AddChild(this);
            }
            else
            {
                UpdateDimensions();
            }

            //if (style != null) ApplyStyle(style);
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (!Visible) return;
                           
            Color currColor = color;
            if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) currColor = hoverColor;

            if (!sprites.Any()) GUI.DrawRectangle(spriteBatch, rect, currColor * (currColor.A/255.0f), true);
            base.Draw(spriteBatch);

            if (OutlineColor != Color.Transparent)
            {
                GUI.DrawRectangle(spriteBatch, rect, OutlineColor * (OutlineColor.A/255.0f), false);
            }


            DrawChildren(spriteBatch);
        }

    }
}
