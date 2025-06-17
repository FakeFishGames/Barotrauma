using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Barotrauma.Extensions;

public static class EnumerableExtensionsCore
{
    public static ImmutableDictionary<TKey, TValue> ToImmutableDictionary<TKey, TValue>(this IEnumerable<(TKey, TValue)> enumerable)
        where TKey : notnull
    {
        return enumerable
               .ToDictionary(static pair => pair.Item1, static pair => pair.Item2)
               .ToImmutableDictionary();
    }

    [return: NotNullIfNotNull("immutableDictionary")]
    public static Dictionary<TKey, TValue>? ToMutable<TKey, TValue>(this ImmutableDictionary<TKey, TValue>? immutableDictionary)
        where TKey : notnull
    {
        if (immutableDictionary == null) { return null; }
        return new Dictionary<TKey, TValue>(immutableDictionary);
    }

}
