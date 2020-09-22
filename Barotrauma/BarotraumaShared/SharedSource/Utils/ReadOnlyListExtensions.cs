using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public static class ReadOnlyListExtensions
    {
        public static int IndexOf<T>(this IReadOnlyList<T> list, T elem)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Equals(elem)) { return i; }
            }
            return -1;
        }

        public static T Find<T>(this IReadOnlyList<T> list, Func<T, bool> predicate)
        {
            return list.FirstOrDefault(predicate);
        }
    }
}