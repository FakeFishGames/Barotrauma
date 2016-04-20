using System;
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

        bool caretVisible;
        float caretTimer;

        GUITextBlock textBlock;

        public delegate bool OnEnterHandler(GUITextBox textBox, string text);
        public OnEnterHandler OnEnterPressed;
        
        public event TextBoxEvent OnKeyHit;

        public delegate bool OnTextChangedHandler(GUITextBox textBox, string text);
        public OnTextChangedHandler OnTextChanged;

        public bool CaretEnabled;
        
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

        public bool LimitText
        {
            get { return textBlock.LimitText; }
            set { textBlock.LimitText = value; }
        }

        public bool Enabled
        {
            get;
            set;
        }

        public override SpriteFont Font
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

                textBlock.Rect = value;
            }
        }
        
        public String Text
        {
            get
            {
                return textBlock.Text;
            }
            set
            {
                textBlock.Text = value;
                if (textBlock.Text == null) textBlock.Text = "";

                if (textBlock.Text != "")
                {
                    //if you attempt to display a Character that is not in your font
                    //you will get an exception, so we filter the characters
                    //remove the filtering if you're using a default Character in your spritefont
                    String filtered = "";
                    foreach (char c in value)
                    {
                        if (Font.Characters.Contains(c)) filtered += c;
                    }

                    textBlock.Text = filtered;

                    if (!Wrap && Font.MeasureString(textBlock.Text).X > rect.Width)
                    {
                        //ensure that text cannot be larger than the box
                        Text = textBlock.Text.Substring(0, textBlock.Text.Length - 1);
                    }
                    
                }
            }
        }

        public GUITextBox(Rectangle rect, GUIStyle style = null, GUIComponent parent = null)
            : this(rect, null, null, Alignment.Left, Alignment.Left, style, parent)
        {

        }

        public GUITextBox(Rectangle rect, Alignment alignment = Alignment.Left, GUIStyle style = null, GUIComponent parent = null)
            : this(rect, null, null, alignment, Alignment.Left, style, parent)
        {

        }

        public GUITextBox(Rectangle rect, Color? color, Color? textColor, Alignment alignment, Alignment textAlignment = Alignment.Left, GUIStyle style = null, GUIComponent parent = null)
            : base(style)
        {
            Enabled = true;

            this.rect = rect;

            if (color != null) this.color = (Color)color;
                        
            this.alignment = alignment;

            //this.textAlignment = textAlignment;
            
            if (parent != null)
                parent.AddChild(this);

            textBlock = new GUITextBlock(new Rectangle(0,0,0,0), "", color, textColor, textAlignment, style, this);

            if (style != null) style.Apply(textBlock, this);
            textBlock.Padding = new Vector4(3.0f, 0.0f, 3.0f, 0.0f);

            //previousMouse = PlayerInput.GetMouseState;

            CaretEnabled = true;
            //SetTextPos();
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
        }

        public override void Flash(Color? color = null)
        {
            textBlock.Flash(color);
        }

        //MouseState previousMouse;
        public override void Update(float deltaTime)
        {
            if (!Visible) return;

            if (flashTimer > 0.0f) flashTimer -= deltaTime;
            if (!Enabled) return;
            
            if (CaretEnabled)
            {
                caretTimer += deltaTime;
                caretVisible = ((caretTimer * 1000.0f) % 1000) < 500;
            }
            
            if (rect.Contains(PlayerInput.MousePosition))
            {

                state = ComponentState.Hover;                
                if (PlayerInput.LeftButtonClicked())
                {
                    if (MouseOn != null && MouseOn != this && MouseOn!=textBlock && !MouseOn.IsParentOf(this)) return;

                    Select();
                    if (OnSelected != null) OnSelected(this, Keys.None);
                }
            }
            else
            {
                state = ComponentState.None;

            }

            textBlock.State = state;

            if (keyboardDispatcher.Subscriber == this)
            {
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
                    break;
                //case '\r': //return
                //    if (OnEnterPressed != null)
                //        OnEnterPressed(this);
                //    break;
                //case '\t': //tab
                //    if (OnTabPressed != null)
                //        OnTabPressed(this);
                //    break;
            }

           if (OnTextChanged != null) OnTextChanged(this, Text);
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
