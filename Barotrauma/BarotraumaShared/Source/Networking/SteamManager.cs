using Barotrauma.Networking;
using Facepunch.Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
#if DEBUG
        public static bool USE_STEAM
        {
            get { return GameMain.Config.UseSteam; }
        }
#else
        //cannot enable/disable steam in release builds
        public const bool USE_STEAM = true;
#endif

        const uint AppID = 602960;
        
        private Facepunch.Steamworks.Client client;
        private Server server;

        private static SteamManager instance;
        public static SteamManager Instance
        {
            get
            {
                if (instance == null) instance = new SteamManager();
                return instance;
            }
        }
                        
        private bool isInitialized;
        public static bool IsInitialized
        {
            get
            {
                return Instance.isInitialized;
            }
        }
        
        public static void Initialize()
        {
            if (!USE_STEAM) return;
            instance = new SteamManager();
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
            catch (DllNotFoundException e)
            {
                isInitialized = false;
#if CLIENT
                new Barotrauma.GUIMessageBox(TextManager.Get("Error"), TextManager.Get("SteamDllNotFound"));
#else
                DebugConsole.ThrowError("Initializing Steam client failed (steam_api64.dll not found).", e);
#endif
            }
            catch (Exception e)
            {
                isInitialized = false;
#if CLIENT
                new Barotrauma.GUIMessageBox(TextManager.Get("Error"), TextManager.Get("SteamClientInitFailed"));
#else
                DebugConsole.ThrowError("Initializing Steam client failed.", e);
#endif
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
        
        public static bool UnlockAchievement(string achievementName)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            DebugConsole.Log("Unlocked achievement \"" + achievementName + "\"");
            return instance.client.Achievements.Trigger(achievementName);
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

            var query = instance.client.ServerList.Internet(filter);
            query.OnUpdate += () => { UpdateServerQuery(query, onServerFound, onServerRulesReceived); };
            query.OnFinished = onFinished;

            var localQuery = instance.client.ServerList.Local(filter);
            localQuery.OnUpdate += () => { UpdateServerQuery(localQuery, onServerFound, onServerRulesReceived); };
            localQuery.OnFinished = onFinished;

            return true;
        }

        private static void UpdateServerQuery(ServerList.Request query, Action<Networking.ServerInfo> onServerFound, Action<Networking.ServerInfo> onServerRulesReceived)
        {
            foreach (ServerList.Server s in query.Responded)
            {
                DebugConsole.Log(s.Name + " responded to server query.");
                var serverInfo = new Networking.ServerInfo()
                {
                    ServerName = s.Name,
                    Port = s.ConnectionPort.ToString(),
                    IP = s.Address.ToString(),
                    PlayerCount = s.Players,
                    MaxPlayers = s.MaxPlayers,
                    HasPassword = s.Passworded,
                };
                serverInfo.PingChecked = true;
                serverInfo.Ping = s.Ping;
                s.FetchRules();
                s.OnReceivedRules += (_) =>
                {
                    if (s.Rules.ContainsKey("message")) serverInfo.ServerMessage = s.Rules["message"];
                    if (s.Rules.ContainsKey("version")) serverInfo.GameVersion = s.Rules["version"];
                    if (s.Rules.ContainsKey("contentpackage")) serverInfo.ContentPackageNames.AddRange(s.Rules["contentpackage"].Split(','));
                    if (s.Rules.ContainsKey("contentpackagehash")) serverInfo.ContentPackageHashes.AddRange(s.Rules["contentpackagehash"].Split(','));
                    if (s.Rules.ContainsKey("usingwhitelist")) serverInfo.UsingWhiteList = s.Rules["usingwhitelist"]=="True";
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
                    if (s.Rules.ContainsKey("traitors"))
                    {
                        if (Enum.TryParse(s.Rules["traitors"], out YesNoMaybe traitorsEnabled)) serverInfo.TraitorsEnabled = traitorsEnabled;
                    }

                    if (serverInfo.ContentPackageNames.Count != serverInfo.ContentPackageHashes.Count)
                    {
                        //invalid contentpackage info
                        serverInfo.ContentPackageNames.Clear();
                        serverInfo.ContentPackageHashes.Clear();
                    }
                    onServerRulesReceived(serverInfo);
                };

                onServerFound(serverInfo);
            }
            foreach (ServerList.Server s in query.Unresponsive)
            {
                DebugConsole.Log(s.Name + " did not respond to server query.");
            }
            query.Responded.Clear();
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
        const string MetadataFileName = "metadata.xml";
        const string PreviewImageName = "PreviewImage.png";

        public static void GetWorkshopItems(Action<IList<Workshop.Item>> onItemsFound, List<string> requireTags = null)
        {
            if (instance == null || !instance.isInitialized) return;

            var query = instance.client.Workshop.CreateQuery();
            query.Order = Workshop.Order.RankedByTrend;
            query.UploaderAppId = AppID;
            if (requireTags != null) query.RequireTags = requireTags;

            query.Run();
            query.OnResult += (Workshop.Query q) =>
            {
                onItemsFound?.Invoke(q.Items);
            };
        }

        /// <summary>
        /// Moves a workshop item from the download folder to the game folder and makes it usable in-game
        /// </summary>
        private void EnableWorkshopItem(Workshop.Item item)
        {
            if (!item.Installed)
            {
                DebugConsole.ThrowError("Cannot enable workshop item \"" + item.Title + "\" because it has not been installed.");
                return;
            }
        }
        
        public static void SaveToWorkshop(Submarine sub)
        {
            if (instance == null || !instance.isInitialized) return;
            
            Workshop.Editor item;
            ContentPackage contentPackage;
            try
            {
                CreateWorkshopItemStaging(
                    new List<ContentFile>() { new ContentFile(sub.FilePath, ContentType.None) }, 
                    out item, out contentPackage);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Creating the workshop item failed.", e);
                return;
            }

            item.Description = sub.Description;
            item.Title = sub.Name;
            item.Tags.Add("Submarine");

            string subPreviewPath =  Path.GetFullPath(Path.Combine(item.Folder, PreviewImageName));
#if CLIENT
            try
            {
                using (Stream s = File.Create(subPreviewPath))
                {
                    sub.PreviewImage.Texture.SaveAsPng(s, (int)sub.PreviewImage.size.X, (int)sub.PreviewImage.size.Y);
                    item.PreviewImage = subPreviewPath;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Saving submarine preview image failed.", e);
                item.PreviewImage = null;
            }
#endif

            StartPublishItem(contentPackage, item);
        }

        /// <summary>
        /// Creates a new folder, copies the specified files there and creates a metadata file with install instructions.
        /// </summary>
        public static void CreateWorkshopItemStaging(List<ContentFile> contentFiles, out Workshop.Editor item, out ContentPackage contentPackage)
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

            item = instance.client.Workshop.CreateItem(Workshop.ItemType.Community);
            item.Visibility = Workshop.Editor.VisibilityType.Public;
            item.WorkshopUploadAppId = AppID;
            item.Folder = stagingFolder.FullName;

            string previewImagePath = Path.GetFullPath(Path.Combine(item.Folder, PreviewImageName));
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
            contentPackage = ContentPackage.CreatePackage("ContentPackage", Path.Combine(item.Folder, MetadataFileName));
            for (int i = 0; i < copiedFilePaths.Count; i++)
            {
                contentPackage.AddFile(copiedFilePaths[i], contentFiles[i].Type);
            }

            contentPackage.Save(Path.Combine(stagingFolder.FullName, MetadataFileName));
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
                DebugConsole.ThrowError("Publishing workshop item " + item.Title + " failed. " + item.Error);
            }
            
            SaveUtil.ClearFolder(WorkshopItemStagingFolder);
            Directory.Delete(WorkshopItemStagingFolder);
            File.Delete(PreviewImageName);

            yield return CoroutineStatus.Success;
        }

        /// <summary>
        /// Enables a workshop item by moving it to the game folder.
        /// </summary>
        public static bool EnableWorkShopItem(Workshop.Item item, bool allowFileOverwrite)
        {
            if (!item.Installed)
            {
                DebugConsole.ThrowError("Cannot enable workshop item \"" + item.Title + "\" because it has not been installed.");
                return false;
            }
            
            string newContentPackagePath = GetWorkshopItemContentPackagePath(item);
            ContentPackage contentPackage = new ContentPackage(Path.Combine(item.Directory.FullName, MetadataFileName));
            
            if (!allowFileOverwrite)
            {
                if (File.Exists(newContentPackagePath))
                {
                    DebugConsole.ThrowError("Cannot enable workshop item \"" + item.Title + "\". The file \"" + newContentPackagePath + "\" would be overwritten by the item.");
                    return false;
                }

                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    if (File.Exists(contentFile.Path))
                    {
                        //TODO: ask the player if they want to let a workshop item overwrite existing files?
                        DebugConsole.ThrowError("Cannot enable workshop item \"" + item.Title + "\". The file \"" + contentFile.Path + "\" would be overwritten by the item.");
                        return false;
                    }
                }
            }

            try
            {
                //we only need to create a new content package for the item if it contains content with a type other than None
                //e.g. items that are just a sub file are just copied to the game folder
                if (contentPackage.Files.Any(f => f.Type != ContentType.None))
                {
                    File.Copy(contentPackage.Path, newContentPackagePath);
                }

                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    string sourceFile = Path.Combine(item.Directory.FullName, contentFile.Path);
                    //make sure the destination directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(contentFile.Path));
                    File.Copy(sourceFile, contentFile.Path, overwrite: true);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Enabling the workshop item \"" + item.Title + "\" failed.", e);
                return false;
            }

            var newPackage = ContentPackage.CreatePackage(item.Title, newContentPackagePath);
            ContentPackage.List.Add(newPackage);
            GameMain.Config.SelectedContentPackages.Add(newPackage);

            foreach (string tag in item.Tags)
            {
                switch (tag)
                {
                    case "Submarine":
                        Submarine.RefreshSavedSubs();
                        break;
                }
            }
            
            return true;
        }

        /// <summary>
        /// Disables a workshop item by removing the files from the game folder.
        /// </summary>
        public static bool DisableWorkShopItem(Workshop.Item item)
        {
            if (!item.Installed)
            {
                DebugConsole.ThrowError("Cannot disable workshop item \"" + item.Title + "\" because it has not been installed.");
                return false;
            }

            ContentPackage contentPackage = new ContentPackage(Path.Combine(item.Directory.FullName, MetadataFileName));

            string installedContentPackagePath = GetWorkshopItemContentPackagePath(item);
            if (File.Exists(installedContentPackagePath)) File.Delete(installedContentPackagePath);
            try
            {
                foreach (ContentFile contentFile in contentPackage.Files)
                {
                    File.Delete(contentFile.Path);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Disabling the workshop item \"" + item.Title + "\" failed.", e);
            }
            return true;
        }

        public static bool CheckWorkshopItemEnabled(Workshop.Item item)
        {
            if (!item.Installed) return false;

            string metaDataPath = Path.Combine(item.Directory.FullName, MetadataFileName);
            if (!File.Exists(metaDataPath))
            {
                throw new FileNotFoundException("Metadata file for the Workshop item \"" + item.Title + "\" not found. The file may be corrupted.");
            }

            ContentPackage contentPackage = new ContentPackage(metaDataPath);
            if (!File.Exists(GetWorkshopItemContentPackagePath(item))) return false;
            foreach (ContentFile contentFile in contentPackage.Files)
            {
                if (!File.Exists(contentFile.Path)) return false;
            }
            
            return true;
        }

        private static string GetWorkshopItemContentPackagePath(Workshop.Item item)
        {
            string fileName = item.Title + ".xml";
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalidChars) fileName = fileName.Replace(c.ToString(), "");
            return Path.Combine("Data", "ContentPackages", fileName);
        }
        
        #endregion

        public static void Update(float deltaTime)
        {
            if (instance == null || !instance.isInitialized) return;
            
            instance.client?.Update();
            instance.server?.Update();

            SteamAchievementManager.Update(deltaTime);
        }

        public static void ShutDown()
        {
            if (instance == null || !instance.isInitialized)
            {
                return;
            }

            instance.client?.Dispose();
            instance.client = null;
            instance.server?.Dispose();
            instance.server = null;
        }
    }
}
