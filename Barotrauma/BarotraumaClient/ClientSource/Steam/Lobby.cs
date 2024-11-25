using Barotrauma.Networking;
using System;
using System.Collections.Generic;
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
            if (!SteamManager.IsInitialized) { return; }
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

            serverSettings.UpdateServerListInfo(SetServerListInfo);

            currentLobby?.SetData("lobbyowner", GetSteamId().TryUnwrap(out var steamId)
                ? steamId.StringRepresentation
                : throw new InvalidOperationException("Steamworks not initialized"));

            if (EosInterface.IdQueries.GetLoggedInPuids() is { Length: > 0 } puids)
            {
                currentLobby?.SetData("EosEndpoint", puids[0].Value);
            }

            DebugConsole.Log("Lobby updated!");
        }

        private static void SetServerListInfo(Identifier key, object value)
        {
            switch (value)
            {
                case IEnumerable<ContentPackage> contentPackages:
                    currentLobby?.SetData("contentpackage", contentPackages.Select(p => p.Name).JoinEscaped(','));
                    currentLobby?.SetData("contentpackagehash", contentPackages.Select(p => p.Hash.StringRepresentation).JoinEscaped(','));
                    currentLobby?.SetData("contentpackageid", contentPackages
                        .Select(p => p.UgcId.Select(ugcId => ugcId.StringRepresentation).Fallback(""))
                        .JoinEscaped(','));
                    return;
            }

            currentLobby?.SetData(key.Value.ToLowerInvariant(), value.ToString());
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
