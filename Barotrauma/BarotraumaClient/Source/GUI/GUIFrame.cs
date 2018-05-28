using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace Barotrauma
{
    public class GUIFrame : GUIComponent
    {
        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIFrame(Rectangle rect, string style = "", GUIComponent parent = null)
            : this(rect, null, (Alignment.Left | Alignment.Top), style, parent)
        {
        }
        
        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIFrame(Rectangle rect, Color color, string style = "", GUIComponent parent = null)
            : this(rect, color, (Alignment.Left | Alignment.Top), style, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIFrame(Rectangle rect, Color? color, Alignment alignment, string style = "", GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;

            this.alignment = alignment;

            if (color != null) this.color = (Color)color;

            if (parent != null)
            {
                //parent.AddChild(this);
            }

            //if (style != null) ApplyStyle(style);
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIFrame(RectTransform rectT, string style = "", Color? color = null) : base(style, rectT)
        {
            if (color.HasValue)
            {
                this.color = color.Value;
            }
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
                           
            Color currColor = color;
            if (state == ComponentState.Selected) currColor = selectedColor;
            if (state == ComponentState.Hover) currColor = hoverColor;

            if (sprites == null || !sprites.Any()) GUI.DrawRectangle(spriteBatch, Rect, currColor * (currColor.A/255.0f), true);
            base.Draw(spriteBatch);

            if (OutlineColor != Color.Transparent)
            {
                GUI.DrawRectangle(spriteBatch, Rect, OutlineColor * (OutlineColor.A/255.0f), false);
            }
        }
    }
}
