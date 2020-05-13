using Barotrauma.Networking;
using RestSharp;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RestSharp.Contrib;
using System.Xml.Linq;
using Color = Microsoft.Xna.Framework.Color;
using System.Runtime.InteropServices;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        private static Dictionary<Steamworks.Data.PublishedFileId, Task> modCopiesInProgress = new Dictionary<Steamworks.Data.PublishedFileId, Task>();

        private static void InitializeProjectSpecific()
        {
            if (isInitialized) { return; }

            try
            {
                Steamworks.SteamClient.Init(AppID, false);
                isInitialized = Steamworks.SteamClient.IsLoggedOn && Steamworks.SteamClient.IsValid;

                if (isInitialized)
                {
                    DebugConsole.NewMessage("Logged in as " + GetUsername() + " (SteamID " + SteamIDUInt64ToString(GetSteamID()) + ")");
                    
                    popularTags.Clear();
                    int i = 0;
                    foreach (KeyValuePair<string, int> commonness in tagCommonness)
                    {
                        popularTags.Insert(i, commonness.Key);
                        i++;
                    }

                    LogSteamworksNetworkingDelegate = LogSteamworksNetworking;

                    IntPtr logSteamworksNetworkingPtr = Marshal.GetFunctionPointerForDelegate(LogSteamworksNetworkingDelegate);
                    Steamworks.SteamNetworkingUtils.SetDebugOutputFunction(Steamworks.Data.DebugOutputType.Everything, logSteamworksNetworkingPtr);
                }
            }
            catch (DllNotFoundException)
            {
                isInitialized = false;
                initializationErrors.Add("SteamDllNotFound");
            }
            catch (Exception)
            {
                isInitialized = false;
                initializationErrors.Add("SteamClientInitFailed");
            }

            if (!isInitialized)
            {
                try
                {
                    if (Steamworks.SteamClient.IsValid) { Steamworks.SteamClient.Shutdown(); }
                }
                catch (Exception e)
                {
                    if (GameSettings.VerboseLogging) DebugConsole.ThrowError("Disposing Steam client failed.", e);
                }
            }
        }

        public static bool NetworkingDebugLog = false;

        private static Steamworks.Data.FSteamNetworkingSocketsDebugOutput LogSteamworksNetworkingDelegate;

        private static void LogSteamworksNetworking(Steamworks.Data.DebugOutputType nType, string pszMsg)
        {
            if (NetworkingDebugLog) { DebugConsole.NewMessage($"({nType}) {pszMsg}", Color.Orange); }
        }

        private static void UpdateProjectSpecific(float deltaTime)
        {
            if (ugcSubscriptionTasks != null)
            {
                var ugcSubscriptionKeys = ugcSubscriptionTasks.Keys.ToList();
                foreach (var key in ugcSubscriptionKeys)
                {
                    var task = ugcSubscriptionTasks[key];

                    if (task.IsCompleted)
                    {
                        if (!task.IsCompletedSuccessfully)
                        {
                            DebugConsole.ThrowError("Failed to subscribe to a Steam Workshop item with id " + key.ToString() + ": TaskStatus = " + task.Status.ToString());
                        }
                        ugcSubscriptionTasks.Remove(key);
                    }
                }
            }
        }

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
            TaskPool.Add(Steamworks.SteamMatchmaking.CreateLobbyAsync(serverSettings.MaxPlayers + 10),
                (lobby) =>
                {
                    currentLobby = lobby.Result;

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
            
            var contentPackages = GameMain.Config.SelectedContentPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);
            
            currentLobby?.SetData("name", serverSettings.ServerName);
            currentLobby?.SetData("playercount", (GameMain.Client?.ConnectedClients?.Count ?? 0).ToString());
            currentLobby?.SetData("maxplayernum", serverSettings.MaxPlayers.ToString());
            //currentLobby?.SetData("hostipaddress", lobbyIP);
            string pingLocation = Steamworks.SteamNetworkingUtils.LocalPingLocation.ToString();
            currentLobby?.SetData("pinglocation", pingLocation ?? "");
            currentLobby?.SetData("lobbyowner", SteamIDUInt64ToString(GetSteamID()));
            currentLobby?.SetData("haspassword", serverSettings.HasPassword.ToString());

            currentLobby?.SetData("message", serverSettings.ServerMessageText);
            currentLobby?.SetData("version", GameMain.Version.ToString());

            currentLobby?.SetData("contentpackage", string.Join(",", contentPackages.Select(cp => cp.Name)));
            currentLobby?.SetData("contentpackagehash", string.Join(",", contentPackages.Select(cp => cp.MD5hash.Hash)));
            currentLobby?.SetData("contentpackageurl", string.Join(",", contentPackages.Select(cp => cp.SteamWorkshopUrl ?? "")));
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
            currentLobby?.SetData("gamemode", GameMain.NetLobbyScreen?.SelectedMode?.Identifier ?? "");

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

            TaskPool.Add(Steamworks.SteamMatchmaking.JoinLobbyAsync(lobbyID),
                (lobby) =>
                {
                    currentLobby = lobby.Result;
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
            if (!isInitialized) { return false; }


            int doneTasks = 0;
            Action taskDone = () =>
            {
                doneTasks++;
                if (doneTasks >= 2)
                {
                    serverQueryFinished?.Invoke();
                    serverQueryFinished = null;
                }
            };

            //TODO: find a better strategy to fetch all lobbies, this is gonna take forever if we actually have 10000 lobbies
            Steamworks.Data.LobbyQuery lobbyQuery = Steamworks.SteamMatchmaking.CreateLobbyQuery().FilterDistanceWorldwide().WithMaxResults(10000);

            TaskPool.Add(Task.Run(async () =>
            {
                Steamworks.Data.Lobby[] lobbies = await lobbyQuery.RequestAsync();
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

                    CrossThread.RequestExecutionOnMainThread(() =>
                    {
                        addToServerList(serverInfo);
                    });
                }
            }),
            (t) =>
            {
                taskDone();
                if (t.Status == TaskStatus.Faulted)
                {
                    TaskPool.PrintTaskExceptions(t, "Failed to retrieve SteamP2P lobbies");
                    return;
                }
            });

            Steamworks.ServerList.Internet serverQuery = new Steamworks.ServerList.Internet();
            Action<Steamworks.Data.ServerInfo, bool> onServer = (Steamworks.Data.ServerInfo info, bool responsive) =>
            {
                if (string.IsNullOrEmpty(info.Name)) { return; }

                ServerInfo serverInfo = new ServerInfo();
                serverInfo.ServerName = info.Name;
                serverInfo.IP = info.Address.ToString();
                serverInfo.Port = info.ConnectionPort.ToString();
                serverInfo.PlayerCount = info.Players;
                serverInfo.MaxPlayers = info.MaxPlayers;
                serverInfo.RespondedToSteamQuery = responsive;

                if (responsive)
                {
                    TaskPool.Add(info.QueryRulesAsync(),
                        (t) =>
                        {
                            if (t.Status == TaskStatus.Faulted)
                            {
                                TaskPool.PrintTaskExceptions(t, "Failed to retrieve rules for "+info.Name);
                                return;
                            }

                            var rules = t.Result;
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

            };
            serverQuery.OnResponsiveServer += (info) => onServer(info, true);
            serverQuery.OnUnresponsiveServer += (info) => onServer(info, false);

            TaskPool.Add(serverQuery.RunQueryAsync(),
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

        public static void AssignLobbyDataToServerInfo(Steamworks.Data.Lobby lobby, ServerInfo serverInfo)
        {
            serverInfo.OwnerVerified = true;

            serverInfo.ServerMessage = lobby.GetData("message");
            serverInfo.GameVersion = lobby.GetData("version");

            serverInfo.ContentPackageNames.AddRange(lobby.GetData("contentpackage").Split(','));
            serverInfo.ContentPackageHashes.AddRange(lobby.GetData("contentpackagehash").Split(','));
            serverInfo.ContentPackageWorkshopUrls.AddRange(lobby.GetData("contentpackageurl").Split(','));

            serverInfo.UsingWhiteList = getLobbyBool("usingwhitelist");
            SelectionMode selectionMode;
            if (Enum.TryParse(lobby.GetData("modeselectionmode"), out selectionMode)) { serverInfo.ModeSelectionMode = selectionMode; }
            if (Enum.TryParse(lobby.GetData("subselectionmode"), out selectionMode)) { serverInfo.SubSelectionMode = selectionMode; }

            serverInfo.AllowSpectating = getLobbyBool("allowspectating");
            serverInfo.AllowRespawn = getLobbyBool("allowrespawn");
            serverInfo.VoipEnabled = getLobbyBool("voicechatenabled");
            serverInfo.KarmaEnabled = getLobbyBool("karmaenabled");
            serverInfo.FriendlyFireEnabled = getLobbyBool("friendlyfireenabled");
            if (Enum.TryParse(lobby.GetData("traitors"), out YesNoMaybe traitorsEnabled)) { serverInfo.TraitorsEnabled = traitorsEnabled; }

            serverInfo.GameStarted = lobby.GetData("gamestarted") == "True";
            serverInfo.GameMode = lobby.GetData("gamemode") ?? "";
            if (Enum.TryParse(lobby.GetData("playstyle"), out PlayStyle playStyle)) serverInfo.PlayStyle = playStyle;

            if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count ||
                serverInfo.ContentPackageHashes.Count != serverInfo.ContentPackageWorkshopUrls.Count)
            {
                //invalid contentpackage info
                serverInfo.ContentPackageNames.Clear();
                serverInfo.ContentPackageHashes.Clear();
            }

            string pingLocation = lobby.GetData("pinglocation");
            if (!string.IsNullOrEmpty(pingLocation))
            {
                serverInfo.PingLocation = Steamworks.Data.PingLocation.TryParseFromString(pingLocation);
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
            serverInfo.ContentPackageWorkshopUrls.Clear();
            if (rules.ContainsKey("contentpackage")) serverInfo.ContentPackageNames.AddRange(rules["contentpackage"].Split(','));
            if (rules.ContainsKey("contentpackagehash")) serverInfo.ContentPackageHashes.AddRange(rules["contentpackagehash"].Split(','));
            if (rules.ContainsKey("contentpackageurl")) serverInfo.ContentPackageWorkshopUrls.AddRange(rules["contentpackageurl"].Split(','));

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
                serverInfo.GameMode = rules["gamemode"];
            }

            if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count ||
                serverInfo.ContentPackageHashes.Count != serverInfo.ContentPackageWorkshopUrls.Count)
            {
                //invalid contentpackage info
                serverInfo.ContentPackageNames.Clear();
                serverInfo.ContentPackageHashes.Clear();
            }
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

        //TODO: reimplement server list queries

        private static Steamworks.AuthTicket currentTicket = null;
        public static Steamworks.AuthTicket GetAuthSessionTicket()
        {
            if (!isInitialized)
            {
                return null;
            }

            currentTicket?.Cancel();
            currentTicket = Steamworks.SteamUser.GetAuthSessionTicket();
            return currentTicket;
        }

        public static Steamworks.BeginAuthResult StartAuthSession(byte[] authTicketData, ulong clientSteamID)
        {
            if (!isInitialized || !Steamworks.SteamClient.IsValid) return Steamworks.BeginAuthResult.ServerNotConnectedToSteam;

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
            if (!isInitialized || !Steamworks.SteamClient.IsValid) return;

            DebugConsole.NewMessage("SteamManager ending auth session with Steam client " + clientSteamID);
            Steamworks.SteamUser.EndAuthSession(clientSteamID);
        }

        #endregion

        #region Workshop
        
        public const string WorkshopItemPreviewImageFolder = "Workshop";
        public const string PreviewImageName = "PreviewImage.png";
        public const string DefaultPreviewImagePath = "Content/DefaultWorkshopPreviewImage.png";

        private static Sprite defaultPreviewImage;
        public static Sprite DefaultPreviewImage
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

        private static async Task<List<Steamworks.Ugc.Item>> GetWorkshopItemsAsync(Steamworks.Ugc.Query query, int clampResults = 0, Predicate<Steamworks.Ugc.Item> itemPredicate=null)
        {
            await Task.Yield();

            int pageIndex = 1;
            Steamworks.Ugc.ResultPage? resultPage = await query.GetPageAsync(pageIndex);

            List<Steamworks.Ugc.Item> retVal = new List<Steamworks.Ugc.Item>();
            while (resultPage.HasValue && resultPage?.ResultCount > 0)
            {
                if (itemPredicate != null)
                {
                    retVal.AddRange(resultPage.Value.Entries.Where(it => itemPredicate(it)));
                }
                else
                {
                    retVal.AddRange(resultPage.Value.Entries);
                }

                if (clampResults > 0 && retVal.Count >= clampResults)
                {
                    retVal = retVal.Take(clampResults).ToList();
                    break;
                }

                pageIndex++;
                resultPage = await query.GetPageAsync(pageIndex);
            }

            return retVal;
        }

        public static void GetSubscribedWorkshopItems(Action<IList<Steamworks.Ugc.Item>> onItemsFound, List<string> requireTags = null)
        {
            if (!isInitialized) return;

            var query = new Steamworks.Ugc.Query(Steamworks.UgcType.All)
                .RankedByTotalUniqueSubscriptions()
                .WhereUserSubscribed()
                .WithLongDescription();
            if (requireTags != null) { query = query.WithTags(requireTags); }

            TaskPool.Add(GetWorkshopItemsAsync(query), (task) => { onItemsFound?.Invoke(task.Result); });
        }

        public static void GetPopularWorkshopItems(Action<IList<Steamworks.Ugc.Item>> onItemsFound, int amount, List<string> requireTags = null)
        {
            if (!isInitialized) return;

            var query = new Steamworks.Ugc.Query(Steamworks.UgcType.All)
                .RankedByTotalUniqueSubscriptions()
                .WithLongDescription();
            if (requireTags != null) query.WithTags(requireTags);

            TaskPool.Add(GetWorkshopItemsAsync(query, amount, (item) => !item.IsSubscribed), (task) => {
                var entries = task.Result;

                //count the number of each unique tag
                foreach (var item in entries)
                {
                    foreach (string tag in item.Tags)
                    {
                        if (string.IsNullOrEmpty(tag)) { continue; }
                        string caseInvariantTag = tag.ToLowerInvariant();
                        if (!tagCommonness.ContainsKey(caseInvariantTag))
                        {
                            tagCommonness[caseInvariantTag] = 1;
                        }
                        else
                        {
                            tagCommonness[caseInvariantTag]++;
                        }
                    }
                }
                //populate the popularTags list with tags sorted by commonness
                popularTags.Clear();
                foreach (KeyValuePair<string, int> tagCommonnessKVP in tagCommonness)
                {
                    int i = 0;
                    while (i < popularTags.Count &&
                            tagCommonness[popularTags[i]] > tagCommonnessKVP.Value)
                    {
                        i++;
                    }
                    popularTags.Insert(i, tagCommonnessKVP.Key);
                }
                onItemsFound?.Invoke(task.Result);
            });
        }

        public static void GetPublishedWorkshopItems(Action<IList<Steamworks.Ugc.Item>> onItemsFound, List<string> requireTags = null)
        {
            if (!isInitialized) return;

            var query = new Steamworks.Ugc.Query(Steamworks.UgcType.All)
                .RankedByPublicationDate()
                .WhereUserPublished()
                .WithLongDescription();
            if (requireTags != null) query.WithTags(requireTags);

            TaskPool.Add(GetWorkshopItemsAsync(query), (task) => { onItemsFound?.Invoke(task.Result); });
        }

        private static Dictionary<ulong, Task> ugcSubscriptionTasks;

        public static void SubscribeToWorkshopItem(string itemUrl)
        {
            if (!isInitialized) return;

            ulong id = GetWorkshopItemIDFromUrl(itemUrl);

            SubscribeToWorkshopItem(id);
        }

        public static void SubscribeToWorkshopItem(ulong id)
        {
            if (!isInitialized) return;

            if (id == 0) { return; }

            if (ugcSubscriptionTasks?.ContainsKey(id) ?? false) { return; }

            ugcSubscriptionTasks ??= new Dictionary<ulong, Task>();
            ugcSubscriptionTasks.Add(id, Task.Run(async () =>
            {
                Steamworks.Ugc.Item? item = await Steamworks.SteamUGC.QueryFileAsync(id);

                if (!item.HasValue)
                {
                    DebugConsole.ThrowError("Failed to find a Steam Workshop item with the ID " + id.ToString() + ".");
                    return;
                }

                bool subscribed = await item?.Subscribe();
                if (!subscribed)
                {
                    DebugConsole.ThrowError("Failed to subscribe to Steam Workshop item with the ID " + id.ToString() + ".");
                }
                bool downloading = item?.Download() ?? false;
                if (!downloading)
                {
                    DebugConsole.ThrowError("Failed to start downloading Steam Workshop item with the ID " + id.ToString() + ".");
                }
            }));
        }

        public static void CreateWorkshopItemStaging(ContentPackage contentPackage, out Steamworks.Ugc.Editor? itemEditor)
        {
            string folderPath = Path.GetDirectoryName(contentPackage.Path);
            if (!Directory.Exists(folderPath)) { Directory.CreateDirectory(folderPath); }
            itemEditor = Steamworks.Ugc.Editor.CreateCommunityFile()
                .WithPublicVisibility()
                .ForAppId(AppID)
                .WithContent(folderPath);

            string previewImagePath = Path.GetFullPath(Path.Combine(folderPath, PreviewImageName));
            
            if (!File.Exists(previewImagePath))
            {
                File.Copy("Content/DefaultWorkshopPreviewImage.png", previewImagePath);
            }
        }

        /// <summary>
        /// Creates a new empty content package
        /// </summary>
        public static void CreateWorkshopItemStaging(string itemName, out Steamworks.Ugc.Editor? itemEditor, out ContentPackage contentPackage)
        {
            string dirPath = Path.Combine("Mods", ToolBox.RemoveInvalidFileNameChars(itemName));
            Directory.CreateDirectory("Mods");
            Directory.CreateDirectory(dirPath);

            itemEditor = Steamworks.Ugc.Editor.CreateCommunityFile()
#if DEBUG
                .WithPrivateVisibility()
#else
                .WithPublicVisibility()
#endif
                .ForAppId(AppID)
                .WithContent(dirPath);

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
        public static bool CreateWorkshopItemStaging(Steamworks.Ugc.Item? existingItem, out Steamworks.Ugc.Editor? itemEditor, out ContentPackage contentPackage)
        {
            if (!(existingItem?.IsInstalled ?? false))
            {
                itemEditor = null;
                contentPackage = null;
                DebugConsole.ThrowError("Cannot edit the workshop item \"" + (existingItem?.Title ?? "[NULL]") + "\" because it has not been installed.");
                return false;
            }

            itemEditor = new Steamworks.Ugc.Editor(existingItem.Value.Id)
                .ForAppId(AppID)
                .WithTitle(existingItem.Value.Title)
                .WithTags(existingItem.Value.Tags)
                .WithDescription(existingItem.Value.Description);

            if (existingItem.Value.IsPublic)
            {
                itemEditor = itemEditor?.WithPublicVisibility();
            }
            else if (existingItem.Value.IsFriendsOnly)
            {
                itemEditor = itemEditor?.WithFriendsOnlyVisibility();
            }
            else if (existingItem.Value.IsPrivate)
            {
                itemEditor = itemEditor?.WithPrivateVisibility();
            }

            if (!CheckWorkshopItemEnabled(existingItem))
            {
                if (!EnableWorkShopItem(existingItem, out string errorMsg))
                {
                    DebugConsole.NewMessage(errorMsg, Color.Red);
                    new GUIMessageBox(
                        TextManager.Get("Error"),
                        TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { existingItem?.Title, errorMsg }));
                    itemEditor = null;
                    contentPackage = null;
                    return false;
                }
            }

            ContentPackage tempContentPackage = new ContentPackage(Path.Combine(existingItem?.Directory, MetadataFileName)) { SteamWorkshopUrl = existingItem.Value.Url };
            string installedContentPackagePath = Path.GetFullPath(GetWorkshopItemContentPackagePath(tempContentPackage));
            contentPackage = ContentPackage.List.Find(cp => Path.GetFullPath(cp.Path) == installedContentPackagePath);

            itemEditor = itemEditor?.WithContent(Path.GetDirectoryName(installedContentPackagePath));

            string previewImagePath = Path.GetFullPath(Path.Combine(itemEditor?.ContentFolder.FullName, PreviewImageName));
            itemEditor = itemEditor?.WithPreviewFile(previewImagePath);

            try
            {
                if (File.Exists(previewImagePath)) { File.Delete(previewImagePath); }

                Uri baseAddress = new Uri(existingItem?.PreviewImageUrl);
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

        public class WorkshopPublishStatus
        {
            public CoroutineHandle Coroutine;
            public ContentPackage ContentPackage;
            public Steamworks.Ugc.Editor? Item;
            public bool? Success;
            public Steamworks.Ugc.PublishResult? Result;
            public TaskStatus? TaskStatus;
        }

        public static WorkshopPublishStatus StartPublishItem(ContentPackage contentPackage, Steamworks.Ugc.Editor? item)
        {
            if (!isInitialized) return null;

            if (string.IsNullOrEmpty(item?.Title))
            {
                DebugConsole.ThrowError("Cannot publish workshop item - title not set.");
                return null;
            }
            if (string.IsNullOrEmpty(item?.ContentFolder?.FullName))
            {
                DebugConsole.ThrowError("Cannot publish workshop item \"" + item?.Title + "\" - folder not set.");
                return null;
            }

#if DEBUG
            item = item?.WithPrivateVisibility();
#endif

            contentPackage.GameVersion = GameMain.Version;
            contentPackage.Save(contentPackage.Path);

            if (File.Exists(PreviewImageName)) { File.Delete(PreviewImageName); }
            //move the preview image out of the staging folder, it does not need to be included in the folder sent to Workshop
            File.Move(Path.GetFullPath(Path.Combine(item?.ContentFolder?.FullName, PreviewImageName)), PreviewImageName);
            item = item?.WithPreviewFile(Path.GetFullPath(PreviewImageName));

            var workshopPublishStatus = new WorkshopPublishStatus() { Item = item, Result = null, Success = null, ContentPackage = contentPackage };
            workshopPublishStatus.Coroutine = CoroutineManager.StartCoroutine(PublishItem(workshopPublishStatus));
            return workshopPublishStatus;
        }

        private static IEnumerable<object> PublishItem(WorkshopPublishStatus workshopPublishStatus)
        {
            if (!isInitialized)
            {
                yield return CoroutineStatus.Success;
            }

            var item = workshopPublishStatus.Item;
            var contentPackage = workshopPublishStatus.ContentPackage;

            Task<Steamworks.Ugc.PublishResult> task = item?.SubmitAsync();
            while (!task.IsCompleted)
            {
                yield return new WaitForSeconds(1.0f);
            }

            if (task.Status != TaskStatus.RanToCompletion)
            {
                workshopPublishStatus.Success = false;
                workshopPublishStatus.TaskStatus = task.Status;

                DebugConsole.NewMessage("Publishing workshop item " + item?.Title + " failed: task failed with status " + task.Status.ToString(), Color.Red);
            }
            else if (!task.Result.Success)
            {
                workshopPublishStatus.Success = false;
                workshopPublishStatus.Result = task.Result;
                DebugConsole.NewMessage("Publishing workshop item " + item?.Title + " failed: Workshop result "+task.Result.Result.ToString(), Color.Red);
            }
            else
            {
                workshopPublishStatus.Success = true;
                workshopPublishStatus.Result = task.Result;
                DebugConsole.NewMessage("Published workshop item " + item?.Title + " successfully.", Microsoft.Xna.Framework.Color.LightGreen);

                contentPackage.SteamWorkshopUrl = $"http://steamcommunity.com/sharedfiles/filedetails/?source=Facepunch.Steamworks&id={task.Result.FileId.Value}";
                //NOTE: This sets InstallTime one hour into the future to guarantee
                //that the published content package won't be autoupdated incorrectly.
                //Change if it causes issues.
                contentPackage.InstallTime = DateTime.UtcNow + TimeSpan.FromHours(1);
                contentPackage.Save(contentPackage.Path);

                SubscribeToWorkshopItem(task.Result.FileId);
            }

            yield return CoroutineStatus.Success;
        }

        /// <summary>
        /// Enables a workshop item by moving it to the game folder.
        /// </summary>
        public static bool EnableWorkShopItem(Steamworks.Ugc.Item? item, out string errorMsg, bool selectContentPackage = false, bool suppressInstallNotif = false)
        {
            if (!(item?.IsInstalled ?? false))
            {
                errorMsg = TextManager.GetWithVariable("WorkshopErrorInstallRequiredToEnable", "[itemname]", item?.Title ?? "[NULL]");
                DebugConsole.NewMessage(errorMsg, Color.Red);
                return false;
            }

            string metaDataFilePath = Path.Combine(item?.Directory, MetadataFileName);

            if (!File.Exists(metaDataFilePath))
            {
                errorMsg = TextManager.GetWithVariable("WorkshopErrorInstallRequiredToEnable", "[itemname]", item?.Title);
                DebugConsole.ThrowError(errorMsg);
                return false;
            }

            ContentPackage contentPackage = new ContentPackage(metaDataFilePath)
            {
                SteamWorkshopUrl = item?.Url
            };
            string newContentPackagePath = GetWorkshopItemContentPackagePath(contentPackage);

            List<ContentPackage> existingPackages = ContentPackage.List.Where(cp => cp.Path.CleanUpPath() == newContentPackagePath.CleanUpPath()).ToList();
            if (existingPackages.Any())
            {
                if (item?.Owner.Id != Steamworks.SteamClient.SteamId)
                {
                    errorMsg = TextManager.GetWithVariables("WorkshopErrorSamePathInstalled",
                        new string[] { "[itemname]", "[itempath]" },
                        new string[] { item?.Title, Path.GetDirectoryName(newContentPackagePath) });
                    return false;
                }
                else
                {
                    RemoveMods(cp => !string.IsNullOrWhiteSpace(cp.SteamWorkshopUrl) && cp.SteamWorkshopUrl == contentPackage.SteamWorkshopUrl,
                        false);
                }
            }

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

            Task<string> newTask = null;

            lock (modCopiesInProgress)
            {
                if (modCopiesInProgress.ContainsKey(item.Value.Id))
                {
                    errorMsg = ""; return true;
                }
                newTask = CopyWorkShopItemAsync(item, contentPackage, newContentPackagePath, metaDataFilePath);
                modCopiesInProgress.Add(item.Value.Id, newTask);
            }
            
            TaskPool.Add(newTask,
                contentPackage,
                (task, cp) =>
                {
                    try
                    {
                        if (task.IsFaulted || task.IsCanceled)
                        {
                            DebugConsole.ThrowError($"Failed to copy \"{item?.Title}\"", task.Exception);
                            GameMain.SteamWorkshopScreen?.SetReinstallButtonStatus(item, true, GUI.Style.Red);
                            return;
                        }
                        if (!string.IsNullOrWhiteSpace(task.Result))
                        {
                            DebugConsole.ThrowError($"Failed to copy \"{item?.Title}\": {task.Result}");
                            GameMain.SteamWorkshopScreen?.SetReinstallButtonStatus(item, true, GUI.Style.Red);
                            return;
                        }

                        GameMain.Config.SuppressModFolderWatcher = true;

                        var newPackage = new ContentPackage(cp.Path, newContentPackagePath)
                        {
                            SteamWorkshopUrl = item?.Url,
                            InstallTime = item?.Updated > item?.Created ? item?.Updated : item?.Created
                        };

                        foreach (ContentFile contentFile in newPackage.Files)
                        {
                            contentFile.Path = CorrectContentFilePath(contentFile.Path, contentFile.Type, cp, true);
                        }

                        foreach (ContentFile file in existingPackages.SelectMany(p => p.Files))
                        {
                            string path = CorrectContentFilePath(file.Path, file.Type, cp, true).CleanUpPath();
                            if (newPackage.Files.Any(f => f.Path.CleanUpPath() == path)) { continue; }
                            newPackage.AddFile(path, file.Type);
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
                                GameMain.Config.SelectCorePackage(newPackage);
                            }
                            else
                            {
                                GameMain.Config.SelectContentPackage(newPackage);
                            }
                            GameMain.Config.SaveNewPlayerConfig();

                            GameMain.Config.WarnIfContentPackageSelectionDirty();

                            if (newPackage.Files.Any(f => f.Type == ContentType.Submarine))
                            {
                                SubmarineInfo.RefreshSavedSubs();
                            }
                        }
                        else if (!suppressInstallNotif)
                        {
                            GameMain.MainMenuScreen?.SetEnableModsNotification(true);
                        }

                        GameMain.Config.SuppressModFolderWatcher = false;

                        GameMain.SteamWorkshopScreen?.SetReinstallButtonStatus(item, true, GUI.Style.Green);

                    }
                    catch
                    {
                        throw;
                    }
                    finally
                    {
                        modCopiesInProgress.Remove(item.Value.Id);
                    }
                });
            
            errorMsg = "";
            return true;
        }

        /// <summary>
        /// Asynchronously copies a Workshop item into the Mods folder.
        /// </summary>
        /// <returns>Returns an empty string on success, otherwise returns an error message.</returns>
        private async static Task<string> CopyWorkShopItemAsync(Steamworks.Ugc.Item? item, ContentPackage contentPackage, string newContentPackagePath, string metaDataFilePath)
        {
            await Task.Yield();

            string targetPath = Path.GetDirectoryName(GetWorkshopItemContentPackagePath(contentPackage));
            string copyingPath = Path.Combine(targetPath, CopyIndicatorFileName);

            string errorMsg = "";
            if (contentPackage.GameVersion > new Version(0, 9, 1, 0))
            {
                Directory.CreateDirectory(targetPath);
                File.WriteAllText(copyingPath, "TEMPORARY FILE");

                SaveUtil.CopyFolder(item?.Directory, targetPath, copySubDirs: true, overwriteExisting: false);

                File.Delete(copyingPath);
                return "";
            }

            var allPackageFiles = Directory.GetFiles(item?.Directory, "*", System.IO.SearchOption.AllDirectories);
            List<string> nonContentFiles = new List<string>();
            foreach (string file in allPackageFiles)
            {
                if (file == metaDataFilePath) { continue; }
                string relativePath = UpdaterUtil.GetRelativePath(file, item?.Directory);
                string fullPath = Path.GetFullPath(relativePath);
                if (contentPackage.Files.Any(f => { string fp = Path.GetFullPath(f.Path); return fp == fullPath; })) { continue; }
                nonContentFiles.Add(relativePath);
            }

            /*if (File.Exists(newContentPackagePath) && !CheckFileEquality(newContentPackagePath, metaDataFilePath))
            {
                errorMsg = TextManager.GetWithVariables("WorkshopErrorOverwriteOnEnable", new string[2] { "[itemname]", "[filename]" }, new string[2] { item?.Title, newContentPackagePath });
                DebugConsole.NewMessage(errorMsg, Color.Red);
                return errorMsg;
            }

            foreach (ContentFile contentFile in contentPackage.Files)
            {
                string sourceFile = Path.Combine(item?.Directory, contentFile.Path);

                if (File.Exists(sourceFile) && File.Exists(contentFile.Path) && !CheckFileEquality(sourceFile, contentFile.Path))
                {
                    errorMsg = TextManager.GetWithVariables("WorkshopErrorOverwriteOnEnable", new string[2] { "[itemname]", "[filename]" }, new string[2] { item?.Title, contentFile.Path });
                    DebugConsole.NewMessage(errorMsg, Color.Red);
                    return errorMsg;
                }
            }*/

            Directory.CreateDirectory(targetPath);
            File.WriteAllText(copyingPath, "TEMPORARY FILE");

            foreach (ContentFile contentFile in contentPackage.Files)
            {
                contentFile.Path = contentFile.Path.CleanUpPath();
                string sourceFile = Path.Combine(item?.Directory, contentFile.Path);
                if (!File.Exists(sourceFile))
                {
                    string[] splitPath = contentFile.Path.Split('/');
                    if (splitPath.Length >= 2 && splitPath[0] == "Mods")
                    {
                        sourceFile = Path.Combine(item?.Directory, string.Join("/", splitPath.Skip(2)));
                    }
                }

                contentFile.Path = CorrectContentFilePath(contentFile.Path, contentFile.Type, contentPackage,
                    contentFile.Type != ContentType.Submarine);

                //path not allowed -> the content file must be a reference to an external file (such as some vanilla file outside the Mods folder)
                if (!ContentPackage.IsModFilePathAllowed(contentFile))
                {
                    //the content package is trying to copy a file to a prohibited path, which is not allowed
                    if (File.Exists(sourceFile))
                    {
                        errorMsg = TextManager.GetWithVariable("WorkshopErrorIllegalPathOnEnable", "[filename]", contentFile.Path);
                        return errorMsg;
                    }
                    //not trying to copy anything, so this is a reference to an external file
                    //if the external file doesn't exist, we cannot enable the package
                    else if (!File.Exists(contentFile.Path))
                    {
                        errorMsg = TextManager.GetWithVariable("WorkshopErrorEnableFailed", "[itemname]", item?.Title) + " " + TextManager.GetWithVariable("WorkshopFileNotFound", "[path]", "\"" + contentFile.Path + "\"");
                        return errorMsg;
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
                        errorMsg = TextManager.GetWithVariable("WorkshopErrorEnableFailed", "[itemname]", item?.Title) + " " + TextManager.GetWithVariable("WorkshopFileNotFound", "[path]", "\"" + contentFile.Path + "\"");
                        return errorMsg;
                    }
                }

                //make sure the destination directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(contentFile.Path));
                CorrectContentFileCopy(contentPackage, sourceFile, contentFile.Path, overwrite: false);
            }

            foreach (string nonContentFile in nonContentFiles)
            {
                string sourceFile = Path.Combine(item?.Directory, nonContentFile);
                if (!File.Exists(sourceFile)) { continue; }
                string destinationPath = CorrectContentFilePath(nonContentFile, ContentType.None, contentPackage, false);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                CorrectContentFileCopy(contentPackage, sourceFile, destinationPath, overwrite: false);
            }

            File.Delete(copyingPath);
            return "";
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

        private static void RemoveMods(Func<ContentPackage, bool> predicate, bool delete = true)
        {
            var toRemove = ContentPackage.List.Where(predicate).ToList();
            var packagesToDeselect = GameMain.Config.SelectedContentPackages.Where(p => toRemove.Contains(p)).ToList();
            foreach (var cp in packagesToDeselect)
            {
                if (cp.CorePackage)
                {
                    GameMain.Config.SelectCorePackage(ContentPackage.List.Find(cpp => cpp.CorePackage && !toRemove.Contains(cpp)));
                }
                else
                {
                    GameMain.Config.DeselectContentPackage(cp);
                }
            }

            if (delete)
            {
                foreach (var cp in toRemove)
                {
                    try
                    {
                        string path = Path.GetDirectoryName(cp.Path);
                        if (Directory.Exists(path)) { Directory.Delete(path, true); }
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError($"An error occurred while attempting to delete {Path.GetDirectoryName(cp.Path)}", e);
                    }
                }
            }

            ContentPackage.List.RemoveAll(cp => toRemove.Contains(cp));
            GameMain.Config.SelectedContentPackages.RemoveAll(cp => !ContentPackage.List.Contains(cp));

            ContentPackage.SortContentPackages();
            GameMain.Config.SaveNewPlayerConfig();

            GameMain.Config.WarnIfContentPackageSelectionDirty();
        }

        /// <summary>
        /// Disables a workshop item by removing the files from the game folder.
        /// </summary>
        public static bool DisableWorkShopItem(Steamworks.Ugc.Item? item, bool noLog, out string errorMsg)
        {
            errorMsg = null;
            if (!(item?.IsInstalled ?? false))
            {
                errorMsg = "Cannot disable workshop item \"" + item?.Title + "\" because it has not been installed.";
                if (!noLog)
                {
                    DebugConsole.NewMessage(errorMsg, Color.Red);
                }
                return false;
            }

            ContentPackage contentPackage = new ContentPackage(Path.Combine(item?.Directory, MetadataFileName))
            {
                SteamWorkshopUrl = item?.Url
            };

            GameMain.Config.SuppressModFolderWatcher = true;
            try
            {
                RemoveMods(cp => !string.IsNullOrWhiteSpace(cp.SteamWorkshopUrl) && cp.SteamWorkshopUrl == contentPackage.SteamWorkshopUrl);
            }
            catch (Exception e)
            {
                errorMsg = "Disabling the workshop item \"" + item?.Title + "\" failed. " + e.Message + "\n" + e.StackTrace;
                if (!noLog)
                {
                    DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                }
                return false;
            }
            GameMain.Config.SuppressModFolderWatcher = false;

            GameMain.SteamWorkshopScreen?.SetReinstallButtonStatus(item, false, null);

            errorMsg = "";
            return true;
        }

        /// <summary>
        /// Is the item compatible with this version of Barotrauma. Returns null if compatibility couldn't be determined (item not installed)
        /// </summary>
        public static bool? CheckWorkshopItemCompatibility(Steamworks.Ugc.Item? item)
        {
            if (!(item?.IsInstalled ?? false)) { return null; }

            string metaDataPath = Path.Combine(item?.Directory, MetadataFileName);
            if (!File.Exists(metaDataPath))
            {
                throw new System.IO.FileNotFoundException("Metadata file for the Workshop item \"" + item?.Title + "\" not found. The file may be corrupted.");
            }

            ContentPackage contentPackage = new ContentPackage(metaDataPath);
            return contentPackage.IsCompatible();
        }

        public static bool CheckWorkshopItemEnabled(Steamworks.Ugc.Item? item)
        {
            if (!(item?.IsInstalled ?? false)) { return false; }

            if (!Directory.Exists(item?.Directory))
            {
                DebugConsole.ThrowError("Workshop item \"" + item?.Title + "\" has been installed but the install directory cannot be found. Attempting to redownload...");
                item?.Download();
                return false;                
            }

            string metaDataPath = "";
            try
            {
                metaDataPath = Path.Combine(item?.Directory, MetadataFileName);
            }
            catch (ArgumentException)
            {
                string errorMessage = "Metadata file for the Workshop item \"" + item?.Title +
                    "\" not found. Could not combine path (" + (item?.Directory ?? "directory name empty") + ").";
                DebugConsole.ThrowError(errorMessage);
                GameAnalyticsManager.AddErrorEventOnce("SteamManager.CheckWorkshopItemEnabled:PathCombineException" + item?.Title,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMessage);
                return false;
            }

            if (!File.Exists(metaDataPath))
            {
                DebugConsole.ThrowError("Metadata file for the Workshop item \"" + item?.Title + "\" not found. The file may be corrupted.");
                return false;
            }

            ContentPackage contentPackage = new ContentPackage(metaDataPath)
            {
                SteamWorkshopUrl = item?.Url
            };
            //make sure the contentpackage file is present 
            if (!File.Exists(GetWorkshopItemContentPackagePath(contentPackage)) ||
                !ContentPackage.List.Any(cp => cp.SteamWorkshopUrl == contentPackage.SteamWorkshopUrl ||
                                               (string.IsNullOrWhiteSpace(cp.SteamWorkshopUrl) && cp.Name == contentPackage.Name)))
            {
                return false;
            }

            return true;
        }

        public static bool CheckWorkshopItemUpToDate(Steamworks.Ugc.Item? item)
        {
            if (!(item?.IsInstalled ?? false)) return false;

            string metaDataPath = Path.Combine(item?.Directory, MetadataFileName);
            if (!File.Exists(metaDataPath))
            {
                DebugConsole.ThrowError("Metadata file for the Workshop item \"" + item?.Title + "\" not found. The file may be corrupted.");
                return false;
            }

            ContentPackage steamPackage = new ContentPackage(metaDataPath)
            {
                SteamWorkshopUrl = item?.Url
            };
            ContentPackage myPackage = ContentPackage.List.Find(cp => cp.SteamWorkshopUrl == steamPackage.SteamWorkshopUrl);

            if (myPackage?.InstallTime == null)
            {
                return false;
            }
            DateTime latestTime = item.Value.Updated > item.Value.Created ? item.Value.Updated : item.Value.Created;
            bool upToDate = latestTime <= myPackage.InstallTime.Value;
            return upToDate;
        }

        public static async Task<bool> AutoUpdateWorkshopItemsAsync()
        {
            await Task.Yield();

            if (!isInitialized) { return false; }

            var query = new Steamworks.Ugc.Query(Steamworks.UgcType.All)
                .WhereUserSubscribed()
                .WithLongDescription();

            List<Steamworks.Ugc.Item> items = await GetWorkshopItemsAsync(query);

            GameMain.Config.SuppressModFolderWatcher = true;

            //remove mods that the player is no longer subscribed to
            RemoveMods(cp => !string.IsNullOrWhiteSpace(cp.SteamWorkshopUrl) && !items.Any(it => it.Id == GetWorkshopItemIDFromUrl(cp.SteamWorkshopUrl)));

            GameMain.Config.SuppressModFolderWatcher = false;


            List<string> updateNotifications = new List<string>();
            foreach (var item in items)
            {
                try
                {
                    if (!item.IsInstalled) { continue; }

                    bool installedSuccessfully = false;
                    string errorMsg;
                    if (!CheckWorkshopItemEnabled(item))
                    {
                        installedSuccessfully = EnableWorkShopItem(item, out errorMsg);
                    }
                    else if (!CheckWorkshopItemUpToDate(item))
                    {
                        installedSuccessfully = UpdateWorkshopItem(item, out errorMsg);
                    }
                    else
                    {
                        continue;
                    }

                    if (!installedSuccessfully)
                    {
                        CrossThread.RequestExecutionOnMainThread(() =>
                        {
                            DebugConsole.NewMessage(errorMsg, Color.Red);
                            string errorId = errorMsg;
                            if (!GUIMessageBox.MessageBoxes.Any(m => m.UserData as string == errorId))
                            {
                                new GUIMessageBox(
                                    TextManager.Get("Error"),
                                    TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { item.Title, errorMsg }))
                                {
                                    UserData = errorId
                                };
                            }
                        });
                    }
                    else
                    {
                        updateNotifications.Add(TextManager.GetWithVariable("WorkshopItemUpdated", "[itemname]", item.Title));
                    }
                }
                catch (Exception e)
                {
                    CrossThread.RequestExecutionOnMainThread(() =>
                    {
                        string errorId = e.Message;
                        if (!GUIMessageBox.MessageBoxes.Any(m => m.UserData as string == errorId))
                        {
                            new GUIMessageBox(
                                TextManager.Get("Error"),
                                TextManager.GetWithVariables("WorkshopItemUpdateFailed", new string[2] { "[itemname]", "[errormessage]" }, new string[2] { item.Title, e.Message + ", " + e.TargetSite }))
                            {
                                UserData = errorId
                            };
                        }
                        GameAnalyticsManager.AddErrorEventOnce(
                            "SteamManager.AutoUpdateWorkshopItems:" + e.Message,
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                            "Failed to autoupdate workshop item \"" + item.Title + "\". " + e.Message + "\n" + e.StackTrace);
                    });
                }
            }

            if (updateNotifications.Count > 0)
            {
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    while (updateNotifications.Count > 0)
                    {
                        float width = updateNotifications.Max(notif => GUI.Font.MeasureString(notif).X) * 1.25f;

                        int notificationsPerMsgBox = 20;
                        new GUIMessageBox("", string.Join('\n', updateNotifications.Take(notificationsPerMsgBox)),
                            relativeSize: new Microsoft.Xna.Framework.Vector2(0.25f, 0.0f),
                            minSize: new Microsoft.Xna.Framework.Point((int)width, 0));
                        updateNotifications.RemoveRange(0, Math.Min(notificationsPerMsgBox, updateNotifications.Count));
                    }
                });
            }

            List<Task> tasks;
            lock (modCopiesInProgress)
            {
                tasks = modCopiesInProgress.Values.ToList();
            }
            await Task.WhenAll(tasks);

            return true;
        }

        public static bool UpdateWorkshopItem(Steamworks.Ugc.Item? item, out string errorMsg)
        {
            errorMsg = "";
            if (!(item?.IsInstalled ?? false)) { return false; }
            bool reenable = GameMain.Config.SelectedContentPackages.Any(p => !string.IsNullOrEmpty(p.SteamWorkshopUrl) && GetWorkshopItemIDFromUrl(p.SteamWorkshopUrl) == item?.Id);
            if (item?.Owner.Id != Steamworks.SteamClient.SteamId)
            {
                if (!DisableWorkShopItem(item, false, out errorMsg)) { return false; }
            }
            if (!EnableWorkShopItem(item, errorMsg: out errorMsg, selectContentPackage: reenable)) { return false; }
            return true;
        }

        private static string GetWorkshopItemContentPackagePath(ContentPackage contentPackage)
        {
            string packageName = contentPackage.Name.Trim();
            packageName = ToolBox.RemoveInvalidFileNameChars(packageName);
            while (packageName.Last() == '.') { packageName = packageName.Substring(0, packageName.Length-1); }
            //packageName = packageName + "_" + GetWorkshopItemIDFromUrl(contentPackage.SteamWorkshopUrl);

            return Path.Combine("Mods", packageName, MetadataFileName);
        }

        private static void CorrectXMLFilePaths(ContentPackage package, XElement element)
        {
            foreach (var attr in element.Attributes())
            {
                if ((attr.Name.ToString() == "file" ||
                    attr.Name.ToString() == "folder" ||
                    attr.Name.ToString() == "texture" ||
                    attr.Name.ToString() == "monsterfile" ||
                    attr.Name.ToString() == "characterfile") &&
                    attr.Value.CleanUpPath().Contains("/"))
                {
                    ContentType type = ContentType.None;
                    Enum.TryParse(attr.Name.LocalName, true, out type);
                    attr.Value = CorrectContentFilePath(attr.Value, type, package, true);
                }
            }

            foreach (var child in element.Elements())
            {
                CorrectXMLFilePaths(package, child);
            }
        }

        private static void CorrectContentFileCopy(ContentPackage package, string src, string dest, bool overwrite)
        {
            if (!overwrite && File.Exists(dest)) { return; }

            if (Path.GetExtension(src).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                XDocument doc = XMLExtensions.TryLoadXml(src);
                if (doc != null)
                {
                    CorrectXMLFilePaths(package, doc.Root);
                    using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
                    {
                        System.Xml.XmlWriterSettings settings = new System.Xml.XmlWriterSettings();
                        settings.Indent = true;
                        settings.Encoding = new System.Text.UTF8Encoding(false);
                        using (var xmlWriter = System.Xml.XmlWriter.Create(stream, settings))
                        {
                            doc.WriteTo(xmlWriter);
                            xmlWriter.Flush();
                            string contents = System.Text.Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n");
                            File.WriteAllText(dest, contents, System.Text.Encoding.UTF8);
                        }
                    }
                }
                else
                {
                    File.Copy(src, dest, overwrite: true);
                }
            }
            else
            {
                File.Copy(src, dest, overwrite: true);
            }
        }

        private static string CorrectContentFilePath(string contentFilePath, ContentType type, ContentPackage package, bool checkIfFileExists = false)
        {
            string packageName = Path.GetDirectoryName(GetWorkshopItemContentPackagePath(package));

            contentFilePath = contentFilePath.CleanUpPathCrossPlatform();

            if (checkIfFileExists)
            {
                bool exists = File.Exists(contentFilePath);
                if (type == ContentType.Executable ||
                    type == ContentType.ServerExecutable)
                {
                    exists |= File.Exists(contentFilePath + ".dll");
                }
                return contentFilePath;
            }

            string[] splitPath = contentFilePath.Split('/');
            if (splitPath.Length < 2 || splitPath[0] != "Mods" || splitPath[1] != packageName)
            {
                string newPath;
                if (splitPath.Length >= 2 && splitPath[0] == "Mods")
                {
                    if (checkIfFileExists)
                    {
                        ContentPackage otherContentPackage = ContentPackage.List.Find(cp => cp.Name.Equals(splitPath[1], StringComparison.OrdinalIgnoreCase));
                        if (otherContentPackage != null)
                        {
                            string otherPackageName = Path.GetDirectoryName(otherContentPackage.Path);
                            newPath = Path.Combine(otherPackageName, string.Join("/", splitPath.Skip(2)));
                            if (File.Exists(newPath))
                            {
                                contentFilePath = newPath;
                                return contentFilePath;
                            }
                        }
                    }
                    splitPath = splitPath.Skip(Math.Clamp(splitPath.Length-1, 0, 2)).ToArray();
                    newPath = Path.Combine(packageName, string.Join("/", splitPath));
                }
                else
                {
                    newPath = Path.Combine(packageName, contentFilePath);
                }
                contentFilePath = newPath;
            }

            return contentFilePath.CleanUpPathCrossPlatform(false);
        }

#endregion

    }
}
