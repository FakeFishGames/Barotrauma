#nullable enable
using System;
namespace Barotrauma.Networking;

public sealed class EpicAccountId : AccountId
{
    private EpicAccountId(string value)
    {
        EosStringRepresentation = value.ToLowerInvariant();
    }

    private const string prefix = "EPIC_";

    public override string StringRepresentation => $"{prefix}{EosStringRepresentation.ToUpperInvariant()}";
    public override string EosStringRepresentation { get; }

    public override bool Equals(object? obj)
        => obj is EpicAccountId otherId
           && otherId.EosStringRepresentation.Equals(EosStringRepresentation, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => EosStringRepresentation.GetHashCode(StringComparison.OrdinalIgnoreCase);

    public new static Option<EpicAccountId> Parse(string str)
    {
        if (str.IsNullOrWhiteSpace()) { return Option.None; }
        if (str.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { str = str[prefix.Length..]; }
        if (!str.IsHexString()) { return Option.None; }

        return Option.Some(new EpicAccountId(str));
    }
}
