using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
{
    class GUITickBox : GUIComponent
    {
        GUIFrame box;
        GUITextBlock text;

        public delegate bool OnSelectedHandler(object obj);
        public OnSelectedHandler OnSelected;

        bool selected;

        public GUITickBox(Rectangle rect, string label, Alignment alignment, GUIComponent parent)
        {
            if (parent != null)
                parent.AddChild(this);

            box = new GUIFrame(new Rectangle(rect.X, rect.Y, 30, 30), Color.LightGray, this);
            text = new GUITextBlock(new Rectangle(rect.X + 40, rect.Y, 200, 30), label, Color.Transparent, Color.White, Alignment.Left | Alignment.CenterY, this);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (box.Rect.Contains(PlayerInput.GetMouseState.Position))
            {
                box.State = ComponentState.Hover;

                if (PlayerInput.GetMouseState.LeftButton == ButtonState.Pressed)
                    box.State = ComponentState.Selected;

                if (PlayerInput.LeftButtonClicked())
                {
                    selected = !selected;
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
            DrawChildren(spriteBatch);

            if (selected)
            {
                GUI.DrawRectangle(spriteBatch, new Rectangle(box.Rect.X + 5, box.Rect.Y + 5, box.Rect.Width - 10, box.Rect.Height - 10), Color.Green * alpha, true);
            }
        }
    }
}
