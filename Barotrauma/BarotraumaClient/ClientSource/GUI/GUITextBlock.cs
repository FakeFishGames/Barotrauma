using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public enum ForceUpperCase
    {
        Inherit,
        No,
        Yes
    }

    public class GUITextBlock : GUIComponent
    {
        protected RichString text;

        protected Alignment textAlignment;

        private float textScale = 1;

        protected Vector2 textPos;
        protected Vector2 origin;

        protected Color textColor, disabledTextColor, selectedTextColor;

        private LocalizedString wrappedText;
        private string censoredText;

        public delegate LocalizedString TextGetterHandler();
        public TextGetterHandler TextGetter;

        public bool Wrap;

        public bool RoundToNearestPixel = true;

        private bool overflowClipActive;
        public bool OverflowClip;

        public bool OverflowClipActive
        {
            get { return overflowClipActive; }
        }

        private float textDepth;

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

        public override GUIFont Font
        {
            get
            {
                return base.Font;
            }
            set
            {
                if (base.Font == value) { return; }
                base.Font = value;
                if (text != null) { Text = text; }
                SetTextPos();
            }
        }

        public RichString Text
        {
            get { return text; }
            set
            {
                #warning TODO: Remove this eventually. Nobody should want to pass null.
                value ??= "";
                RichString newText = forceUpperCase switch
                {
                    ForceUpperCase.Inherit => value.CaseTiedToFontAndStyle(Font, Style),
                    ForceUpperCase.No => value.CaseTiedToFontAndStyle(null, null),
                    ForceUpperCase.Yes => value.ToUpper()
                };

                if (Text == newText) { return; }

                //reset scale, it gets recalculated in SetTextPos
                if (autoScaleHorizontal || autoScaleVertical) { textScale = 1.0f; }

                text = newText;
                wrappedText = newText.SanitizedString;
                SetTextPos();
            }
        }

        public LocalizedString WrappedText
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
            set
            {
                textPos = value;
                ClearCaretPositions();
            }
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

        private ForceUpperCase forceUpperCase = ForceUpperCase.Inherit;
        public ForceUpperCase ForceUpperCase
        {
            get { return forceUpperCase; }
            set
            {
                if (forceUpperCase == value) { return; }

                forceUpperCase = value;
                if (text != null) { Text = text; }
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
            public Color Color { get; set; } = GUIStyle.Red;
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

        public ImmutableArray<RichTextData>? RichTextData => text.RichTextData;

        public bool HasColorHighlight => RichTextData.HasValue;

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
        public GUITextBlock(RectTransform rectT, RichString text, Color? textColor = null, GUIFont font = null, 
            Alignment textAlignment = Alignment.Left, bool wrap = false, string style = "", Color? color = null) 
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

            //if the text is in chinese/korean/japanese and we're not using a CJK-compatible font,
            //use the default CJK font as a fallback
            var selectedFont = font ?? GUIStyle.Font;
            this.Font = selectedFont;
            this.textAlignment = textAlignment;
            this.Wrap = wrap;
            this.Text = text ?? "";
            if (rectT.Rect.Height == 0 && !text.IsNullOrEmpty())
            {
                CalculateHeightFromText();
            }
            SetTextPos();

            RectTransform.ScaleChanged += SetTextPos;
            RectTransform.SizeChanged += SetTextPos;

            Enabled = true;
            Censor = false;
        }

        public void CalculateHeightFromText(int padding = 0, bool removeExtraSpacing = false)
        {
            if (wrappedText == null) { return; }
            RectTransform.Resize(new Point(RectTransform.Rect.Width, (int)Font.MeasureString(wrappedText, removeExtraSpacing).Y + padding));
        }

        public void SetRichText(LocalizedString richText)
        {
            Text = RichString.Rich(richText);
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

            if (Font == null || !componentStyle.Font.IsEmpty)
            {
                Font = GUIStyle.Fonts[componentStyle.Font.AppendIfMissing("Font")];
            }
        }

        public void ClearCaretPositions()
        {
            cachedCaretPositions = ImmutableArray<Vector2>.Empty;
        }
        
        public void SetTextPos()
        {
            ClearCaretPositions();
            if (text == null) { return; }

            censoredText = text.IsNullOrEmpty() ? "" : new string('\u2022', text.Length);

            var rect = Rect;

            overflowClipActive = false;
            wrappedText = text.SanitizedString;
            
            TextSize = MeasureText(text.SanitizedString);
            
            if (Wrap && rect.Width > 0)
            {
                wrappedText = ToolBox.WrapText(text.SanitizedString, rect.Width - padding.X - padding.Z, Font, textScale);
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

            origin.X = 0;
            if (textAlignment.HasFlag(Alignment.Left))
            {
                textPos.X = padding.X;
            }            
            if (textAlignment.HasFlag(Alignment.Right))
            {
                textPos.X = rect.Width - padding.Z;
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

        private Vector2 MeasureText(LocalizedString text)
        {
            return MeasureText(text.Value);
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
            string textDrawn = Censor ? CensoredText : Text.SanitizedValue;
            float w = Wrap
                ? (Rect.Width - Padding.X - Padding.Z) / TextScale
                : float.PositiveInfinity;
            string wrapped = Font.WrapText(textDrawn, w, out Vector2[] positions);
            int textWidth = (int)Font.MeasureString(wrapped).X;
            int alignmentXDiff
                = textAlignment.HasFlag(Alignment.Right) ? textWidth
                    : textAlignment.HasFlag(Alignment.Center) ? textWidth / 2
                    : 0;
            cachedCaretPositions = positions
                .Select(p => p - new Vector2(alignmentXDiff, 0))
                .Select(p => p * TextScale + TextPos - Origin * TextScale)
                .ToImmutableArray();
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

            if (!text.IsNullOrEmpty())
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
                    string textToShow = Censor ? censoredText : (Wrap ? wrappedText.Value : text.SanitizedValue);
                    Color colorToShow = currentTextColor * (currentTextColor.A / 255.0f);

                    if (Shadow)
                    {
                        Vector2 shadowOffset = new Vector2(GUI.IntScale(2));
                        Font.DrawString(spriteBatch, textToShow, pos + shadowOffset, Color.Black, 0.0f, origin, TextScale, SpriteEffects.None, textDepth, alignment: textAlignment, forceUpperCase: ForceUpperCase);
                    }

                    Font.DrawString(spriteBatch, textToShow, pos, colorToShow, 0.0f, origin, TextScale, SpriteEffects.None, textDepth, alignment: textAlignment, forceUpperCase: ForceUpperCase);
                }
                else
                {
                    if (OverrideRichTextDataAlpha)
                    {
                        RichTextData.Value.ForEach(rt => rt.Alpha = currentTextColor.A / 255.0f);
                    }
                    Font.DrawStringWithColors(spriteBatch, Censor ? censoredText : (Wrap ? wrappedText : text.SanitizedString).Value, pos,
                        currentTextColor * (currentTextColor.A / 255.0f), 0.0f, origin, TextScale, SpriteEffects.None, textDepth, RichTextData.Value, alignment: textAlignment, forceUpperCase: ForceUpperCase);
                }

                Strikethrough?.Draw(spriteBatch, (int)Math.Ceiling(TextSize.X / 2f), pos.X,
                    /* TODO: ???? */ForceUpperCase == ForceUpperCase.Yes ? pos.Y : pos.Y + GUI.Scale * 2f);
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
