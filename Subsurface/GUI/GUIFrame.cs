using Microsoft.Xna.Framework;

namespace Subsurface
{
    class GUIFrame : GUIComponent
    {
        public GUIFrame(Rectangle rect, Color color, Alignment alignment, GUIStyle style, GUIComponent parent = null)
            : this(rect, color, alignment, parent)
        {
            hoverColor = style.hoverColor;
            selectedColor = style.selectedColor;
        }

        public GUIFrame(Rectangle rect, Color color, GUIComponent parent = null)
            : this(rect,color,(Alignment.Left | Alignment.Top), parent)
        {
        }

        public GUIFrame(Rectangle rect, Color color, Alignment alignment, GUIComponent parent = null)
        {
            this.rect = rect;

            this.alignment = alignment;
            
            this.color = color;
            if (parent!=null)
                parent.AddChild(this);
        }

    }
}
