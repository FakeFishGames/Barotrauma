using System.Collections.Generic;
using System;
using System.Linq;

namespace Barotrauma.Extensions
{
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// Randomizes the collection (using OrderBy) and returns it.
        /// </summary>
        public static IOrderedEnumerable<T> Randomize<T>(this IEnumerable<T> source, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            return source.OrderBy(i => Rand.Value(randSync));
        }

        /// <summary>
        /// Randomizes the list in place without creating a new collection, using a Fisher-Yates-based algorithm.
        /// </summary>
        public static void Shuffle<T>(this IList<T> list, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Rand.Int(n + 1, randSync);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public static T GetRandom<T>(this IEnumerable<T> source, Func<T, bool> predicate, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            if (predicate == null) { return GetRandom(source, randSync); }
            return source.Where(predicate).GetRandom(randSync);
        }

        public static T GetRandom<T>(this IEnumerable<T> source, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            if (source is IList<T> list)
            {
                int count = list.Count;
                return count == 0 ? default : list[Rand.Range(0, count, randSync)];
            }
            else
            {
                int count = source.Count();
                return count == 0 ? default : source.ElementAt(Rand.Range(0, count, randSync));
            }
        }

        /// <summary>
        /// Executes an action that modifies the collection on each element (such as removing items from the list).
        /// Creates a temporary list.
        /// </summary>
        public static void ForEachMod<T>(this IEnumerable<T> source, Action<T> action)
        {
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
    }
}
