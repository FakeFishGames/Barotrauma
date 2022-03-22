using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class GUITextBlock : GUIComponent
    {
        protected string text;

        protected Alignment textAlignment;

        private float textScale = 1;

        protected Vector2 textPos;
        protected Vector2 origin;

        protected Color textColor, disabledTextColor, selectedTextColor;

        private string wrappedText;
        private string censoredText;

        public delegate string TextGetterHandler();
        public TextGetterHandler TextGetter;

        public bool Wrap;
        private bool playerInput;

        public bool RoundToNearestPixel = true;

        private bool overflowClipActive;
        public bool OverflowClip;

        public bool OverflowClipActive
        {
            get { return overflowClipActive; }
        }

        private float textDepth;

        private ScalableFont originalFont;

        public Vector2 TextOffset { get; set; }

        private Vector4 padding;
        public Vector4 Padding
        {
            get { return padding; }
            set 
            { 
                padding = value;
                SetTextPos();
            }
        }

        public override ScalableFont Font
        {
            get
            {
                return base.Font;
            }
            set
            {
                if (base.Font == value) { return; }
                base.Font = originalFont = value;
                if (text != null && GUI.Style.ForceFontUpperCase.ContainsKey(Font) && GUI.Style.ForceFontUpperCase[Font])
                {
                    Text = text.ToUpper();
                }
                SetTextPos();
            }
        }

        public string Text
        {
            get { return text; }
            set
            {
                string newText = forceUpperCase || (GUI.Style.ForceFontUpperCase.ContainsKey(Font) && GUI.Style.ForceFontUpperCase[Font]) || (style != null && style.ForceUpperCase) ? 
                    value?.ToUpper() : 
                    value;

                if (Text == newText) { return; }

                //reset scale, it gets recalculated in SetTextPos
                if (autoScaleHorizontal || autoScaleVertical) { textScale = 1.0f; }

                text = newText;
                wrappedText = newText;
                if (TextManager.IsCJK(text))
                {
                    //switch to fallback CJK font
                    if (!Font.IsCJK) { base.Font = GUI.CJKFont; }
                }
                else
                {
                    if (Font == GUI.CJKFont) { base.Font = originalFont; }
                }
                SetTextPos();
            }
        }

        public string WrappedText
        {
            get { return wrappedText; }
        }
        
        public float TextDepth
        {
            get { return textDepth; }
            set { textDepth = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }
        
        public Vector2 TextPos
        {
            get { return textPos; }
            set { textPos = value; }
        }

        public float TextScale
        {
            get { return textScale; }
            set
            {
                if (value != textScale)
                {
                    textScale = value;
                    SetTextPos();
                }
            }
        }

        private bool autoScaleHorizontal, autoScaleVertical;

        /// <summary>
        /// When enabled, the text is automatically scaled down to fit the textblock horizontally.
        /// </summary>
        public bool AutoScaleHorizontal
        {
            get { return autoScaleHorizontal; }
            set
            {
                if (autoScaleHorizontal == value) { return; }
                autoScaleHorizontal = value;
                if (autoScaleHorizontal)
                {
                    SetTextPos();
                }
            }
        }

        /// <summary>
        /// When enabled, the text is automatically scaled down to fit the textblock vertically.
        /// </summary>
        public bool AutoScaleVertical
        {
            get { return autoScaleVertical; }
            set
            {
                if (autoScaleVertical == value) { return; }
                autoScaleVertical = value;
                if (autoScaleVertical)
                {
                    SetTextPos();
                }
            }
        }

        private bool forceUpperCase;
        public bool ForceUpperCase
        {
            get { return forceUpperCase; }
            set
            {
                if (forceUpperCase == value) { return; }

                forceUpperCase = value;
                if (forceUpperCase || 
                    (style != null && style.ForceUpperCase) || 
                    (GUI.Style.ForceFontUpperCase.ContainsKey(Font) && GUI.Style.ForceFontUpperCase[Font]))
                {
                    Text = text?.ToUpper();
                }
            }
        }

        public Vector2 Origin
        {
            get { return origin; }
        }

        public Vector2 TextSize
        {
            get;
            private set;
        }

        public Color TextColor
        {
            get { return textColor; }
            set { textColor = value; }
        }

        public Color DisabledTextColor
        {
            get => disabledTextColor;
            set => disabledTextColor = value;
        }

        private Color? hoverTextColor;
        public Color HoverTextColor
        {
            get { return hoverTextColor ?? textColor; }
            set { hoverTextColor = value; }
        }

        public Color SelectedTextColor
        {
            get { return selectedTextColor; }
            set { selectedTextColor = value; }
        }

        public Alignment TextAlignment
        {
            get { return textAlignment; }
            set
            {
                if (textAlignment == value) return;
                textAlignment = value;
                SetTextPos();
            }
        }

        public bool Censor
        {
            get;
            set;
        }

        public string CensoredText
        {
            get { return censoredText; }
        }

        public class StrikethroughSettings
        {
            public Color Color { get; set; } = GUI.Style.Red;
            private int thickness;
            private int expand;

            public StrikethroughSettings(Color? color = null, int thickness = 1, int expand = 0)
            {
                if (color != null) { Color = color.Value; }
                this.thickness = thickness;
                this.expand = expand;
            }

            public void Draw(SpriteBatch spriteBatch, float textSizeHalf, float xPos, float yPos)
            {
                ShapeExtensions.DrawLine(spriteBatch, new Vector2(xPos - textSizeHalf - expand, yPos), new Vector2(xPos + textSizeHalf + expand, yPos), Color, thickness);
            }
        }

        public StrikethroughSettings Strikethrough = null;

        public List<RichTextData> RichTextData
        {
            get;
            private set;
        }

        public bool HasColorHighlight => RichTextData != null;

        public bool OverrideRichTextDataAlpha = true;

        public struct ClickableArea
        {
            public RichTextData Data;

            public delegate void OnClickDelegate(GUITextBlock textBlock, ClickableArea area);
            public OnClickDelegate OnClick;
            public OnClickDelegate OnSecondaryClick;
        }
        public List<ClickableArea> ClickableAreas { get; private set; } = new List<ClickableArea>();

        public bool Shadow { get; set; }
        
        /// <summary>
        /// This is the new constructor.
        /// If the rectT height is set 0, the height is calculated from the text.
        /// </summary>
        public GUITextBlock(RectTransform rectT, string text, Color? textColor = null, ScalableFont font = null, 
            Alignment textAlignment = Alignment.Left, bool wrap = false, string style = "", Color? color = null,
            bool playerInput = false, bool parseRichText = false) 
            : base(style, rectT)
        {
            if (color.HasValue)
            {
                this.color = color.Value;
            }
            if (textColor.HasValue)
            {
                OverrideTextColor(textColor.Value);
            }

            if (parseRichText)
            {
                RichTextData = Barotrauma.RichTextData.GetRichTextData(text, out text);
                if (RichTextData != null && RichTextData.Count == 0)
                {
                    RichTextData = null;
                }
            }

            //if the text is in chinese/korean/japanese and we're not using a CJK-compatible font,
            //use the default CJK font as a fallback
            var selectedFont = originalFont = font ?? GUI.Font;
            if (TextManager.IsCJK(text) && !selectedFont.IsCJK)
            {                
                selectedFont = GUI.CJKFont;
            }
            this.Font = selectedFont;
            this.textAlignment = textAlignment;
            this.Wrap = wrap;
            this.Text = text ?? "";
            this.playerInput = playerInput;
            if (rectT.Rect.Height == 0 && !string.IsNullOrEmpty(text))
            {
                CalculateHeightFromText();
            }
            SetTextPos();

            RectTransform.ScaleChanged += SetTextPos;
            RectTransform.SizeChanged += SetTextPos;

            Enabled = true;
            Censor = false;
        }
        public GUITextBlock(RectTransform rectT, List<RichTextData> richTextData, string text, Color? textColor = null, ScalableFont font = null, Alignment textAlignment = Alignment.Left, bool wrap = false, string style = "", Color? color = null, bool playerInput = false)
        : this(rectT, text, textColor, font, textAlignment, wrap, style, color, playerInput)
        {
            this.RichTextData = richTextData;
        }

        public void CalculateHeightFromText(int padding = 0, bool removeExtraSpacing = false)
        {
            if (wrappedText == null) { return; }
            RectTransform.Resize(new Point(RectTransform.Rect.Width, (int)Font.MeasureString(wrappedText, removeExtraSpacing).Y + padding));
        }

        public void SetRichText(string richText)
        {
            RichTextData = Barotrauma.RichTextData.GetRichTextData(richText, out string sanitizedText);
            Text = sanitizedText;
        }
        
        public override void ApplyStyle(GUIComponentStyle componentStyle)
        {
            if (componentStyle == null) { return; }
            base.ApplyStyle(componentStyle);
            padding = componentStyle.Padding;

            textColor = componentStyle.TextColor;
            hoverTextColor = componentStyle.HoverTextColor;
            disabledTextColor = componentStyle.DisabledTextColor;
            selectedTextColor = componentStyle.SelectedTextColor;

            switch (componentStyle.Font)
            {
                case "font":
                    Font = componentStyle.Style.Font;
                    break;
                case "smallfont":
                    Font = componentStyle.Style.SmallFont;
                    break;
                case "largefont":
                    Font = componentStyle.Style.LargeFont;
                    break;
                case "objectivetitle":
                case "subheading":
                    Font = componentStyle.Style.SubHeadingFont;
                    break;
            }
        }
        
        public void SetTextPos()
        {
            cachedCaretPositions = ImmutableArray<Vector2>.Empty;
            if (text == null) { return; }

            censoredText = string.IsNullOrEmpty(text) ? "" : new string('\u2022', text.Length);

            var rect = Rect;

            overflowClipActive = false;
            wrappedText = text;
            
            TextSize = MeasureText(text);
            
            if (Wrap && rect.Width > 0)
            {
                wrappedText = ToolBox.WrapText(text, rect.Width - padding.X - padding.Z, Font, textScale);
                TextSize = MeasureText(wrappedText);
            }
            else if (OverflowClip)
            {
                overflowClipActive = TextSize.X > rect.Width - padding.X - padding.Z;
            }

            Vector2 minSize = new Vector2(
                Math.Max(rect.Width - padding.X - padding.Z, 5.0f),
                Math.Max(rect.Height - padding.Y - padding.W, 5.0f));
            if (!autoScaleHorizontal) { minSize.X = float.MaxValue; }
            if (!Wrap && !autoScaleVertical) { minSize.Y = float.MaxValue; }

            if ((autoScaleHorizontal || autoScaleVertical) && textScale > 0.1f &&
                (TextSize.X * textScale > minSize.X || TextSize.Y * textScale > minSize.Y))
            {
                TextScale = Math.Max(0.1f, Math.Min(minSize.X / TextSize.X, minSize.Y / TextSize.Y)) - 0.01f;
                return;
            }

            textPos = new Vector2(padding.X + (rect.Width - padding.Z - padding.X) / 2.0f, padding.Y + (rect.Height - padding.Y - padding.W) / 2.0f);
            origin = TextSize * 0.5f;

            if (textAlignment.HasFlag(Alignment.Left) && !overflowClipActive)
            {
                textPos.X = padding.X;
                origin.X = 0;
            }            
            if (textAlignment.HasFlag(Alignment.Right) || overflowClipActive)
            {
                textPos.X = rect.Width - padding.Z;
                origin.X = TextSize.X;
            }
            if (textAlignment.HasFlag(Alignment.Top))
            {
                textPos.Y = padding.Y;
                origin.Y = 0;
            }
            if (textAlignment.HasFlag(Alignment.Bottom))
            {
                textPos.Y = rect.Height - padding.W;
                origin.Y = TextSize.Y;
            }

            origin.X = (int)(origin.X);
            origin.Y = (int)(origin.Y);

            textPos.X = (int)textPos.X;
            textPos.Y = (int)textPos.Y;
        }

        private Vector2 MeasureText(string text) 
        {
            if (Font == null) return Vector2.Zero;

            if (string.IsNullOrEmpty(text))
            {
                return Font.MeasureString(" ");
            }

            Vector2 size = Vector2.Zero;
            while (size == Vector2.Zero)
            {
                try { size = Font.MeasureString(string.IsNullOrEmpty(text) ? " " : text); }
                catch { text = text.Length > 0 ? text.Substring(0, text.Length - 1) : ""; }
            }

            return size;
        }

        protected override void SetAlpha(float a)
        {
            // base.SetAlpha(a);
            textColor = new Color(TextColor.R / 255.0f, TextColor.G / 255.0f, TextColor.B / 255.0f, a);
        }

        /// <summary>
        /// Overrides the color for all the states.
        /// </summary>
        public void OverrideTextColor(Color color)
        {
            textColor = color;
            hoverTextColor = color;
            selectedTextColor = color;
            disabledTextColor = color;
        }

        private ImmutableArray<Vector2> cachedCaretPositions = ImmutableArray<Vector2>.Empty;
        
        public ImmutableArray<Vector2> GetAllCaretPositions()
        {
            if (cachedCaretPositions.Any())
            {
                return cachedCaretPositions;
            }
            string textDrawn = Censor ? CensoredText : Text;
            float w = Wrap
                ? (Rect.Width - Padding.X - Padding.Z) / TextScale
                : float.PositiveInfinity;
            Font.WrapText(textDrawn, w, out Vector2[] positions);
            cachedCaretPositions = positions.Select(p => p * TextScale + TextPos - Origin * TextScale).ToImmutableArray();
            return cachedCaretPositions;
        }

        public int GetCaretIndexFromScreenPos(in Vector2 pos)
        {
            return GetCaretIndexFromLocalPos(pos - Rect.Location.ToVector2());
        }

        public int GetCaretIndexFromLocalPos(in Vector2 pos)
        {
            var positions = GetAllCaretPositions();
            if (positions.Length == 0) { return 0; }

            float closestXDist = float.PositiveInfinity;
            float closestYDist = float.PositiveInfinity;
            int closestIndex = -1;
            for (int i = 0; i < positions.Length; i++)
            {
                float xDist = Math.Abs(pos.X - positions[i].X);
                float yDist = Math.Abs(pos.Y - (positions[i].Y + Font.LineHeight * 0.5f));
                if (yDist < closestYDist || (MathUtils.NearlyEqual(yDist, closestYDist) && xDist < closestXDist))
                {
                    closestIndex = i;
                    closestXDist = xDist;
                    closestYDist = yDist;
                }
            }
            
            return closestIndex >= 0 ? closestIndex : Text.Length;
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (ClickableAreas.Any() && ((GUI.MouseOn?.IsParentOf(this) ?? true) || GUI.MouseOn == this))
            {
                if (!Rect.Contains(PlayerInput.MousePosition)) { return; }
                int index = GetCaretIndexFromScreenPos(PlayerInput.MousePosition);
                foreach (ClickableArea clickableArea in ClickableAreas)
                {
                    if (clickableArea.Data.StartIndex <= index && index <= clickableArea.Data.EndIndex)
                    {
                        GUI.MouseCursor = CursorState.Hand;
                        if (PlayerInput.PrimaryMouseButtonClicked())
                        {
                            clickableArea.OnClick?.Invoke(this, clickableArea);
                        }
                        if (PlayerInput.SecondaryMouseButtonClicked())
                        {
                            clickableArea.OnSecondaryClick?.Invoke(this, clickableArea);
                        }
                        break;
                    }
                }
            }
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) { return; }

            Color currColor = GetColor(State);

            var rect = Rect;

            base.Draw(spriteBatch);

            if (TextGetter != null) { Text = TextGetter(); }

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (overflowClipActive)
            {
                Rectangle scissorRect = new Rectangle(rect.X + (int)padding.X, rect.Y, rect.Width - (int)padding.X - (int)padding.Z, rect.Height);
                if (!scissorRect.Intersects(prevScissorRect)) { return; }
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, scissorRect);
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }

            if (!string.IsNullOrEmpty(text))
            {
                Vector2 pos = rect.Location.ToVector2() + textPos + TextOffset;
                if (RoundToNearestPixel)
                {
                    pos.X = (int)pos.X;
                    pos.Y = (int)pos.Y;
                }

                Color currentTextColor = State == ComponentState.Hover || State == ComponentState.HoverSelected ? HoverTextColor : TextColor;
                if (!enabled)
                {
                    currentTextColor = disabledTextColor;
                }
                else if (State == ComponentState.Selected)
                {
                    currentTextColor = selectedTextColor;
                }

                if (!HasColorHighlight)
                {
                    string textToShow = Censor ? censoredText : (Wrap ? wrappedText : text);
                    Color colorToShow = currentTextColor * (currentTextColor.A / 255.0f);

                    if (Shadow)
                    {
                        Vector2 shadowOffset = new Vector2(GUI.IntScale(2));
                        Font.DrawString(spriteBatch, textToShow, pos + shadowOffset, Color.Black, 0.0f, origin, TextScale, SpriteEffects.None, textDepth);
                    }

                    Font.DrawString(spriteBatch, textToShow, pos, colorToShow, 0.0f, origin, TextScale, SpriteEffects.None, textDepth);
                }
                else
                {
                    if (OverrideRichTextDataAlpha)
                    {
                        RichTextData.ForEach(rt => rt.Alpha = currentTextColor.A / 255.0f);
                    }
                    Font.DrawStringWithColors(spriteBatch, Censor ? censoredText : (Wrap ? wrappedText : text), pos,
                        currentTextColor * (currentTextColor.A / 255.0f), 0.0f, origin, TextScale, SpriteEffects.None, textDepth, RichTextData);
                }

                Strikethrough?.Draw(spriteBatch, (int)Math.Ceiling(TextSize.X / 2f), pos.X, ForceUpperCase ? pos.Y : pos.Y + GUI.Scale * 2f);
            }

            if (overflowClipActive)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }

            if (OutlineColor.A * currColor.A > 0.0f) GUI.DrawRectangle(spriteBatch, rect, OutlineColor * (currColor.A / 255.0f), false);
        }

        /// <summary>
        /// Set the text scale of the GUITextBlocks so that they all use the same scale and can fit the text within the block.
        /// </summary>
        public static void AutoScaleAndNormalize(params GUITextBlock[] textBlocks)
        {
            AutoScaleAndNormalize(textBlocks.AsEnumerable<GUITextBlock>());
        }

        /// <summary>
        /// Set the text scale of the GUITextBlocks so that they all use the same scale and can fit the text within the block.
        /// </summary>
        public static void AutoScaleAndNormalize(bool scaleHorizontal = true, bool scaleVertical = false, params GUITextBlock[] textBlocks)
        {
            AutoScaleAndNormalize(textBlocks.AsEnumerable<GUITextBlock>(), scaleHorizontal, scaleVertical);
        }

        /// <summary>
        /// Set the text scale of the GUITextBlocks so that they all use the same scale and can fit the text within the block.
        /// </summary>
        public static void AutoScaleAndNormalize(IEnumerable<GUITextBlock> textBlocks, bool scaleHorizontal = true, bool scaleVertical = false, float? defaultScale = null)
        {
            if (!textBlocks.Any()) { return; }
            float minScale = Math.Max(textBlocks.First().TextScale, 1.0f);
            foreach (GUITextBlock textBlock in textBlocks)
            {
                if (defaultScale.HasValue) { textBlock.TextScale = defaultScale.Value; }
                textBlock.AutoScaleHorizontal = scaleHorizontal;
                textBlock.AutoScaleVertical = scaleVertical;
                minScale = Math.Min(textBlock.TextScale, minScale);
            }

            foreach (GUITextBlock textBlock in textBlocks)
            {
                textBlock.AutoScaleHorizontal = false;
                textBlock.AutoScaleVertical = false;
                textBlock.TextScale = minScale;
            }
        }
    }
}
