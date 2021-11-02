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
        public static T GetRandom<T>(this IEnumerable<T> source, Random random)
        {
            if (source is IList<T> list)
            {
                int count = list.Count;
                return count == 0 ? default : list[random.Next(0, count)];
            }
            else
            {
                int count = source.Count();
                return count == 0 ? default : source.ElementAt(random.Next(0, count));
            }
        }

        public static T RandomElementByWeight<T>(this IEnumerable<T> source, Func<T, float> weightSelector, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            float totalWeight = source.Sum(weightSelector);

            float itemWeightIndex = Rand.Range(0f, 1f, randSync) * totalWeight;
            float currentWeightIndex = 0;

            foreach (T weightedItem in source)
            {
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
    }
}
