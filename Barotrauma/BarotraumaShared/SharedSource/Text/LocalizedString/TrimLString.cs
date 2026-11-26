#nullable enable
using System;

namespace Barotrauma
{
    public class TrimLString : LocalizedString
    {
        [Flags]
        public enum Mode { Start = 0x1, End = 0x2, Both=0x3 }
        private readonly LocalizedString nestedStr;
        private readonly Mode mode;
        private readonly char[]? trimCharacters;

        public TrimLString(LocalizedString nestedStr, Mode mode, char[]? trimCharacters = null)
        {
            this.nestedStr = nestedStr;
            this.mode = mode;
            this.trimCharacters = trimCharacters;
        }

        public override bool Loaded => nestedStr.Loaded;
        public override bool UsingDefaultLanguageAsFallback => nestedStr.UsingDefaultLanguageAsFallback;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value;
            if (mode.HasFlag(Mode.Start)) { cachedValue = cachedValue.TrimStart(trimCharacters); }
            if (mode.HasFlag(Mode.End)) { cachedValue = cachedValue.TrimEnd(trimCharacters); }
            UpdateLanguage();
        }
    }
}