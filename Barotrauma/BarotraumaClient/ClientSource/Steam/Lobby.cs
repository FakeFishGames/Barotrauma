using Barotrauma.Networking;
using Microsoft.Xna.Framework;
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

                    if (serverSettings.IsPublic)
                    {
                        currentLobby?.SetPublic();
                    }
                    else
                    {
                        currentLobby?.SetFriendsOnly();
                    }
                    currentLobby?.SetJoinable(true);

                    UpdateLobby(serverSettings);
                });
        }

        public static void UpdateLobby(ServerSettings serverSettings)
        {
            if (GameMain.Client == null)
            {
                LeaveLobby();
            }

            if (lobbyState == LobbyState.NotConnected)
            {
                CreateLobby(serverSettings);
            }

            if (lobbyState != LobbyState.Owner)
            {
                return;
            }

            var contentPackages = ContentPackageManager.EnabledPackages.All.Where(cp => cp.HasMultiplayerIncompatibleContent);

            currentLobby?.SetData("name", serverSettings.ServerName);
            currentLobby?.SetData("playercount", (GameMain.Client?.ConnectedClients?.Count ?? 0).ToString());
            currentLobby?.SetData("maxplayernum", serverSettings.MaxPlayers.ToString());
            //currentLobby?.SetData("hostipaddress", lobbyIP);
            string pingLocation = Steamworks.SteamNetworkingUtils.LocalPingLocation?.ToString();
            currentLobby?.SetData("pinglocation", pingLocation ?? "");
            currentLobby?.SetData("lobbyowner", SteamIDUInt64ToString(GetSteamID()));
            currentLobby?.SetData("haspassword", serverSettings.HasPassword.ToString());

            currentLobby?.SetData("message", serverSettings.ServerMessageText);
            currentLobby?.SetData("version", GameMain.Version.ToString());

            currentLobby?.SetData("contentpackage", string.Join(",", contentPackages.Select(cp => cp.Name)));
            currentLobby?.SetData("contentpackagehash", string.Join(",", contentPackages.Select(cp => cp.Hash.StringRepresentation)));
            currentLobby?.SetData("contentpackageid", string.Join(",", contentPackages.Select(cp => cp.SteamWorkshopId)));
            currentLobby?.SetData("usingwhitelist", (serverSettings.Whitelist != null && serverSettings.Whitelist.Enabled).ToString());
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
                        GameMain.Instance.ConnectLobby = 0;
                        GameMain.Instance.ConnectName = currentLobby?.GetData("servername");
                        GameMain.Instance.ConnectEndpoint = SteamIDUInt64ToString((currentLobby?.Owner.Id).Value);
                    }
                });
        }

        public static bool GetServers(Action<ServerInfo> addToServerList, Action serverQueryFinished)
        {
            if (!IsInitialized) { return false; }

            int doneTasks = 0;
            void taskDone()
            {
                doneTasks++;
                if (doneTasks >= 2)
                {
                    serverQueryFinished?.Invoke();
                    serverQueryFinished = null;
                }
            }


            Steamworks.Dispatch.OnDebugCallback = (callbackType, contents, isServer) =>
            {
                DebugConsole.NewMessage($"{callbackType}: " + contents, Color.Yellow);
            };

            TaskPool.Add("LobbyQueryRequest", LobbyQueryRequest(),
            (t) =>
            {
                Steamworks.Dispatch.OnDebugCallback = null;
                if (t.Status == TaskStatus.Faulted)
                {
                    TaskPool.PrintTaskExceptions(t, "Failed to retrieve SteamP2P lobbies");
                    taskDone();
                    return;
                }
                var lobbies = ((Task<List<Steamworks.Data.Lobby>>)t).Result;
                if (lobbies != null)
                {
                    foreach (var lobby in lobbies)
                    {
                        if (string.IsNullOrEmpty(lobby.GetData("name"))) { continue; }

                        ServerInfo serverInfo = new ServerInfo();
                        serverInfo.ServerName = lobby.GetData("name");
                        serverInfo.OwnerID = SteamIDStringToUInt64(lobby.GetData("lobbyowner"));
                        serverInfo.LobbyID = lobby.Id;
                        bool.TryParse(lobby.GetData("haspassword"), out serverInfo.HasPassword);
                        serverInfo.PlayerCount = int.TryParse(lobby.GetData("playercount"), out int playerCount) ? playerCount : 0;
                        serverInfo.MaxPlayers = int.TryParse(lobby.GetData("maxplayernum"), out int maxPlayers) ? maxPlayers : 1;
                        serverInfo.RespondedToSteamQuery = true;

                        AssignLobbyDataToServerInfo(lobby, serverInfo);

                        addToServerList(serverInfo);
                    }
                }
                taskDone();
            });

            Steamworks.ServerList.Internet serverQuery = new Steamworks.ServerList.Internet();
            void onServer(Steamworks.Data.ServerInfo info, bool responsive)
            {
                if (string.IsNullOrEmpty(info.Name)) { return; }

                ServerInfo serverInfo = new ServerInfo
                {
                    ServerName = info.Name,
                    HasPassword = info.Passworded,
                    IP = info.Address.ToString(),
                    Port = info.ConnectionPort.ToString(),
                    PlayerCount = info.Players,
                    MaxPlayers = info.MaxPlayers,
                    RespondedToSteamQuery = responsive
                };

                if (responsive)
                {
                    TaskPool.Add($"QueryServerRules (GetServers, {info.Name}, {info.Address})", info.QueryRulesAsync(),
                        (t) =>
                        {
                            if (t.Status == TaskStatus.Faulted)
                            {
                                TaskPool.PrintTaskExceptions(t, "Failed to retrieve rules for " + info.Name);
                                return;
                            }

                            var rules = ((Task<Dictionary<string, string>>)t).Result;
                            AssignServerRulesToServerInfo(rules, serverInfo);

                            CrossThread.RequestExecutionOnMainThread(() =>
                            {
                                addToServerList(serverInfo);
                            });
                        });
                }
                else
                {
                    CrossThread.RequestExecutionOnMainThread(() =>
                    {
                        addToServerList(serverInfo);
                    });
                }

            }
            serverQuery.OnResponsiveServer += (info) => onServer(info, true);
            serverQuery.OnUnresponsiveServer += (info) => onServer(info, false);

            TaskPool.Add("RunServerQuery", serverQuery.RunQueryAsync(),
            (t) =>
            {
                serverQuery.Dispose();
                taskDone();
                if (t.Status == TaskStatus.Faulted)
                {
                    TaskPool.PrintTaskExceptions(t, "Failed to retrieve servers");
                    return;
                }
            });

            return true;
        }

        public static async Task<List<Steamworks.Data.Lobby>> LobbyQueryRequest()
        {
            List<Steamworks.Data.Lobby> allLobbies = new List<Steamworks.Data.Lobby>();
            Steamworks.Data.LobbyQuery lobbyQuery = Steamworks.SteamMatchmaking.CreateLobbyQuery()
                    .FilterDistanceWorldwide()
                    .WithMaxResults(50);
            //steamworks seems to unable to retrieve more than 50
            //lobbies per request; to work around this, we'll make
            //up to 10 requests, asking to ignore all previous results
            //in each subsequent request
            for (int i = 0; i < 10; i++)
            {
                Steamworks.Data.Lobby[] lobbies = await lobbyQuery.RequestAsync();
                if (lobbies == null) { break; }
                foreach (var l in lobbies)
                {
                    lobbyQuery = lobbyQuery
                        .WithoutKeyValue("lobbyowner", l.GetData("lobbyowner"));
                }
                allLobbies.AddRange(lobbies);
            }

            //make sure all returned lobbies are distinct, don't want any duplicates here
            return allLobbies.Select(l => l.Id).Distinct().Select(i => allLobbies.Find(l => l.Id == i)).ToList();
        }

        public static void AssignLobbyDataToServerInfo(Steamworks.Data.Lobby lobby, ServerInfo serverInfo)
        {
            serverInfo.OwnerVerified = true;

            serverInfo.ServerMessage = lobby.GetData("message");
            serverInfo.GameVersion = lobby.GetData("version");

            serverInfo.ContentPackageNames.AddRange(lobby.GetData("contentpackage").Split(','));
            serverInfo.ContentPackageHashes.AddRange(lobby.GetData("contentpackagehash").Split(','));

            string workshopIdData = lobby.GetData("contentpackageid");
            if (!string.IsNullOrEmpty(workshopIdData))
            {
                serverInfo.ContentPackageWorkshopIds.AddRange(ParseWorkshopIds(workshopIdData));
            }
            else
            {
                string[] workshopUrls = lobby.GetData("contentpackageurl").Split(',');
                serverInfo.ContentPackageWorkshopIds.AddRange(WorkshopUrlsToIds(workshopUrls));
            }

            serverInfo.UsingWhiteList = getLobbyBool("usingwhitelist");
            if (Enum.TryParse(lobby.GetData("modeselectionmode"), out SelectionMode selectionMode)) { serverInfo.ModeSelectionMode = selectionMode; }
            if (Enum.TryParse(lobby.GetData("subselectionmode"), out selectionMode)) { serverInfo.SubSelectionMode = selectionMode; }

            serverInfo.AllowSpectating = getLobbyBool("allowspectating");
            serverInfo.AllowRespawn = getLobbyBool("allowrespawn");
            serverInfo.VoipEnabled = getLobbyBool("voicechatenabled");
            serverInfo.KarmaEnabled = getLobbyBool("karmaenabled");
            serverInfo.FriendlyFireEnabled = getLobbyBool("friendlyfireenabled");
            if (Enum.TryParse(lobby.GetData("traitors"), out YesNoMaybe traitorsEnabled)) { serverInfo.TraitorsEnabled = traitorsEnabled; }

            serverInfo.GameStarted = lobby.GetData("gamestarted") == "True";
            serverInfo.GameMode = (lobby.GetData("gamemode") ?? "").ToIdentifier();
            if (Enum.TryParse(lobby.GetData("playstyle"), out PlayStyle playStyle)) serverInfo.PlayStyle = playStyle;

            if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count ||
                serverInfo.ContentPackageHashes.Count != serverInfo.ContentPackageWorkshopIds.Count)
            {
                //invalid contentpackage info
                serverInfo.ContentPackageNames.Clear();
                serverInfo.ContentPackageHashes.Clear();
                serverInfo.ContentPackageWorkshopIds.Clear();
            }

            string pingLocation = lobby.GetData("pinglocation");
            if (!string.IsNullOrEmpty(pingLocation))
            {
                serverInfo.PingLocation = Steamworks.Data.NetPingLocation.TryParseFromString(pingLocation);
            }

            bool? getLobbyBool(string key)
            {
                string data = lobby.GetData(key);
                if (string.IsNullOrEmpty(data)) { return null; }
                return data == "True" || data == "true";
            }
        }

        public static void AssignServerRulesToServerInfo(Dictionary<string, string> rules, ServerInfo serverInfo)
        {
            serverInfo.OwnerVerified = true;

            if (rules == null) { return; }

            if (rules.ContainsKey("message")) serverInfo.ServerMessage = rules["message"];
            if (rules.ContainsKey("version")) serverInfo.GameVersion = rules["version"];

            if (rules.ContainsKey("playercount"))
            {
                if (int.TryParse(rules["playercount"], out int playerCount)) serverInfo.PlayerCount = playerCount;
            }

            serverInfo.ContentPackageNames.Clear();
            serverInfo.ContentPackageHashes.Clear();
            serverInfo.ContentPackageWorkshopIds.Clear();
            if (rules.ContainsKey("contentpackage")) serverInfo.ContentPackageNames.AddRange(rules["contentpackage"].Split(','));
            if (rules.ContainsKey("contentpackagehash")) serverInfo.ContentPackageHashes.AddRange(rules["contentpackagehash"].Split(','));
            if (rules.ContainsKey("contentpackageid"))
            {
                serverInfo.ContentPackageWorkshopIds.AddRange(ParseWorkshopIds(rules["contentpackageid"]));
            }
            else if (rules.ContainsKey("contentpackageurl"))
            {
                string[] workshopUrls = rules["contentpackageurl"].Split(',');
                serverInfo.ContentPackageWorkshopIds.AddRange(WorkshopUrlsToIds(workshopUrls));
            }

            if (rules.ContainsKey("usingwhitelist")) serverInfo.UsingWhiteList = rules["usingwhitelist"] == "True";
            if (rules.ContainsKey("modeselectionmode"))
            {
                if (Enum.TryParse(rules["modeselectionmode"], out SelectionMode selectionMode)) serverInfo.ModeSelectionMode = selectionMode;
            }
            if (rules.ContainsKey("subselectionmode"))
            {
                if (Enum.TryParse(rules["subselectionmode"], out SelectionMode selectionMode)) serverInfo.SubSelectionMode = selectionMode;
            }
            if (rules.ContainsKey("allowspectating")) serverInfo.AllowSpectating = rules["allowspectating"] == "True";
            if (rules.ContainsKey("allowrespawn")) serverInfo.AllowRespawn = rules["allowrespawn"] == "True";
            if (rules.ContainsKey("voicechatenabled")) serverInfo.VoipEnabled = rules["voicechatenabled"] == "True";
            if (rules.ContainsKey("traitors"))
            {
                if (Enum.TryParse(rules["traitors"], out YesNoMaybe traitorsEnabled)) serverInfo.TraitorsEnabled = traitorsEnabled;
            }

            if (rules.ContainsKey("gamestarted")) serverInfo.GameStarted = rules["gamestarted"] == "True";
            if (rules.ContainsKey("gamemode"))
            {
                serverInfo.GameMode = rules["gamemode"].ToIdentifier();
            }
            if (rules.ContainsKey("playstyle") && Enum.TryParse(rules["playstyle"], out PlayStyle playStyle))
            {
                serverInfo.PlayStyle = playStyle;
            }

            if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count ||
                serverInfo.ContentPackageHashes.Count != serverInfo.ContentPackageWorkshopIds.Count)
            {
                //invalid contentpackage info
                serverInfo.ContentPackageNames.Clear();
                serverInfo.ContentPackageHashes.Clear();
                serverInfo.ContentPackageWorkshopIds.Clear();
            }
        }
    }
}
