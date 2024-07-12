using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Extensions;

public static class IEnumerableExtensionsCore
{
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
        for (int i = 0; i < list.Count; i++)
        {
            if (predicate(list[i])) { return i; }
        }
        return -1;
    }

    /// <summary>
    /// Same as FirstOrDefault but will always return null instead of default(T) when no element is found
    /// </summary>
    public static T? FirstOrNull<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : struct
        => source.FirstOrNone(predicate).TryUnwrap(out T t) ? t : null;

    public static T? FirstOrNull<T>(this IEnumerable<T> source) where T : struct
        => source.FirstOrNone().TryUnwrap(out T t) ? t : null;

    public static Option<T> FirstOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : notnull
    {
        foreach (T t in source)
        {
            if (predicate(t)) { return Option.Some(t); }
        }
        return Option.None;
    }

    public static Option<T> FirstOrNone<T>(this IEnumerable<T> source) where T : notnull
    {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        return enumerator.MoveNext()
            ? Option.Some(enumerator.Current)
            : Option.None;
    }

    public static IEnumerable<T> NotNone<T>(this IEnumerable<Option<T>> source) where T : notnull
    {
        foreach (var o in source)
        {
            if (o.TryUnwrap(out var v)) { yield return v; }
        }
    }

    public static IEnumerable<TSuccess> Successes<TSuccess, TFailure>(
        this IEnumerable<Result<TSuccess, TFailure>> source)
        where TSuccess : notnull
        where TFailure : notnull
        => source
            .OfType<Success<TSuccess, TFailure>>()
            .Select(s => s.Value);

    public static IEnumerable<TFailure> Failures<TSuccess, TFailure>(
        this IEnumerable<Result<TSuccess, TFailure>> source)
        where TSuccess : notnull
        where TFailure : notnull
        => source
            .OfType<Failure<TSuccess, TFailure>>()
            .Select(f => f.Error);
}