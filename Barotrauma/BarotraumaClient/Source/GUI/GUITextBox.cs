using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Barotrauma
{

    delegate void TextBoxEvent(GUITextBox sender, Keys key);

    class GUITextBox : GUIComponent, IKeyboardSubscriber
    {        
        public event TextBoxEvent OnSelected;
        public event TextBoxEvent OnDeselected;

        bool caretVisible;
        float caretTimer;
        
        GUITextBlock textBlock;

        public delegate bool OnEnterHandler(GUITextBox textBox, string text);
        public OnEnterHandler OnEnterPressed;
        
        public event TextBoxEvent OnKeyHit;

        public delegate bool OnTextChangedHandler(GUITextBox textBox, string text);
        public OnTextChangedHandler OnTextChanged;

        public bool CaretEnabled;

        private int? maxTextLength;
        
        public GUITextBlock.TextGetterHandler TextGetter
        {
            get { return textBlock.TextGetter; }
            set { textBlock.TextGetter = value; }
        }

        public bool Wrap
        {
            get { return textBlock.Wrap; }
            set { textBlock.Wrap = value; }
        }

        public int? MaxTextLength
        {
            get { return maxTextLength; }
            set
            {
                textBlock.OverflowClip = true;                
                maxTextLength = value;
            }
        }
        
        public bool Enabled
        {
            get;
            set;
        }

        public override string ToolTip
        {
            get
            {
                return base.ToolTip;
            }
            set
            {
                base.ToolTip = value;
                textBlock.ToolTip = value;
            }
        }

        public override ScalableFont Font
        {
            set
            {
                base.Font = value;
                if (textBlock == null) return;
                textBlock.Font = value;
            }
        }

        public override Color Color
        {
            get { return color; }
            set
            {
                color = value;
                textBlock.Color = color;
            }
        }

        public Color TextColor
        {
            get { return textBlock.TextColor; }
            set { textBlock.TextColor = value; }
        }

        public override Color HoverColor
        {
            get
            {
                return base.HoverColor;
            }
            set
            {
                base.HoverColor = value;
                textBlock.HoverColor = value;
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
                base.Rect = value;

                if (textBlock != null)
                {
                    textBlock.Rect = value;
                }
            }
        }
        
        public string Text
        {
            get
            {
                return textBlock.Text;
            }
            set
            {
                if (textBlock.Text == value) return;

                textBlock.Text = value;
                if (textBlock.Text == null) textBlock.Text = "";

                if (textBlock.Text != "")
                {
                    if (!Wrap)
                    {
                        if (maxTextLength != null)
                        {
                            if (Text.Length > maxTextLength)
                            {
                                Text = textBlock.Text.Substring(0, (int)maxTextLength);
                            }
                        }
                        else if (Font.MeasureString(textBlock.Text).X > (int)(textBlock.Rect.Width - textBlock.Padding.X - textBlock.Padding.Z))
                        {
                            Text = textBlock.Text.Substring(0, textBlock.Text.Length - 1);
                        }
                    }
                }
            }
        }

        public GUITextBox(Rectangle rect, string style = null, GUIComponent parent = null)
            : this(rect, null, null, Alignment.Left, Alignment.Left, style, parent)
        {

        }

        public GUITextBox(Rectangle rect, Alignment alignment = Alignment.Left, string style = null, GUIComponent parent = null)
            : this(rect, null, null, alignment, Alignment.Left, style, parent)
        {

        }

        public GUITextBox(Rectangle rect, Color? color, Color? textColor, Alignment alignment, Alignment textAlignment = Alignment.CenterLeft, string style = null, GUIComponent parent = null)
            : base(style)
        {
            Enabled = true;

            this.rect = rect;

            if (color != null) this.color = (Color)color;
                        
            this.alignment = alignment;
            
            if (parent != null)
                parent.AddChild(this);


            textBlock = new GUITextBlock(new Rectangle(0,0,0,0), "", color, textColor, textAlignment, style, this);
            
            Font = GUI.Font;

            GUI.Style.Apply(textBlock, style == "" ? "GUITextBox" : style);
            textBlock.Padding = new Vector4(3.0f, 0.0f, 3.0f, 0.0f);
            
            CaretEnabled = true;
        }

        public void Select()
        {
            Selected = true;
            keyboardDispatcher.Subscriber = this;
            //if (Clicked != null) Clicked(this);
        }

        public void Deselect()
        {
            Selected = false;
            if (keyboardDispatcher.Subscriber == this) keyboardDispatcher.Subscriber = null;

            OnDeselected?.Invoke(this, Keys.None);
        }

        public override void Flash(Color? color = null)
        {
            textBlock.Flash(color);
        }
        
        public override void Update(float deltaTime)
        {
            if (!Visible) return;

            if (flashTimer > 0.0f) flashTimer -= deltaTime;
            if (!Enabled) return;
            
            if (MouseRect.Contains(PlayerInput.MousePosition) && Enabled &&
                (MouseOn == null || MouseOn == this || IsParentOf(MouseOn) || MouseOn.IsParentOf(this)))
            {
                state = ComponentState.Hover;
                if (PlayerInput.LeftButtonClicked())
                {
                    Select();
                    OnSelected?.Invoke(this, Keys.None);
                }
            }
            else
            {
                state = ComponentState.None;
            }
            
            if (CaretEnabled)
            {
                caretTimer += deltaTime;
                caretVisible = ((caretTimer * 1000.0f) % 1000) < 500;
            }
            
            if (keyboardDispatcher.Subscriber == this)
            {
                state = ComponentState.Selected;
                Character.DisableControls = true;
                if (OnEnterPressed != null &&  PlayerInput.KeyHit(Keys.Enter))
                {
                    string input = Text;
                    Text = "";
                    OnEnterPressed(this, input);
                }
#if LINUX
                else if (PlayerInput.KeyHit(Keys.Back) && Text.Length>0)
                {
                    Text = Text.Substring(0, Text.Length-1);
                }
#endif
            }
            else if (Selected)
            {
                Deselect();
            }

            textBlock.State = state;
            textBlock.Update(deltaTime);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            DrawChildren(spriteBatch);

            if (!CaretEnabled) return;
            
            Vector2 caretPos = textBlock.CaretPos;

            if (caretVisible && Selected)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2((int)caretPos.X + 2, caretPos.Y + 3),
                    new Vector2((int)caretPos.X + 2, caretPos.Y + Font.MeasureString("I").Y - 3),
                    textBlock.TextColor * (textBlock.TextColor.A / 255.0f));
            }
        }

        public void ReceiveTextInput(char inputChar)
        {
            Text = Text + inputChar;

            if (OnTextChanged!=null) OnTextChanged(this, Text);
        }
        public void ReceiveTextInput(string text)
        {
            Text = Text + text;

            if (OnTextChanged != null) OnTextChanged(this, Text);
        }
        public void ReceiveCommandInput(char command)
        {
            if (Text == null) Text = "";

            switch (command)
            {
                case '\b': //backspace
                    if (Text.Length > 0)  Text = Text.Substring(0, Text.Length - 1);
                    if (OnTextChanged != null) OnTextChanged(this, Text);
                    break;
            }

        }

        public void ReceiveSpecialInput(Keys key)
        {
            if (OnKeyHit != null) OnKeyHit(this, key);
        }

        //public event TextBoxEvent OnTabPressed;

        public bool Selected
        {
            get;
            set;
        }
    }
}
