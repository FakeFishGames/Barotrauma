using System.Collections.Immutable;

namespace Barotrauma.Networking;

[NetworkSerialize(ArrayMaxSize = MaxSize)]
readonly record struct MessageFragment(
    MessageFragment.Id FragmentId,
    ImmutableArray<byte> Data) : INetSerializableStruct
{
    public const int MaxSize = 1100;

    [NetworkSerialize]
    public readonly record struct Id(
        ushort FragmentIndex,
        ushort FragmentCount,
        ushort MessageId) : INetSerializableStruct;
}
