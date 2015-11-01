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

        public bool Enabled
        {
            get;
            set;
        }

        public GUITickBox(Rectangle rect, string label, Alignment alignment, GUIComponent parent)
            : base(null)
        {
            if (parent != null)
                parent.AddChild(this);

            box = new GUIFrame(rect, Color.DarkGray, null, this);
            box.HoverColor = Color.Gray;
            box.SelectedColor = Color.DarkGray;


            text = new GUITextBlock(new Rectangle(rect.X + 40, rect.Y, 200, rect.Height), label, Color.Transparent, Color.White, Alignment.TopLeft, null, this);

            this.rect = new Rectangle(box.Rect.X, box.Rect.Y, 240, rect.Height);

            Enabled = true;
        }

        public override void Update(float deltaTime)
        {
            if (!Visible || !Enabled) return;

            if (text.Rect.Contains(PlayerInput.MousePosition)) MouseOn = this;

            if (box.Rect.Contains(PlayerInput.MousePosition))
            {
                //ToolTip = this.ToolTip;
                MouseOn = this;

                box.State = ComponentState.Hover;

                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
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

            GUI.DrawRectangle(spriteBatch, new Rectangle(box.Rect.X + 1, box.Rect.Y + 1, box.Rect.Width - 2, box.Rect.Height - 2),
                box.State == ComponentState.Hover ? new Color(50,50,50,255) : Color.Black, true);

            if (!selected) return;
            GUI.DrawRectangle(spriteBatch, new Rectangle(box.Rect.X + 5, box.Rect.Y + 5, box.Rect.Width - 10, box.Rect.Height - 10), 
                Color.Green * 0.8f, true);
            
        }
    }
}
