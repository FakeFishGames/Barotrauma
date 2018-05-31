using Facepunch.Steamworks;
using System;
using System.Collections.Generic;
using System.IO;

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
        
        private Client client;
        private Server server;

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

        public static void GetWorkshopItems(Action<IList<Workshop.Item>> onItemsFound)
        {
            if (instance == null || !instance.isInitialized) return;

            var query = instance.client.Workshop.CreateQuery();
            query.Order = Workshop.Order.RankedByTrend;
            query.UploaderAppId = AppID;

            query.Run();

            query.OnResult += (Workshop.Query q) =>
            {
                onItemsFound(q.Items);
            };
        }

        public static void SaveToWorkshop(Submarine sub)
        {
            if (instance == null || !instance.isInitialized) return;
            
            var item = instance.client.Workshop.CreateItem(Workshop.ItemType.Community);
            item.Visibility = Workshop.Editor.VisibilityType.Public;
            item.Description = sub.Description;
            item.WorkshopUploadAppId = AppID;
            item.Title = sub.Name;

            string subPreviewPath = "SubPreview.png";
#if CLIENT
            FileStream fs = new FileStream(subPreviewPath, FileMode.Create);
            sub.PreviewImage.Texture.SaveAsPng(fs, (int)sub.PreviewImage.size.X, (int)sub.PreviewImage.size.Y);
            item.PreviewImage = subPreviewPath;
#endif

            var tempFolder = new DirectoryInfo("Temp");
            item.Folder = tempFolder.FullName;
            if (tempFolder.Exists)
            {
                SaveUtil.ClearFolder(tempFolder.FullName);
            }                
            else
            {
                tempFolder.Create();
            }

            File.Copy(sub.FilePath, Path.Combine(tempFolder.FullName, Path.GetFileName(sub.FilePath)));

            CoroutineManager.StartCoroutine(instance.PublishItem(item, tempFolder, subPreviewPath));
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
                DebugConsole.ThrowError("Published workshop item " + item.Title + " succesfully.");
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
