using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking;

sealed class EgsOwnershipTokenAuthenticator : Authenticator
{
    public override async Task<AccountInfo> VerifyTicket(AuthenticationTicket ticket)
    {
        var jwtOption = JsonWebToken.Parse(Encoding.UTF8.GetString(ticket.Data.AsSpan()));

        if (!jwtOption.TryUnwrap(out var jwt)) { return AccountInfo.None; }
        var ownershipToken = new EosInterface.Ownership.Token(jwt);
        var accountIdOption = await ownershipToken.Verify();

        if (!accountIdOption.TryUnwrap(out var accountId)) { return AccountInfo.None; }
        return new AccountInfo(accountId);
    }

    public override void EndAuthSession(AccountId accountId) { /* do nothing */ }
}