#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public class ReplaceLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;
        private readonly ImmutableDictionary<LocalizedString, (LocalizedString Value, FormatCapitals FormatCapitals)> replacements;
        private readonly StringComparison stringComparison;

        public ReplaceLString(LocalizedString nStr, StringComparison sc, IEnumerable<(LocalizedString Key, LocalizedString Value, FormatCapitals FormatCapitals)> r)
        {
            nestedStr = nStr;
            replacements = r.Select(kvf => (kvf.Key, (kvf.Value, kvf.FormatCapitals))).ToImmutableDictionary();
            stringComparison = sc;
        }
        
        public ReplaceLString(LocalizedString nStr, StringComparison sc, params (LocalizedString Key, LocalizedString Value)[] r)
            : this(nStr, sc, r.Select(kv => (kv.Key, kv.Value, FormatCapitals.No))) { }

        public ReplaceLString(LocalizedString nStr, StringComparison sc, IEnumerable<(Identifier Key, LocalizedString Value, FormatCapitals FormatCapitals)> r)
            : this(nStr, sc, r.Select(p => ((LocalizedString)p.Key.Value, p.Value, p.FormatCapitals))) { }
        
        public ReplaceLString(LocalizedString nStr, StringComparison sc, params (Identifier Key, LocalizedString Value)[] r)
            : this(nStr, sc, r.Select(kv => ((LocalizedString)kv.Key.Value, kv.Value, FormatCapitals.No))) { }

        private static string HandleVariableCapitalization(string text, string variableTag, string variableValue)
        {
            int index = text.IndexOf(variableTag, StringComparison.InvariantCulture) - 1;
            if (index == -1)
            {
                return variableValue;
            }

            for (int i = index; i >= 0; i--)
            {
                if (char.IsWhiteSpace(text[i])) { continue; }

                if (text[i] != '.')
                {
                    variableValue = variableValue.ToLowerInvariant();
                }
                else
                {
                    variableValue = TextManager.Capitalize(variableValue).Value;
                    break;
                }
            }

            return variableValue;
        }
        
        public override bool Loaded => nestedStr.Loaded;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value;
            foreach (var varName in replacements.Keys)
            {
                string key = varName.Value;
                string value = replacements[varName].Value.Value;
                if (replacements[varName].FormatCapitals == FormatCapitals.Yes)
                {
                    value = HandleVariableCapitalization(cachedValue, key, value);
                }
                cachedValue = cachedValue.Replace(key, value, stringComparison);
            }
            UpdateLanguage();
        }
    }
}