using System.Collections.Generic;
using System;
using System.Linq;
using System.Collections.Immutable;

namespace Barotrauma.Extensions
{
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// Randomizes the collection (using OrderBy) and returns it.
        /// </summary>
        public static T[] Randomize<T>(this IList<T> source, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            return source.OrderBy(i => Rand.Value(randSync)).ToArray();
        }

        /// <summary>
        /// Randomizes the list in place without creating a new collection, using a Fisher-Yates-based algorithm.
        /// </summary>
        public static void Shuffle<T>(this IList<T> list, Rand.RandSync randSync = Rand.RandSync.Unsynced)
            => list.Shuffle(Rand.GetRNG(randSync));

        public static void Shuffle<T>(this IList<T> list, Random rng)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public static T GetRandom<T>(this IReadOnlyList<T> source, Func<T, bool> predicate, Rand.RandSync randSync)
        {
            if (predicate == null) { return GetRandom(source, randSync); }
            return source.Where(predicate).ToArray().GetRandom(randSync);
        }

        /// <summary>
        /// Gets a random element of a list using one of the synced random number generators.
        /// It's recommended that you guarantee a deterministic order of the elements of the
        /// input list via sorting.
        /// </summary>
        /// <param name="source">List to pick a random element from</param>
        /// <param name="randSync">Which RNG to use</param>
        /// <returns>A random item from the list. Return value should match between clients and
        /// the server, if applicable.</returns>
        public static T GetRandom<T>(this IReadOnlyList<T> source, Rand.RandSync randSync)
        {
            int count = source.Count;
            return count == 0 ? default : source[Rand.Range(0, count, randSync)];
        }

        public static T GetRandom<T>(this IReadOnlyList<T> source, Random random)
        {
            int count = source.Count;
            return count == 0 ? default : source[random.Next(0, count)];
        }

        // The reason these "GetRandomUnsynced" methods exist is because
        // they can be used on all enumerables; GetRandom can only be used
        // on lists as they can be sorted to guarantee a certain order.
        public static T GetRandomUnsynced<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (predicate == null) { return GetRandomUnsynced(source); }
            return source.Where(predicate).GetRandomUnsynced();
        }

        public static T GetRandomUnsynced<T>(this IEnumerable<T> source)
        {
            if (source is IReadOnlyList<T> list)
            {
                return list.GetRandom(Rand.RandSync.Unsynced);
            }
            else
            {
                int count = source.Count();
                return count == 0 ? default : source.ElementAt(Rand.Range(0, count, Rand.RandSync.Unsynced));
            }
        }
        
        public static T GetRandom<T>(this IEnumerable<T> source, Rand.RandSync randSync)
            where T : PrefabWithUintIdentifier
        {
            return source.OrderBy(p => p.UintIdentifier).ToArray().GetRandom(randSync);
        }

        public static T GetRandom<T>(this IEnumerable<T> source, Func<T, bool> predicate, Rand.RandSync randSync)
            where T : PrefabWithUintIdentifier
        {
            return source.Where(predicate).OrderBy(p => p.UintIdentifier).ToArray().GetRandom(randSync);
        }


        public static T RandomElementByWeight<T>(this IList<T> source, Func<T, float> weightSelector, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            float totalWeight = source.Sum(weightSelector);

            float itemWeightIndex = Rand.Range(0f, 1f, randSync) * totalWeight;
            float currentWeightIndex = 0;

            for (int i = 0; i < source.Count; i++)
            {
                T weightedItem = source[i];
                float weight = weightSelector(weightedItem);
                currentWeightIndex += weight;

                if (currentWeightIndex >= itemWeightIndex)
                {
                    return weightedItem;
                }
            }

            return default;
        }

        /// <summary>
        /// Executes an action that modifies the collection on each element (such as removing items from the list).
        /// Creates a temporary list, unless the collection is empty.
        /// </summary>
        public static void ForEachMod<T>(this IEnumerable<T> source, Action<T> action)
        {
            if (source.None()) { return; }
            var temp = new List<T>(source);
            temp.ForEach(action);
        }

        /// <summary>
        /// Generic version of List.ForEach.
        /// Performs the specified action on each element of the collection (short hand for a foreach loop).
        /// </summary>
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (var item in source)
            {
                action(item);
            }
        }

        /// <summary>
        /// Iterates over all elements in a given enumerable and discards the result.
        /// </summary>
        public static void Consume<T>(this IEnumerable<T> enumerable)
        {
            foreach (var _ in enumerable) { /* do nothing */ }
        }

        /// <summary>
        /// Shorthand for !source.Any(predicate) -> i.e. not any.
        /// </summary>
        public static bool None<T>(this IEnumerable<T> source, Func<T, bool> predicate = null)
        {
            if (predicate == null)
            {
                return !source.Any();
            }
            else
            {
                return !source.Any(predicate);
            }
        }

        public static bool Multiple<T>(this IEnumerable<T> source, Func<T, bool> predicate = null)
        {
            if (predicate == null)
            {
                return source.Count() > 1;
            }
            else
            {
                return source.Count(predicate) > 1;
            }
        }

        public static IEnumerable<T> ToEnumerable<T>(this T item)
        {
            yield return item;
        }

        // source: https://stackoverflow.com/questions/19237868/get-all-children-to-one-list-recursive-c-sharp
        public static IEnumerable<T> SelectManyRecursive<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> selector)
        {
            var result = source.SelectMany(selector);
            if (!result.Any())
            {
                return result;
            }
            return result.Concat(result.SelectManyRecursive(selector));
        }

        public static void AddIfNotNull<T>(this IList<T> source, T value)
        {
            if (value != null) { source.Add(value); }
        }

        public static ImmutableDictionary<TKey, TValue> ToImmutableDictionary<TKey, TValue>(this IEnumerable<(TKey, TValue)> enumerable)
        {
            return enumerable.ToDictionary().ToImmutableDictionary();
        }
        
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<(TKey, TValue)> enumerable)
        {
            var dictionary = new Dictionary<TKey, TValue>();
            foreach (var (k,v) in enumerable)
            {
                dictionary.Add(k, v);
            }
            return dictionary;
        }

        public static Dictionary<TKey, TValue> ToMutable<TKey, TValue>(this ImmutableDictionary<TKey, TValue> immutableDictionary)
        {
            if (immutableDictionary == null) { return null; }
            return new Dictionary<TKey, TValue>(immutableDictionary);
        }

        /// <summary>
        /// Returns whether a given collection has at least a certain amount
        /// of elements for which the predicate returns true.
        /// </summary>
        /// <param name="source">Input collection</param>
        /// <param name="amount">How many elements to match before stopping</param>
        /// <param name="predicate">Predicate used to evaluate the elements</param>
        public static bool AtLeast<T>(this IEnumerable<T> source, int amount, Predicate<T> predicate)
        {
            foreach (T elem in source)
            {
                if (predicate(elem)) { amount--; }
                if (amount <= 0) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Equivalent to LINQ's Enumerable.Concat. The main difference is that this
        /// takes advantage of ICollection<T> optimizations for Enumerable.Contains
        /// and Enumerable.Count.
        /// </summary>
        /// <returns></returns>
        public static ICollection<T> CollectionConcat<T>(this IEnumerable<T> self, IEnumerable<T> other)
            => new CollectionConcat<T>(self, other);
        
        public static IReadOnlyList<T> ListConcat<T>(this IEnumerable<T> self, IEnumerable<T> other)
            => new ListConcat<T>(self, other);

        /// <summary>
        /// Returns the maximum element in a given enumerable, or null if there
        /// aren't any elements in the input.
        /// </summary>
        /// <param name="enumerable">Input collection</param>
        /// <returns>Maximum element or null</returns>
        public static T? MaxOrNull<T>(this IEnumerable<T> enumerable) where T : struct, IComparable<T>
        {
            T? retVal = null;
            foreach (T v in enumerable)
            {
                if (!retVal.HasValue || v.CompareTo(retVal.Value) > 0) { retVal = v; }
            }
            return retVal;
        }

        public static TOut? MaxOrNull<TIn, TOut>(this IEnumerable<TIn> enumerable, Func<TIn, TOut> conversion)
            where TOut : struct, IComparable<TOut>
            => enumerable.Select(conversion).MaxOrNull();

        public static int FindIndex<T>(this IReadOnlyList<T> list, Predicate<T> predicate)
        {
            for (int i=0; i<list.Count; i++)
            {
                if (predicate(list[i])) { return i; }
            }
            return -1;
        }

        /// <summary>
        /// Same as FirstOrDefault but will always return null instead of default(T) when no element is found
        /// </summary>
        public static T? FirstOrNull<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : struct
        {
            if (source.FirstOrDefault(predicate) is var first && !first.Equals(default(T)))
            {
                return first;
            }

            return null;
        }

        public static T? FirstOrNull<T>(this IEnumerable<T> source) where T : struct
        {
            if (source.FirstOrDefault() is var first && !first.Equals(default(T)))
            {
                return first;
            }

            return null;
        }

        public static IEnumerable<T> NotNull<T>(this IEnumerable<T?> source) where T : struct
            => source
                .Where(nullable => nullable.HasValue)
                .Select(nullable => nullable.Value);
        
        public static IEnumerable<T> NotNone<T>(this IEnumerable<Option<T>> source)
            => source
                .OfType<Some<T>>()
                .Select(some => some.Value);
    }
}
