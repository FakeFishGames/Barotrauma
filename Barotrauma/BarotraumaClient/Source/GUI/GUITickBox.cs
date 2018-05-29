using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUITickBox : GUIComponent
    {
        private GUIFrame box;
        private GUITextBlock text;

        public delegate bool OnSelectedHandler(GUITickBox obj);
        public OnSelectedHandler OnSelected;

        public static int size = 20;

        private bool selected;

        public bool Selected
        {
            get { return selected; }
            set 
            { 
                if (value == selected) return;
                selected = value;
                state = (selected) ? ComponentState.Selected : ComponentState.None;

                box.State = state;
            }
        }
        
        public Color TextColor
        {
            get { return text.TextColor; }
            set { text.TextColor = value; }
        }

        public override Rectangle MouseRect
        {
            get { return ClampMouseRectToParent ? ClampRect(box.Rect) : box.Rect; }
        }

        public override ScalableFont Font
        {
            get
            {
                return base.Font;
            }

            set
            {
                base.Font = value;
                if (text != null) text.Font = value;
            }
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUITickBox(RectTransform rectT, string label, ScalableFont font = null, string style = "") : base(null, rectT)
        {
            box = new GUIFrame(new RectTransform(new Point(rectT.Rect.Height, rectT.Rect.Height), rectT, Anchor.CenterLeft)
            {
                IsFixedSize = false
            }, string.Empty, Color.DarkGray)
            {
                HoverColor = Color.Gray,
                SelectedColor = Color.DarkGray,
                CanBeFocused = false
            };
            GUI.Style.Apply(box, style == "" ? "GUITickBox" : style);
            text = new GUITextBlock(new RectTransform(Vector2.One, rectT, Anchor.CenterLeft) { AbsoluteOffset = new Point(box.Rect.Width, 0) }, label, font: font, textAlignment: Alignment.CenterLeft);
            GUI.Style.Apply(text, "GUIButtonHorizontal", this);
            Enabled = true;

            ResizeBox();

            rectT.ScaleChanged += ResizeBox;
            rectT.SizeChanged += ResizeBox;
        }

        private void ResizeBox()
        {
            box.RectTransform.NonScaledSize = new Point(box.RectTransform.NonScaledSize.Y);
        }
        
        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            if (GUI.MouseOn == this && Enabled)
            {
                box.State = ComponentState.Hover;

                if (PlayerInput.LeftButtonHeld())
                {
                    box.State = ComponentState.Selected;                    
                }

                if (PlayerInput.LeftButtonClicked())
                {
                    Selected = !Selected;
                    OnSelected?.Invoke(this);
                }
            }
            else
            {
                box.State = ComponentState.None;
            }

            if (selected)
            {
                box.State = ComponentState.Selected;
            }
        }
    }
}
