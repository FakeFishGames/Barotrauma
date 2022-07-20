namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        private static Steamworks.AuthTicket currentTicket = null;
        public static Steamworks.AuthTicket GetAuthSessionTicket()
        {
            if (!IsInitialized)
            {
                return null;
            }

            currentTicket?.Cancel();
            currentTicket = Steamworks.SteamUser.GetAuthSessionTicket();
            return currentTicket;
        }
    }
}
