using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
{
    class GUIButton : GUIComponent
    {
        protected GUITextBlock textBlock;
        protected GUIFrame frame;

        public delegate bool OnClickedHandler(GUIButton button, object obj);
        public OnClickedHandler OnClicked;

        public delegate bool OnPressedHandler();
        public OnPressedHandler OnPressed;

        public bool Enabled { get; set; }
        
        public string Text
        {
            get { return textBlock.Text; }
            set { textBlock.Text = value; }
        }

        public GUIButton(Rectangle rect, string text, GUIStyle style, GUIComponent parent = null)
            : this(rect, text, null, Alignment.Left, style, parent)
        {
        }

        public GUIButton(Rectangle rect, string text, Alignment alignment, GUIStyle style, GUIComponent parent = null)
            : this(rect, text, null, alignment, style, parent)
        {
        }

        public GUIButton(Rectangle rect, string text, Color? color, GUIStyle style, GUIComponent parent = null)
            : this(rect, text, color, (Alignment.Left | Alignment.Top), style, parent)
        {
        }

        public GUIButton(Rectangle rect, string text, Color? color, Alignment alignment, GUIStyle style, GUIComponent parent = null)
            :base (style)
        {
            this.rect = rect;
            if (color!=null) this.color = (Color)color;
            this.alignment = alignment;

            Enabled = true;

            if (parent != null)
                parent.AddChild(this);

            frame = new GUIFrame(new Rectangle(0,0,0,0), style, this);
            if (style!=null) style.Apply(frame, this);

            textBlock = new GUITextBlock(new Rectangle(0, 0, 0, 0), text,
                Color.Transparent, (this.style==null) ? Color.Black : this.style.textColor, 
                Alignment.Center, style, this);
        }
        
        public override void Draw(SpriteBatch spriteBatch)
        {
            if (rect.Contains(PlayerInput.GetMouseState.Position) && Enabled && (MouseOn == null || MouseOn == this || IsParentOf(MouseOn)))
            {
                state = ComponentState.Hover;
                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                {
                    if (OnPressed != null)
                    {
                        if (OnPressed()) state = ComponentState.Selected;
                    }
                }
                else if (PlayerInput.LeftButtonClicked())
                {
                    if (OnClicked != null)
                    {
                        if (OnClicked(this, UserData)) state = ComponentState.Selected;
                    }
                }
            }
            else
            {
                state = ComponentState.None;
            }

            frame.State = state;

            //Color currColor = color;
            //if (state == ComponentState.Hover) currColor = hoverColor;
            //if (state == ComponentState.Selected) currColor = selectedColor;

            //GUI.DrawRectangle(spriteBatch, rect, currColor * alpha, true);

            ////spriteBatch.DrawString(HUD.font, text, new Vector2(rect.X+rect.Width/2, rect.Y+rect.Height/2), Color.Black, 0.0f, new Vector2(0.5f,0.5f), 1.0f, SpriteEffects.None, 0.0f);

            //GUI.DrawRectangle(spriteBatch, rect, Color.Black * alpha, false);

            DrawChildren(spriteBatch);

            if (!Enabled) GUI.DrawRectangle(spriteBatch, rect, Color.Gray*0.5f, true);
        }
    }
}
