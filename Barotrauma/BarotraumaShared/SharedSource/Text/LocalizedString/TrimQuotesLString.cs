#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Barotrauma
{
    /// <summary>
    /// Removes any quotation characters from the beginning and end of the localized string, supports different 
    /// quotation characters in other languages as well.
    /// </summary>
    public class TrimQuotesLString : LocalizedString
    {
        private readonly LocalizedString nestedStr;

        /// <summary>
        /// While this could instead just get the unicode category for the character and check if it's 
        /// a <see cref="UnicodeCategory.InitialQuotePunctuation"/> or <see cref="UnicodeCategory.FinalQuotePunctuation"/>, 
        /// some unicode characters (specifically Low-high) seem to be incorrectly categorized. So I decided
        /// to just build this list of the characters the localization texts use as their main quotation marks.
        /// Single quotes, nested quotes (e.g. ‘ ’, ‚ ‘, 『 』) and title marks (e.g. 《 》) are intentionally left out:
        /// single quotes are mostly used as apostrophes (e.g. "can't", "l'homme"), which would make us trim the text at the wrong place.
        /// </summary>
        private static readonly HashSet<char> QuoteCharacters = new()
        {
            // ASCII
            '"',
            // Typographic (English, Chinese, Turkish, Korean: “ ”, German: „ “, Polish: „ ”)
            '“','”','„',
            // Reversed (alternative opening quote, not currently used in the texts)
            '‟',
            // Guillemets (Russian, Polish)
            '«','»',
            // CJK corner brackets (Japanese, Traditional Chinese)
            '「','」',
            // Decorative (not currently used in the texts)
            '❝','❞'
        };

        private bool IsQuotation(char c) => QuoteCharacters.Contains(c);

        public TrimQuotesLString(LocalizedString nestedStr)
        {
            this.nestedStr = nestedStr;
        }

        public override bool Loaded => nestedStr.Loaded;
        public override bool UsingDefaultLanguageAsFallback => nestedStr.UsingDefaultLanguageAsFallback;
        public override void RetrieveValue()
        {
            cachedValue = nestedStr.Value;

            int start = -1;
            int end = -1;

            for (int i = 0; i < cachedValue.Length; i++)
            {
                if (IsQuotation(cachedValue[i]))
                {
                    start = i + 1;
                    break;
                }
            }

            if (start >= 0)
            {
                // Find the last quotation mark
                for (int i = cachedValue.Length - 1; i >= start; i--)
                {
                    if (IsQuotation(cachedValue[i]))
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (start >= 0 && end >= start)
            {
                cachedValue = cachedValue.Substring(start, end - start).Trim();
            }

            cachedValue = cachedValue.Trim();

            UpdateLanguage();
        }
    }
}