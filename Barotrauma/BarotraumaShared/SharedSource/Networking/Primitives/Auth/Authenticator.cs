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
    public abstract bool CheckContentPackageAppLicenseOwnership(AccountInfo accountInfo);

    public static ImmutableDictionary<AuthenticationTicketKind, Authenticator> GetAuthenticatorsForHost(Option<Endpoint> ownerEndpointOption)
    {
        var authenticators = new Dictionary<AuthenticationTicketKind, Authenticator>();

        if (SteamManager.IsInitialized)
        {
            // Steam P2P hosts and dedicated servers have access to Steamworks
            authenticators.Add(AuthenticationTicketKind.SteamAuthTicketForSteamHost, new SteamAuthTicketForSteamHostAuthenticator());
        }

        return authenticators.ToImmutableDictionary();
    }
}