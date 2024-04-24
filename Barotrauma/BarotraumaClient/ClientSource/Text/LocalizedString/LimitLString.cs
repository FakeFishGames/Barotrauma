#nullable enable
namespace Barotrauma
{
    public class LimitLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;
        private readonly GUIFont font;
        private readonly int maxWidth;

        private ScalableFont? cachedFont = null;
        
        public LimitLString(LocalizedString text, GUIFont font, int maxWidth)
        {
            this.nestedStr = text;
            this.font = font;
            this.maxWidth = maxWidth;
        }

        public override bool Loaded => nestedStr.Loaded;
        protected override bool MustRetrieveValue()
        {
            return base.MustRetrieveValue() || cachedFont != font.Value || cachedFont?.Size != font.Size;
        }
        
        public override void RetrieveValue()
        {
            cachedValue = font.Value != null
                ? ToolBox.LimitString(nestedStr.Value, font.Value, maxWidth)
                : nestedStr.Value;
            cachedFont = font.Value;
            UpdateLanguage();
        }
    }
}