#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Barotrauma.Steam;

namespace Barotrauma.Networking;

abstract class Authenticator
{
    public abstract Task<AccountInfo> VerifyTicket(AuthenticationTicket ticket);
    public abstract void EndAuthSession(AccountId accountId);

    public static ImmutableDictionary<AuthenticationTicketKind, Authenticator> GetAuthenticatorsForHost(Option<Endpoint> ownerEndpointOption)
    {
        var authenticators = new Dictionary<AuthenticationTicketKind, Authenticator>();

        if (EosInterface.Core.IsInitialized)
        {
            // Every kind of host should be able to do EOS ID Token authentication if they have EOS enabled
            authenticators.Add(AuthenticationTicketKind.EgsOwnershipToken, new EgsOwnershipTokenAuthenticator());
            
            if (ownerEndpointOption.TryUnwrap(out var ownerEndpoint) && ownerEndpoint is EosP2PEndpoint)
            {
                // EOS P2P hosts do not have access to Steamworks
                authenticators.Add(AuthenticationTicketKind.SteamAuthTicketForEosHost, new SteamAuthTicketForEosHostAuthenticator());
            }
        }

        if (!(ownerEndpointOption.TryUnwrap(out var ownerEndpoint2) && ownerEndpoint2 is EosP2PEndpoint) && SteamManager.IsInitialized)
        {
            // Steam P2P hosts and dedicated servers have access to Steamworks
            authenticators.Add(AuthenticationTicketKind.SteamAuthTicketForSteamHost, new SteamAuthTicketForSteamHostAuthenticator());
        }

        return authenticators.ToImmutableDictionary();
    }
}