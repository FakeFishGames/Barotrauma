using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace Barotrauma
{

    delegate void TextBoxEvent(GUITextBox sender, Keys key);

    class GUITextBox : GUIComponent, IKeyboardSubscriber
    {        
        public event TextBoxEvent OnSelected;
        public event TextBoxEvent OnDeselected;

        bool caretVisible;
        float caretTimer;

        private GUIFrame frame;
        private GUITextBlock textBlock;

        public delegate bool OnEnterHandler(GUITextBox textBox, string text);
        public OnEnterHandler OnEnterPressed;
        
        public event TextBoxEvent OnKeyHit;

        public delegate bool OnTextChangedHandler(GUITextBox textBox, string text);
        public OnTextChangedHandler OnTextChanged;

        public bool CaretEnabled;
        
        private int? maxTextLength;

        private int caretIndex;
        private bool caretPosDirty;
        protected Vector2 caretPos;

        public GUITextBlock.TextGetterHandler TextGetter
        {
            get { return textBlock.TextGetter; }
            set { textBlock.TextGetter = value; }
        }

        public bool Selected
        {
            get;
            set;
        }

        public bool Wrap
        {
            get { return textBlock.Wrap; }
            set
            {
                textBlock.Wrap = value;
            }
        }

        //should the text be limited to the size of the box
        //ignored when MaxTextLength is set or text wrapping is enabled
        public bool ClampText
        {
            get;
            set;
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

        public override bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                if (!enabled && Selected)
                {
                    Deselect();
                }
            }
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

                if (textBlock.Text != "" && !Wrap)
                {
                    if (maxTextLength != null)
                    {
                        if (Text.Length > maxTextLength)
                        {
                            Text = textBlock.Text.Substring(0, (int)maxTextLength);
                        }
                    }
                    else if (ClampText && Font.MeasureString(textBlock.Text).X > (int)(textBlock.Rect.Width - textBlock.Padding.X - textBlock.Padding.Z))
                    {
                        Text = textBlock.Text.Substring(0, textBlock.Text.Length - 1);
                    }                    
                }

                caretIndex = Text.Length;
                caretPosDirty = true;
            }
        }
        
        public GUITextBox(RectTransform rectT, string text = "", Color? textColor = null, ScalableFont font = null,
            Alignment textAlignment = Alignment.Left, bool wrap = false, string style = "", Color? color = null)
            : base(style, rectT)
        {
            Enabled = true;
            this.color = color ?? Color.White;
            frame = new GUIFrame(new RectTransform(Vector2.One, rectT, Anchor.Center), style, color);
            GUI.Style.Apply(frame, style == "" ? "GUITextBox" : style);
            textBlock = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.Center), text, textColor, font, textAlignment, wrap);
            GUI.Style.Apply(textBlock, "", this);
            CaretEnabled = true;
            caretPosDirty = true;

            Font = textBlock.Font;
            
            rectT.SizeChanged += () => { caretPosDirty = true; };
            rectT.ScaleChanged += () => { caretPosDirty = true; };
        }

        private void CalculateCaretPos()
        {
            if (textBlock.WrappedText.Contains("\n"))
            {
                string[] lines = textBlock.WrappedText.Split('\n');
                int n = 0;
                for (int i = 0; i<lines.Length; i++)
                {
                    //add the number of letters in the line
                    n += lines[i].Length;
                    //caret is on this line
                    if (caretIndex <= n)
                    {
                        Vector2 lastLineSize = Font.MeasureString(lines[i]);
                        Vector2 textSize = Font.MeasureString(textBlock.WrappedText.Substring(n+i));
                        caretPos = new Vector2(lastLineSize.X, textSize.Y - lastLineSize.Y) + textBlock.TextPos - textBlock.Origin;
                    }
                }
            }
            else
            {
                Vector2 textSize = Font.MeasureString(textBlock.Text.Substring(0, caretIndex));
                caretPos = new Vector2(textSize.X, 0) + textBlock.TextPos - textBlock.Origin;
            }
            caretPosDirty = false;
        }

        public void Select()
        {
            caretIndex = textBlock.Text.Length;
            Selected = true;
            GUI.KeyboardDispatcher.Subscriber = this;
            //if (Clicked != null) Clicked(this);
        }

        public void Deselect()
        {
            Selected = false;
            if (GUI.KeyboardDispatcher.Subscriber == this)
            {
                GUI.KeyboardDispatcher.Subscriber = null;
            }
            OnDeselected?.Invoke(this, Keys.None);
        }

        public override void Flash(Color? color = null)
        {
            textBlock.Flash(color);
        }
        
        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            if (flashTimer > 0.0f) flashTimer -= deltaTime;
            if (!Enabled) return;
            
            if (MouseRect.Contains(PlayerInput.MousePosition) && Enabled &&
                (GUI.MouseOn == null || GUI.MouseOn == this || IsParentOf(GUI.MouseOn) || GUI.MouseOn.IsParentOf(this)))
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
                if (caretVisible && caretPosDirty)
                {
                    CalculateCaretPos();
                }
            }
            
            if (GUI.KeyboardDispatcher.Subscriber == this)
            {
                state = ComponentState.Selected;
                Character.DisableControls = true;
                if (OnEnterPressed != null &&  PlayerInput.KeyHit(Keys.Enter))
                {
                    string input = Text;
                    Text = "";
                    OnEnterPressed(this, input);
                }
            }
            else if (Selected)
            {
                Deselect();
            }

            textBlock.State = state;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
            base.Draw(spriteBatch);
            // Frame is not used in the old system.
            frame?.DrawManually(spriteBatch);
            textBlock.DrawManually(spriteBatch);
            if (caretVisible)
            {
                if (caretVisible && Selected)
                {
                    GUI.DrawLine(spriteBatch,
                        new Vector2(Rect.X + (int)caretPos.X + 2, Rect.Y + caretPos.Y + 3),
                        new Vector2(Rect.X + (int)caretPos.X + 2, Rect.Y + caretPos.Y + Font.MeasureString("I").Y - 3),
                        textBlock.TextColor * (textBlock.TextColor.A / 255.0f));
                }
            }
        }

        public void ReceiveTextInput(char inputChar)
        {
            int prevCaretIndex = caretIndex; 
            Text = Text.Insert(caretIndex, inputChar.ToString());
            caretIndex = Math.Min(Text.Length, ++prevCaretIndex);
            caretPosDirty = true;
            OnTextChanged?.Invoke(this, Text);
        }
        public void ReceiveTextInput(string text)
        {
            Text += text;
            OnTextChanged?.Invoke(this, Text);
        }
        public void ReceiveCommandInput(char command)
        {
            if (Text == null) Text = "";

            switch (command)
            {
                case '\b': //backspace
                    if (Text.Length > 0 && caretIndex > 0)
                    {
                        caretIndex--;
                        int prevCaretIndex = caretIndex;
                        Text = Text.Remove(caretIndex, 1);
                        caretIndex = prevCaretIndex;
                    }
                    OnTextChanged?.Invoke(this, Text);
                    break;
            }
        }

        public void ReceiveSpecialInput(Keys key)
        {
            if (key == Keys.Left)
            {
                caretIndex = Math.Max(caretIndex - 1, 0);
                caretTimer = 0;
            }
            else if (key == Keys.Right)
            {
                caretIndex = Math.Min(caretIndex + 1, Text.Length);
                caretTimer = 0;
            }
            else if (key == Keys.Delete)
            {
                if (Text.Length > 0 && caretIndex < Text.Length)
                {
                    int prevCaretIndex = caretIndex;
                    Text = Text.Remove(caretIndex, 1);
                    caretIndex = prevCaretIndex;
                }

            }
            caretPosDirty = true;
            OnKeyHit?.Invoke(this, key);
        }
    }
}
