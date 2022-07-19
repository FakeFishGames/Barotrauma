#nullable enable
namespace Barotrauma
{
    public class LowerLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;

        public LowerLString(LocalizedString nestedStr)
        {
            this.nestedStr = nestedStr;
        }

        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value.ToLowerInvariant();
            UpdateLanguage();
        }
    }
}