#nullable enable
namespace Barotrauma
{
    [NetworkSerialize]
    public readonly record struct NetPair<T, U>(T First, U Second) : INetSerializableStruct;
}