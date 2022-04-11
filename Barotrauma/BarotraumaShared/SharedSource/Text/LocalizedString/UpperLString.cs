#nullable enable
namespace Barotrauma
{
    public class UpperLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;

        public UpperLString(LocalizedString nestedStr)
        {
            this.nestedStr = nestedStr;
        }

        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value.ToUpperInvariant();
            UpdateLanguage();
        }

        public override LocalizedString ToUpper()
        {
            return this;
        }
    }
}