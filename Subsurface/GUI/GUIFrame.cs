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

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
                           
            Color newColor = color;
            if (state == ComponentState.Selected) newColor = selectedColor;
            if (state == ComponentState.Hover) newColor = hoverColor;

            GUI.DrawRectangle(spriteBatch, rect, newColor * alpha, true);
            DrawChildren(spriteBatch);        
        }

    }
}
