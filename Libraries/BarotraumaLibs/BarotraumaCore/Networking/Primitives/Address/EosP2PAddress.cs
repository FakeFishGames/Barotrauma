#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
namespace Barotrauma.Networking;

public sealed class EosP2PAddress : P2PAddress
{
    private const string prefix = "EOS_";

    public readonly string EosStringRepresentation;
    
    public EosP2PAddress(string value)
    {
        EosStringRepresentation = value.ToLowerInvariant();
    }

    public new static Option<EosP2PAddress> Parse(string addressStr)
    {
        if (addressStr.StartsWith(prefix)) { addressStr = addressStr[prefix.Length..]; }
        if (!addressStr.IsHexString()) { return Option.None; }
        
        return Option.Some(new EosP2PAddress(addressStr));
    }

    public override string StringRepresentation => $"{prefix}{EosStringRepresentation}";
    public override bool IsLocalHost => false;

    public override bool Equals(object? obj)
        => obj is EosP2PAddress other
           && other.EosStringRepresentation.ToString().Equals(EosStringRepresentation.ToString(), StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
    {
        using var md5 = MD5.Create();
        return unchecked((int)ToolBoxCore.StringToUInt32Hash(EosStringRepresentation, md5));
    }
}
