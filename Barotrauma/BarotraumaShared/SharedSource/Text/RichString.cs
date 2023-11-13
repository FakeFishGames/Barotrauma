#nullable enable
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Barotrauma
{
    public class RichString
    {
        protected bool loaded = false;
        protected LanguageIdentifier language = LanguageIdentifier.None;

        protected string cachedSanitizedValue = "";
        public string SanitizedValue
        {
            get
            {
                if (MustRetrieveValue()) { RetrieveValue(); }
                return cachedSanitizedValue;
            }
        }

        public int Length => SanitizedValue.Length;
        
        private readonly Func<string, string>? postProcess;
        private readonly bool shouldParseRichTextData;

        private readonly LocalizedString originalStr;
        public LocalizedString NestedStr { get; private set; }
        public readonly LocalizedString SanitizedString;

#if CLIENT
        private readonly GUIFont? font;
        private readonly GUIComponentStyle? componentStyle;
        private readonly bool forceUpperCase = false;

        private bool fontOrStyleForceUpperCase
            => font is { ForceUpperCase: true } || componentStyle is { ForceUpperCase: true };
#endif
        
        public ImmutableArray<RichTextData>? RichTextData { get; private set; }

#if CLIENT
        private RichString(
            LocalizedString nestedStr, bool shouldParseRichTextData, Func<string, string>? postProcess = null,
            GUIFont? font = null, GUIComponentStyle? componentStyle = null) : this(nestedStr, shouldParseRichTextData, postProcess)
        {
            this.font = font;
            this.componentStyle = componentStyle;
        }
#endif
        
        private RichString(LocalizedString nestedStr, bool shouldParseRichTextData, Func<string,string>? postProcess = null)
        {
            originalStr = nestedStr;
            NestedStr = originalStr;
            this.shouldParseRichTextData = shouldParseRichTextData;
            this.postProcess = postProcess;
            SanitizedString = new StripRichTagsLString(this);
#if CLIENT
            this.font = null;
            this.componentStyle = null;
#endif
        }

        public static RichString Rich(LocalizedString str, Func<string, string>? postProcess = null)
        {
            return new RichString(str, true, postProcess);
        }

        public static RichString Plain(LocalizedString str)
        {
            return new RichString(str, false, postProcess: null);
        }

        public static implicit operator LocalizedString(RichString richStr) => richStr.NestedStr;

        public static implicit operator RichString(LocalizedString lStr)
        {
#if DEBUG
            if (!lStr.IsNullOrEmpty() && lStr.Contains("‖"))
            {
                //if (Debugger.IsAttached) { Debugger.Break(); }
            }
#endif
            return Plain(lStr ?? string.Empty);
        }
        public static implicit operator RichString(string str) => (LocalizedString)str;

        protected virtual bool MustRetrieveValue()
        {
            return NestedStr.Loaded != loaded
                || language != GameSettings.CurrentConfig.Language
#if CLIENT
                || (fontOrStyleForceUpperCase != forceUpperCase)
#endif
                ;
        }
        
        public void RetrieveValue()
        {
#if CLIENT
            NestedStr = fontOrStyleForceUpperCase ? originalStr.ToUpper() : originalStr;
#endif

            if (shouldParseRichTextData)
            {
                RichTextData = Barotrauma.RichTextData.GetRichTextData(NestedStr.Value, out cachedSanitizedValue);
            }
            else
            {
                cachedSanitizedValue = NestedStr.Value;
            }
            if (postProcess != null) { cachedSanitizedValue = postProcess(cachedSanitizedValue); }
            language = GameSettings.CurrentConfig.Language;
            loaded = NestedStr.Loaded;
        }

#if CLIENT
        public RichString CaseTiedToFontAndStyle(GUIFont? font, GUIComponentStyle? componentStyle)
        {
            return new RichString(originalStr, shouldParseRichTextData, postProcess, font, componentStyle);
        }
#endif
        
        public RichString ToUpper()
        {
            return new RichString(NestedStr.ToUpper(), shouldParseRichTextData, postProcess);
        }
        
        public RichString ToLower()
        {
            return new RichString(NestedStr.ToLower(), shouldParseRichTextData, postProcess);
        }

        public RichString Replace(string from, string to, StringComparison stringComparison = StringComparison.Ordinal)
        {
            return new RichString(NestedStr.Replace(from, to, stringComparison), shouldParseRichTextData, postProcess);
        }

        public override string ToString()
        {
            return SanitizedValue;
        }

        public bool Contains(string str, StringComparison stringComparison = StringComparison.Ordinal) =>
            SanitizedValue.Contains(str, stringComparison);

        public bool Contains(char chr, StringComparison stringComparison = StringComparison.Ordinal) =>
            SanitizedValue.Contains(chr, stringComparison);


        public static bool operator ==(RichString? a, RichString? b)
            => a?.SanitizedValue == b?.SanitizedValue
#if CLIENT
                && a?.font == b?.font
                && a?.componentStyle == b?.componentStyle
#endif
                ;

        public static bool operator !=(RichString? a, RichString? b) => !(a == b);
        
        public static bool operator ==(RichString? a, LocalizedString? b)
            => a?.SanitizedValue == b?.Value;

        public static bool operator !=(RichString? a, LocalizedString? b) => !(a == b);
        
        public static bool operator ==(LocalizedString? a, RichString? b)
            => a?.Value == b?.SanitizedValue;

        public static bool operator !=(LocalizedString? a, RichString? b) => !(a == b);
        
        public static bool operator ==(RichString? a, string? b)
            => a?.SanitizedValue == b;

        public static bool operator !=(RichString? a, string? b) => !(a == b);
        
        public static bool operator ==(string? a, RichString? b)
            => a == b?.SanitizedValue;

        public static bool operator !=(string? a, RichString? b) => !(a == b);
    }
    
    class StripRichTagsLString : LocalizedString
    {
        public readonly RichString RichStr;

        public StripRichTagsLString(RichString richStr)
        {
            RichStr = richStr;
        }

        public override bool Loaded => RichStr.NestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = RichStr.SanitizedValue;
        }
    }
}