using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public bool CaretEnabled { get; set; }
        public Color? CaretColor { get; set; }
        
        private int? maxTextLength;

        private int _caretIndex;
        private int CaretIndex
        {
            get { return _caretIndex; }
            set
            {
                if (_caretIndex == value) { return; }
                previousCaretIndex = _caretIndex;
                _caretIndex = value;
                caretPosDirty = true;
            }
        }
        private bool caretPosDirty;
        protected Vector2 caretPos;

        private bool isSelecting;
        private string selectedText = string.Empty;
        private string clipboard = string.Empty;
        private int selectedCharacters;
        private int selectionStartIndex;
        private int selectionEndIndex;
        private bool IsLeftToRight => selectionStartIndex <= selectionEndIndex;
        private int previousCaretIndex;
        private Vector2 selectionStartPos;
        private Vector2 selectionRectSize;

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

                CaretIndex = Text.Length;
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
                int totalIndex = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    int currentLineLength = lines[i].Length;
                    totalIndex += currentLineLength;
                    // The caret is on this line
                    if (CaretIndex < totalIndex || totalIndex == textBlock.Text.Length)
                    {
                        int diff = totalIndex - CaretIndex;
                        int index = currentLineLength - diff;
                        Vector2 lineTextSize = Font.MeasureString(lines[i].Substring(0, index));
                        Vector2 lastLineSize = Font.MeasureString(lines[i]);
                        float totalTextHeight = Font.MeasureString(textBlock.WrappedText.Substring(0, totalIndex)).Y;
                        caretPos = new Vector2(lineTextSize.X, totalTextHeight - lastLineSize.Y) + textBlock.TextPos - textBlock.Origin;
                        break;
                    }
                }
            }
            else
            {
                Vector2 textSize = Font.MeasureString(textBlock.Text.Substring(0, CaretIndex));
                caretPos = new Vector2(textSize.X, 0) + textBlock.TextPos - textBlock.Origin;
            }
            caretPosDirty = false;
        }

        protected List<Tuple<Vector2, int>> GetAllPositions()
        {
            var positions = new List<Tuple<Vector2, int>>();
            if (textBlock.WrappedText.Contains("\n"))
            {
                string[] lines = textBlock.WrappedText.Split('\n');
                int index = 0;
                int totalIndex = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    totalIndex += line.Length;
                    float totalTextHeight = Font.MeasureString(textBlock.WrappedText.Substring(0, totalIndex)).Y;
                    for (int j = 0; j <= line.Length; j++)
                    {
                        Vector2 lineTextSize = Font.MeasureString(line.Substring(0, j));
                        Vector2 indexPos = new Vector2(lineTextSize.X + textBlock.Padding.X, totalTextHeight + textBlock.Padding.Y);
                        //DebugConsole.NewMessage($"index: {idx}, pos: {indexPos}", Color.AliceBlue);
                        positions.Add(new Tuple<Vector2, int>(textBlock.Rect.Location.ToVector2() + indexPos, index + j));
                    }
                    index = totalIndex;
                }
            }
            else
            {
                for (int i = 0; i <= textBlock.Text.Length; i++)
                {
                    Vector2 textSize = Font.MeasureString(textBlock.Text.Substring(0, i));
                    Vector2 indexPos = new Vector2(textSize.X + textBlock.Padding.X, textSize.Y + textBlock.Padding.Y);
                    //DebugConsole.NewMessage($"index: {i}, pos: {indexPos}", Color.WhiteSmoke);
                    positions.Add(new Tuple<Vector2, int>(textBlock.Rect.Location.ToVector2() + indexPos, i));
                }
            }
            return positions;
        }

        public int GetCaretIndexFromScreenPos(Vector2 pos)
        {
            var positions = GetAllPositions().OrderBy(p => Vector2.DistanceSquared(p.Item1, pos));
            var posIndex = positions.FirstOrDefault();
            //GUI.AddMessage($"index: {posIndex.Item2}, pos: {posIndex.Item1}", Color.WhiteSmoke);
            return posIndex != null ? posIndex.Item2 : textBlock.Text.Length;
        }

        public void Select()
        {
            Selected = true;
            CaretIndex = GetCaretIndexFromScreenPos(PlayerInput.MousePosition);
            ClearSelection();
            GUI.KeyboardDispatcher.Subscriber = this;
            OnSelected?.Invoke(this, Keys.None);
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
            if (MouseRect.Contains(PlayerInput.MousePosition) && (GUI.MouseOn == null || GUI.IsMouseOn(this)))
            {
                state = ComponentState.Hover;
                if (PlayerInput.LeftButtonDown())
                {
                    Select();
                }
                else
                {
                    isSelecting = PlayerInput.LeftButtonHeld();
                }
                if (PlayerInput.DoubleClicked())
                {
                    SelectAll();
                }
                if (isSelecting)
                {
                    if (!MathUtils.NearlyEqual(PlayerInput.MouseSpeed.X, 0))
                    {
                        CaretIndex = GetCaretIndexFromScreenPos(PlayerInput.MousePosition);
                        CalculateCaretPos();
                        CalculateSelection();
                    }
                }
            }
            else
            {
                isSelecting = false;
                state = ComponentState.None;
            }
            if (!isSelecting)
            {
                isSelecting = PlayerInput.KeyDown(Keys.LeftShift) || PlayerInput.KeyDown(Keys.RightShift);
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
            if (Selected)
            {
                if (caretVisible )
                {
                    GUI.DrawLine(spriteBatch,
                        new Vector2(Rect.X + (int)caretPos.X + 2, Rect.Y + caretPos.Y + 3),
                        new Vector2(Rect.X + (int)caretPos.X + 2, Rect.Y + caretPos.Y + Font.MeasureString("I").Y - 3),
                        CaretColor ?? textBlock.TextColor * (textBlock.TextColor.A / 255.0f));
                }
                if (selectedCharacters > 0)
                {
                    // TODO: multiline edit
                    Vector2 topLeft = IsLeftToRight ? selectionStartPos : new Vector2(selectionStartPos.X - selectionRectSize.X, selectionStartPos.Y);
                    GUI.DrawRectangle(spriteBatch, Rect.Location.ToVector2() + topLeft, selectionRectSize, Color.White * 0.25f, isFilled: true);
                }
                //GUI.DrawString(spriteBatch, new Vector2(100, 0), selectedCharacters.ToString(), Color.LightBlue, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 20), selectionStartIndex.ToString(), Color.White, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(140, 20), selectionEndIndex.ToString(), Color.White, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 40), selectedText.ToString(), Color.Yellow, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 60), $"caret index: {CaretIndex.ToString()}", Color.Red, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 80), $"caret pos: {caretPos.ToString()}", Color.Red, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 100), $"text start pos: {(textBlock.TextPos - textBlock.Origin).ToString()}", Color.White, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 120), $"cursor pos: {PlayerInput.MousePosition.ToString()}", Color.White, Color.Black);
            }
        }

        public void ReceiveTextInput(char inputChar)
        {
            int prevCaretIndex = CaretIndex;
            Text = Text.Insert(CaretIndex, inputChar.ToString());
            CaretIndex = Math.Min(Text.Length, ++prevCaretIndex);
            OnTextChanged?.Invoke(this, Text);
        }

        public void ReceiveTextInput(string text)
        {
            int prevCaretIndex = CaretIndex;
            Text = Text.Insert(CaretIndex, text);
            CaretIndex = Math.Min(Text.Length, prevCaretIndex + text.Length);
            OnTextChanged?.Invoke(this, Text);
        }

        public void ReceiveCommandInput(char command)
        {
            if (Text == null) Text = "";

            switch (command)
            {
                case '\b': //backspace
                    if (selectedCharacters > 0)
                    {
                        RemoveSelectedText();
                    }
                    else if (Text.Length > 0 && CaretIndex > 0)
                    {
                        CaretIndex--;
                        int prevCaretIndex = CaretIndex;
                        Text = Text.Remove(CaretIndex, 1);
                        CaretIndex = prevCaretIndex;
                        ClearSelection();
                    }
                    OnTextChanged?.Invoke(this, Text);
                    break;
                case (char)0x3: // ctrl-c
                    CopySelectedText();
                    break;
                case (char)0x16: // ctrl-v
                    string text = GetCopiedText();
                    int previousCaretIndex = CaretIndex;
                    Text = Text.Insert(CaretIndex, text);
                    CaretIndex = Math.Min(Text.Length, previousCaretIndex + text.Length);
                    OnTextChanged?.Invoke(this, Text);
                    break;
                case (char)0x18: // ctrl-x
                    CopySelectedText();
                    RemoveSelectedText();
                    break;
                case (char)0x1: // ctrl-a
                    SelectAll();
                    break;
            }
        }

        public void ReceiveSpecialInput(Keys key)
        {
            switch (key)
            {
                case Keys.Left:
                    CaretIndex = Math.Max(CaretIndex - 1, 0);
                    caretTimer = 0;
                    if (isSelecting)
                    {
                        if (selectionStartIndex == -1)
                        {
                            selectionStartIndex = CaretIndex + 1;
                            selectionStartPos = caretPos;
                        }
                        CalculateSelection();
                    }
                    else
                    {
                        ClearSelection();
                    }
                    break;
                case Keys.Right:
                    CaretIndex = Math.Min(CaretIndex + 1, Text.Length);
                    caretTimer = 0;
                    if (isSelecting)
                    {
                        if (selectionStartIndex == -1)
                        {
                            selectionStartIndex = CaretIndex - 1;
                            selectionStartPos = caretPos;
                        }
                        CalculateSelection();
                    }
                    else
                    {
                        ClearSelection();
                    }
                    break;
                case Keys.Delete:
                    if (selectedCharacters > 0)
                    {
                        RemoveSelectedText();
                    }
                    else if (Text.Length > 0 && CaretIndex < Text.Length)
                    {
                        int prevCaretIndex = CaretIndex;
                        Text = Text.Remove(CaretIndex, 1);
                        CaretIndex = prevCaretIndex;
                        OnTextChanged?.Invoke(this, Text);
                    }
                    break;
            }
            OnKeyHit?.Invoke(this, key);
        }

        public void SelectAll()
        {
            CaretIndex = 0;
            CalculateCaretPos();
            selectionStartPos = caretPos;
            selectionStartIndex = 0;
            CaretIndex = Text.Length;
            CalculateSelection();
        }

        private void CopySelectedText()
        {
#if WINDOWS
            System.Windows.Clipboard.SetText(selectedText);
#else
            clipboard = selectedText;
#endif
        }

        private void ClearSelection()
        {
            selectedCharacters = 0;
            selectionStartIndex = -1;
            selectionEndIndex = -1;
            selectedText = string.Empty;
        }

        private string GetCopiedText()
        {
            string t;
#if WINDOWS
            t = System.Windows.Clipboard.GetText();
#else
            t = clipboard;
#endif
            return t;
        }

        private void RemoveSelectedText()
        {
            if (IsLeftToRight)
            {
                Text = Text.Remove(selectionStartIndex, selectedText.Length);
            }
            else
            {
                Text = Text.Remove(selectionEndIndex, selectedText.Length);
            }
            CaretIndex = Math.Min(Text.Length, previousCaretIndex);
            ClearSelection();
            OnTextChanged?.Invoke(this, Text);
        }

        private void CalculateSelection()
        {
            if (selectionStartIndex == -1)
            {
                selectionStartIndex = CaretIndex;
                selectionStartPos = caretPos;
            }
            selectionEndIndex = CaretIndex;
            selectedCharacters = Math.Abs(selectionStartIndex - selectionEndIndex);
            if (IsLeftToRight)
            {
                selectedText = Text.Substring(selectionStartIndex, selectedCharacters);
                selectionRectSize = Font.MeasureString(textBlock.WrappedText.Substring(selectionStartIndex, selectedCharacters));
            }
            else
            {
                selectedText = Text.Substring(selectionEndIndex, selectedCharacters);
                selectionRectSize = Font.MeasureString(textBlock.WrappedText.Substring(selectionEndIndex, selectedCharacters));
            }
        }
    }
}
