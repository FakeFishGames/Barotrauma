using Facepunch.Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Steam
{
    class SteamManager
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
        
        private Client client;
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
                client = new Client(AppID);
                isInitialized = client.IsSubscribed && client.IsValid;

                if (isInitialized)
                {
                    DebugConsole.Log("Logged in as " + client.Username + " (SteamID " + client.SteamId + ")");
                }
            }
            catch (Exception e)
            {
                isInitialized = false;
#if CLIENT
                new Barotrauma.GUIMessageBox("Error", "Initializing Steam client failed. Please make sure Steam is running and you are logged in to an account.");
#else
                DebugConsole.ThrowError("Initializing Steam client failed.", e);
#endif
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

            instance.client.Achievements.Trigger(achievementName);
            return true;
        }

#region Connecting to servers

        public static bool GetServers(Action<Networking.ServerInfo> onServerFound, Action onFinished)
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
            query.OnUpdate += () => { UpdateServerQuery(query, onServerFound); };
            query.OnFinished = onFinished;

            var localQuery = instance.client.ServerList.Local(filter);
            localQuery.OnUpdate += () => { UpdateServerQuery(localQuery, onServerFound); };
            localQuery.OnFinished = onFinished;

            return true;
        }

        private static void UpdateServerQuery(ServerList.Request query, Action<Networking.ServerInfo> onServerFound)
        {
            foreach (ServerList.Server s in query.Responded)
            {
                var serverInfo = new Networking.ServerInfo()
                {
                    ServerName = s.Name,
                    Port = s.ConnectionPort.ToString(),
                    IP = s.Address.ToString(),
                    PlayerCount = s.Players,
                    MaxPlayers = s.MaxPlayers,
                    HasPassword = s.Passworded
                };
                s.FetchRules();
                s.OnReceivedRules += (bool asd) =>
                {
                    DebugConsole.Log(string.Join(", ", s.Rules.Values));
                };
                onServerFound(serverInfo);
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

#region Server

        public static bool CreateServer(Networking.GameServer server)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            ServerInit options = new ServerInit("Barotrauma", "Barotrauma")
            {
                GamePort = (ushort)server.Port,
                //QueryPort = (ushort)server.Port
            };
            //options.QueryShareGamePort();

            instance.server = new Server(AppID, options);
            if (!instance.server.IsValid)
            {
                instance.server.Dispose();
                instance.server = null;
                DebugConsole.ThrowError("Initializing Steam server failed.");
                return false;
            }

            RefreshServerDetails(server);

            instance.server.Auth.OnAuthChange = server.OnAuthChange;

            return true;
        }

        public static bool RegisterToMasterServer()
        {
            if (instance == null || !instance.isInitialized || instance.server == null) return false;
            Instance.server.LogOnAnonymous();
            return true;
        }

        public static bool RefreshServerDetails(Networking.GameServer server)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            // These server state variables may be changed at any time.  Note that there is no lnoger a mechanism
            // to send the player count.  The player count is maintained by steam and you should use the player
            // creation/authentication functions to maintain your player count.
            instance.server.ServerName = server.Name;
            instance.server.MaxPlayers = server.MaxPlayers;
            instance.server.Passworded = server.HasPassword;
            Instance.server.SetKey("version", GameMain.Version.ToString());
            Instance.server.SetKey("contentpackage", GameMain.Config.SelectedContentPackage.Name);
            Instance.server.SetKey("contentpackagehash", GameMain.Config.SelectedContentPackage.MD5hash.Hash);
#if SERVER
            instance.server.DedicatedServer = true;
#endif

            return true;
        }

        public static bool StartAuthSession(byte[] authTicketData, ulong clientSteamID)
        {
            if (instance == null || !instance.isInitialized || instance.server == null) return false;

            DebugConsole.Log("SteamManager authenticating Steam client " + clientSteamID);
            if (!instance.server.Auth.StartSession(authTicketData, clientSteamID))
            {
                DebugConsole.Log("Authentication failed");
                return false;
            }
            return true;
        }

        public static bool CloseServer()
        {
            if (instance == null || !instance.isInitialized || instance.server == null) return false;
            
            instance.server.Dispose();
            instance.server = null;
            
            return true;
        }

        #endregion

        #region Workshop
        
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
            
            var item = instance.client.Workshop.CreateItem(Workshop.ItemType.Community);
            item.Visibility = Workshop.Editor.VisibilityType.Public;
            item.Description = sub.Description;
            item.WorkshopUploadAppId = AppID;
            item.Title = sub.Name;
            item.Tags.Add("Submarine");

            string subPreviewPath = Path.GetFullPath("SubPreview.png");
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
            DirectoryInfo tempFolder;
            try
            {
                CreateWorkshopItemFolder(new List<string>() { sub.FilePath }, out tempFolder);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Creating the workshop item failed.", e);
                return;
            }

            item.Folder = tempFolder.FullName;
            
            CoroutineManager.StartCoroutine(instance.PublishItem(item, tempFolder, subPreviewPath));
        }

        /// <summary>
        /// Creates a new folder, copies the specified files there and creates a metadata file with install instructions.
        /// </summary>
        /// <param name="itemPaths">Files to include in the workshop item</param>
        /// <param name="itemFolder">The path of the created folder</param>
        private static void CreateWorkshopItemFolder(List<string> filePaths, out DirectoryInfo itemFolder)
        {
            itemFolder = new DirectoryInfo("Temp");
            if (itemFolder.Exists)
            {
                SaveUtil.ClearFolder(itemFolder.FullName);
            }
            else
            {
                itemFolder.Create();
            }

            XDocument metaDoc = new XDocument(new XElement("files"));
            foreach (string filePath in filePaths)
            {
                //get relative path in case we've been passed a full path
                string originalPath = UpdaterUtil.GetRelativePath(Path.GetFullPath(filePath), Environment.CurrentDirectory);
                string destinationPath = Path.Combine(itemFolder.FullName, Path.GetFileName(filePath));
                File.Copy(filePath, destinationPath);

                metaDoc.Root.Add(new XElement("file", new XAttribute("path", originalPath)));
            }

            metaDoc.Save(Path.Combine(itemFolder.FullName, "metadata.meta"));            
        }

        private IEnumerable<object> PublishItem(Workshop.Editor item, DirectoryInfo tempFolder, string previewImgPath)
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
                DebugConsole.NewMessage("Published workshop item " + item.Title + " succesfully.", Microsoft.Xna.Framework.Color.LightGreen);
            }
            else
            {
                DebugConsole.ThrowError("Publishing workshop item " + item.Title + " failed. " + item.Error);
            }
            
            SaveUtil.ClearFolder(tempFolder.FullName);
            tempFolder.Delete();
            if (File.Exists(previewImgPath))
            {
                File.Delete(previewImgPath);
            }

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

            var metaDoc = GetWorkshopItemMetaData(item);
            if (metaDoc?.Root == null) return false;

            List<string> filePaths = new List<string>();
            foreach (XElement fileElement in metaDoc.Root.Elements())
            {
                filePaths.Add(fileElement.GetAttributeString("path", ""));
            }

            if (!allowFileOverwrite)
            {
                foreach (string filePath in filePaths)
                {
                    if (File.Exists(filePath))
                    {
                        //TODO: ask the player if they want to let a workshop item overwrite existing files?
                        DebugConsole.ThrowError("Cannot enable workshop item \"" + item.Title + "\". The file \"" + filePath + "\" would be overwritten by the item.");
                        return false;
                    }
                }
            }

            try
            {
                foreach (string filePath in filePaths)
                {
                    string sourceFile = Path.Combine(item.Directory.FullName, Path.GetFileName(filePath));
                    File.Copy(sourceFile, filePath, overwrite: true);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Enabling the workshop item \"" + item.Title + "\" failed.", e);
                return false;
            }

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

            var metaDoc = GetWorkshopItemMetaData(item);
            if (metaDoc?.Root == null) return false;

            List<string> filePaths = new List<string>();
            foreach (XElement fileElement in metaDoc.Root.Elements())
            {
                filePaths.Add(fileElement.GetAttributeString("path", ""));
            }
            
            try
            {
                foreach (string filePath in filePaths)
                {
                    File.Delete(filePath);
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

            var metaDoc = GetWorkshopItemMetaData(item);
            if (metaDoc?.Root == null) return false;
            
            foreach (XElement fileElement in metaDoc.Root.Elements())
            {
                string filePath = fileElement.GetAttributeString("path", "");
                if (!File.Exists(filePath))
                {
                    //TODO: check the MD5 hash of the file
                    return false;
                }
            }

            return true;
        }

        private static XDocument GetWorkshopItemMetaData(Workshop.Item item)
        {
            string metaFilePath = Path.Combine(item.Directory.FullName, "metadata.meta");
            if (!File.Exists(metaFilePath))
            {
                DebugConsole.ThrowError("Error: could not find a metadata file of the workshop item \"" + item.Title + "\". The item may be corrupted.");
                return null;
            }
            XDocument metaDoc = XMLExtensions.TryLoadXml(metaFilePath);
            if (metaDoc?.Root == null)
            {
                DebugConsole.ThrowError("Error: could not read the metadata file of the workshop item \"" + item.Title + "\". The item may be corrupted.");
                return null;
            }

            return metaDoc;
        }


        #endregion

        public static void Update()
        {
            if (instance == null || !instance.isInitialized) return;
            
            instance.client?.Update();
            instance.server?.Update();
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
