using Barotrauma.Networking;
using Facepunch.Steamworks;
using RestSharp;
using RestSharp.Extensions.MonoHttp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
        public Facepunch.Steamworks.Networking Networking => client?.Networking;
        public Facepunch.Steamworks.User User => client?.User;
        public Facepunch.Steamworks.Friends Friends => client?.Friends;
        public Facepunch.Steamworks.Overlay Overlay => client?.Overlay;
        public Facepunch.Steamworks.Auth Auth => client?.Auth;
        public Facepunch.Steamworks.Lobby Lobby => client?.Lobby;
        public Facepunch.Steamworks.LobbyList LobbyList => client?.LobbyList;
        public Facepunch.Steamworks.ServerList ServerList => client?.ServerList;
        public Facepunch.Steamworks.Client Client => client;

        private SteamManager()
        {
            client = null;
            isInitialized = InitializeClient();
        }

        private bool InitializeClient()
        {
            if (client != null) { return true; }
            bool clientInitialized;
            try
            {
                client = new Facepunch.Steamworks.Client(AppID);
                clientInitialized = client.IsSubscribed && client.IsValid;

                if (clientInitialized)
                {
                    DebugConsole.NewMessage("Logged in as " + client.Username + " (SteamID " + SteamIDUInt64ToString(client.SteamId) + ")");
                }
            }
            catch (DllNotFoundException)
            {
                clientInitialized = false;
                initializationErrors.Add("SteamDllNotFound");
            }
            catch (Exception)
            {
                clientInitialized = false;
                initializationErrors.Add("SteamClientInitFailed");
            }

            if (!clientInitialized)
            {
                try
                {
                    Facepunch.Steamworks.Client.Instance.Dispose();
                }
                catch (Exception e)
                {
                    if (GameSettings.VerboseLogging) DebugConsole.ThrowError("Disposing Steam client failed.", e);
                }
                client = null;
            }
            return clientInitialized;
        }

        private enum LobbyState
        {
            NotConnected,
            Creating,
            Owner,
            Joining,
            Joined
        }

        public static UInt64 LobbyID { get; private set; } = 0;
        private static LobbyState lobbyState = LobbyState.NotConnected;
        private static string lobbyIP = "";
        private static Thread lobbyIPRetrievalThread;

        private static void RetrieveLobbyIP()
        {
            //TODO: set up our own server for IP retrieval?

            Server tempServer = null;
            try
            {
                var serverInit = new ServerInit("Barotrauma", "Barotrauma IP Retrieval")
                {
                    GamePort = (ushort)27015,
                    QueryPort = (ushort)27016
                };
                tempServer = new Server(AppID, serverInit, false);
                if (!tempServer.IsValid)
                {
                    tempServer.Dispose();
                    tempServer = null;
                    DebugConsole.ThrowError("Failed to retrieve public IP: Initializing Steam server failed.");
                    return;
                }

                tempServer.LogOnAnonymous();
                lobbyIP = "";
                string error = "Timed out.";
                for (int i = 0; i < 30*60; i++)
                {
                    if (instance.client.Lobby.CurrentLobby == 0)
                    {
                        error = "";
                        break;
                    }
                    tempServer.Update();
                    tempServer.ForceHeartbeat();
                    if (tempServer.PublicIp != null)
                    {
                        if (instance.client.Lobby.CurrentLobby != 0)
                        {
                            lobbyIP = tempServer.PublicIp.ToString();
                            DebugConsole.NewMessage("Successfully retrieved public IP: " + lobbyIP, Microsoft.Xna.Framework.Color.Lime);
                            instance.client.Lobby.CurrentLobbyData.SetData("hostipaddress", lobbyIP);
                        }
                        else
                        {
                            error = "";
                            lobbyIP = "";
                        }
                        break;
                    }
                    Thread.Sleep(16);
                }

                tempServer.Dispose();
                tempServer = null;
                if (string.IsNullOrWhiteSpace(lobbyIP) && !string.IsNullOrWhiteSpace(error))
                {
                    DebugConsole.ThrowError("Failed to retrieve public IP: "+error);
                }
            }
            catch
            {
                tempServer?.Dispose();
                tempServer = null;
            }
        }

        public static void CreateLobby(ServerSettings serverSettings)
        {
            instance.client.Lobby.OnLobbyJoined = null;
            instance.client.Lobby.OnLobbyCreated = (success) =>
            {
                if (!success)
                {
                    DebugConsole.ThrowError("Failed to create Steam lobby!");
                    lobbyState = LobbyState.NotConnected;
                    return;
                }
                
                DebugConsole.NewMessage("Lobby created!", Microsoft.Xna.Framework.Color.Lime);

                lobbyIPRetrievalThread?.Abort();
                lobbyIPRetrievalThread?.Join();
                lobbyIPRetrievalThread = null;
                lobbyIPRetrievalThread = new Thread(new ThreadStart(RetrieveLobbyIP))
                {
                    IsBackground = true
                };
                lobbyIPRetrievalThread.Start();
                
                lobbyState = LobbyState.Owner;
                LobbyID = instance.client.Lobby.CurrentLobby;
                UpdateLobby(serverSettings);
            };
            if (lobbyState != LobbyState.NotConnected) { return; }
            lobbyState = LobbyState.Creating;
            instance.client.Lobby.Create(serverSettings.isPublic ? Lobby.Type.Public : Lobby.Type.FriendsOnly, serverSettings.MaxPlayers+10);
            instance.client.Lobby.Joinable = true;
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

            var contentPackages = GameMain.Config.SelectedContentPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);

            instance.client.Lobby.Name = serverSettings.ServerName;
            instance.client.Lobby.Owner = GetSteamID();
            instance.client.Lobby.MaxMembers = serverSettings.MaxPlayers + 10;
            instance.client.Lobby.CurrentLobbyData.SetData("playercount", (GameMain.Client?.ConnectedClients?.Count ?? 0).ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("maxplayernum", serverSettings.MaxPlayers.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("hostipaddress", lobbyIP);
            instance.client.Lobby.CurrentLobbyData.SetData("lobbyowner", SteamIDUInt64ToString(GetSteamID()));
            instance.client.Lobby.CurrentLobbyData.SetData("haspassword", serverSettings.HasPassword.ToString());

            instance.client.Lobby.CurrentLobbyData.SetData("message", serverSettings.ServerMessageText);
            instance.client.Lobby.CurrentLobbyData.SetData("version", GameMain.Version.ToString());

            instance.client.Lobby.CurrentLobbyData.SetData("contentpackage", string.Join(",", contentPackages.Select(cp => cp.Name)));
            instance.client.Lobby.CurrentLobbyData.SetData("contentpackagehash", string.Join(",", contentPackages.Select(cp => cp.MD5hash.Hash)));
            instance.client.Lobby.CurrentLobbyData.SetData("contentpackageurl", string.Join(",", contentPackages.Select(cp => cp.SteamWorkshopUrl ?? "")));
            instance.client.Lobby.CurrentLobbyData.SetData("usingwhitelist", (serverSettings.Whitelist != null && serverSettings.Whitelist.Enabled).ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("modeselectionmode", serverSettings.ModeSelectionMode.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("subselectionmode", serverSettings.SubSelectionMode.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("voicechatenabled", serverSettings.VoiceChatEnabled.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("allowspectating", serverSettings.AllowSpectating.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("allowrespawn", serverSettings.AllowRespawn.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("karmaenabled", serverSettings.KarmaEnabled.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("friendlyfireenabled", serverSettings.AllowFriendlyFire.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("traitors", serverSettings.TraitorsEnabled.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("gamestarted", GameMain.Client.GameStarted.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("playstyle", serverSettings.PlayStyle.ToString());
            instance.client.Lobby.CurrentLobbyData.SetData("gamemode", GameMain.NetLobbyScreen?.SelectedMode?.Identifier ?? "");

            DebugConsole.Log("Lobby updated!");
        }

        public static void LeaveLobby()
        {
            if (lobbyState != LobbyState.NotConnected)
            {
                lobbyIPRetrievalThread?.Abort();
                lobbyIPRetrievalThread?.Join();
                lobbyIPRetrievalThread = null;

                instance.client.Lobby.Leave();
                LobbyID = 0;
                lobbyIP = "";
                lobbyState = LobbyState.NotConnected;

                instance.client.Lobby.OnLobbyJoined = null;
            }
        }
        public static void JoinLobby(UInt64 id, bool joinServer)
        {
            if (instance.client.Lobby.CurrentLobby == id) { return; }
            if (LobbyID == id) { return; }
            instance.client.Lobby.OnLobbyJoined = (success) =>
            {
                try
                {
                    if (!success)
                    {
                        DebugConsole.ThrowError("Failed to join Steam lobby: "+id.ToString());
                        return;
                    }
                    lobbyState = LobbyState.Joined;
                    LobbyID = instance.client.Lobby.CurrentLobby;
                    if (joinServer)
                    {
                        GameMain.Instance.ConnectLobby = 0;
                        GameMain.Instance.ConnectName = instance.client.Lobby.Name;
                        GameMain.Instance.ConnectEndpoint = instance.client.Lobby.Owner.ToString();
                    }
                }
                finally
                {
                    instance.client.Lobby.OnLobbyJoined = null;
                }
            };
            lobbyState = LobbyState.Joining;
            LobbyID = id;
            instance.client.Lobby.Join(id);
        }

        public static ulong GetWorkshopItemIDFromUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string idStr = HttpUtility.ParseQueryString(uri.Query).Get("id");
                if (ulong.TryParse(idStr, out ulong id))
                {
                    return id;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to get Workshop item ID from the url \"" + url + "\"!", e);
            }

            return 0;
        }

        #region Connecting to servers

        public static bool GetServers(Action<Networking.ServerInfo> onServerFound, Action<Networking.ServerInfo> onServerRulesReceived, Action onFinished)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            var filter = new ServerList.Filter
            {
                { "appid", AppID.ToString() },
                { "gamedir", "Barotrauma" },
                { "secure", "1" }
            };

            //include unresponsive servers in the server list

            //the response is queried using the server's query port, not the game port,
            //so it may be possible to play on the server even if it doesn't respond to server list queries
            var query = instance.client.ServerList.Internet(filter);
            query.OnUpdate = () => { UpdateServerQuery(query, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
            query.OnFinished = onFinished;

#if !DEBUG
            var localQuery = instance.client.ServerList.Local(filter);
            localQuery.OnUpdate = () => { UpdateServerQuery(localQuery, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
            localQuery.OnFinished = onFinished;
#endif

            instance.client.LobbyList.OnLobbiesUpdated = () => { UpdateLobbyQuery(onServerFound, onServerRulesReceived, onFinished); };
            instance.client.LobbyList.Refresh();

            return true;
        }

        public static bool GetFavouriteServers(Action<Networking.ServerInfo> onServerFound, Action<Networking.ServerInfo> onServerRulesReceived, Action onFinished)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            var filter = new ServerList.Filter
            {
                { "appid", AppID.ToString() },
                { "gamedir", "Barotrauma" },
                { "secure", "1" }
            };

            //include unresponsive servers in the server list

            //the response is queried using the server's query port, not the game port,
            //so it may be possible to play on the server even if it doesn't respond to server list queries
            var query = instance.client.ServerList.Favourites(filter);
            query.OnUpdate = () => { UpdateServerQuery(query, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
            query.OnFinished = onFinished;

            return true;
        }

        public static bool GetServersFromHistory(Action<Networking.ServerInfo> onServerFound, Action<Networking.ServerInfo> onServerRulesReceived, Action onFinished)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            var filter = new ServerList.Filter
            {
                { "appid", AppID.ToString() },
                { "gamedir", "Barotrauma" },
                { "secure", "1" }
            };

            //include unresponsive servers in the server list

            //the response is queried using the server's query port, not the game port,
            //so it may be possible to play on the server even if it doesn't respond to server list queries
            var query = instance.client.ServerList.History(filter);
            query.OnUpdate = () => { UpdateServerQuery(query, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
            query.OnFinished = onFinished;

            return true;
        }

        private static void UpdateLobbyQuery(Action<Networking.ServerInfo> onServerFound, Action<Networking.ServerInfo> onServerRulesReceived, Action onFinished)
        {
            foreach (LobbyList.Lobby lobby in instance.client.LobbyList.Lobbies)
            {
                if (string.IsNullOrWhiteSpace(lobby.GetData("haspassword"))) { continue; }
                bool.TryParse(lobby.GetData("haspassword"), out bool hasPassword);
                int.TryParse(lobby.GetData("playercount"), out int currPlayers);
                int.TryParse(lobby.GetData("maxplayernum"), out int maxPlayers);
                UInt64 ownerId = SteamIDStringToUInt64(lobby.GetData("lobbyowner"));
                //UInt64.TryParse(lobby.GetData("connectsteamid"), out ulong connectSteamId);
                string ip = lobby.GetData("hostipaddress");
                if (string.IsNullOrWhiteSpace(ip)) { ip = ""; }

                var serverInfo = new ServerInfo()
                {
                    ServerName = lobby.Name,
                    Port = "",
                    QueryPort = "",
                    IP = ip,
                    PlayerCount = currPlayers,
                    MaxPlayers = maxPlayers,
                    HasPassword = hasPassword,
                    RespondedToSteamQuery = true,
                    LobbyID = lobby.LobbyID,
                    OwnerID = ownerId
                };
                serverInfo.PingChecked = false;
                AssignLobbyDataToServerInfo(lobby, serverInfo);

                onServerFound(serverInfo);
                //onServerRulesReceived(serverInfo);
            }

            onFinished();
        }

        public static void AssignLobbyDataToServerInfo(LobbyList.Lobby lobby, ServerInfo serverInfo)
        {
            serverInfo.ServerMessage = lobby.GetData("message");
            serverInfo.GameVersion = lobby.GetData("version");

            serverInfo.ContentPackageNames.AddRange(lobby.GetData("contentpackage").Split(','));
            serverInfo.ContentPackageHashes.AddRange(lobby.GetData("contentpackagehash").Split(','));
            serverInfo.ContentPackageWorkshopUrls.AddRange(lobby.GetData("contentpackageurl").Split(','));

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
            serverInfo.GameMode = lobby.GetData("gamemode");
            if (Enum.TryParse(lobby.GetData("playstyle"), out PlayStyle playStyle)) serverInfo.PlayStyle = playStyle;

            if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count ||
                serverInfo.ContentPackageHashes.Count != serverInfo.ContentPackageWorkshopUrls.Count)
            {
                //invalid contentpackage info
                serverInfo.ContentPackageNames.Clear();
                serverInfo.ContentPackageHashes.Clear();
            }

            bool? getLobbyBool(string key)
            {
                string data = lobby.GetData(key);
                if (string.IsNullOrEmpty(data)) { return null; }
                return data == "True" || data == "true";
            }
        }

        private static void UpdateServerQuery(ServerList.Request query, Action<Networking.ServerInfo> onServerFound, Action<Networking.ServerInfo> onServerRulesReceived, bool includeUnresponsive)
        {
            IEnumerable<ServerList.Server> servers = includeUnresponsive ?
                new List<ServerList.Server>(query.Responded).Concat(query.Unresponsive) :
                query.Responded;

            foreach (ServerList.Server s in servers)
            {
                if (!ValidateServerInfo(s)) { continue; }

                bool responded = query.Responded.Contains(s);
                if (responded)
                {
                    DebugConsole.Log(s.Name + " responded to server query.");
                }
                else
                {
                    DebugConsole.Log(s.Name + " did not respond to server query.");
                }
                
                if (s.Description == "Barotrauma IP Retrieval") { continue; }

                var serverInfo = new ServerInfo()
                {
                    ServerName = s.Name,
                    Port = s.ConnectionPort.ToString(),
                    QueryPort = s.QueryPort.ToString(),
                    IP = s.Address.ToString(),
                    PlayerCount = s.Players,
                    MaxPlayers = s.MaxPlayers,
                    HasPassword = s.Passworded,
                    RespondedToSteamQuery = responded
                };
                serverInfo.PingChecked = true;
                serverInfo.Ping = s.Ping;
                serverInfo.LobbyID = 0;
                if (responded)
                {
                    s.FetchRules();
                }
                s.OnReceivedRules += (bool received) => { OnReceivedRules(s, serverInfo, received); onServerRulesReceived(serverInfo); };

                onServerFound(serverInfo);
            }
            query.Responded.Clear();
        }

        public static void OnReceivedRules(ServerList.Server s, ServerInfo serverInfo, bool rulesReceived)
        {
            if (!rulesReceived || s.Rules == null) { return; }
                    
            if (s.Rules.ContainsKey("message")) serverInfo.ServerMessage = s.Rules["message"];
            if (s.Rules.ContainsKey("version")) serverInfo.GameVersion = s.Rules["version"];

            if (s.Rules.ContainsKey("playercount"))
            {
                if (int.TryParse(s.Rules["playercount"], out int playerCount)) serverInfo.PlayerCount = playerCount;
            }

            serverInfo.ContentPackageNames.Clear();
            serverInfo.ContentPackageHashes.Clear();
            serverInfo.ContentPackageWorkshopUrls.Clear();
            if (s.Rules.ContainsKey("contentpackage")) serverInfo.ContentPackageNames.AddRange(s.Rules["contentpackage"].Split(','));
            if (s.Rules.ContainsKey("contentpackagehash")) serverInfo.ContentPackageHashes.AddRange(s.Rules["contentpackagehash"].Split(','));
            if (s.Rules.ContainsKey("contentpackageurl")) serverInfo.ContentPackageWorkshopUrls.AddRange(s.Rules["contentpackageurl"].Split(','));

            if (s.Rules.ContainsKey("usingwhitelist")) serverInfo.UsingWhiteList = s.Rules["usingwhitelist"] == "True";
            if (s.Rules.ContainsKey("modeselectionmode"))
            {
                if (Enum.TryParse(s.Rules["modeselectionmode"], out SelectionMode selectionMode)) serverInfo.ModeSelectionMode = selectionMode;
            }
            if (s.Rules.ContainsKey("subselectionmode"))
            {
                if (Enum.TryParse(s.Rules["subselectionmode"], out SelectionMode selectionMode)) serverInfo.SubSelectionMode = selectionMode;
            }
            if (s.Rules.ContainsKey("allowspectating")) serverInfo.AllowSpectating = s.Rules["allowspectating"] == "True";
            if (s.Rules.ContainsKey("allowrespawn")) serverInfo.AllowRespawn = s.Rules["allowrespawn"] == "True";
            if (s.Rules.ContainsKey("voicechatenabled")) serverInfo.VoipEnabled = s.Rules["voicechatenabled"] == "True";
            if (s.Rules.ContainsKey("karmaenabled")) serverInfo.KarmaEnabled = s.Rules["karmaenabled"] == "True";
            if (s.Rules.ContainsKey("friendlyfireenabled")) serverInfo.FriendlyFireEnabled = s.Rules["friendlyfireenabled"] == "True";
            if (s.Rules.ContainsKey("traitors"))
            {
                if (Enum.TryParse(s.Rules["traitors"], out YesNoMaybe traitorsEnabled)) serverInfo.TraitorsEnabled = traitorsEnabled;
            }

            if (s.Rules.ContainsKey("gamestarted")) serverInfo.GameStarted = s.Rules["gamestarted"] == "True";

            if (s.Rules.ContainsKey("gamemode"))
            {
                serverInfo.GameMode = s.Rules["gamemode"];
            }

            if (s.Rules.ContainsKey("playstyle"))
            {
                if (Enum.TryParse(s.Rules["playstyle"], out PlayStyle playStyle)) serverInfo.PlayStyle = playStyle;
            }

            if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count ||
                serverInfo.ContentPackageHashes.Count != serverInfo.ContentPackageWorkshopUrls.Count)
            {
                //invalid contentpackage info
                serverInfo.ContentPackageNames.Clear();
                serverInfo.ContentPackageHashes.Clear();
            }
        }

        private static bool ValidateServerInfo(ServerList.Server server)
        {
            if (string.IsNullOrWhiteSpace(server.Name)) { return false; }
            if (string.IsNullOrWhiteSpace(server.Name.Replace("\0", ""))) { return false; }
            if (server.Address == null) { return false; }

            return true;
        }

        private static Auth.Ticket currentTicket = null;
        public static Auth.Ticket GetAuthSessionTicket()
        {
            if (instance == null || !instance.isInitialized)
            {
                return null;
            }

            currentTicket?.Cancel();
            currentTicket = instance.client.Auth.GetAuthSessionTicket();
            return currentTicket;
        }

        public static ClientStartAuthSessionResult StartAuthSession(byte[] authTicketData, ulong clientSteamID)
        {
            if (instance == null || !instance.isInitialized || instance.client == null) return ClientStartAuthSessionResult.ServerNotConnectedToSteam;

            DebugConsole.NewMessage("SteamManager authenticating Steam client " + clientSteamID);
            ClientStartAuthSessionResult startResult = instance.client.Auth.StartSession(authTicketData, clientSteamID);
            if (startResult != ClientStartAuthSessionResult.OK)
            {
                DebugConsole.NewMessage("Authentication failed: failed to start auth session (" + startResult.ToString() + ")");
            }

            return startResult;
        }

        public static void StopAuthSession(ulong clientSteamID)
        {
            if (instance == null || !instance.isInitialized || instance.client == null) return;

            DebugConsole.NewMessage("SteamManager ending auth session with Steam client " + clientSteamID);
            instance.client.Auth.EndSession(clientSteamID);
        }

        #endregion

        #region Workshop
        
        public const string WorkshopItemPreviewImageFolder = "Workshop";
        public const string PreviewImageName = "PreviewImage.png";
        public const string DefaultPreviewImagePath = "Content/DefaultWorkshopPreviewImage.png";

        private Sprite defaultPreviewImage;
        public Sprite DefaultPreviewImage
        {
            get
            {
                if (defaultPreviewImage == null)
                {
                    defaultPreviewImage = new Sprite(DefaultPreviewImagePath, sourceRectangle: null);
                }
                return defaultPreviewImage;
            }
        }

        public static void GetSubscribedWorkshopItems(Action<IList<Workshop.Item>> onItemsFound, List<string> requireTags = null)
        {
            if (instance == null || !instance.isInitialized) return;

            var query = instance.client.Workshop.CreateQuery();
            query.Order = Workshop.Order.RankedByTotalUniqueSubscriptions;
            query.UserId = instance.client.SteamId;
            query.UserQueryType = Workshop.UserQueryType.Subscribed;
            query.UploaderAppId = AppID;
            if (requireTags != null) query.RequireTags = requireTags;
            query.Run();
            query.OnResult += (Workshop.Query q) =>
            {
                onItemsFound?.Invoke(q.Items);
            };
        }

        public static void GetPopularWorkshopItems(Action<IList<Workshop.Item>> onItemsFound, int amount, List<string> requireTags = null)
        {
            if (instance == null || !instance.isInitialized) return;

            var query = instance.client.Workshop.CreateQuery();
            query.Order = Workshop.Order.RankedByTotalUniqueSubscriptions;
            query.UploaderAppId = AppID;
            if (requireTags != null) query.RequireTags = requireTags;
            query.Run();
            query.OnResult += (Workshop.Query q) =>
            {
                //count the number of each unique tag
                foreach (var item in q.Items)
                {
                    foreach (string tag in item.Tags)
                    {
                        if (string.IsNullOrEmpty(tag)) { continue; }
                        string caseInvariantTag = tag.ToLowerInvariant();
                        if (!instance.tagCommonness.ContainsKey(caseInvariantTag))
                        {
                            instance.tagCommonness[caseInvariantTag] = 1;
                        }
                        else
                        {
                            instance.tagCommonness[caseInvariantTag]++;
                        }
                    }
                }
                //populate the popularTags list with tags sorted by commonness
                instance.popularTags.Clear();
                foreach (KeyValuePair<string, int> tagCommonness in instance.tagCommonness)
                {
                    int i = 0;
                    while (i < instance.popularTags.Count &&
                            instance.tagCommonness[instance.popularTags[i]] > tagCommonness.Value)
                    {
                        i++;
                    }
                    instance.popularTags.Insert(i, tagCommonness.Key);
                }

                var nonSubscribedItems = q.Items.Where(it => !it.Subscribed && !it.Installed);
                if (nonSubscribedItems.Count() > amount)
                {
                    nonSubscribedItems = nonSubscribedItems.Take(amount);
                }
                onItemsFound?.Invoke(nonSubscribedItems.ToList());
            };
        }

        public static void GetPublishedWorkshopItems(Action<IList<Workshop.Item>> onItemsFound, List<string> requireTags = null)
        {
            if (instance == null || !instance.isInitialized) return;

            var query = instance.client.Workshop.CreateQuery();
            query.Order = Workshop.Order.RankedByPublicationDate;
            query.UserId = instance.client.SteamId;
            query.UserQueryType = Workshop.UserQueryType.Published;
            query.UploaderAppId = AppID;
            if (requireTags != null) query.RequireTags = requireTags;
            query.Run();
            query.OnResult += (Workshop.Query q) =>
            {
                onItemsFound?.Invoke(q.Items);
            };
        }

        public static void SubscribeToWorkshopItem(string itemUrl)
        {
            if (instance == null || !instance.isInitialized) return;

            ulong id = GetWorkshopItemIDFromUrl(itemUrl);
            if (id == 0) { return; }

            var item = instance.client.Workshop.GetItem(id);
            if (item == null)
            {
                DebugConsole.ThrowError("Failed to find a Steam Workshop item with the ID " + id + ".");
                return;
            }

            item.Subscribe();
            item.Download();
        }

        public static void CreateWorkshopItemStaging(ContentPackage contentPackage, out Workshop.Editor itemEditor)
        {
            itemEditor = instance.client.Workshop.CreateItem(Workshop.ItemType.Community);
            itemEditor.Visibility = Workshop.Editor.VisibilityType.Public;
            itemEditor.WorkshopUploadAppId = AppID;
            itemEditor.Folder = Path.GetFullPath(Path.GetDirectoryName(contentPackage.Path));

            string previewImagePath = Path.GetFullPath(Path.Combine(itemEditor.Folder, PreviewImageName));
            if (!Directory.Exists(itemEditor.Folder)) { Directory.CreateDirectory(itemEditor.Folder); }
            if (!File.Exists(previewImagePath))
            {
                File.Copy("Content/DefaultWorkshopPreviewImage.png", previewImagePath);
            }
        }

        /// <summary>
        /// Creates a new empty content package
        /// </summary>
        public static void CreateWorkshopItemStaging(string itemName, out Workshop.Editor itemEditor, out ContentPackage contentPackage)
        {
            string dirPath = Path.Combine("Mods", ToolBox.RemoveInvalidFileNameChars(itemName));
            Directory.CreateDirectory("Mods");
            Directory.CreateDirectory(dirPath);

            itemEditor = instance.client.Workshop.CreateItem(Workshop.ItemType.Community);
            itemEditor.Visibility = Workshop.Editor.VisibilityType.Public;
            itemEditor.WorkshopUploadAppId = AppID;
            itemEditor.Folder = dirPath;

            string previewImagePath = Path.GetFullPath(Path.Combine(dirPath, PreviewImageName));
            if (!File.Exists(previewImagePath))
            {
                File.Copy("Content/DefaultWorkshopPreviewImage.png", previewImagePath);
            }
            
            //create a new content package and include the copied files in it
            contentPackage = ContentPackage.CreatePackage(itemName, Path.Combine(dirPath, MetadataFileName), false);
            contentPackage.Save(Path.Combine(dirPath, MetadataFileName));
        }

        /// <summary>
        /// Creates a copy of the specified workshop item in the staging folder and an editor that can be used to edit and update the item
        /// </summary>
        public static bool CreateWorkshopItemStaging(Workshop.Item existingItem, out Workshop.Editor itemEditor, out ContentPackage contentPackage)
        {
            if (!existingItem.Installed)
            {
                itemEditor = null;
                contentPackage = null;
                DebugConsole.ThrowError("Cannot edit the workshop item \"" + existingItem.Title + "\" because it has not been installed.");
                return false;
            }

            itemEditor = instance.client.Workshop.EditItem(existingItem.Id);
            itemEditor.Visibility = existingItem.Visibility;
            itemEditor.Title = existingItem.Title;
            itemEditor.Tags = existingItem.Tags.ToList();
            itemEditor.Description = existingItem.Description;
            itemEditor.WorkshopUploadAppId = AppID;

            if (!CheckWorkshopItemEnabled(existingItem, checkContentFiles: false))
            {
                if (!EnableWorkShopItem(existingItem, false, out string errorMsg))
                {
                    DebugConsole.ThrowError(errorMsg);
                    new GUIMessageBox(
                        TextManager.Get("Error"),
                        TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { existingItem.Title, errorMsg }));
                    itemEditor = null;
                    contentPackage = null;
                    return false;
                }
            }

            ContentPackage tempContentPackage = new ContentPackage(Path.Combine(existingItem.Directory.FullName, MetadataFileName));
            string installedContentPackagePath = Path.GetFullPath(GetWorkshopItemContentPackagePath(tempContentPackage));
            contentPackage = ContentPackage.List.Find(cp => Path.GetFullPath(cp.Path) == installedContentPackagePath);

            itemEditor.Folder = Path.GetDirectoryName(installedContentPackagePath);

            string previewImagePath = Path.GetFullPath(Path.Combine(itemEditor.Folder, PreviewImageName));
            itemEditor.PreviewImage = previewImagePath;

            try
            {
                if (File.Exists(previewImagePath)) { File.Delete(previewImagePath); }

                Uri baseAddress = new Uri(existingItem.PreviewImageUrl);
                Uri directory = new Uri(baseAddress, "."); // "." == current dir, like MS-DOS
                string fileName = Path.GetFileName(baseAddress.LocalPath);

                IRestClient client = new RestClient(directory);
                var request = new RestRequest(fileName, Method.GET);
                var response = client.Execute(request);

                if (response.ResponseStatus == ResponseStatus.Completed)
                {
                    File.WriteAllBytes(previewImagePath, response.RawBytes);
                }
            }

            catch (Exception e)
            {
                string errorMsg = "Failed to save workshop item preview image to \"" + previewImagePath + "\" when creating workshop item staging folder.";
                GameAnalyticsManager.AddErrorEventOnce("SteamManager.CreateWorkshopItemStaging:WriteAllBytesFailed" + previewImagePath,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg + "\n" + e.Message);
            }
            return true;
        }

        public static void StartPublishItem(ContentPackage contentPackage, Workshop.Editor item)
        {
            if (instance == null || !instance.isInitialized) return;

            if (string.IsNullOrEmpty(item.Title))
            {
                DebugConsole.ThrowError("Cannot publish workshop item - title not set.");
                return;
            }
            if (string.IsNullOrEmpty(item.Folder))
            {
                DebugConsole.ThrowError("Cannot publish workshop item \"" + item.Title + "\" - folder not set.");
                return;
            }
            
            contentPackage.GameVersion = GameMain.Version;
            contentPackage.Save(contentPackage.Path);
            
            string previewImagePath =  Path.GetFullPath(Path.Combine(item.Folder, PreviewImageName));
            item.PreviewImage = File.Exists(previewImagePath) ? previewImagePath : null;

            CoroutineManager.StartCoroutine(PublishItem(item));
        }

        private static IEnumerable<object> PublishItem(Workshop.Editor item)
        {
            if (instance == null || !instance.isInitialized)
            {
                yield return CoroutineStatus.Success;
            }

            item.Publish();
            while (item.Publishing)
            {
                yield return CoroutineStatus.Running;
            }

            if (string.IsNullOrEmpty(item.Error))
            {
                DebugConsole.NewMessage("Published workshop item " + item.Title + " successfully.", Microsoft.Xna.Framework.Color.LightGreen);
                var newItem = instance.client.Workshop.GetItem(item.Id);
                newItem?.Subscribe();
            }
            else
            {
                DebugConsole.NewMessage("Publishing workshop item " + item.Title + " failed. " + item.Error, Microsoft.Xna.Framework.Color.Red);
            }
            
            yield return CoroutineStatus.Success;
        }

        /// <summary>
        /// Enables a workshop item by moving it to the game folder.
        /// </summary>
        public static bool EnableWorkShopItem(Workshop.Item item, bool allowFileOverwrite, out string errorMsg, bool selectContentPackage = true)
        {
            if (!item.Installed)
            {
                errorMsg = TextManager.GetWithVariable("WorkshopErrorInstallRequiredToEnable", "[itemname]", item.Title);
                DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                return false;
            }

            string metaDataFilePath = Path.Combine(item.Directory.FullName, MetadataFileName);

            if (!File.Exists(metaDataFilePath))
            {
                errorMsg = TextManager.GetWithVariable("WorkshopErrorInstallRequiredToEnable", "[itemname]", item.Title);
                DebugConsole.ThrowError(errorMsg);
                return false;
            }

            ContentPackage contentPackage = new ContentPackage(metaDataFilePath)
            {
                SteamWorkshopUrl = item.Url
            };
            string newContentPackagePath = GetWorkshopItemContentPackagePath(contentPackage);

            if (!contentPackage.IsCompatible())
            {
                errorMsg = TextManager.GetWithVariables(contentPackage.GameVersion <= new Version(0, 0, 0, 0) ? "IncompatibleContentPackageUnknownVersion" : "IncompatibleContentPackage",
                    new string[3] { "[packagename]", "[packageversion]", "[gameversion]" }, new string[3] { contentPackage.Name, contentPackage.GameVersion.ToString(), GameMain.Version.ToString() });
                return false;
            }

            if (contentPackage.CorePackage && !contentPackage.ContainsRequiredCorePackageFiles(out List<ContentType> missingContentTypes))
            {
                errorMsg = TextManager.GetWithVariables("ContentPackageMissingCoreFiles", new string[2] { "[packagename]", "[missingfiletypes]" },
                    new string[2] { contentPackage.Name, string.Join(", ", missingContentTypes) }, new bool[2] { false, true });
                return false;
            }

            if (contentPackage.GameVersion > new Version(0, 9, 1, 0))
            {
                SaveUtil.CopyFolder(item.Directory.FullName, Path.GetDirectoryName(GetWorkshopItemContentPackagePath(contentPackage)), copySubDirs: true, overwriteExisting: true);
            }
            else //legacy support
            {
                EnableWorkShopItemLegacy(item, contentPackage, newContentPackagePath, metaDataFilePath, allowFileOverwrite, out errorMsg);
            }

            var newPackage = new ContentPackage(contentPackage.Path, newContentPackagePath)
            {
                SteamWorkshopUrl = item.Url,
                InstallTime = item.Modified > item.Created ? item.Modified : item.Created
            };

            if (contentPackage.GameVersion <= new Version(0, 9, 1, 0))
            {
                string fileName = contentPackage.Name;
                fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

                foreach (ContentFile contentFile in newPackage.Files)
                {
                    CorrectContentFilePath(contentFile, fileName, true);
                }
            }

            if (!Directory.Exists(Path.GetDirectoryName(newContentPackagePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newContentPackagePath));
            }
            newPackage.Save(newContentPackagePath);
            ContentPackage.List.Add(newPackage);
            
            if (selectContentPackage)
            {
                if (newPackage.CorePackage)
                {
                    //if enabling a core package, disable all other core packages
                    GameMain.Config.SelectedContentPackages.RemoveAll(cp => cp.CorePackage);
                }
                GameMain.Config.SelectContentPackage(newPackage);
                GameMain.Config.SaveNewPlayerConfig();

                GameMain.Config.WarnIfContentPackageSelectionDirty();
            }
            errorMsg = "";
            return true;
        }

        private static bool EnableWorkShopItemLegacy(Workshop.Item item, ContentPackage contentPackage, string newContentPackagePath, string metaDataFilePath, bool allowFileOverwrite, out string errorMsg)
        {
            errorMsg = "";

            var allPackageFiles = Directory.GetFiles(item.Directory.FullName, "*", SearchOption.AllDirectories);
            List<string> nonContentFiles = new List<string>();
            foreach (string file in allPackageFiles)
            {
                if (file == metaDataFilePath) { continue; }
                string relativePath = UpdaterUtil.GetRelativePath(file, item.Directory.FullName);
                string fullPath = Path.GetFullPath(relativePath);
                if (contentPackage.Files.Any(f => { string fp = Path.GetFullPath(f.Path); return fp == fullPath; })) { continue; }
                if (ContentPackage.IsModFilePathAllowed(relativePath))
                {
                    nonContentFiles.Add(relativePath);
                }
            }

            if (!allowFileOverwrite)
            {
                if (File.Exists(newContentPackagePath) && !CheckFileEquality(newContentPackagePath, metaDataFilePath))
                {
                    errorMsg = TextManager.GetWithVariables("WorkshopErrorOverwriteOnEnable", new string[2] { "[itemname]", "[filename]" }, new string[2] { item.Title, newContentPackagePath });
                    DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                    return false;
                }

                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    string sourceFile = Path.Combine(item.Directory.FullName, contentFile.Path);
                    if (File.Exists(sourceFile) && File.Exists(contentFile.Path) && !CheckFileEquality(sourceFile, contentFile.Path))
                    {
                        errorMsg = TextManager.GetWithVariables("WorkshopErrorOverwriteOnEnable", new string[2] { "[itemname]", "[filename]" }, new string[2] { item.Title, contentFile.Path });
                        DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                        return false;
                    }
                }
            }

            try
            {
                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    contentFile.Path = contentFile.Path.Replace('\\', '/');
                    string sourceFile = Path.Combine(item.Directory.FullName, contentFile.Path);

                    string fileName = contentPackage.Name;
                    fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

                    CorrectContentFilePath(contentFile, fileName, false);

                    //path not allowed -> the content file must be a reference to an external file (such as some vanilla file outside the Mods folder)
                    if (!ContentPackage.IsModFilePathAllowed(contentFile))
                    {
                        //the content package is trying to copy a file to a prohibited path, which is not allowed
                        if (File.Exists(sourceFile))
                        {
                            errorMsg = TextManager.GetWithVariable("WorkshopErrorIllegalPathOnEnable", "[filename]", contentFile.Path);
                            return false;
                        }
                        //not trying to copy anything, so this is a reference to an external file
                        //if the external file doesn't exist, we cannot enable the package
                        else if (!File.Exists(contentFile.Path))
                        {
                            errorMsg = TextManager.GetWithVariable("WorkshopErrorEnableFailed", "[itemname]", item.Title) + " " + TextManager.GetWithVariable("WorkshopFileNotFound", "[path]", "\"" + contentFile.Path + "\"");
                            return false;
                        }
                        continue;
                    }
                    else if (!File.Exists(sourceFile))
                    {
                        if (File.Exists(contentFile.Path))
                        {
                            //the file is already present in the game folder, all good
                            continue;
                        }
                        else
                        {
                            //file not present in either the mod or the game folder -> cannot enable the package
                            errorMsg = TextManager.GetWithVariable("WorkshopErrorEnableFailed", "[itemname]", item.Title) + " " + TextManager.GetWithVariable("WorkshopFileNotFound", "[path]", "\"" + contentFile.Path + "\"");
                            return false;
                        }
                    }

                    //make sure the destination directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(contentFile.Path));
                    File.Copy(sourceFile, contentFile.Path, overwrite: true);
                }

                foreach (string nonContentFile in nonContentFiles)
                {
                    string sourceFile = Path.Combine(item.Directory.FullName, nonContentFile);
                    if (!File.Exists(sourceFile)) { continue; }
                    if (!ContentPackage.IsModFilePathAllowed(nonContentFile))
                    {
                        DebugConsole.ThrowError(TextManager.GetWithVariable("WorkshopErrorIllegalPathOnEnable", "[filename]", nonContentFile));
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(nonContentFile));
                    File.Copy(sourceFile, nonContentFile, overwrite: true);
                }
            }
            catch (Exception e)
            {
                errorMsg = TextManager.GetWithVariable("WorkshopErrorEnableFailed", "[itemname]", item.Title) + " {" + e.Message + "}";
                DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                return false;
            }

            return true;
        }

        private static bool CheckFileEquality(string filePath1, string filePath2)
        {
            if (filePath1 == filePath2)
            {
                return true;
            }

            using (FileStream fs1 = File.OpenRead(filePath1))
            using (FileStream fs2 = File.OpenRead(filePath2))
            {
                Md5Hash hash1 = new Md5Hash(fs1);
                Md5Hash hash2 = new Md5Hash(fs2);
                return hash1.Hash == hash2.Hash;
            }
        }

        /// <summary>
        /// Disables a workshop item by removing the files from the game folder.
        /// </summary>
        public static bool DisableWorkShopItem(Workshop.Item item, out string errorMsg)
        {
            if (!item.Installed)
            {
                errorMsg = "Cannot disable workshop item \"" + item.Title + "\" because it has not been installed.";
                DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                return false;
            }

            ContentPackage contentPackage = new ContentPackage(Path.Combine(item.Directory.FullName, MetadataFileName));
            string installedContentPackagePath = GetWorkshopItemContentPackagePath(contentPackage);

            var allPackageFiles = Directory.GetFiles(item.Directory.FullName, "*", SearchOption.AllDirectories);
            List<string> nonContentFiles = new List<string>();
            foreach (string file in allPackageFiles)
            {
                if (file == MetadataFileName) { continue; }
                string relativePath = UpdaterUtil.GetRelativePath(file, item.Directory.FullName);
                string fullPath = Path.GetFullPath(relativePath);
                if (contentPackage.Files.Any(f => { string fp = Path.GetFullPath(f.Path); return fp == fullPath; })) { continue; }
                if (ContentPackage.IsModFilePathAllowed(relativePath))
                {
                    nonContentFiles.Add(relativePath);
                }
            }
            if (File.Exists(installedContentPackagePath)) { File.Delete(installedContentPackagePath); }

            HashSet<string> directories = new HashSet<string>();
            try
            {
                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    if (!ContentPackage.IsModFilePathAllowed(contentFile))
                    {
                        //Workshop items are not allowed to add or modify files in the Content or Data folders;
                        continue;
                    }
                    if (!File.Exists(contentFile.Path)) { continue; }
                    File.Delete(contentFile.Path);
                    directories.Add(Path.GetDirectoryName(contentFile.Path));
                }
                foreach (string nonContentFile in nonContentFiles)
                {
                    if (!ContentPackage.IsModFilePathAllowed(nonContentFile))
                    {
                        //Workshop items are not allowed to add or modify files in the Content or Data folders;
                        continue;
                    }
                    if (!File.Exists(nonContentFile)) { continue; }
                    File.Delete(nonContentFile);
                    directories.Add(Path.GetDirectoryName(nonContentFile));
                }

                foreach (string directory in directories)
                {
                    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) { continue; }
                    if (Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Count() == 0)
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }

                ContentPackage.List.RemoveAll(cp => System.IO.Path.GetFullPath(cp.Path) == System.IO.Path.GetFullPath(installedContentPackagePath));
                var packagesToDeselect = GameMain.Config.SelectedContentPackages.Where(p => !ContentPackage.List.Contains(p)).ToList();
                foreach (var cp in packagesToDeselect)
                {
                    GameMain.Config.DeselectContentPackage(cp);
                }
                GameMain.Config.SelectedContentPackages.RemoveAll(cp => !ContentPackage.List.Contains(cp));
                ContentPackage.SortContentPackages();
                GameMain.Config.SaveNewPlayerConfig();

                GameMain.Config.WarnIfContentPackageSelectionDirty();
            }
            catch (Exception e)
            {
                errorMsg = "Disabling the workshop item \"" + item.Title + "\" failed. " + e.Message;
                DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                return false;
            }

            errorMsg = "";
            return true;
        }

        /// <summary>
        /// Is the item compatible with this version of Barotrauma. Returns null if compatibility couldn't be determined (item not installed)
        /// </summary>
        public static bool? CheckWorkshopItemCompatibility(Workshop.Item item)
        {
            if (!item.Installed) { return null; }

            string metaDataPath = Path.Combine(item.Directory.FullName, MetadataFileName);
            if (!File.Exists(metaDataPath))
            {
                throw new FileNotFoundException("Metadata file for the Workshop item \"" + item.Title + "\" not found. The file may be corrupted.");
            }

            ContentPackage contentPackage = new ContentPackage(metaDataPath);
            return contentPackage.IsCompatible();
        }

        public static bool CheckWorkshopItemEnabled(Workshop.Item item, bool checkContentFiles = true)
        {
            if (!item.Installed) { return false; }

            if (!Directory.Exists(item.Directory.FullName))
            {
                DebugConsole.ThrowError("Workshop item \"" + item.Title + "\" has been installed but the install directory cannot be found. Attempting to redownload...");
                item.ForceDownload();
                return false;                
            }

            string metaDataPath = "";
            try
            {
                metaDataPath = Path.Combine(item.Directory.FullName, MetadataFileName);
            }
            catch (ArgumentException)
            {
                string errorMessage = "Metadata file for the Workshop item \"" + item.Title +
                    "\" not found. Could not combine path (" + (item.Directory.FullName ?? "directory name empty") + ").";
                DebugConsole.ThrowError(errorMessage);
                GameAnalyticsManager.AddErrorEventOnce("SteamManager.CheckWorkshopItemEnabled:PathCombineException" + item.Title,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMessage);
                return false;
            }

            if (!File.Exists(metaDataPath))
            {
                DebugConsole.ThrowError("Metadata file for the Workshop item \"" + item.Title + "\" not found. The file may be corrupted.");
                return false;
            }

            ContentPackage contentPackage = new ContentPackage(metaDataPath);
            //make sure the contentpackage file is present 
            if (!File.Exists(GetWorkshopItemContentPackagePath(contentPackage)) &&
                !ContentPackage.List.Any(cp => cp.Name == contentPackage.Name))
            {
                return false;
            }
            if (checkContentFiles)
            {
                string fileName = contentPackage.Name;
                fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    CorrectContentFilePath(contentFile, fileName, true);
                    if (!File.Exists(contentFile.Path)) { return false; }
                }
            }

            return true;
        }

        public static bool CheckWorkshopItemUpToDate(Workshop.Item item)
        {
            if (!item.Installed) { return false; }

            string metaDataPath = Path.Combine(item.Directory.FullName, MetadataFileName);
            if (!File.Exists(metaDataPath))
            {
                DebugConsole.ThrowError("Metadata file for the Workshop item \"" + item.Title + "\" not found. The file may be corrupted.");
                return false;
            }
                                    
            ContentPackage steamPackage = new ContentPackage(metaDataPath);
            ContentPackage myPackage = ContentPackage.List.Find(cp => cp.Name == steamPackage.Name);

            if (myPackage?.InstallTime == null)
            {
                return false;
            }
            return item.Modified <= myPackage.InstallTime.Value;
        }


        public static bool CheckWorkshopItemSelected(Workshop.Item item)
        {
            if (!item.Installed) { return false; }

            string metaDataPath = Path.Combine(item.Directory.FullName, MetadataFileName);
            if (!File.Exists(metaDataPath))
            {
                DebugConsole.ThrowError("Metadata file for the Workshop item \"" + item.Title + "\" not found. The file may be corrupted.");
                return false;
            }

            ContentPackage steamPackage = new ContentPackage(metaDataPath);
            return GameMain.Config.SelectedContentPackages.Any(cp => cp.Name == steamPackage.Name);
        }

        public static bool AutoUpdateWorkshopItems()
        {
            if (instance == null || !instance.isInitialized) { return false; }

            bool? itemsUpdated = null;
            bool timedOut = false;
            var query = instance.client.Workshop.CreateQuery();
            query.FileId = new List<ulong>(instance.client.Workshop.GetSubscribedItemIds());
            query.UploaderAppId = AppID;
            query.Run();
            query.OnResult = (Workshop.Query q) =>
            {
                if (timedOut) { return; }
                itemsUpdated = false;
                foreach (var item in q.Items)
                {
                    try
                    {
                        if (!item.Installed || !CheckWorkshopItemEnabled(item) || CheckWorkshopItemUpToDate(item)) { continue; }
                        if (!UpdateWorkshopItem(item, out string errorMsg))
                        {
                            DebugConsole.ThrowError(errorMsg);
                            new GUIMessageBox(
                                TextManager.Get("Error"),
                                TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { item.Title, errorMsg }));
                        }
                        else
                        {
                            new GUIMessageBox("", TextManager.GetWithVariable("WorkshopItemUpdated", "[itemname]", item.Title));
                            itemsUpdated = true;
                        }
                    }
                    catch (Exception e)
                    {
                        new GUIMessageBox(
                            TextManager.Get("Error"),
                            TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { item.Title, e.Message + ", " + e.TargetSite }));
                        GameAnalyticsManager.AddErrorEventOnce(
                            "SteamManager.AutoUpdateWorkshopItems:" + e.Message,
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                            "Failed to autoupdate workshop item \"" + item.Title + "\". " + e.Message + "\n" + e.StackTrace);
                    }
                }
            };

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 10);
            while (!itemsUpdated.HasValue)
            {
                if (DateTime.Now > timeOut)
                {
                    itemsUpdated = false;
                    timedOut = true;
                    break;
                }
                instance.client.Update();
                System.Threading.Thread.Sleep(10);
            }
            
            return itemsUpdated.Value;
        }

        public static bool UpdateWorkshopItem(Workshop.Item item, out string errorMsg)
        {
            errorMsg = "";
            if (!item.Installed) { return false; }
            bool wasSelected = CheckWorkshopItemSelected(item);
            if (!DisableWorkShopItem(item, out errorMsg)) { return false; }
            if (!EnableWorkShopItem(item, allowFileOverwrite: false, errorMsg: out errorMsg, selectContentPackage: wasSelected)) { return false; }

            return true;
        }

        private static string GetWorkshopItemContentPackagePath(ContentPackage contentPackage)
        {
            string fileName = contentPackage.Name;
            fileName = ToolBox.RemoveInvalidFileNameChars(fileName);

            return Path.Combine("Mods", fileName, MetadataFileName);
        }

        private static void CorrectContentFilePath(ContentFile contentFile, string packageName, bool checkIfFileExists=false)
        {
            contentFile.Path = contentFile.Path.Replace('\\', '/');

            string[] splitPath = contentFile.Path.Split('/');
            if (splitPath.Length < 2 || splitPath[0] != "Mods" || splitPath[1] != packageName)
            {
                string newPath = Path.Combine("Mods", packageName, contentFile.Path);
                if (!checkIfFileExists || File.Exists(newPath))
                {
                    contentFile.Path = newPath;
                }
            }
        }

        #endregion

    }
}
