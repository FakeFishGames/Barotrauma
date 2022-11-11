#nullable enable

using Microsoft.Xna.Framework;

namespace Barotrauma
{
    public sealed class IMEPreviewTextHandler
    {
        public string PreviewText { get; private set; } = string.Empty;
        public Vector2 TextSize { get; private set; }
        public bool HasText => !string.IsNullOrEmpty(PreviewText);

        // This has to be settable because for some reason we update the font of GUITextBox in some places
        public GUIFont Font { get; set; }

        public IMEPreviewTextHandler(GUIFont font)
        {
            Font = font;
        }

        public void Reset()
        {
            TextSize = Vector2.Zero;
            PreviewText = string.Empty;
        }

        public void UpdateText(string text, int start)
        {
            if (string.IsNullOrEmpty(text) && start is 0)
            {
                Reset();
                return;
            }

            int totalLength = start + text.Length;
            string newText = PreviewText;
            if (newText.Length > totalLength)
            {
                newText = newText[..totalLength];
            }

            if (totalLength > newText.Length)
            {
                // this is required for some reason on Windows
                // my guess is that the order which TextEditing events come thru is not guaranteed
                newText = newText.PadRight(totalLength);
            }

            newText = newText.Remove(start, text.Length).Insert(start, text);
            PreviewText = newText;
            TextSize = Font.MeasureString(PreviewText);
        }
    }
}