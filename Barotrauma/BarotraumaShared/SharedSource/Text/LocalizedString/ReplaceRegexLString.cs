#nullable enable
using System;
using System.Text.RegularExpressions;

namespace Barotrauma
{
    public class RegexReplaceLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;
        private readonly Regex regex;
        private readonly string replaceStr;

        public RegexReplaceLString(LocalizedString nestedStr, Regex regex, string replaceStr)
        {
            this.nestedStr = nestedStr;
            this.replaceStr = replaceStr;
            this.regex = regex;
        }

        public override bool Loaded => nestedStr.Loaded;
        public override bool UsingDefaultLanguageAsFallback => nestedStr.UsingDefaultLanguageAsFallback;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value;
            cachedValue = regex.Replace(cachedValue, replaceStr);
            UpdateLanguage();
        }
    }
}