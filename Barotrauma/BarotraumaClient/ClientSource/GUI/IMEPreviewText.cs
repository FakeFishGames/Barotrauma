#nullable enable

using System.Collections.Immutable;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{

    public sealed class IMEPreviewTextHandler
    {
        public bool HasText => !string.IsNullOrEmpty(previewText);

        // This has to be settable because for some reason we update the font of GUITextBox in some places
        public GUIFont Font { get; set; }

        private string previewText = string.Empty;
        private Vector2 textSize;

        private bool isSectioned;
        private ImmutableArray<RichTextData>? richTextData;

        public IMEPreviewTextHandler(GUIFont font)
        {
            Font = font;
        }

        public void Reset()
        {
            textSize = Vector2.Zero;
            previewText = string.Empty;
            richTextData = null;
            isSectioned = false;
        }

        public void UpdateText(string text, int start, int length)
        {
            isSectioned = start >= 0 && length > 0;
            richTextData = null;

            if (string.IsNullOrEmpty(text))
            {
                Reset();
                return;
            }

            previewText = text;

            textSize = Font.MeasureString(text);

            if (!isSectioned) { return; }

            string coloredText = ToolBox.ColorSectionOfString(text, start, length, GUIStyle.Orange);

            RichString richString = RichString.Rich(coloredText);

            previewText = richString.SanitizedValue;
            richTextData = richString.RichTextData;
        }

        public void DrawIMEPreview(SpriteBatch spriteBatch, Vector2 position, GUITextBlock textBlock)
        {
            if (!HasText) { return; }

            int inflate = GUI.IntScale(3);

            RectangleF rect = new RectangleF(position, textSize);
            rect.Inflate(inflate, inflate);

            RectangleF borderRect = rect;
            borderRect.Inflate(1, 1);

            GUI.DrawFilledRectangle(spriteBatch, borderRect, Color.White, depth: 0.02f);
            GUI.DrawFilledRectangle(spriteBatch, rect, Color.Black, depth: 0.01f);

            Font.DrawStringWithColors(spriteBatch,
                text: previewText,
                position: position,
                color: isSectioned ? GUIStyle.TextColorNormal : GUIStyle.Orange,
                rotation: 0.0f,
                origin: Vector2.Zero,
                scale: 1f,
                spriteEffects: SpriteEffects.None,
                layerDepth: 0,
                richTextData: richTextData,
                alignment: textBlock.TextAlignment,
                forceUpperCase: textBlock.ForceUpperCase);
        }
    }
}