#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Steam;

namespace Barotrauma.Networking;

#if CLIENT
using SteamAuthSessionInterface = Steamworks.SteamUser;
#else
using SteamAuthSessionInterface = Steamworks.SteamServer;
#endif

sealed class SteamAuthTicketForSteamHostAuthenticator : Authenticator
{
    private static Steamworks.BeginAuthResult BeginAuthSession(Steamworks.AuthTicket authTicket, SteamId clientSteamId)
    {
        if (!SteamManager.IsInitialized) { return Steamworks.BeginAuthResult.ServerNotConnectedToSteam; }
        if (authTicket.Data is null) { return Steamworks.BeginAuthResult.InvalidTicket; }

        DebugConsole.Log("Authenticating Steam client " + clientSteamId);
        Steamworks.BeginAuthResult startResult = SteamAuthSessionInterface.BeginAuthSession(authTicket.Data, clientSteamId.Value);
        if (startResult != Steamworks.BeginAuthResult.OK)
        {
            DebugConsole.Log($"Steam authentication failed: failed to start auth session ({startResult})");
        }

        return startResult;
    }

    public override async Task<AccountInfo> VerifyTicket(AuthenticationTicket ticket)
    {
        if (ticket.Data.Length < 8) { return AccountInfo.None; }

        var ticketData = ticket.Data.ToArray();
        var steamAuthTicket = new Steamworks.AuthTicket { Data = ticketData[8..] };
        var steamId = new SteamId(BitConverter.ToUInt64(ticketData.AsSpan()[..8]));

        using var janitor = Janitor.Start();

        (Steamworks.AuthResponse AuthResponse, SteamId OwnerSteamId)? authResult = null;
        void onValidateAuthTicketResponse(Steamworks.SteamId clientId, Steamworks.SteamId ownerClientId, Steamworks.AuthResponse response)
        {
            if (clientId != steamId.Value) { response = Steamworks.AuthResponse.AuthTicketInvalid; }
            authResult = (response, new SteamId(ownerClientId));
        }

        SteamAuthSessionInterface.OnValidateAuthTicketResponse += onValidateAuthTicketResponse;
        janitor.AddAction(() => SteamAuthSessionInterface.OnValidateAuthTicketResponse -= onValidateAuthTicketResponse);
        var beginAuthSessionResult = BeginAuthSession(steamAuthTicket, steamId);

        if (beginAuthSessionResult != Steamworks.BeginAuthResult.OK) { return AccountInfo.None; }

        while (authResult is null)
        {
            await Task.Delay(32);
        }
        if (authResult.Value.AuthResponse != Steamworks.AuthResponse.OK) { return AccountInfo.None; }

        return new AccountInfo(steamId, authResult.Value.OwnerSteamId);
    }

    public override void EndAuthSession(AccountId accountId)
    {
        if (accountId is not SteamId steamId) { return; }
        SteamAuthSessionInterface.EndAuthSession(steamId.Value);
    }

    // check for possible DLC ownership
    // this check has to be done after auth session has started (VerifyTicket calls BeginAuthSession)
    public override bool CheckContentPackageAppLicenseOwnership(AccountInfo accountInfo)
    {
        if (!accountInfo.AccountId.TryUnwrap(out var accountId) || accountId is not SteamId accountSteamId)
        {
            return false; // in theory should never happen since the removal of EOS
        }

        HashSet<Steamworks.AppId>? appIds = null;
        foreach (ContentPackage package in ContentPackageManager.EnabledPackages.All)
        {
            if (package.CheckSteamAppOwnership && package.SteamDlcAppId.TryUnwrap(out Steamworks.AppId appId))
            {
                appIds ??= [];
                appIds.Add(appId);
            }
        }

        if (appIds != null)
        {
            if (!CheckAppLicenseOwnership(accountSteamId, appIds))
            {
                return false;
            }
        }

        return true; // all good, let them in

        static bool CheckAppLicenseOwnership(SteamId clientSteamId, IEnumerable<Steamworks.AppId> appIds)
        {
#if DEBUG
            // always allow on debug builds
            return true;
#endif

            foreach (Steamworks.AppId appId in appIds)
            {
                var result = SteamAuthSessionInterface.UserHasLicenseForApp(clientSteamId.Value, appId);
                switch (result)
                {
                    case Steamworks.UserHasLicenseForAppResult.HasLicense:
                        continue; // continue checking
                    case Steamworks.UserHasLicenseForAppResult.DoesNotHaveLicense:
                        return false; // fail
                    case Steamworks.UserHasLicenseForAppResult.NoAuth:
                        DebugConsole.ThrowError("Unable to complete DLC ownership check: User has not been authenticated. BeginAuthSession was probably not called.");
                        return true; // I suppose just let them in?
                }
            }

            return true; // all good
        }
    }

}