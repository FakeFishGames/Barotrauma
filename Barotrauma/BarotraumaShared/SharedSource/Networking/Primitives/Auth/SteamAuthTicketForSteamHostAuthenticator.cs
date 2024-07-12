#nullable enable
using System;
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

    private static void EndAuthSession(SteamId clientSteamId)
    {
        if (!SteamManager.IsInitialized) { return; }

        DebugConsole.Log($"Ending auth session with Steam client {clientSteamId}");
        SteamAuthSessionInterface.EndAuthSession(clientSteamId.Value);
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
}