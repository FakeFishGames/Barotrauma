#if DEBUG && MODBREAKER
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    /// <summary>
    /// Dictionary that's been deliberately designed to be a piece of
    /// shit. Meant to be used to stress-test scenarios where we might
    /// accidentally be relying on the implementation details of a
    /// dictionary that shouldn't be relied on.
    /// </summary>
    public class CursedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue> where TKey: notnull
    {
        private ICollection<TKey> keys;
        private ICollection<TValue> values;
        private readonly List<KeyValuePair<TKey, TValue>> keyValuePairs = new List<KeyValuePair<TKey, TValue>>();
        private readonly Dictionary<TKey, int> keyToKvpIndex = new Dictionary<TKey, int>();
        private readonly object mutex = new object();

        private readonly Random rng;

        public CursedDictionary()
        {
            rng = new Random((int)(DateTime.Now.ToBinary() % int.MaxValue));
            keys = Array.Empty<TKey>();
            values = Array.Empty<TValue>();
        }

        private void Refresh()
        {
            keys = keyValuePairs.Select(kvp => kvp.Key).ToArray();
            values = keyValuePairs.Select(kvp => kvp.Value).ToArray();
            keyToKvpIndex.Clear();
            for (int i=0; i<keyValuePairs.Count; i++)
            {
                keyToKvpIndex[keyValuePairs[i].Key] = i;
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            KeyValuePair<TKey, TValue>[] clone;
            lock (mutex)
            {
                keyValuePairs.Shuffle(rng);
                Refresh();
                 clone = keyValuePairs.ToArray();
            }

            foreach (var kvp in clone)
            {
                yield return kvp;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            lock (mutex)
            {
                if (keyToKvpIndex.ContainsKey(item.Key))
                {
                    throw new InvalidOperationException($"Duplicate key: {item.Key}");
                }

                keyValuePairs.Add(item);
                keyValuePairs.Shuffle(rng);
                Refresh();
            }
        }

        public void Clear()
        {
            lock (mutex)
            {
                keyValuePairs.Clear();
                Refresh();
            }
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            lock (mutex)
            {
                return keyValuePairs.Contains(item);
            }
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            lock (mutex)
            {
                keyValuePairs.Shuffle(rng);
                keyValuePairs.CopyTo(array, arrayIndex);
                Refresh();
            }
        }
        
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            lock (mutex)
            {
                bool success = keyValuePairs.Remove(item);
                keyValuePairs.Shuffle(rng);
                Refresh();
                return success;
            }
        }

        public int Count
        {
            get
            {
                lock (mutex)
                {
                    return keyValuePairs.Count;
                }
            }
        }

        public bool IsReadOnly => false;
        
        public void Add(TKey key, TValue value) => Add(new KeyValuePair<TKey, TValue>(key, value));

        public bool ContainsKey(TKey key)
        {
            lock (mutex)
            {
                return keyToKvpIndex.ContainsKey(key);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (mutex)
            {
                value = default!;
                bool success = keyToKvpIndex.TryGetValue(key, out int index);
                if (success)
                {
                    value = keyValuePairs[index].Value;
                }

                keyValuePairs.Shuffle(rng);
                Refresh();
                return success;
            }
        }

        public bool Remove(TKey key) => TryRemove(key, out _);

        public bool TryRemove(TKey key, out TValue value)
        {
            lock (mutex)
            {
                value = default!;
                bool success = false;
                if (keyToKvpIndex.TryGetValue(key, out int index))
                {
                    value = keyValuePairs[index].Value;
                    keyValuePairs.RemoveAt(index);
                    success = true;
                }

                keyValuePairs.Shuffle(rng);
                Refresh();
                return success;
            }
        }
        
        public TValue this[TKey key]
        {
            get
            {
                lock (mutex)
                {
                    return keyValuePairs[keyToKvpIndex[key]].Value;
                }
            }
            set
            {
                lock (mutex)
                {
                    if (!keyToKvpIndex.ContainsKey(key))
                    {
                        Add(key, value);
                    }
                    else
                    {
                        keyValuePairs[keyToKvpIndex[key]] = new KeyValuePair<TKey, TValue>(key, value);
                    }

                    keyValuePairs.Shuffle(rng);
                    Refresh();
                }
            }
        }


        public ICollection<TKey> Keys
        {
            get
            {
                lock (mutex)
                {
                    return keys.ToArray();
                }
            }
        }
        public ICollection<TValue> Values
        {
            get
            {
                lock (mutex)
                {
                    return values.ToArray();
                }
            }
        }
        
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
    }
}
#endif
