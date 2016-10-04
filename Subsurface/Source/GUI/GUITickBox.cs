using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{
    public class GUITickBox : GUIComponent
    {
        GUIFrame box;
        GUITextBlock text;

        public delegate bool OnSelectedHandler(GUITickBox obj);
        public OnSelectedHandler OnSelected;

        private bool selected;

        public bool Selected
        {
            get { return selected; }
            set 
            { 
                if (value == selected) return;
                selected = value;
                state = (selected) ? ComponentState.Selected : ComponentState.None;
            }
        }

        private bool enabled;

        public bool Enabled
        {
            get
            {
                return enabled;
            }
            set
            {
                enabled = value;
                text.TextColor = enabled ? Color.White : Color.White * 0.5f;
            }
        }

        public override Rectangle Rect
        {
            get
            {
                return rect;
            }
            set
            {
                box.Rect = new Rectangle(value.X,value.Y,box.Rect.Width,box.Rect.Height);
                text.Rect = new Rectangle(box.Rect.Right + 10, box.Rect.Y + 2, 20, box.Rect.Height);
                rect = value;
            }
        }

        public GUITickBox(Rectangle rect, string label, Alignment alignment, GUIComponent parent)
            : this(rect, label, alignment, GUI.Font, parent)
        {
        }

        public GUITickBox(Rectangle rect, string label, Alignment alignment, SpriteFont font, GUIComponent parent)
            : base(null)
        {
            if (parent != null)
                parent.AddChild(this);

            box = new GUIFrame(rect, Color.DarkGray, null, this);
            box.HoverColor = Color.Gray;
            box.SelectedColor = Color.DarkGray;
            
            text = new GUITextBlock(new Rectangle(rect.Right + 10, rect.Y+2, 20, rect.Height), label, GUI.Style, this, font);

            this.rect = new Rectangle(box.Rect.X, box.Rect.Y, 240, rect.Height);

            Enabled = true;
        }

        public override void Update(float deltaTime)
        {
            if (!Visible || !Enabled) return;

            if (MouseOn != null && MouseOn != this && !MouseOn.IsParentOf(this)) return;

            if (text.Rect.Contains(PlayerInput.MousePosition)) MouseOn = this;

            if (box.Rect.Contains(PlayerInput.MousePosition))
            {
                //ToolTip = this.ToolTip;
                MouseOn = this;

                box.State = ComponentState.Hover;

                if (PlayerInput.LeftButtonHeld())
                {
                    box.State = ComponentState.Selected;                    
                }


                if (PlayerInput.LeftButtonClicked())
                {
                    Selected = !Selected;
                    if (OnSelected != null) OnSelected(this);
                }
            }
            else
            {
                box.State = ComponentState.None;
            }
            
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            DrawChildren(spriteBatch);

            float alpha = enabled ? 1.0f : 0.8f;

            GUI.DrawRectangle(spriteBatch, new Rectangle(box.Rect.X + 1, box.Rect.Y + 1, box.Rect.Width - 2, box.Rect.Height - 2),
                (box.State == ComponentState.Hover ? new Color(50, 50, 50, 255) : Color.Black) * alpha, true);

            if (!selected) return;
            GUI.DrawRectangle(spriteBatch, new Rectangle(box.Rect.X + 5, box.Rect.Y + 5, box.Rect.Width - 10, box.Rect.Height - 10),
                Color.Green * 0.8f * alpha, true);
            
        }
    }
}
