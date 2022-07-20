#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class JoinLString : LocalizedString
    {
        private readonly IEnumerable<LocalizedString> subStrs;
        private readonly string separator;

        public JoinLString(string separator, IEnumerable<LocalizedString> subStrs)
        {
            this.separator = separator; this.subStrs = subStrs;
        }

        public override bool Loaded => subStrs.All(s => s.Loaded);
        public override void RetrieveValue()
        {
            cachedValue = string.Join(separator, subStrs);
            UpdateLanguage();
        }
    }
}