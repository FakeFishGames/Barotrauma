#nullable enable
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class FormattedLString : LocalizedString
    {
        private readonly LocalizedString str;
        private readonly ImmutableArray<LocalizedString> subStrs;
        public FormattedLString(LocalizedString str, params LocalizedString[] subStrs)
        {
            this.str = str;
            this.subStrs = subStrs.ToImmutableArray();
        }

        public override bool Loaded => str.Loaded && subStrs.All(s => s.Loaded);
        public override void RetrieveValue()
        {
            //TODO: possibly broken!
            cachedValue = string.Format(str.Value, subStrs.Select(s => s.Value as object).ToArray());
            UpdateLanguage();
        }
    }
}