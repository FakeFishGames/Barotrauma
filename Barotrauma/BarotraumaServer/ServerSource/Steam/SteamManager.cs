using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
        private static void InitializeProjectSpecific() { IsInitialized = true; }

        public static bool CreateServer(Networking.GameServer server, bool isPublic)
        {
            IsInitialized = true;

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
            if (!IsInitialized || !Steamworks.SteamServer.IsValid)
            {
                return false;
            }

            var contentPackages = ContentPackageManager.EnabledPackages.All.Where(cp => cp.HasMultiplayerSyncedContent);

            // These server state variables may be changed at any time.  Note that there is no longer a mechanism
            // to send the player count. The player count is maintained by Steam and you should use the player
            // creation/authentication functions to maintain your player count.
            Steamworks.SteamServer.ServerName = server.ServerName;
            Steamworks.SteamServer.MaxPlayers = server.ServerSettings.MaxPlayers;
            Steamworks.SteamServer.Passworded = server.ServerSettings.HasPassword;
            Steamworks.SteamServer.MapName = GameMain.NetLobbyScreen?.SelectedSub?.DisplayName?.Value ?? "";
            Steamworks.SteamServer.SetKey("haspassword", server.ServerSettings.HasPassword.ToString());
            Steamworks.SteamServer.SetKey("message", server.ServerSettings.ServerMessageText);
            Steamworks.SteamServer.SetKey("version", GameMain.Version.ToString());
            Steamworks.SteamServer.SetKey("playercount", server.ConnectedClients.Count.ToString());
            Steamworks.SteamServer.SetKey("contentpackage", string.Join(",", contentPackages.Select(cp => cp.Name)));
            Steamworks.SteamServer.SetKey("contentpackagehash", string.Join(",", contentPackages.Select(cp => cp.Hash.StringRepresentation)));
            Steamworks.SteamServer.SetKey("contentpackageid", string.Join(",", contentPackages.Select(cp
                => cp.UgcId.TryUnwrap(out var ugcId) ? ugcId.StringRepresentation : "")));
            Steamworks.SteamServer.SetKey("modeselectionmode", server.ServerSettings.ModeSelectionMode.ToString());
            Steamworks.SteamServer.SetKey("subselectionmode", server.ServerSettings.SubSelectionMode.ToString());
            Steamworks.SteamServer.SetKey("voicechatenabled", server.ServerSettings.VoiceChatEnabled.ToString());
            Steamworks.SteamServer.SetKey("allowspectating", server.ServerSettings.AllowSpectating.ToString());
            Steamworks.SteamServer.SetKey("allowrespawn", server.ServerSettings.AllowRespawn.ToString());
            Steamworks.SteamServer.SetKey("traitors", server.ServerSettings.TraitorsEnabled.ToString());
            Steamworks.SteamServer.SetKey("friendlyfireenabled", server.ServerSettings.AllowFriendlyFire.ToString());
            Steamworks.SteamServer.SetKey("karmaenabled", server.ServerSettings.KarmaEnabled.ToString());
            Steamworks.SteamServer.SetKey("gamestarted", server.GameStarted.ToString());
            Steamworks.SteamServer.SetKey("gamemode", server.ServerSettings.GameModeIdentifier.Value);
            Steamworks.SteamServer.SetKey("playstyle", server.ServerSettings.PlayStyle.ToString());

            Steamworks.SteamServer.DedicatedServer = true;

            return true;
        }

        public static Steamworks.BeginAuthResult StartAuthSession(byte[] authTicketData, SteamId clientSteamID)
        {
            if (!IsInitialized || !Steamworks.SteamServer.IsValid) return Steamworks.BeginAuthResult.ServerNotConnectedToSteam;

            DebugConsole.Log("SteamManager authenticating Steam client " + clientSteamID);
            Steamworks.BeginAuthResult startResult = Steamworks.SteamServer.BeginAuthSession(authTicketData, clientSteamID.Value);
            if (startResult != Steamworks.BeginAuthResult.OK)
            {
                DebugConsole.Log("Authentication failed: failed to start auth session (" + startResult.ToString() + ")");
            }

            return startResult;
        }

        public static void StopAuthSession(SteamId clientSteamId)
        {
            if (!IsInitialized || !Steamworks.SteamServer.IsValid) return;

            DebugConsole.Log("SteamManager ending auth session with Steam client " + clientSteamId);
            Steamworks.SteamServer.EndSession(clientSteamId.Value);
        }

        public static bool CloseServer()
        {
            if (!IsInitialized || !Steamworks.SteamServer.IsValid) return false;

            Steamworks.SteamServer.Shutdown();

            return true;
        }
    }
}
