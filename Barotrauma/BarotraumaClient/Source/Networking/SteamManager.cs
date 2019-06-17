using Barotrauma.Networking;
using Facepunch.Steamworks;
using RestSharp;
using RestSharp.Extensions.MonoHttp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
        private static List<string> initializationErrors = new List<string>();
        public static IEnumerable<string> InitializationErrors
        {
            get { return initializationErrors; }
        }

        private SteamManager()
        {
            try
            {
                client = new Facepunch.Steamworks.Client(AppID);
                isInitialized = client.IsSubscribed && client.IsValid;

                if (isInitialized)
                {
                    DebugConsole.Log("Logged in as " + client.Username + " (SteamID " + client.SteamId + ")");
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

                    Facepunch.Steamworks.Client.Instance.Dispose();
                }
                catch (Exception e)
                {
                    if (GameSettings.VerboseLogging) DebugConsole.ThrowError("Disposing Steam client failed.", e);
                }
            }
        }

        public static ulong GetSteamID()
        {
            if (instance == null || !instance.isInitialized)
            {
                return 0;
            }
            return instance.client.SteamId;
        }

        public static string GetUsername()
        {
            if (instance == null || !instance.isInitialized)
            {
                return "";
            }
            return instance.client.Username;
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
            query.OnUpdate += () => { UpdateServerQuery(query, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
            query.OnFinished = onFinished;

            var localQuery = instance.client.ServerList.Local(filter);
            localQuery.OnUpdate += () => { UpdateServerQuery(localQuery, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
            localQuery.OnFinished = onFinished;

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
            query.OnUpdate += () => { UpdateServerQuery(query, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
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
            query.OnUpdate += () => { UpdateServerQuery(query, onServerFound, onServerRulesReceived, includeUnresponsive: true); };
            query.OnFinished = onFinished;

            return true;
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
                var serverInfo = new ServerInfo()
                {
                    ServerName = s.Name,
                    Port = s.ConnectionPort.ToString(),
                    IP = s.Address.ToString(),
                    PlayerCount = s.Players,
                    MaxPlayers = s.MaxPlayers,
                    HasPassword = s.Passworded,
                    RespondedToSteamQuery = responded
                };
                serverInfo.PingChecked = true;
                serverInfo.Ping = s.Ping;
                if (responded)
                {
                    s.FetchRules();
                }
                s.OnReceivedRules += (bool rulesReceived) =>
                {
                    if (!rulesReceived || s.Rules == null) { return; }

                    if (s.Rules.ContainsKey("message")) serverInfo.ServerMessage = s.Rules["message"];
                    if (s.Rules.ContainsKey("version")) serverInfo.GameVersion = s.Rules["version"];

                    if (s.Rules.ContainsKey("playercount"))
                    {
                       if (int.TryParse(s.Rules["playercount"], out int playerCount)) serverInfo.PlayerCount = playerCount;
                    }

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
                    if (s.Rules.ContainsKey("traitors"))
                    {
                        if (Enum.TryParse(s.Rules["traitors"], out YesNoMaybe traitorsEnabled)) serverInfo.TraitorsEnabled = traitorsEnabled;
                    }

                    if (s.Rules.ContainsKey("gamestarted")) serverInfo.GameStarted = s.Rules["gamestarted"] == "True";

                    if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count ||
                        serverInfo.ContentPackageHashes.Count != serverInfo.ContentPackageWorkshopUrls.Count)
                    {
                        //invalid contentpackage info
                        serverInfo.ContentPackageNames.Clear();
                        serverInfo.ContentPackageHashes.Clear();
                    }
                    onServerRulesReceived(serverInfo);
                };

                onServerFound(serverInfo);
            }
            query.Responded.Clear();
        }

        private static bool ValidateServerInfo(ServerList.Server server)
        {
            if (string.IsNullOrEmpty(server.Name)) { return false; }
            if (server.Address == null) { return false; }

            return true;
        }

        public static Auth.Ticket GetAuthSessionTicket()
        {
            if (instance == null || !instance.isInitialized)
            {
                return null;
            }

            return instance.client.Auth.GetAuthSessionTicket();
        }

        #endregion

        #region Workshop

        public const string WorkshopItemStagingFolder = "NewWorkshopItem";
        public const string WorkshopItemPreviewImageFolder = "Workshop";
        public const string PreviewImageName = "PreviewImage.png";
        private const string MetadataFileName = "filelist.xml";
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
        
        /// <summary>
        /// Creates a new folder, copies the specified files there and creates a metadata file with install instructions.
        /// </summary>
        public static void CreateWorkshopItemStaging(List<ContentFile> contentFiles, out Workshop.Editor itemEditor, out ContentPackage contentPackage)
        {
            var stagingFolder = new DirectoryInfo(WorkshopItemStagingFolder);
            if (stagingFolder.Exists)
            {
                SaveUtil.ClearFolder(stagingFolder.FullName);
            }
            else
            {
                stagingFolder.Create();
            }
            Directory.CreateDirectory(Path.Combine(WorkshopItemStagingFolder, "Submarines"));
            Directory.CreateDirectory(Path.Combine(WorkshopItemStagingFolder, "Mods"));
            Directory.CreateDirectory(Path.Combine(WorkshopItemStagingFolder, "Mods", "ModName"));

            itemEditor = instance.client.Workshop.CreateItem(Workshop.ItemType.Community);
            itemEditor.Visibility = Workshop.Editor.VisibilityType.Public;
            itemEditor.WorkshopUploadAppId = AppID;
            itemEditor.Folder = stagingFolder.FullName;

            string previewImagePath = Path.GetFullPath(Path.Combine(itemEditor.Folder, PreviewImageName));
            File.Copy("Content/DefaultWorkshopPreviewImage.png", previewImagePath);

            //copy content files to the staging folder
            List<string> copiedFilePaths = new List<string>();
            foreach (ContentFile file in contentFiles)
            {
                string relativePath = UpdaterUtil.GetRelativePath(Path.GetFullPath(file.Path), Environment.CurrentDirectory);
                string destinationPath = Path.Combine(stagingFolder.FullName, relativePath);
                //make sure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.Copy(file.Path, destinationPath);
                copiedFilePaths.Add(destinationPath);
            }
            System.Diagnostics.Debug.Assert(copiedFilePaths.Count == contentFiles.Count);

            //create a new content package and include the copied files in it
            contentPackage = ContentPackage.CreatePackage("ContentPackage", Path.Combine(itemEditor.Folder, MetadataFileName), false);
            for (int i = 0; i < copiedFilePaths.Count; i++)
            {
                contentPackage.AddFile(copiedFilePaths[i], contentFiles[i].Type);
            }

            contentPackage.Save(Path.Combine(stagingFolder.FullName, MetadataFileName));
        }

        /// <summary>
        /// Creates a copy of the specified workshop item in the staging folder and an editor that can be used to edit and update the item
        /// </summary>
        public static void CreateWorkshopItemStaging(Workshop.Item existingItem, out Workshop.Editor itemEditor, out ContentPackage contentPackage)
        {
            if (!existingItem.Installed)
            {
                itemEditor = null;
                contentPackage = null;
                DebugConsole.ThrowError("Cannot edit the workshop item \"" + existingItem.Title + "\" because it has not been installed.");
                return;
            }

            var stagingFolder = new DirectoryInfo(WorkshopItemStagingFolder);
            if (stagingFolder.Exists)
            {
                SaveUtil.ClearFolder(stagingFolder.FullName);
            }
            else
            {
                stagingFolder.Create();
            }

            itemEditor = instance.client.Workshop.EditItem(existingItem.Id);
            itemEditor.Visibility = Workshop.Editor.VisibilityType.Public;
            itemEditor.Title = existingItem.Title;
            itemEditor.Tags = existingItem.Tags.ToList();
            itemEditor.Description = existingItem.Description;
            itemEditor.WorkshopUploadAppId = AppID;
            itemEditor.Folder = stagingFolder.FullName;

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

            ContentPackage tempContentPackage = new ContentPackage(Path.Combine(existingItem.Directory.FullName, MetadataFileName));
            //item already installed, copy it from the game folder
            if (existingItem != null && CheckWorkshopItemEnabled(existingItem, checkContentFiles: false))
            {
                string installedItemPath = GetWorkshopItemContentPackagePath(tempContentPackage);
                if (File.Exists(installedItemPath))
                {
                    tempContentPackage = new ContentPackage(installedItemPath);
                }
            }
            if (File.Exists(tempContentPackage.Path))
            {
                string newContentPackagePath = Path.Combine(WorkshopItemStagingFolder, MetadataFileName);
                File.Copy(tempContentPackage.Path, newContentPackagePath, overwrite: true);
                contentPackage = new ContentPackage(newContentPackagePath);
                foreach (ContentFile contentFile in tempContentPackage.Files)
                {
                    string sourceFile;
                    if (contentFile.Type == ContentType.Submarine && File.Exists(contentFile.Path))
                    {
                        sourceFile = contentFile.Path;
                    }
                    else
                    {
                        sourceFile = Path.Combine(existingItem.Directory.FullName, contentFile.Path);
                    }
                    if (!File.Exists(sourceFile)) { continue; }
                    //make sure the destination directory exists
                    string destinationPath = Path.Combine(WorkshopItemStagingFolder, contentFile.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.Copy(sourceFile, destinationPath, overwrite: true);
                    contentPackage.AddFile(contentFile.Path, contentFile.Type);
                }
            }
            else
            {
                contentPackage = ContentPackage.CreatePackage(existingItem.Title, Path.Combine(WorkshopItemStagingFolder, MetadataFileName), false);
                contentPackage.Save(contentPackage.Path);
            }
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

            contentPackage.Name = item.Title;
            contentPackage.GameVersion = GameMain.Version;
            contentPackage.Save(Path.Combine(WorkshopItemStagingFolder, MetadataFileName));

            if (File.Exists(PreviewImageName)) File.Delete(PreviewImageName);
            //move the preview image out of the staging folder, it does not need to be included in the folder sent to Workshop
            File.Move(Path.GetFullPath(Path.Combine(item.Folder, PreviewImageName)), PreviewImageName);
            item.PreviewImage = Path.GetFullPath(PreviewImageName);

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
            }
            else
            {
                DebugConsole.NewMessage("Publishing workshop item " + item.Title + " failed. " + item.Error, Microsoft.Xna.Framework.Color.Red);
            }

            SaveUtil.ClearFolder(WorkshopItemStagingFolder);
            File.Delete(PreviewImageName);
            try
            {
                Directory.Delete(WorkshopItemStagingFolder);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to delete Workshop item staging folder.", e);
            }

            yield return CoroutineStatus.Success;
        }

        /// <summary>
        /// Enables a workshop item by moving it to the game folder.
        /// </summary>
        public static bool EnableWorkShopItem(Workshop.Item item, bool allowFileOverwrite, out string errorMsg)
        {
            if (!item.Installed)
            {
                errorMsg = TextManager.GetWithVariable("WorkshopErrorInstallRequiredToEnable", "[itemname]", item.Title);
                DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                return false;
            }

            string metaDataFilePath = Path.Combine(item.Directory.FullName, MetadataFileName);
            ContentPackage contentPackage = new ContentPackage(metaDataFilePath);
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
                    string sourceFile = Path.Combine(item.Directory.FullName, contentFile.Path);

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

            var newPackage = new ContentPackage(contentPackage.Path, newContentPackagePath)
            {
                SteamWorkshopUrl = item.Url,
                InstallTime = item.Modified > item.Created ? item.Modified : item.Created
            };
            newPackage.Save(newContentPackagePath);
            ContentPackage.List.Add(newPackage);
            if (newPackage.CorePackage)
            {
                //if enabling a core package, disable all other core packages
                GameMain.Config.SelectedContentPackages.RemoveWhere(cp => cp.CorePackage);
            }
            GameMain.Config.SelectedContentPackages.Add(newPackage);
            GameMain.Config.SaveNewPlayerConfig();

            if (newPackage.Files.Any(f => f.Type == ContentType.Submarine))
            {
                Submarine.RefreshSavedSubs();
            }

            errorMsg = "";
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

            bool wasSub = contentPackage.Files.Any(f => f.Type == ContentType.Submarine);

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
                GameMain.Config.SelectedContentPackages.RemoveWhere(cp => !ContentPackage.List.Contains(cp));
                GameMain.Config.SaveNewPlayerConfig();
            }
            catch (Exception e)
            {
                errorMsg = "Disabling the workshop item \"" + item.Title + "\" failed. " + e.Message;
                DebugConsole.NewMessage(errorMsg, Microsoft.Xna.Framework.Color.Red);
                return false;
            }

            if (wasSub)
            {
                Submarine.RefreshSavedSubs();
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
            if (!item.Installed) return false;

            string metaDataPath = Path.Combine(item.Directory.FullName, MetadataFileName);
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
                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    if (!File.Exists(contentFile.Path)) { return false; }
                }
            }

            return true;
        }

        public static bool CheckWorkshopItemUpToDate(Workshop.Item item)
        {
            if (!item.Installed) return false;

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
                    if (item.Installed && CheckWorkshopItemEnabled(item) && !CheckWorkshopItemUpToDate(item))
                    {
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
            if (!DisableWorkShopItem(item, out errorMsg)) { return false; }
            if (!EnableWorkShopItem(item, allowFileOverwrite: false, errorMsg: out errorMsg)) { return false; }

            return true;
        }

        public static string GetWorkshopItemContentPackagePath(ContentPackage contentPackage)
        {
            string fileName = contentPackage.Name + ".xml";
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalidChars) fileName = fileName.Replace(c.ToString(), "");
            return Path.Combine("Data", "ContentPackages", fileName);
        }

        #endregion

    }
}
