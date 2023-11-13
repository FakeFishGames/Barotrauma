using System;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    static class RichTextDataExtensions
    {
        public static Client ExtractClient(this RichTextData data)
        {
            bool isInt = UInt64.TryParse(data.Metadata, out ulong uintId);
            Option<AccountId> accountId = AccountId.Parse(data.Metadata);
            Client client = GameMain.Client.ConnectedClients.Find(c => accountId.IsSome() && accountId == c.AccountId)
                            ?? GameMain.Client.ConnectedClients.Find(c => isInt && c.SessionId == uintId)
                            ?? GameMain.Client.PreviouslyConnectedClients.FirstOrDefault(c => accountId.IsSome() && accountId == c.AccountId)
                            ?? GameMain.Client.PreviouslyConnectedClients.FirstOrDefault(c => isInt && c.SessionId == uintId);
            return client;
        }
    }
}