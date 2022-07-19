#nullable enable
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class AddedPunctuationLString : LocalizedString
    {
        private readonly ImmutableArray<LocalizedString> nestedStrs;
        private readonly char punctuationSymbol;

        public AddedPunctuationLString(char symbol, params LocalizedString[] nStrs) { nestedStrs = nStrs.ToImmutableArray(); punctuationSymbol = symbol; }

        public override bool Loaded => nestedStrs.All(s => s.Loaded);
        public override void RetrieveValue()
        {
            string separator = "";
            if (GameSettings.CurrentConfig.Language == "French".ToLanguageIdentifier())
            {
                bool addNonBreakingSpace =
                    punctuationSymbol == ':' || punctuationSymbol == ';' ||
                    punctuationSymbol == '!' || punctuationSymbol == '?';
                separator = addNonBreakingSpace ?
                    new string(new char[] { (char)(0xA0), punctuationSymbol, ' ' }) :
                    new string(new char[] { punctuationSymbol, ' ' });
            }
            else
            {
                separator = new string(new char[] { punctuationSymbol, ' ' });
            }
            cachedValue = string.Join(separator, nestedStrs.Select(str => str.Value));
            UpdateLanguage();
        }
    }
}