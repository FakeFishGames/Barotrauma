namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        public static Steamworks.BeginAuthResult StartAuthSession(byte[] authTicketData, ulong clientSteamID)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) return Steamworks.BeginAuthResult.ServerNotConnectedToSteam;

            DebugConsole.Log("SteamManager authenticating Steam client " + clientSteamID);
            Steamworks.BeginAuthResult startResult = Steamworks.SteamUser.BeginAuthSession(authTicketData, clientSteamID);
            if (startResult != Steamworks.BeginAuthResult.OK)
            {
                DebugConsole.Log("Authentication failed: failed to start auth session (" + startResult.ToString() + ")");
            }

            return startResult;
        }

        public static void StopAuthSession(ulong clientSteamID)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) return;

            DebugConsole.NewMessage("SteamManager ending auth session with Steam client " + clientSteamID);
            Steamworks.SteamUser.EndAuthSession(clientSteamID);
        }
    }
}
