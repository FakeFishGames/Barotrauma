using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

#nullable enable
namespace Barotrauma
{
    public class LStringSplitter
    {
        public IReadOnlyList<LocalizedString> Substrings => substrings;

        private class SubstringList : IReadOnlyList<LocalizedString>
        {
            public SubstringList(LStringSplitter splitter) { this.splitter = splitter; }
            
            private LStringSplitter splitter;
            private readonly List<LocalizedString> underlyingList = new List<LocalizedString>();

            public List<LocalizedString> UnderlyingList
            {
                get
                {
                    splitter.UpdateSubstrings();
                    return underlyingList;
                }
            }

            public IEnumerator<LocalizedString> GetEnumerator() => UnderlyingList.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public int Count => UnderlyingList.Count;

            public LocalizedString this[int index] => UnderlyingList[index];
        }
        
        private readonly SubstringList substrings;
        private readonly char[] separators;
        private readonly LocalizedString originalString;
        private string[] substrValues;

        private string cachedOriginal;

        public bool Loaded => originalString.Loaded;

        public LStringSplitter(LocalizedString input, params char[] separators)
        {
            originalString = input;
            substrings = new SubstringList(this);
            substrValues = Array.Empty<string>();
            this.separators = separators;
            cachedOriginal = "";
        }

        private void UpdateSubstrings()
        {
            if (originalString.Value != cachedOriginal)
            {
                cachedOriginal = originalString.Value;
                substrValues = cachedOriginal.Split(separators);
                substrings.UnderlyingList.Clear();
                substrings.UnderlyingList.AddRange(Enumerable.Range(0, substrValues.Length).Select(i => new SplitLString(this, i) as LocalizedString));
            }
        }
        
        public string GetValue(int index)
        {
            UpdateSubstrings();
            return substrValues[index];
        }
    }
    
    public class SplitLString : LocalizedString
    {
        private bool loaded = false;
        private readonly LStringSplitter splitter;
        private readonly int index;

        public SplitLString(LStringSplitter splitter, int index)
        {
            this.splitter = splitter; this.index = index;
        }

        public override bool Loaded => loaded && splitter.Loaded;
        public override void RetrieveValue()
        {
            loaded = true;
            cachedValue = splitter.GetValue(index);
            UpdateLanguage();
        }
    }
}