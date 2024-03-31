#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Steam;

namespace Barotrauma.Networking;

public enum AuthenticationTicketKind
{
    SteamAuthTicketForSteamHost = 0,
    SteamAuthTicketForEosHost = 1,
    EgsOwnershipToken = 2
}

[NetworkSerialize(ArrayMaxSize = UInt16.MaxValue)]
readonly record struct AuthenticationTicket(
    AuthenticationTicketKind Kind,
    ImmutableArray<byte> Data) : INetSerializableStruct
{
    public static async Task<Option<AuthenticationTicket>> Create(Endpoint serverEndpoint)
    {
        if (SteamManager.IsInitialized && SteamManager.GetSteamId().TryUnwrap(out var steamId))
        {
            if (serverEndpoint is EosP2PEndpoint)
            {
                var authTicket = await SteamManager.GetAuthTicketForEosHostAuth();
                return authTicket
                    .Bind(t => t.Data != null ? Option.Some(t.Data) : Option.None)
                    .Select(data => new AuthenticationTicket(AuthenticationTicketKind.SteamAuthTicketForEosHost, data.ToImmutableArray()));
            }
            else
            {
                var authTicket = SteamManager.GetAuthSessionTicketForSteamHost(serverEndpoint);
                var steamIdBytes = BitConverter.GetBytes(steamId.Value);
                return authTicket
                    .Bind(t => t.Data != null ? Option.Some(t.Data) : Option.None)
                    .Select(data => new AuthenticationTicket(
                        AuthenticationTicketKind.SteamAuthTicketForSteamHost,
                        steamIdBytes.Concat(data).ToImmutableArray()));
            }
        }

        if (EosInterface.IdQueries.GetLoggedInPuids() is { Length: > 0 } puids)
        {
            var externalAccountIdsResult = await EosInterface.IdQueries.GetSelfExternalAccountIds(puids[0]);
            if (externalAccountIdsResult.TryUnwrapSuccess(out var externalAccountIds))
            {
                var epicAccountIdOption = externalAccountIds.OfType<EpicAccountId>().FirstOrNone();
                if (epicAccountIdOption.TryUnwrap(out var epicAccountId))
                {
                    return (await EosInterface.Ownership.GetGameOwnershipToken(epicAccountId))
                        .Select(t => new AuthenticationTicket(
                            AuthenticationTicketKind.EgsOwnershipToken,
                            Encoding.UTF8.GetBytes(t.Jwt.ToString()).ToImmutableArray()));
                }
            }
        }

        return Option.None;
    }
}