using RestSharp;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Barotrauma.Networking;

sealed class SteamAuthTicketForEosHostAuthenticator : Authenticator
{
    private const string ServerUrl = "https://barotraumagame.com/baromaster/";
    private const string ServerFile = "getOwnerSteamId.php";
    private const int RemoteRequestVersion = 1;
    
    public override async Task<AccountInfo> VerifyTicket(AuthenticationTicket ticket)
    {
        string ticketData = ToolBoxCore.ByteArrayToHexString(ticket.Data);

        var client = new RestClient(ServerUrl);

        var request = new RestRequest(ServerFile, Method.GET);
        request.AddParameter("authticket", ticketData);
        request.AddParameter("request_version", RemoteRequestVersion);

        var response = await client.ExecuteAsync(request, Method.GET);
        if (!response.IsSuccessful) { return AccountInfo.None; }

        try
        {
            var jsonDoc = JsonDocument.Parse(response.Content);
            Option<SteamId> steamId = Option.None;
            Option<SteamId> ownerSteamId = Option.None;
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (!property.Name.ToIdentifier().Contains("SteamId")) { continue; }
                var accountIdOption = SteamId.Parse(property.Value.GetString() ?? "");
                if (accountIdOption.IsNone()) { continue; }
                if (property.Name.ToIdentifier() == "SteamId")
                {
                    steamId = accountIdOption;
                }
                else if (property.Name.ToIdentifier() == "OwnerSteamId")
                {
                    ownerSteamId = accountIdOption;
                }
            }
            var otherIds = ownerSteamId.TryUnwrap(out var id) ? new AccountId[] { id } : Array.Empty<AccountId>();
            return new AccountInfo(steamId.Select(static id => (AccountId)id), otherIds);
        }
        catch
        {
            return AccountInfo.None;
        }
    }

    public override void EndAuthSession(AccountId accountId) { /* do nothing */ }
}