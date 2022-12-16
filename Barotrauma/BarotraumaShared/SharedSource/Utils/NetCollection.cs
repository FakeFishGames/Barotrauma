using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    [NetworkSerialize]
    public readonly record struct NetCollection<T>(ImmutableArray<T> Array) : INetSerializableStruct, IEnumerable<T>
    {
        public static readonly NetCollection<T> Empty = new(ImmutableArray<T>.Empty);

        public NetCollection(params T[] elements) : this(elements.ToImmutableArray()) { }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Array).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Array).GetEnumerator();
    }
}