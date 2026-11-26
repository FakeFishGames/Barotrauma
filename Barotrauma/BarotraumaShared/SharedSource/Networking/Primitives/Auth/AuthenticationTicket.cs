#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Steam;

namespace Barotrauma.Networking;

public enum AuthenticationTicketKind
{
    SteamAuthTicketForSteamHost = 0,
}

[NetworkSerialize(ArrayMaxSize = UInt16.MaxValue)]
readonly record struct AuthenticationTicket(
    AuthenticationTicketKind Kind,
    ImmutableArray<byte> Data) : INetSerializableStruct
{
    public static Option<AuthenticationTicket> Create(Endpoint serverEndpoint)
    {
        if (SteamManager.IsInitialized && SteamManager.GetSteamId().TryUnwrap(out var steamId))
        {
            var authTicket = SteamManager.GetAuthSessionTicketForSteamHost(serverEndpoint);
            var steamIdBytes = BitConverter.GetBytes(steamId.Value);
            return authTicket
                   .Bind(t => t.Data != null ? Option.Some(t.Data) : Option.None)
                   .Select(data => new AuthenticationTicket(
                               AuthenticationTicketKind.SteamAuthTicketForSteamHost,
                               steamIdBytes.Concat(data).ToImmutableArray()));
        }

        return Option.None;
    }
}