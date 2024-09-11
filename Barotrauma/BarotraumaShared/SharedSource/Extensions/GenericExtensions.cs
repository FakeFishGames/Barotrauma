using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Extensions
{
    public static class GenericExtensions
    {
        public static IEnumerable<T> SelectManyRecursive<T>(this T source, Func<T, T> selector)
        {
            IEnumerable<T> result = new List<T>();
            T child = selector.Invoke(source);

            while (child != null)
            {
                result.Append(child);
                child = selector.Invoke(child);
            }
            return result;
        }

        public static IEnumerable<T> SelectManyRecursive<T>(this T source, Func<T, T> selector, int maxDepth, int minDepth = 1)
        {
            minDepth = Math.Max(minDepth, 1);
            maxDepth = Math.Max(maxDepth, minDepth);

            IEnumerable<T> result = new List<T>();
            T child = selector.Invoke(source);

            int depth = 1;
            while (child != null && depth <= maxDepth)
            {
                if (depth >= minDepth) { result.Append(child); }
                child = selector.Invoke(child);
                depth++;
            }
            return result;
        }
    }
}