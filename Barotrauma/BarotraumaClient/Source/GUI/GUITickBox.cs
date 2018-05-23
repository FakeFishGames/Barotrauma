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
            }
        }

        public override Rectangle Rect
        {
            get
            {
                return base.Rect;
            }
            set
            {
                if (RectTransform != null) { return; }
                base.Rect = value;

                if (box != null) box.Rect = new Rectangle(value.X,value.Y,box.Rect.Width,box.Rect.Height);
                if (text != null) text.Rect = new Rectangle(box.Rect.Right, box.Rect.Y + 2, 20, box.Rect.Height);
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

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUITickBox(Rectangle rect, string label, Alignment alignment, GUIComponent parent)
            : this(rect, label, alignment, GUI.Font, "", parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUITickBox(Rectangle rect, string label, Alignment alignment, ScalableFont font, string style, GUIComponent parent)
            : base(null)
        {
            if (parent != null)
                parent.AddChild(this);

            box = new GUIFrame(rect, Color.DarkGray, "", this);
            box.HoverColor = Color.Gray;
            box.SelectedColor = Color.DarkGray;
            box.CanBeFocused = false;

            GUI.Style.Apply(box, style == "" ? "GUITickBox" : style);
            
            text = new GUITextBlock(new Rectangle(rect.Right, rect.Y, 20, rect.Height), label, "", Alignment.TopLeft, Alignment.Left | Alignment.CenterY, this, false, font);
            GUI.Style.Apply(text, "GUIButtonHorizontal", this);
            
            this.rect = new Rectangle(box.Rect.X, box.Rect.Y, 240, rect.Height);

            Enabled = true;
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
                    if (OnSelected != null) OnSelected(this);
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
