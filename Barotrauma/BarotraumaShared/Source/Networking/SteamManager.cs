using Facepunch.Steamworks;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Barotrauma.Steam
{
    class SteamManager
    {
        const uint AppID = 602960;

        // UDP port for the server to do authentication on (ie, talk to Steam on)
        const ushort SERVER_AUTHENTICATION_PORT = 8766;
        
        // UDP port for the master server updater to listen on
        const ushort MASTER_SERVER_UPDATER_PORT = 27016;

        // Current game server version
        const string SERVER_VERSION = "1.0.0.0";

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
        
        private bool m_bInitialized;
        private bool m_bConnectedToSteam;

        /*private List<ServerInfo> serverList = new List<ServerInfo>();
        private Action<List<ServerInfo>> onLobbyFound;*/

        private Client client;
        private Server server;

        public static void Initialize()
        {
            instance = new SteamManager();
        }

        private SteamManager()
        {
            client = new Client(AppID);
            isInitialized = client.IsSubscribed && client.IsValid;
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
            query.OnUpdate += () =>
            {
                foreach (ServerList.Server s in query.Responded)
                {
                    var serverInfo = new Networking.ServerInfo()
                    {
                        ServerName = s.Name,
                        Port = s.ConnectionPort.ToString(),
                        IP = s.Address.ToString(), //TODO: check
                        //GameStarted = serverDetails.???,
                        PlayerCount = s.Players,
                        MaxPlayers = s.MaxPlayers,
                        HasPassword = s.Passworded
                    };
                    onServerFound(serverInfo);
                }
                query.Responded.Clear();
            };
            query.OnFinished = onFinished;
                
            return true;
        }

        #endregion

        #region Server

        public static bool CreateServer(Networking.GameServer server, int maxPlayers)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            ServerInit options = new ServerInit("Barotrauma", "Barotrauma");
            Instance.server = new Server(AppID, options);
            RefreshServerDetails(server);
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
#if SERVER
            instance.server.DedicatedServer = true;
#endif

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

            CoroutineManager.StartCoroutine(instance.PublishItem(item, tempFolder));
        }

        private IEnumerable<object> PublishItem(Workshop.Editor item, DirectoryInfo tempFolder)
        {
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
