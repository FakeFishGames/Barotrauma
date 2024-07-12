#nullable enable
namespace Barotrauma
{
    public class WrappedLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;
        private readonly float lineLength;
        private readonly GUIFont font;
        private readonly float textScale;

        public WrappedLString(LocalizedString text, float lineLength, GUIFont font, float textScale = 1.0f)
        {
            this.nestedStr = text;
            this.lineLength = lineLength;
            this.font = font;
            this.textScale = textScale;
        }

        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = 
                font.GetFontForStr(nestedStr.Value) is ScalableFont scalableFont
                    ? ToolBox.WrapText(nestedStr.Value, lineLength, scalableFont, textScale)
                    : nestedStr.Value;
            UpdateLanguage();
        }
    }
}