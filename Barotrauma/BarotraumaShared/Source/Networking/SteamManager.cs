using Microsoft.Xna.Framework;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    class SteamManager
    {
        static readonly AppId_t appID = (AppId_t)480;

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

        private SteamAPIWarningMessageHook_t m_SteamAPIWarningMessageHook;
        private static void SteamAPIDebugTextHook(int nSeverity, System.Text.StringBuilder pchDebugText)
        {
#if DEBUG
            DebugConsole.NewMessage(pchDebugText.ToString(), Color.Orange);
#endif
        }
        
        private HServerListRequest m_ServerListRequest;
        private HServerQuery m_ServerQuery;
        private ISteamMatchmakingServerListResponse m_ServerListResponse;
        private ISteamMatchmakingPingResponse m_PingResponse;
        private ISteamMatchmakingPlayersResponse m_PlayersResponse;

        // Tells us when we have successfully connected to Steam
        protected Callback<SteamServersConnected_t> m_CallbackSteamServersConnected;

        bool m_bInitialized;
        bool m_bConnectedToSteam;

        private List<ServerInfo> serverList = new List<ServerInfo>();
        private Action<List<ServerInfo>> onLobbyFound;

        public static void Initialize()
        {
            instance = new SteamManager();
        }

        private SteamManager()
        {            
            if (!Packsize.Test())
            {
                DebugConsole.ThrowError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.");
            }

            if (!DllCheck.Test())
            {
                DebugConsole.ThrowError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.");
            }
            
            /*try
            {
                // If Steam is not running or the game wasn't started through Steam, SteamAPI_RestartAppIfNecessary starts the
                // Steam client and also launches this game again if the User owns it. This can act as a rudimentary form of DRM.

                // Once you get a Steam AppID assigned by Valve, you need to replace AppId_t.Invalid with it and
                // remove steam_appid.txt from the game depot. eg: "(AppId_t)480" or "new AppId_t(480)".
                // See the Valve documentation for more information: https://partner.steamgames.com/doc/sdk/api#initialization_and_shutdown
                if (SteamAPI.RestartAppIfNecessary(AppId_t.Invalid))
                {
                    Application.Quit();
                    return;
                }
            }
            catch (System.DllNotFoundException e)
            { // We catch this exception here, as it will be the first occurence of it.
                DebugConsole.ThrowError("[Steamworks.NET] Could not load [lib]steam_api.dll/so/dylib. It's likely not in the correct location. Refer to the README for more details.", e);

                Application.Quit();
                return;
            }*/

            // Initializes the Steamworks API.
            // If this returns false then this indicates one of the following conditions:
            // [*] The Steam client isn't running. A running Steam client is required to provide implementations of the various Steamworks interfaces.
            // [*] The Steam client couldn't determine the App ID of game. If you're running your application from the executable or debugger directly then you must have a [code-inline]steam_appid.txt[/code-inline] in your game directory next to the executable, with your app ID in it and nothing else. Steam will look for this file in the current working directory. If you are running your executable from a different directory you may need to relocate the [code-inline]steam_appid.txt[/code-inline] file.
            // [*] Your application is not running under the same OS user context as the Steam client, such as a different user or administration access level.
            // [*] Ensure that you own a license for the App ID on the currently active Steam account. Your game must show up in your Steam library.
            // [*] Your App ID is not completely set up, i.e. in [code-inline]Release State: Unavailable[/code-inline], or it's missing default packages.
            // Valve's documentation for this is located here:
            // https://partner.steamgames.com/doc/sdk/api#initialization_and_shutdown
            isInitialized = SteamAPI.Init();
            if (!isInitialized)
            {
                DebugConsole.ThrowError("[Steamworks.NET] SteamAPI_Init() failed. Refer to Valve's documentation or the comment above this line for more information.");
                return;
            }

            if (m_SteamAPIWarningMessageHook == null)
            {
                // Set up our callback to recieve warning messages from Steam.
                // You must launch with "-debug_steamapi" in the launch args to recieve warnings.
                m_SteamAPIWarningMessageHook = new SteamAPIWarningMessageHook_t(SteamAPIDebugTextHook);
                SteamClient.SetWarningMessageHook(m_SteamAPIWarningMessageHook);
            }

            m_ServerListRequest = HServerListRequest.Invalid;
            m_ServerQuery = HServerQuery.Invalid;

            m_ServerListResponse = new ISteamMatchmakingServerListResponse(OnServerResponded, OnServerFailedToRespond, OnRefreshComplete);
            m_PingResponse = new ISteamMatchmakingPingResponse(OnServerResponded, OnServerFailedToRespond);
            m_PlayersResponse = new ISteamMatchmakingPlayersResponse(OnAddPlayerToList, OnPlayersFailedToRespond, OnPlayersRefreshComplete);
            m_CallbackSteamServersConnected = Callback<SteamServersConnected_t>.CreateGameServer(OnSteamServersConnected);
		    //m_CallbackSteamServersConnectFailure = Callback<SteamServerConnectFailure_t>.CreateGameServer(OnSteamServersConnectFailure);
            //m_RulesResponse = new ISteamMatchmakingRulesResponse(OnRulesResponded, OnRulesFailedToRespond, OnRulesRefreshComplete);
        }
        
        public static bool UnlockAchievement(string achievementName)
        {
            if (Instance == null || !Instance.isInitialized)
            {
                return false;
            }

            SteamUserStats.SetAchievement(achievementName);
            return true;
        }


        public static bool GetLobbies(Action<List<ServerInfo>> onLobbyFound)
        {
            if (Instance == null || !Instance.isInitialized)
            {
                return false;
            }

            instance.onLobbyFound = onLobbyFound;
            instance.serverList.Clear();
            instance.ReleaseRequest();

            MatchMakingKeyValuePair_t[] filters = new MatchMakingKeyValuePair_t[0];
            /*{
                new MatchMakingKeyValuePair_t { m_szKey = "appid", m_szValue = SteamAPI. },
                new MatchMakingKeyValuePair_t { m_szKey = "gamedir", m_szValue = "tf" },
                new MatchMakingKeyValuePair_t { m_szKey = "gametagsand", m_szValue = "beta" },
            };*/

            instance.m_ServerListRequest =
                SteamMatchmakingServers.RequestInternetServerList(appID, filters, (uint)filters.Length, instance.m_ServerListResponse);

            return true;
        }


        public static bool CreateServer(GameServer server, int maxPlayers)
        {
            if (Instance == null || !Instance.isInitialized)
            {
                return false;
            }

#if USE_GS_AUTH_API
		    EServerMode eMode = EServerMode.eServerModeAuthenticationAndSecure;
#else
            // Don't let Steam do authentication
            EServerMode eMode = EServerMode.eServerModeNoAuthentication;
#endif

            instance.m_bInitialized = Steamworks.GameServer.Init(
                0, SERVER_AUTHENTICATION_PORT, (ushort)server.Port, MASTER_SERVER_UPDATER_PORT, eMode, SERVER_VERSION);
            if (!instance.m_bInitialized)
            {
                DebugConsole.ThrowError("SteamGameServer_Init call failed");
                return false;
            }
            
            SteamGameServer.LogOnAnonymous();            
		    SteamGameServer.EnableHeartbeats(true);
            UpdateServerDetails();
            
            return true;
        }

        public static bool UpdateServerDetails()
        {
            if (Instance == null || !Instance.isInitialized)
            {
                return false;
            }

            // These server state variables may be changed at any time.  Note that there is no lnoger a mechanism
            // to send the player count.  The player count is maintained by steam and you should use the player
            // creation/authentication functions to maintain your player count.
            SteamGameServer.SetMaxPlayerCount(GameMain.Server.MaxPlayers);
            SteamGameServer.SetPasswordProtected(GameMain.Server.HasPassword);
            SteamGameServer.SetServerName(GameMain.Server.Name);
            //TODO: rest of the server data

            return true;
        }

        public static bool CloseServer()
        {
            if (Instance == null || !Instance.isInitialized)
            {
                return false;
            }

            throw new NotImplementedException();

            return true;
        }

        private void OnServerResponded(HServerListRequest hRequest, int iServer)
        {
            DebugConsole.Log("OnServerResponded: " + hRequest + " - " + iServer);
            
            var serverDetails = SteamMatchmakingServers.GetServerDetails(hRequest, iServer);
            var serverInfo = new ServerInfo()
            {
                ServerName = serverDetails.GetServerName(),
                Port = serverDetails.m_NetAdr.GetConnectionPort().ToString(),
                IP = serverDetails.m_NetAdr.GetIP().ToString(),
                //GameStarted = serverDetails.???,                
                PlayerCount = serverDetails.m_nPlayers,
                MaxPlayers = serverDetails.m_nMaxPlayers,
                HasPassword = serverDetails.m_bPassword
            };
            serverList.Add(serverInfo);
            onLobbyFound(serverList);
        }

        void OnSteamServersConnected(SteamServersConnected_t pLogonSuccess)
        {
            DebugConsole.Log("Server connected to Steam successfully");
            m_bConnectedToSteam = true;

            // log on is not finished until OnPolicyResponse() is called

            // Tell Steam about our server details
            UpdateServerDetails();
        }

        private void OnServerFailedToRespond(HServerListRequest hRequest, int iServer)
        {
            DebugConsole.Log("OnServerFailedToRespond: " + hRequest + " - " + iServer);
        }

        private void OnRefreshComplete(HServerListRequest hRequest, EMatchMakingServerResponse response)
        {
            DebugConsole.Log("OnRefreshComplete: " + hRequest + " - " + response);

            onLobbyFound(serverList);
        }

        // ISteamMatchmakingPingResponse
        private void OnServerResponded(gameserveritem_t gsi)
        {
            DebugConsole.Log("OnServerResponded: " + gsi);// + "\n" + GameServerItemFormattedString(gsi));
        }

        private void OnServerFailedToRespond()
        {
            DebugConsole.Log("OnServerFailedToRespond");
        }

        // ISteamMatchmakingPlayersResponse
        private void OnAddPlayerToList(string pchName, int nScore, float flTimePlayed)
        {
            DebugConsole.Log("OnAddPlayerToList: " + pchName + " - " + nScore + " - " + flTimePlayed);
        }

        private void OnPlayersFailedToRespond()
        {
            DebugConsole.Log("OnPlayersFailedToRespond");
        }

        private void OnPlayersRefreshComplete()
        {
            DebugConsole.Log("OnPlayersRefreshComplete");
        }

        private void ReleaseRequest()
        {
            if (m_ServerListRequest != HServerListRequest.Invalid)
            {
                SteamMatchmakingServers.ReleaseRequest(m_ServerListRequest);
                m_ServerListRequest = HServerListRequest.Invalid;
                DebugConsole.Log("SteamMatchmakingServers.ReleaseRequest(m_ServerListRequest)");
            }
        }
        
        public static void Update()
        {
            if (Instance == null || !Instance.isInitialized)
            {
                return;
            }

            // Run Steam client callbacks
            SteamAPI.RunCallbacks();
        }

        public static void ShutDown()
        {
            if (instance == null || !instance.isInitialized)
            {
                return;
            }

            SteamAPI.Shutdown();
        }
    }
}
