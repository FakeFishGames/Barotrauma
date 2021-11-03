using System.Linq;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
        #region Server

        private static void InitializeProjectSpecific() { isInitialized = true; }

        public static bool CreateServer(Networking.GameServer server, bool isPublic)
        {
            isInitialized = true;

            Steamworks.SteamServerInit options = new Steamworks.SteamServerInit("Barotrauma", "Barotrauma")
            {
                GamePort = (ushort)server.Port,
                QueryPort = isPublic ? (ushort)server.QueryPort : (ushort)0,
                Mode = isPublic ? Steamworks.InitServerMode.Authentication : Steamworks.InitServerMode.NoAuthentication
            };
            //options.QueryShareGamePort();

            Steamworks.SteamServer.Init(AppID, options, false);
            if (!Steamworks.SteamServer.IsValid)
            {
                Steamworks.SteamServer.Shutdown();
                DebugConsole.ThrowError("Initializing Steam server failed.");
                return false;
            }

            RefreshServerDetails(server);

            server.ServerPeer.InitializeSteamServerCallbacks();

            Steamworks.SteamServer.LogOnAnonymous();

            return true;
        }

        public static bool RefreshServerDetails(Networking.GameServer server)
        {
            if (!isInitialized || !Steamworks.SteamServer.IsValid)
            {
                return false;
            }

            var contentPackages = GameMain.Config.AllEnabledPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);

            // These server state variables may be changed at any time.  Note that there is no longer a mechanism
            // to send the player count.  The player count is maintained by steam and you should use the player
            // creation/authentication functions to maintain your player count.
            Steamworks.SteamServer.ServerName = server.ServerName;
            Steamworks.SteamServer.MaxPlayers = server.ServerSettings.MaxPlayers;
            Steamworks.SteamServer.Passworded = server.ServerSettings.HasPassword;
            Steamworks.SteamServer.MapName = GameMain.NetLobbyScreen?.SelectedSub?.DisplayName ?? "";
            Steamworks.SteamServer.SetKey("haspassword", server.ServerSettings.HasPassword.ToString());
            Steamworks.SteamServer.SetKey("message", GameMain.Server.ServerSettings.ServerMessageText);
            Steamworks.SteamServer.SetKey("version", GameMain.Version.ToString());
            //Steamworks.SteamServer.SetKey("playercount", GameMain.Server.ConnectedClients.Count.ToString());
            if (GameMain.Server.ConnectedClients.Count == 0) { Steamworks.SteamServer.SetKey("playercount", GameMain.Server.ConnectedClients.Count.ToString()); }
            else { Steamworks.SteamServer.SetKey("playercount", (GameMain.Server.ConnectedClients.Count + 5).ToString()); }

            Steamworks.SteamServer.SetKey("contentpackage", string.Join(",", contentPackages.Select(cp => cp.Name)));
            Steamworks.SteamServer.SetKey("contentpackagehash", string.Join(",", contentPackages.Select(cp => cp.MD5hash.Hash)));
            Steamworks.SteamServer.SetKey("contentpackageid", string.Join(",", contentPackages.Select(cp => cp.SteamWorkshopId)));
            Steamworks.SteamServer.SetKey("usingwhitelist", (server.ServerSettings.Whitelist != null && server.ServerSettings.Whitelist.Enabled).ToString());
            Steamworks.SteamServer.SetKey("modeselectionmode", server.ServerSettings.ModeSelectionMode.ToString());
            Steamworks.SteamServer.SetKey("subselectionmode", server.ServerSettings.SubSelectionMode.ToString());
            Steamworks.SteamServer.SetKey("voicechatenabled", server.ServerSettings.VoiceChatEnabled.ToString());
            Steamworks.SteamServer.SetKey("allowspectating", server.ServerSettings.AllowSpectating.ToString());
            Steamworks.SteamServer.SetKey("allowrespawn", server.ServerSettings.AllowRespawn.ToString());
            Steamworks.SteamServer.SetKey("traitors", server.ServerSettings.TraitorsEnabled.ToString());
            Steamworks.SteamServer.SetKey("gamestarted", server.GameStarted.ToString());
            Steamworks.SteamServer.SetKey("gamemode", server.ServerSettings.GameModeIdentifier);
            Steamworks.SteamServer.SetKey("playstyle", server.ServerSettings.PlayStyle.ToString());

            Steamworks.SteamServer.DedicatedServer = true;

            return true;
        }

        public static Steamworks.BeginAuthResult StartAuthSession(byte[] authTicketData, ulong clientSteamID)
        {
            if (!isInitialized || !Steamworks.SteamServer.IsValid) return Steamworks.BeginAuthResult.ServerNotConnectedToSteam;

            DebugConsole.Log("SteamManager authenticating Steam client " + clientSteamID);
            Steamworks.BeginAuthResult startResult = Steamworks.SteamServer.BeginAuthSession(authTicketData, clientSteamID);
            if (startResult != Steamworks.BeginAuthResult.OK)
            {
                DebugConsole.Log("Authentication failed: failed to start auth session (" + startResult.ToString() + ")");
            }

            return startResult;
        }

        public static void StopAuthSession(ulong clientSteamID)
        {
            if (!isInitialized || !Steamworks.SteamServer.IsValid) return;

            DebugConsole.Log("SteamManager ending auth session with Steam client " + clientSteamID);
            Steamworks.SteamServer.EndSession(clientSteamID);
        }

        public static bool CloseServer()
        {
            if (!isInitialized || !Steamworks.SteamServer.IsValid) return false;

            Steamworks.SteamServer.Shutdown();

            return true;
        }

        #endregion
    }
}
