#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// A serializable dictionary that can be sent over the network.
    /// </summary>
    /// <param name="Pairs">The backing array of key-value pairs that gets serialized.</param>
    /// <typeparam name="T">Key</typeparam>
    /// <typeparam name="U">Value</typeparam>
    /// <remarks>
    /// This isn't a full implementation of a dictionary, but rather a simple wrapper around a list of key-value pairs
    /// that can be serialized and deserialized in an INetSerializableStruct.
    /// Normally there wouldn't be duplicate keys in a dictionary, but this implementation doesn't enforce that.
    /// </remarks>
    [NetworkSerialize]
    public readonly record struct NetDictionary<T, U>(ImmutableArray<NetPair<T, U>> Pairs) : INetSerializableStruct where T : notnull
    {
        public Dictionary<T, U> ToDictionary()
            => Pairs.ToDictionary(
                static pair => pair.First,
                static pair => pair.Second);

        public ImmutableDictionary<T, U> ToImmutableDictionary()
            => Pairs.ToImmutableDictionary(
                static pair => pair.First,
                static pair => pair.Second);
    }

    public static class DictionaryExtensions
    {
        public static NetDictionary<T, U> ToNetDictionary<T, U>(this Dictionary<T, U> source) where T : notnull
            => new NetDictionary<T, U>(source.Select(static pair => new NetPair<T, U>(pair.Key, pair.Value)).ToImmutableArray());

        public static NetDictionary<T, U> ToNetDictionary<T, U>(this ImmutableDictionary<T, U> source) where T : notnull
            => new NetDictionary<T, U>(source.Select(static pair => new NetPair<T, U>(pair.Key, pair.Value)).ToImmutableArray());
    }
}