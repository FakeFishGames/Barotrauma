#nullable enable
namespace Barotrauma
{
    public class ConcatLString : LocalizedString
    {
        private readonly LocalizedString left;
        private readonly LocalizedString right;

        public ConcatLString(LocalizedString l, LocalizedString r)
        {
            left = l; right = r;
        }

        public override bool Loaded => left.Loaded || right.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = left.Value + right.Value;
            UpdateLanguage();
        }
    }
}