using System;
using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Subsurface
{

    delegate void TextBoxEvent(GUITextBox sender);

    class GUITextBox : GUIComponent, IKeyboardSubscriber
    {        
        public event TextBoxEvent Clicked;

        bool caretVisible;
        float caretTimer;

        GUITextBlock textBlock;

        public delegate bool OnEnterHandler(GUITextBox textBox, string text);
        public OnEnterHandler OnEnter;

        public delegate bool OnTextChangedHandler(GUITextBox textBox, string text);
        public OnTextChangedHandler OnTextChanged;

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
                    //if you attempt to display a character that is not in your font
                    //you will get an exception, so we filter the characters
                    //remove the filtering if you're using a default character in your spritefont
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
            textBlock.Padding = new Vector4(10.0f, 0.0f, 10.0f, 0.0f);
            if (style != null) style.Apply(textBlock, this);

            previousMouse = PlayerInput.GetMouseState;
            //SetTextPos();
        }

        public void Select()
        {
            Selected = true;
            keyboardDispatcher.Subscriber = this;
            if (Clicked != null) Clicked(this);
        }

        public void Deselect()
        {
            if (keyboardDispatcher.Subscriber == this) keyboardDispatcher.Subscriber = null;
        }

        MouseState previousMouse;
        public override void Update(float deltaTime)
        {
            if (!Enabled) return;

            caretTimer += deltaTime;
            caretVisible = ((caretTimer*1000.0f) % 1000) < 500;
            
            if (rect.Contains(PlayerInput.GetMouseState.Position))
            {
                state = ComponentState.Hover;
                if (PlayerInput.LeftButtonClicked()) Select();                
            }
            else
            {
                state = ComponentState.None;
            }

            if (keyboardDispatcher.Subscriber == this)
            {
                Character.DisableControls = true;
                if (OnEnter != null &&  PlayerInput.KeyHit(Keys.Enter))
                {
                    string input = Text;
                    Text = "";
                    OnEnter(this, input);
                    
                }
            }
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            DrawChildren(spriteBatch);
            
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
            switch (command)
            {
                case '\b': //backspace
                    if (Text.Length > 0)
                        Text = Text.Substring(0, Text.Length - 1);
                    break;
                case '\r': //return
                    if (OnEnterPressed != null)
                        OnEnterPressed(this);
                    break;
                case '\t': //tab
                    if (OnTabPressed != null)
                        OnTabPressed(this);
                    break;
            }

           if (OnTextChanged != null) OnTextChanged(this, Text);
        }

        public void ReceiveSpecialInput(Keys key)
        {
        }

        public event TextBoxEvent OnEnterPressed;
        public event TextBoxEvent OnTabPressed;

        public bool Selected
        {
            get;
            set;
        }
    }
}
