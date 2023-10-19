#nullable enable
using System;
using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        private static Option<Steamworks.AuthTicket> currentMultiplayerTicket = Option.None;
        public static Option<Steamworks.AuthTicket> GetAuthSessionTicketForMultiplayer(Endpoint remoteHostEndpoint)
        {
            if (!IsInitialized)
            {
                return Option.None;
            }

            if (currentMultiplayerTicket.TryUnwrap(out var ticketToCancel))
            {
                ticketToCancel.Cancel();
            }
            currentMultiplayerTicket = Option.None;

            var netIdentity = remoteHostEndpoint switch
            {
                LidgrenEndpoint { Address: LidgrenAddress { NetAddress: var ipAddr }, Port: var ipPort }
                    => (Steamworks.Data.NetIdentity)Steamworks.Data.NetAddress.From(ipAddr, (ushort)ipPort),
                SteamP2PEndpoint { SteamId: var steamId }
                    => (Steamworks.Data.NetIdentity)(Steamworks.SteamId)steamId.Value,
                _
                    => throw new ArgumentOutOfRangeException(nameof(remoteHostEndpoint))
            };
            var newTicket = Steamworks.SteamUser.GetAuthSessionTicket(netIdentity);

            currentMultiplayerTicket = newTicket != null
                ? Option.Some(newTicket)
                : Option.None;

            return currentMultiplayerTicket;
        }

        private const string GameAnalyticsConsentIdentity = "BarotraumaGameAnalyticsConsent";

        private static Option<Steamworks.AuthTicketForWebApi> currentGameAnalyticsConsentTicket = Option.None;
        public static async Task<Option<Steamworks.AuthTicketForWebApi>> GetAuthTicketForGameAnalyticsConsent()
        {
            if (!IsInitialized)
            {
                return Option.None;
            }

            if (currentGameAnalyticsConsentTicket.TryUnwrap(out var ticketToCancel))
            {
                ticketToCancel.Cancel();
            }
            currentGameAnalyticsConsentTicket = Option.None;

            var newTicket = await Steamworks.SteamUser.GetAuthTicketForWebApi(identity: GameAnalyticsConsentIdentity);

            currentGameAnalyticsConsentTicket = newTicket != null
                ? Option.Some(newTicket)
                : Option.None;

            return currentGameAnalyticsConsentTicket;
        }
    }
}
