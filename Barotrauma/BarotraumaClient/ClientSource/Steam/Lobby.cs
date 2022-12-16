using Barotrauma.Networking;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        private enum LobbyState
        {
            NotConnected,
            Creating,
            Owner,
            Joining,
            Joined
        }
        private static UInt64 lobbyID = 0;
        private static LobbyState lobbyState = LobbyState.NotConnected;
        private static Steamworks.Data.Lobby? currentLobby;
        public static UInt64 CurrentLobbyID
        {
            get { return currentLobby?.Id ?? 0; }
        }

        public static void CreateLobby(ServerSettings serverSettings)
        {
            if (lobbyState != LobbyState.NotConnected) { return; }
            lobbyState = LobbyState.Creating;
            TaskPool.Add("CreateLobbyAsync", Steamworks.SteamMatchmaking.CreateLobbyAsync(serverSettings.MaxPlayers + 10),
                (lobby) =>
                {
                    if (lobbyState != LobbyState.Creating)
                    {
                        LeaveLobby();
                        return;
                    }

                    currentLobby = ((Task<Steamworks.Data.Lobby?>)lobby).Result;

                    if (currentLobby == null)
                    {
                        DebugConsole.ThrowError("Failed to create Steam lobby");
                        lobbyState = LobbyState.NotConnected;
                        return;
                    }

                    DebugConsole.NewMessage("Lobby created!", Microsoft.Xna.Framework.Color.Lime);

                    lobbyState = LobbyState.Owner;
                    lobbyID = (currentLobby?.Id).Value;

                    SetLobbyPublic(serverSettings.IsPublic);
                    currentLobby?.SetJoinable(true);

                    UpdateLobby(serverSettings);
                });
        }

        public static void SetLobbyPublic(bool isPublic)
        {
            if (isPublic)
            {
                currentLobby?.SetPublic();
            }
            else
            {
                currentLobby?.SetFriendsOnly();
            }
        }

        public static void UpdateLobby(ServerSettings serverSettings)
        {
            if (GameMain.Client == null)
            {
                LeaveLobby();
                return;
            }

            if (lobbyState == LobbyState.NotConnected)
            {
                CreateLobby(serverSettings);
            }

            if (lobbyState != LobbyState.Owner)
            {
                return;
            }

            var contentPackages = ContentPackageManager.EnabledPackages.All.Where(cp => cp.HasMultiplayerSyncedContent);

            currentLobby?.SetData("name", serverSettings.ServerName);
            currentLobby?.SetData("playercount", (GameMain.Client?.ConnectedClients?.Count ?? 0).ToString());
            currentLobby?.SetData("maxplayernum", serverSettings.MaxPlayers.ToString());
            //currentLobby?.SetData("hostipaddress", lobbyIP);
            string pingLocation = Steamworks.SteamNetworkingUtils.LocalPingLocation?.ToString();
            currentLobby?.SetData("pinglocation", pingLocation ?? "");
            currentLobby?.SetData("lobbyowner", GetSteamId().TryUnwrap(out var steamId)
                ? steamId.StringRepresentation
                : throw new InvalidOperationException("Steamworks not initialized"));
            currentLobby?.SetData("haspassword", serverSettings.HasPassword.ToString());

            currentLobby?.SetData("message", serverSettings.ServerMessageText);
            currentLobby?.SetData("version", GameMain.Version.ToString());

            currentLobby?.SetData("contentpackage", string.Join(",", contentPackages.Select(cp => cp.Name)));
            currentLobby?.SetData("contentpackagehash", string.Join(",", contentPackages.Select(cp => cp.Hash.StringRepresentation)));
            currentLobby?.SetData("contentpackageid", string.Join(",", contentPackages.Select(cp => cp.UgcId)));
            currentLobby?.SetData("modeselectionmode", serverSettings.ModeSelectionMode.ToString());
            currentLobby?.SetData("subselectionmode", serverSettings.SubSelectionMode.ToString());
            currentLobby?.SetData("voicechatenabled", serverSettings.VoiceChatEnabled.ToString());
            currentLobby?.SetData("allowspectating", serverSettings.AllowSpectating.ToString());
            currentLobby?.SetData("allowrespawn", serverSettings.AllowRespawn.ToString());
            currentLobby?.SetData("karmaenabled", serverSettings.KarmaEnabled.ToString());
            currentLobby?.SetData("friendlyfireenabled", serverSettings.AllowFriendlyFire.ToString());
            currentLobby?.SetData("traitors", serverSettings.TraitorsEnabled.ToString());
            currentLobby?.SetData("gamestarted", GameMain.Client.GameStarted.ToString());
            currentLobby?.SetData("playstyle", serverSettings.PlayStyle.ToString());
            currentLobby?.SetData("gamemode", GameMain.NetLobbyScreen?.SelectedMode?.Identifier.Value ?? "");

            DebugConsole.Log("Lobby updated!");
        }

        public static void LeaveLobby()
        {
            if (lobbyState != LobbyState.NotConnected)
            {
                currentLobby?.Leave(); currentLobby = null;
                lobbyState = LobbyState.NotConnected;

                lobbyID = 0;

                Steamworks.SteamMatchmaking.ResetActions();
            }
        }
        public static void JoinLobby(UInt64 id, bool joinServer)
        {
            if (currentLobby.HasValue && currentLobby.Value.Id == id) { return; }
            if (lobbyID == id) { return; }
            lobbyState = LobbyState.Joining;
            lobbyID = id;

            TaskPool.Add("JoinLobbyAsync", Steamworks.SteamMatchmaking.JoinLobbyAsync(lobbyID),
                (lobby) =>
                {
                    currentLobby = ((Task<Steamworks.Data.Lobby?>)lobby).Result;
                    lobbyState = LobbyState.Joined;
                    lobbyID = (currentLobby?.Id).Value;
                    if (joinServer)
                    {
                        GameMain.Instance.ConnectCommand = Option<ConnectCommand>.Some(
                            new ConnectCommand(
                                currentLobby?.GetData("servername") ?? "Server",
                                new SteamP2PEndpoint(new SteamId(currentLobby?.Owner.Id ?? 0))));
                    }
                });
        }
    }
}
