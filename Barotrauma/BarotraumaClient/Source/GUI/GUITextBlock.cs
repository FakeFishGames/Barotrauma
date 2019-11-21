using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
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
        
        protected Color textColor;

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
                if (base.Font == value) return;
                base.Font = originalFont = value;
                SetTextPos();
            }
        }

        public string Text
        {
            get { return text; }
            set
            {
                string newText = forceUpperCase ? value?.ToUpper() : value;

                if (Text == newText) { return; }


                //reset scale, it gets recalculated in SetTextPos
                if (autoScale) { textScale = 1.0f; }

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

        private bool autoScale;

        /// <summary>
        /// When enabled, the text is automatically scaled down to fit the textblock.
        /// </summary>
        public bool AutoScale
        {
            get { return autoScale; }
            set
            {
                if (autoScale == value) return;
                autoScale = value;
                if (autoScale)
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
                if (forceUpperCase)
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
                
        /// <summary>
        /// This is the new constructor.
        /// If the rectT height is set 0, the height is calculated from the text.
        /// </summary>
        public GUITextBlock(RectTransform rectT, string text, Color? textColor = null, ScalableFont font = null, 
            Alignment textAlignment = Alignment.Left, bool wrap = false, string style = "", Color? color = null, bool playerInput = false) 
            : base(style, rectT)
        {
            if (color.HasValue)
            {
                this.color = color.Value;
            }
            if (textColor.HasValue)
            {
                this.textColor = textColor.Value;
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

            Censor = false;
        }

        public void CalculateHeightFromText(int padding = 0)
        {
            if (wrappedText == null) { return; }
            RectTransform.Resize(new Point(RectTransform.Rect.Width, (int)Font.MeasureString(wrappedText).Y + padding));
        }
        
        public override void ApplyStyle(GUIComponentStyle style)
        {
            if (style == null) return;
            base.ApplyStyle(style);
            padding = style.Padding;

            textColor = style.textColor;
        }
        
        public void SetTextPos()
        {
            if (text == null) return;

            censoredText = "";
            for (int i = 0; i < text.Length; i++)
            {
                censoredText += "\u2022";
            }

            var rect = Rect;

            overflowClipActive = false;
            wrappedText = text;
            
            TextSize = MeasureText(text);
            
            if (Wrap && rect.Width > 0)
            {
                wrappedText = ToolBox.WrapText(text, rect.Width - padding.X - padding.Z, Font, textScale, playerInput);
                TextSize = MeasureText(wrappedText);
            }
            else if (OverflowClip)
            {
                overflowClipActive = TextSize.X > rect.Width - padding.X - padding.Z;
            }

            if (autoScale && textScale > 0.1f &&
                (TextSize.X * textScale > rect.Width - padding.X - padding.Z || TextSize.Y * textScale > rect.Height - padding.Y - padding.W))
            {
                TextScale = Math.Max(0.1f, Math.Min(
                    (rect.Width - padding.X - padding.Z) / TextSize.X, 
                    (rect.Height - padding.Y - padding.W) / TextSize.Y)) - 0.01f;
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
                catch { text = text.Substring(0, text.Length - 1); }
            }

            return size;
        }

        protected override void SetAlpha(float a)
        {
            base.SetAlpha(a);
            textColor = new Color(textColor.R, textColor.G, textColor.B, a);
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            Color currColor = GetCurrentColor(state);

            var rect = Rect;

            base.Draw(spriteBatch);

            if (TextGetter != null) Text = TextGetter();

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (overflowClipActive)
            {
                spriteBatch.End();
                Rectangle scissorRect = new Rectangle(rect.X + (int)padding.X, rect.Y, rect.Width - (int)padding.X - (int)padding.Z, rect.Height);
                spriteBatch.GraphicsDevice.ScissorRectangle = scissorRect;
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

                Font.DrawString(spriteBatch,
                    Censor ? censoredText : (Wrap ? wrappedText : text),
                    pos,
                    textColor * (textColor.A / 255.0f),
                    0.0f, origin, TextScale,
                    SpriteEffects.None, textDepth);
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
        public static void AutoScaleAndNormalize(IEnumerable<GUITextBlock> textBlocks)
        {
            if (!textBlocks.Any()) { return; }
            float minScale = Math.Max(textBlocks.First().TextScale, 1.0f);
            foreach (GUITextBlock textBlock in textBlocks)
            {
                textBlock.AutoScale = true;
                minScale = Math.Min(textBlock.TextScale, minScale);
            }

            foreach (GUITextBlock textBlock in textBlocks)
            {
                textBlock.AutoScale = false;
                textBlock.TextScale = minScale;
            }
        }
    }
}
