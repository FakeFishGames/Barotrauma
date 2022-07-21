using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class ListDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    {
        private readonly ImmutableDictionary<TKey, int> keyToIndex;
        private readonly IReadOnlyList<TValue> list;

        public ListDictionary(IReadOnlyList<TValue> list, int len, Func<int, TKey> keyFunc)
        {
            this.list = list;
            var keyToIndex = new Dictionary<TKey, int>();
            for (int i = 0; i < len; i++)
            {
                keyToIndex.Add(keyFunc(i), i);
            }

            this.keyToIndex = keyToIndex.ToImmutableDictionary();
        }
        
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            foreach (var kvp in keyToIndex)
            {
                yield return new KeyValuePair<TKey, TValue>(kvp.Key, list[kvp.Value]);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count => keyToIndex.Count;
        public bool ContainsKey(TKey key) => keyToIndex.ContainsKey(key);

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (keyToIndex.TryGetValue(key, out int index))
            {
                value = list[index];
                return true;
            }
            value = default(TValue);
            return false;
        }

        public TValue this[TKey key] => list[keyToIndex[key]];

        public IEnumerable<TKey> Keys => keyToIndex.Keys;
        public IEnumerable<TValue> Values => keyToIndex.Values.Select(i => list[i]);
    }
}