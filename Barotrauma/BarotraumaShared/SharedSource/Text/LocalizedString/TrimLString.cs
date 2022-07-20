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

        public TrimLString(LocalizedString nestedStr, Mode mode)
        {
            this.nestedStr = nestedStr;
            this.mode = mode;
        }

        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value;
            if (mode.HasFlag(Mode.Start)) { cachedValue = cachedValue.TrimStart(); }
            if (mode.HasFlag(Mode.End)) { cachedValue = cachedValue.TrimEnd(); }
            UpdateLanguage();
        }
    }
}