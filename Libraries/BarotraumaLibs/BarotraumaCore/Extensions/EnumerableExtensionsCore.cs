using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Barotrauma.Extensions;

public static class EnumerableExtensionsCore
{
    public static ImmutableDictionary<TKey, TValue> ToImmutableDictionary<TKey, TValue>(this IEnumerable<(TKey, TValue)> enumerable)
        where TKey : notnull
    {
        return enumerable.ToDictionary().ToImmutableDictionary();
    }
        
    public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<(TKey, TValue)> enumerable)
        where TKey : notnull
    {
        var dictionary = new Dictionary<TKey, TValue>();
        foreach (var (k,v) in enumerable)
        {
            dictionary.Add(k, v);
        }
        return dictionary;
    }

    [return: NotNullIfNotNull("immutableDictionary")]
    public static Dictionary<TKey, TValue>? ToMutable<TKey, TValue>(this ImmutableDictionary<TKey, TValue>? immutableDictionary)
        where TKey : notnull
    {
        if (immutableDictionary == null) { return null; }
        return new Dictionary<TKey, TValue>(immutableDictionary);
    }

}
