using Facepunch.Steamworks;
using System.Linq;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
        #region Server

        public static bool CreateServer(Networking.GameServer server, bool isPublic)
        {
            Instance.isInitialized = true;

            ServerInit options = new ServerInit("Barotrauma", "Barotrauma")
            {
                GamePort = (ushort)server.Port,
                QueryPort = (ushort)server.QueryPort
            };
            //options.QueryShareGamePort();

            instance.server = new Server(AppID, options, isPublic);
            if (!instance.server.IsValid)
            {
                instance.server.Dispose();
                instance.server = null;
                DebugConsole.ThrowError("Initializing Steam server failed.");
                return false;
            }

            RefreshServerDetails(server);

            server.ServerPeer.InitializeSteamServerCallbacks(instance.server);

            Instance.server.LogOnAnonymous();

            return true;
        }

        public static bool RefreshServerDetails(Networking.GameServer server)
        {
            if (instance?.server == null || !instance.isInitialized)
            {
                return false;
            }

            var contentPackages = GameMain.Config.SelectedContentPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);

            // These server state variables may be changed at any time.  Note that there is no longer a mechanism
            // to send the player count.  The player count is maintained by steam and you should use the player
            // creation/authentication functions to maintain your player count.
            instance.server.ServerName = server.ServerName;
            instance.server.MaxPlayers = server.ServerSettings.MaxPlayers;
            instance.server.Passworded = server.ServerSettings.HasPassword;
            instance.server.MapName = GameMain.NetLobbyScreen?.SelectedSub?.DisplayName ?? "";
            Instance.server.SetKey("message", GameMain.Server.ServerSettings.ServerMessageText);
            Instance.server.SetKey("version", GameMain.Version.ToString());
            Instance.server.SetKey("playercount", GameMain.Server.ConnectedClients.Count.ToString());
            Instance.server.SetKey("contentpackage", string.Join(",", contentPackages.Select(cp => cp.Name)));
            Instance.server.SetKey("contentpackagehash", string.Join(",", contentPackages.Select(cp => cp.MD5hash.Hash)));
            Instance.server.SetKey("contentpackageurl", string.Join(",", contentPackages.Select(cp => cp.SteamWorkshopUrl ?? "")));
            Instance.server.SetKey("usingwhitelist", (server.ServerSettings.Whitelist != null && server.ServerSettings.Whitelist.Enabled).ToString());
            Instance.server.SetKey("modeselectionmode", server.ServerSettings.ModeSelectionMode.ToString());
            Instance.server.SetKey("subselectionmode", server.ServerSettings.SubSelectionMode.ToString());
            Instance.server.SetKey("voicechatenabled", server.ServerSettings.VoiceChatEnabled.ToString());
            Instance.server.SetKey("allowspectating", server.ServerSettings.AllowSpectating.ToString());
            Instance.server.SetKey("allowrespawn", server.ServerSettings.AllowRespawn.ToString());
            Instance.server.SetKey("traitors", server.ServerSettings.TraitorsEnabled.ToString());
            Instance.server.SetKey("gamestarted", server.GameStarted.ToString());
            Instance.server.SetKey("gamemode", server.ServerSettings.GameModeIdentifier);

            instance.server.DedicatedServer = true;

            return true;
        }

        public static ServerAuth.StartAuthSessionResult StartAuthSession(byte[] authTicketData, ulong clientSteamID)
        {
            if (instance == null || !instance.isInitialized || instance.server == null) return ServerAuth.StartAuthSessionResult.ServerNotConnectedToSteam;

            DebugConsole.Log("SteamManager authenticating Steam client " + clientSteamID);
            ServerAuth.StartAuthSessionResult startResult = instance.server.Auth.StartSession(authTicketData, clientSteamID);
            if (startResult != ServerAuth.StartAuthSessionResult.OK)
            {
                DebugConsole.Log("Authentication failed: failed to start auth session (" + startResult.ToString() + ")");
            }

            return startResult;
        }

        public static void StopAuthSession(ulong clientSteamID)
        {
            if (instance == null || !instance.isInitialized || instance.server == null) return;

            DebugConsole.Log("SteamManager ending auth session with Steam client " + clientSteamID);
            instance.server.Auth.EndSession(clientSteamID);
        }

        public static bool CloseServer()
        {
            if (instance == null || !instance.isInitialized || instance.server == null) return false;

            instance.server.Dispose();
            instance.server = null;

            return true;
        }

        #endregion
    }
}
