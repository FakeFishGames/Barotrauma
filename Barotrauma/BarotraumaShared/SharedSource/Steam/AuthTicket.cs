#nullable enable
using System;
using System.Threading.Tasks;
using Barotrauma.Networking;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        #region Auth ticket for Steam host
        private static Option<Steamworks.AuthTicket> currentSteamHostAuthTicket = Option.None;
        public static Option<Steamworks.AuthTicket> GetAuthSessionTicketForSteamHost(Endpoint remoteHostEndpoint)
        {
            if (!IsInitialized)
            {
                return Option.None;
            }

            if (currentSteamHostAuthTicket.TryUnwrap(out var ticketToCancel))
            {
                ticketToCancel.Cancel();
            }
            currentSteamHostAuthTicket = Option.None;

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

            currentSteamHostAuthTicket = newTicket != null
                ? Option.Some(newTicket)
                : Option.None;

            return currentSteamHostAuthTicket;
        }
        #endregion Auth ticket for Steam host

        #region Auth ticket for GameAnalytics consent server
        private const string GameAnalyticsConsentIdentity = "BarotraumaGameAnalyticsConsent";

        private static Option<Steamworks.AuthTicket> currentGameAnalyticsConsentTicket = Option.None;
        public static async Task<Option<Steamworks.AuthTicket>> GetAuthTicketForGameAnalyticsConsent()
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

            var newTicket = await Steamworks.SteamUser.GetAuthTicketForWebApiAsync(identity: GameAnalyticsConsentIdentity);

            currentGameAnalyticsConsentTicket = newTicket != null
                ? Option.Some(newTicket)
                : Option.None;

            return currentGameAnalyticsConsentTicket;
        }
        #endregion Auth ticket for GameAnalytics consent server
    }
}
