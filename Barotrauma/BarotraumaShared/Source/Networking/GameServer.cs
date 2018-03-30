using Barotrauma.Items.Components;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        private List<Client> connectedClients = new List<Client>();

        //for keeping track of disconnected clients in case the reconnect shortly after
        private List<Client> disconnectedClients = new List<Client>();

        private int roundStartSeed;
        
        //is the server running
        private bool started;

        private NetServer server;
        private NetPeerConfiguration config;
       
        private DateTime refreshMasterTimer;

        private DateTime roundStartTime;

        private RestClient restClient;
        private bool masterServerResponded;
        private IRestResponse masterServerResponse;

        private ServerLog log;

        private bool initiatedStartGame;
        private CoroutineHandle startGameCoroutine;

        public TraitorManager TraitorManager;

        private ServerEntityEventManager entityEventManager;

        private FileSender fileSender;

        public override List<Client> ConnectedClients
        {
            get
            {
                return connectedClients;
            }
        }

        
        public ServerEntityEventManager EntityEventManager
        {
            get { return entityEventManager; }
        }

        public ServerLog ServerLog
        {
            get { return log; }
        }

        public TimeSpan UpdateInterval
        {
            get { return updateInterval; }
        }
        
        public GameServer(string name, int port, bool isPublic = false, string password = "", bool attemptUPnP = false, int maxPlayers = 10)
        {
            name = name.Replace(":", "");
            name = name.Replace(";", "");
            
            this.name = name;
            this.isPublic = isPublic;
            this.maxPlayers = maxPlayers;
            this.password = "";
            if (password.Length > 0)
            {
                SetPassword(password);
            }

            config = new NetPeerConfiguration("barotrauma");

#if CLIENT
            netStats = new NetStats();
#endif

#if DEBUG
            config.SimulatedLoss = 0.05f;
            config.SimulatedRandomLatency = 0.05f;
            config.SimulatedDuplicatesChance = 0.05f;
            config.SimulatedMinimumLatency = 0.1f;

            config.ConnectionTimeout = 60.0f;

            NetIdUtils.Test();
#endif
            config.Port = port;
            Port = port;

            if (attemptUPnP)
            {
                config.EnableUPnP = true;
            }

            config.MaximumConnections = maxPlayers * 2; //double the lidgren connections for unauthenticated players            

            config.DisableMessageType(NetIncomingMessageType.DebugMessage |
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);

            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            log = new ServerLog(name);

            InitProjSpecific();

            entityEventManager = new ServerEntityEventManager(this);

            whitelist = new WhiteList();
            banList = new BanList();

            LoadSettings();
            PermissionPreset.LoadAll(PermissionPresetFile);
            LoadClientPermissions();
                        
            CoroutineManager.StartCoroutine(StartServer(isPublic));
        }

        public void SetPassword(string password)
        {
            this.password = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password)));
        }

        private IEnumerable<object> StartServer(bool isPublic)
        {
            bool error = false;
            try
            {
                Log("Starting the server...", ServerLog.MessageType.ServerMessage);
                server = new NetServer(config);
                netPeer = server;

                fileSender = new FileSender(this);
                fileSender.OnEnded += FileTransferChanged;
                fileSender.OnStarted += FileTransferChanged;
                
                server.Start();
            }
            catch (Exception e)
            {
                Log("Error while starting the server (" + e.Message + ")", ServerLog.MessageType.Error);

                System.Net.Sockets.SocketException socketException = e as System.Net.Sockets.SocketException;

#if CLIENT
                if (socketException != null && socketException.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
                {
                    new GUIMessageBox("Starting the server failed", e.Message + ". Are you trying to run multiple servers on the same port?");
                }
                else
                {
                    new GUIMessageBox("Starting the server failed", e.Message);
                }
#endif

                error = true;
            }                  
      
            if (error)
            {
                if (server != null) server.Shutdown("Error while starting the server");

#if CLIENT
                GameMain.NetworkMember = null;
#elif SERVER
                Environment.Exit(-1);
#endif
                yield return CoroutineStatus.Success;
            }
            
            if (config.EnableUPnP)
            {
                InitUPnP();

                //DateTime upnpTimeout = DateTime.Now + new TimeSpan(0,0,5);
                while (DiscoveringUPnP())// && upnpTimeout>DateTime.Now)
                {
                    yield return null;
                }

                FinishUPnP();
            }

            if (isPublic)
            {
                CoroutineManager.StartCoroutine(RegisterToMasterServer());
            }
                        
            updateInterval = new TimeSpan(0, 0, 0, 0, 150);

            Log("Server started", ServerLog.MessageType.ServerMessage);
                        
            GameMain.NetLobbyScreen.Select();
            GameMain.NetLobbyScreen.RandomizeSettings();
            started = true;

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> RegisterToMasterServer()
        {
            if (restClient==null)
            {
                restClient = new RestClient(NetConfig.MasterServerUrl);            
            }
                
            var request = new RestRequest("masterserver3.php", Method.GET);            
            request.AddParameter("action", "addserver");
            request.AddParameter("servername", name);
            request.AddParameter("serverport", Port);
            request.AddParameter("currplayers", connectedClients.Count);
            request.AddParameter("maxplayers", maxPlayers);
            request.AddParameter("password", string.IsNullOrWhiteSpace(password) ? 0 : 1);
            request.AddParameter("version", GameMain.Version.ToString());
            if (GameMain.Config.SelectedContentPackage != null)
            {
                request.AddParameter("contentpackage", GameMain.Config.SelectedContentPackage.Name);
            }

            masterServerResponded = false;
            masterServerResponse = null;
            var restRequestHandle = restClient.ExecuteAsync(request, response => MasterServerCallBack(response));
            
            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 15);
            while (!masterServerResponded)
            {
                if (DateTime.Now > timeOut)
                {
                    restRequestHandle.Abort();
                    DebugConsole.NewMessage("Couldn't register to master server (request timed out)", Color.Red);
                    Log("Couldn't register to master server (request timed out)", ServerLog.MessageType.Error);
                    yield return CoroutineStatus.Success;
                }

                yield return CoroutineStatus.Running;
            }

            if (masterServerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                DebugConsole.ThrowError("Error while connecting to master server (" + masterServerResponse.StatusCode + ": " + masterServerResponse.StatusDescription + ")");
            }
            else if (masterServerResponse != null && !string.IsNullOrWhiteSpace(masterServerResponse.Content))
            {
                DebugConsole.ThrowError("Error while connecting to master server (" + masterServerResponse.Content + ")");
            }
            else
            {
                registeredToMaster = true;
                refreshMasterTimer = DateTime.Now + refreshMasterInterval;
            }

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> RefreshMaster()
        {
            if (restClient == null)
            {
                restClient = new RestClient(NetConfig.MasterServerUrl);
            }

            var request = new RestRequest("masterserver3.php", Method.GET);
            request.AddParameter("action", "refreshserver");
            request.AddParameter("serverport", Port);
            request.AddParameter("gamestarted", gameStarted ? 1 : 0);
            request.AddParameter("currplayers", connectedClients.Count);
            request.AddParameter("maxplayers", maxPlayers);

            Log("Refreshing connection with master server...", ServerLog.MessageType.ServerMessage);

            var sw = new Stopwatch();
            sw.Start();

            masterServerResponded = false;
            masterServerResponse = null;
            var restRequestHandle = restClient.ExecuteAsync(request, response => MasterServerCallBack(response));

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 15);
            while (!masterServerResponded)
            {
                if (DateTime.Now > timeOut)
                {
                    restRequestHandle.Abort();
                    DebugConsole.NewMessage("Couldn't connect to master server (request timed out)", Color.Red);
                    Log("Couldn't connect to master server (request timed out)", ServerLog.MessageType.Error);
                    yield return CoroutineStatus.Success;
                }
                
                yield return CoroutineStatus.Running;
            }

            if (masterServerResponse.Content == "Error: server not found")
            {
                Log("Not registered to master server, re-registering...", ServerLog.MessageType.Error);
                CoroutineManager.StartCoroutine(RegisterToMasterServer());
            }
            else if (masterServerResponse.ErrorException != null)
            {
                DebugConsole.NewMessage("Error while registering to master server (" + masterServerResponse.ErrorException + ")", Color.Red);
                Log("Error while registering to master server (" + masterServerResponse.ErrorException + ")", ServerLog.MessageType.Error);
            }
            else if (masterServerResponse.StatusCode != System.Net.HttpStatusCode.OK)
            {
                DebugConsole.NewMessage("Error while reporting to master server (" + masterServerResponse.StatusCode + ": " + masterServerResponse.StatusDescription + ")", Color.Red);
                Log("Error while reporting to master server (" + masterServerResponse.StatusCode + ": " + masterServerResponse.StatusDescription + ")", ServerLog.MessageType.Error);
            }
            else
            {
                Log("Master server responded", ServerLog.MessageType.ServerMessage);
            }

            System.Diagnostics.Debug.WriteLine("took "+sw.ElapsedMilliseconds+" ms");

            yield return CoroutineStatus.Success;
        }

        private void MasterServerCallBack(IRestResponse response)
        {
            masterServerResponse = response;
            masterServerResponded = true;
        }
        
        public override void Update(float deltaTime)
        {
#if CLIENT
            if (ShowNetStats) netStats.Update(deltaTime);
            if (settingsFrame != null) settingsFrame.Update(deltaTime);
            if (log.LogFrame != null) log.LogFrame.Update(deltaTime);
#endif
            
            if (!started) return;
            
            base.Update(deltaTime);

            foreach (UnauthenticatedClient unauthClient in unauthenticatedClients)
            {
                unauthClient.AuthTimer -= deltaTime;
                if (unauthClient.AuthTimer <= 0.0f)
                {
                    unauthClient.Connection.Disconnect("Connection timed out");
                }
            }

            unauthenticatedClients.RemoveAll(uc => uc.AuthTimer <= 0.0f);

            fileSender.Update(deltaTime);         
            
            if (gameStarted)
            {
#if CLIENT
                SetRadioButtonColor();
#endif
                if (respawnManager != null) respawnManager.Update(deltaTime);

                entityEventManager.Update(connectedClients);

                bool isCrewDead =
                    connectedClients.All(c => c.Character == null || c.Character.IsDead || c.Character.IsUnconscious) &&
                    (myCharacter == null || myCharacter.IsDead || myCharacter.IsUnconscious);

                //restart if all characters are dead or submarine is at the end of the level
                if ((autoRestart && isCrewDead)
                    ||
                    (EndRoundAtLevelEnd && Submarine.MainSub != null && Submarine.MainSub.AtEndPosition && Submarine.MainSubs[1] == null))
                {
                    if (AutoRestart && isCrewDead)
                    {
                        Log("Ending round (entire crew dead)", ServerLog.MessageType.ServerMessage);
                    }
                    else
                    {
                        Log("Ending round (submarine reached the end of the level)", ServerLog.MessageType.ServerMessage);
                    }

                    EndGame();
                    return;
                }
            }
            else if (initiatedStartGame)
            {
                //tried to start up the game and StartGame coroutine is not running anymore
                // -> something wen't wrong during startup, re-enable start button and reset AutoRestartTimer
                if (startGameCoroutine != null && !CoroutineManager.IsCoroutineRunning(startGameCoroutine))
                {
                    if (autoRestart) AutoRestartTimer = Math.Max(AutoRestartInterval, 5.0f);
                    GameMain.NetLobbyScreen.StartButtonEnabled = true;

                    GameMain.NetLobbyScreen.LastUpdateID++;

                    startGameCoroutine = null;
                    initiatedStartGame = false;
                }
            }
            else if (autoRestart && Screen.Selected == GameMain.NetLobbyScreen && connectedClients.Count > 0)
            {
                AutoRestartTimer -= deltaTime;                
                if (AutoRestartTimer < 0.0f && !initiatedStartGame)
                {
                    StartGame();
                }
            }

            for (int i = disconnectedClients.Count - 1; i >= 0; i-- )
            {
                disconnectedClients[i].DeleteDisconnectedTimer -= deltaTime;
                if (disconnectedClients[i].DeleteDisconnectedTimer > 0.0f) continue;

                if (gameStarted && disconnectedClients[i].Character!=null)
                {
                    disconnectedClients[i].Character.Kill(CauseOfDeathType.Disconnected, null, true);
                    disconnectedClients[i].Character = null;
                }

                disconnectedClients.RemoveAt(i);
            }

            foreach (Client c in connectedClients)
            {
                //slowly reset spam timers
                c.ChatSpamTimer = Math.Max(0.0f, c.ChatSpamTimer - deltaTime);
                c.ChatSpamSpeed = Math.Max(0.0f, c.ChatSpamSpeed - deltaTime);
            }

            NetIncomingMessage inc = null; 
            while ((inc = server.ReadMessage()) != null)
            {
                try
                {
                    switch (inc.MessageType)
                    {
                        case NetIncomingMessageType.Data:
                            ReadDataMessage(inc);
                            break;
                        case NetIncomingMessageType.StatusChanged:
                            switch (inc.SenderConnection.Status)
                            {
                                case NetConnectionStatus.Disconnected:
                                    var connectedClient = connectedClients.Find(c => c.Connection == inc.SenderConnection);
                                    /*if (connectedClient != null && !disconnectedClients.Contains(connectedClient))
                                    {
                                        connectedClient.deleteDisconnectedTimer = NetConfig.DeleteDisconnectedTime;
                                        disconnectedClients.Add(connectedClient);
                                    }
                                    */
                                    DisconnectClient(inc.SenderConnection,
                                        connectedClient != null ? connectedClient.Name + " has disconnected" : "");
                                    break;
                            }
                            break;
                        case NetIncomingMessageType.ConnectionApproval:
                            if (banList.IsBanned(inc.SenderEndPoint.Address.ToString()))
                            {
                                inc.SenderConnection.Deny("You have been banned from the server");
                            }
                            else if (ConnectedClients.Count >= maxPlayers)
                            {
                                inc.SenderConnection.Deny("Server full");
                            }
                            else
                            {
                                if ((ClientPacketHeader)inc.SenderConnection.RemoteHailMessage.ReadByte() == ClientPacketHeader.REQUEST_AUTH)
                                {
                                    inc.SenderConnection.Approve();
                                    ClientAuthRequest(inc.SenderConnection);
                                }
                            }
                            break;
                    }                            
                }

                catch (Exception e)
                {
                    if (GameSettings.VerboseLogging)
                    {
                        DebugConsole.ThrowError("Failed to read an incoming message. {" + e + "}\n" + e.StackTrace);
                    }
                }
            }
            
            // if 30ms has passed
            if (updateTimer < DateTime.Now)
            {
                if (server.ConnectionsCount > 0)
                {
                    foreach (Client c in ConnectedClients)
                    {
                        try
                        {
                            ClientWrite(c);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Failed to write a network message for the client \""+c.Name+"\"!", e);
                        }
                    }

                    foreach (Item item in Item.ItemList)
                    {
                        item.NeedsPositionUpdate = false;
                    }
                }

                updateTimer = DateTime.Now + updateInterval;
            }

            if (!registeredToMaster || refreshMasterTimer >= DateTime.Now) return;

            CoroutineManager.StartCoroutine(RefreshMaster());
            refreshMasterTimer = DateTime.Now + refreshMasterInterval;
        }

        private void ReadDataMessage(NetIncomingMessage inc)
        {
            if (banList.IsBanned(inc.SenderEndPoint.Address.ToString()))
            {
                KickClient(inc.SenderConnection, "You have been banned from the server.");
                return;
            }
            
            ClientPacketHeader header = (ClientPacketHeader)inc.ReadByte();
            switch (header)
            {
                case ClientPacketHeader.REQUEST_AUTH:
                    ClientAuthRequest(inc.SenderConnection);
                    break;
                case ClientPacketHeader.REQUEST_INIT:
                    ClientInitRequest(inc);
                    break;

                case ClientPacketHeader.RESPONSE_STARTGAME:
                    var connectedClient = connectedClients.Find(c => c.Connection == inc.SenderConnection);
                    if (connectedClient != null)
                    {
                        connectedClient.ReadyToStart = inc.ReadBoolean();
                        UpdateCharacterInfo(inc, connectedClient);

                        //game already started -> send start message immediately
                        if (gameStarted)
                        {
                            SendStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.GameMode.Preset, connectedClient);
                        }
                    }
                    break;
                case ClientPacketHeader.UPDATE_LOBBY:
                    ClientReadLobby(inc);
                    break;
                case ClientPacketHeader.UPDATE_INGAME:
                    if (!gameStarted) return;

                    ClientReadIngame(inc);
                    break;
                case ClientPacketHeader.SERVER_COMMAND:
                    ClientReadServerCommand(inc);
                    break;
                case ClientPacketHeader.FILE_REQUEST:
                    if (AllowFileTransfers)
                    {
                        fileSender.ReadFileRequest(inc);
                    }
                    break;
            }            
        }
        
        public void CreateEntityEvent(IServerSerializable entity, object[] extraData = null)
        {
            entityEventManager.CreateEvent(entity, extraData);
        }

        private byte GetNewClientID()
        {
            byte userID = 1;
            while (connectedClients.Any(c => c.ID == userID))
            {
                userID++;
            }
            return userID;
        }

        private void ClientReadLobby(NetIncomingMessage inc)
        {
            Client c = ConnectedClients.Find(x => x.Connection == inc.SenderConnection);
            if (c == null)
            {
                inc.SenderConnection.Disconnect("You're not a connected client.");
                return;
            }
            
            ClientNetObject objHeader;
            while ((objHeader = (ClientNetObject)inc.ReadByte()) != ClientNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ClientNetObject.SYNC_IDS:
                        //TODO: might want to use a clever class for this
                        c.LastRecvGeneralUpdate = NetIdUtils.Clamp(inc.ReadUInt16(), c.LastRecvGeneralUpdate, GameMain.NetLobbyScreen.LastUpdateID);
                        c.LastRecvChatMsgID     = NetIdUtils.Clamp(inc.ReadUInt16(), c.LastRecvChatMsgID, c.LastChatMsgQueueID);

                        c.LastRecvCampaignSave      = inc.ReadUInt16();
                        if (c.LastRecvCampaignSave > 0)
                        {
                            byte campaignID             = inc.ReadByte();
                            c.LastRecvCampaignUpdate    = inc.ReadUInt16();

                            if (GameMain.GameSession?.GameMode is MultiPlayerCampaign)
                            {
                                //the client has a campaign save for another campaign 
                                //(the server started a new campaign and the client isn't aware of it yet?)
                                if (((MultiPlayerCampaign)GameMain.GameSession.GameMode).CampaignID != campaignID)
                                {
                                    c.LastRecvCampaignSave = 0;
                                    c.LastRecvCampaignUpdate = 0;
                                }
                            }
                        }
                        break;
                    case ClientNetObject.CHAT_MESSAGE:
                        ChatMessage.ServerRead(inc, c);
                        break;
                    case ClientNetObject.VOTE:
                        Voting.ServerRead(inc, c);
                        break;
                    default:
                        return;
                }

                //don't read further messages if the client has been disconnected (kicked due to spam for example)
                if (!connectedClients.Contains(c)) break;
            }
        }

        private void ClientReadIngame(NetIncomingMessage inc)
        {
            Client c = ConnectedClients.Find(x => x.Connection == inc.SenderConnection);
            if (c == null)
            {
                inc.SenderConnection.Disconnect("You're not a connected client.");
                return;
            }

            if (gameStarted)
            {
                if (!c.InGame)
                {
                    //check if midround syncing is needed due to missed unique events
                    entityEventManager.InitClientMidRoundSync(c);                    
                    c.InGame = true;
                }
            }
            
            ClientNetObject objHeader;
            while ((objHeader = (ClientNetObject)inc.ReadByte()) != ClientNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ClientNetObject.SYNC_IDS:
                        //TODO: might want to use a clever class for this
                        
                        UInt16 lastRecvChatMsgID        = inc.ReadUInt16();
                        UInt16 lastRecvEntityEventID    = inc.ReadUInt16();

                        //last msgs we've created/sent, the client IDs should never be higher than these
                        UInt16 lastEntityEventID = entityEventManager.Events.Count == 0 ? (UInt16)0 : entityEventManager.Events.Last().ID;

                        if (c.NeedsMidRoundSync)
                        {
                            //received all the old events -> client in sync, we can switch to normal behavior
                            if (lastRecvEntityEventID >= c.UnreceivedEntityEventCount - 1 ||
                                c.UnreceivedEntityEventCount == 0)
                            {
                                c.NeedsMidRoundSync = false;
                                lastRecvEntityEventID = (UInt16)(c.FirstNewEventID - 1);
                                c.LastRecvEntityEventID = lastRecvEntityEventID;
                            }
                            else
                            {
                                lastEntityEventID = (UInt16)(c.UnreceivedEntityEventCount - 1);
                            }
                        }

                        if (NetIdUtils.IdMoreRecent(lastRecvChatMsgID, c.LastRecvChatMsgID) &&   //more recent than the last ID received by the client
                            !NetIdUtils.IdMoreRecent(lastRecvChatMsgID, c.LastChatMsgQueueID)) //NOT more recent than the latest existing ID
                        {
                            c.LastRecvChatMsgID = lastRecvChatMsgID;
                        }
                        else if (lastRecvChatMsgID != c.LastRecvChatMsgID && GameSettings.VerboseLogging)
                        {
                            DebugConsole.ThrowError(
                                "Invalid lastRecvChatMsgID  " + lastRecvChatMsgID + 
                                " (previous: " + c.LastChatMsgQueueID + ", latest: "+c.LastChatMsgQueueID+")");
                        }

                        if (NetIdUtils.IdMoreRecent(lastRecvEntityEventID, c.LastRecvEntityEventID) &&
                            !NetIdUtils.IdMoreRecent(lastRecvEntityEventID, lastEntityEventID))
                        {
                            c.LastRecvEntityEventID = lastRecvEntityEventID;
                        }
                        else if (lastRecvEntityEventID != c.LastRecvEntityEventID && GameSettings.VerboseLogging)
                        {
                            DebugConsole.ThrowError(
                                "Invalid lastRecvEntityEventID  " + lastRecvEntityEventID + 
                                " (previous: " + c.LastRecvEntityEventID + ", latest: " + lastEntityEventID + ")");
                        }
                        break;
                    case ClientNetObject.CHAT_MESSAGE:
                        ChatMessage.ServerRead(inc, c);
                        break;
                    case ClientNetObject.CHARACTER_INPUT:
                        if (c.Character != null)
                        {
                            c.Character.ServerRead(objHeader, inc, c);
                        }
                        break;
                    case ClientNetObject.ENTITY_STATE:
                        entityEventManager.Read(inc, c);
                        break;
                    case ClientNetObject.VOTE:
                        Voting.ServerRead(inc, c);
                        break;
                    default:
                        return;
                }

                //don't read further messages if the client has been disconnected (kicked due to spam for example)
                if (!connectedClients.Contains(c)) break;
            }
        }

        private void ClientReadServerCommand(NetIncomingMessage inc)
        {
            Client sender = ConnectedClients.Find(x => x.Connection == inc.SenderConnection);
            if (sender == null)
            {
                inc.SenderConnection.Disconnect("You're not a connected client.");
                return;
            }

            ClientPermissions command = ClientPermissions.None;
            try
            {
                command = (ClientPermissions)inc.ReadByte();
            }

            catch
            {
                return;
            }

            if (!sender.HasPermission(command))
            {
                Log("Client \"" + sender.Name + "\" sent a server command \"" + command + "\". Permission denied.", ServerLog.MessageType.ServerMessage);
                return;
            }

            switch (command)
            {
                case ClientPermissions.Kick:
                    string kickedName = inc.ReadString().ToLowerInvariant();
                    string kickReason = inc.ReadString();
                    var kickedClient = connectedClients.Find(cl => cl != sender && cl.Name.ToLowerInvariant() == kickedName);
                    if (kickedClient != null)
                    {
                        Log("Client \"" + sender.Name + "\" kicked \"" + kickedClient.Name + "\".", ServerLog.MessageType.ServerMessage);
                        KickClient(kickedClient, string.IsNullOrEmpty(kickReason) ? "Kicked by " + sender.Name : kickReason);
                    }
                    break;
                case ClientPermissions.Ban:
                    string bannedName = inc.ReadString().ToLowerInvariant();
                    string banReason = inc.ReadString();
                    var bannedClient = connectedClients.Find(cl => cl != sender && cl.Name.ToLowerInvariant() == bannedName);
                    if (bannedClient != null)
                    {
                        Log("Client \"" + sender.Name + "\" banned \"" + bannedClient.Name + "\".", ServerLog.MessageType.ServerMessage);
                        BanClient(bannedClient, string.IsNullOrEmpty(banReason) ? "Banned by " + sender.Name : banReason, false);
                    }
                    break;
                case ClientPermissions.EndRound:
                    if (gameStarted)
                    {
                        Log("Client \"" + sender.Name + "\" ended the round.", ServerLog.MessageType.ServerMessage);
                        EndGame();
                    }
                    break;
                case ClientPermissions.SelectSub:
                    UInt16 subIndex = inc.ReadUInt16();
                    var subList = GameMain.NetLobbyScreen.GetSubList();
                    if (subIndex >= subList.Count)
                    {
                        DebugConsole.NewMessage("Client \"" + sender.Name + "\" attempted to select a sub, index out of bounds (" + subIndex + ")", Color.Red);
                    }
                    else
                    {
                        GameMain.NetLobbyScreen.SelectedSub = subList[subIndex];
                    }
                    break;
                case ClientPermissions.SelectMode:
                    UInt16 modeIndex = inc.ReadUInt16();
                    var modeList = GameMain.NetLobbyScreen.SelectedModeIndex = modeIndex;
                    break;
                case ClientPermissions.ManageCampaign:
                    MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
                    if (campaign != null)
                    {
                        campaign.ServerRead(inc, sender);
                    }
                    break;
                case ClientPermissions.ConsoleCommands:
                    string consoleCommand = inc.ReadString();
                    Vector2 clientCursorPos = new Vector2(inc.ReadSingle(), inc.ReadSingle());
                    DebugConsole.ExecuteClientCommand(sender, clientCursorPos, consoleCommand);
                    break;
            }

            inc.ReadPadBits();
        }


        private void ClientWrite(Client c)
        {
            if (gameStarted && c.InGame)
            {
                ClientWriteIngame(c);
            }
            else
            {                
                //if 30 seconds have passed since the round started and the client isn't ingame yet,
                //kill the client's character
                if (gameStarted && c.Character != null && (DateTime.Now - roundStartTime).Seconds > 30.0f)
                {
                    c.Character.Kill(CauseOfDeathType.Disconnected, null);
                    c.Character = null;
                }

                ClientWriteLobby(c);

                MultiPlayerCampaign campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
                if (campaign != null && NetIdUtils.IdMoreRecent(campaign.LastSaveID, c.LastRecvCampaignSave))
                { 
                    if (!fileSender.ActiveTransfers.Any(t => t.Connection == c.Connection && t.FileType == FileTransferType.CampaignSave))
                    {
                        fileSender.StartTransfer(c.Connection, FileTransferType.CampaignSave, GameMain.GameSession.SavePath);
                    }
                }
            }
        }

        /// <summary>
        /// Write info that the client needs when joining the server
        /// </summary>
        private void ClientWriteInitial(Client c, NetBuffer outmsg)
        {
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Sending initial lobby update", Color.Gray);
            }

            outmsg.Write(c.ID);

            var subList = GameMain.NetLobbyScreen.GetSubList();
            outmsg.Write((UInt16)subList.Count);
            for (int i = 0; i < subList.Count; i++)
            {
                outmsg.Write(subList[i].Name);
                outmsg.Write(subList[i].MD5Hash.ToString());
            }

            outmsg.Write(GameStarted);
            outmsg.Write(AllowSpectating);

            WritePermissions(outmsg, c);
        }

        private void ClientWriteIngame(Client c)
        {
            //don't send position updates to characters who are still midround syncing
            //characters or items spawned mid-round don't necessarily exist at the client's end yet
            if (!c.NeedsMidRoundSync)
            {
                foreach (Character character in Character.CharacterList)
                {
                    if (!character.Enabled) continue;
                    if (c.Character != null &&
                        Vector2.DistanceSquared(character.WorldPosition, c.Character.WorldPosition) >=
                        NetConfig.CharacterIgnoreDistanceSqr)
                    {
                        continue;
                    }
                    if (!c.PendingPositionUpdates.Contains(character)) c.PendingPositionUpdates.Enqueue(character);
                }

                foreach (Submarine sub in Submarine.Loaded)
                {
                    //if docked to a sub with a smaller ID, don't send an update
                    //  (= update is only sent for the docked sub that has the smallest ID, doesn't matter if it's the main sub or a shuttle)
                    if (sub.DockedTo.Any(s => s.ID < sub.ID)) continue;
                    if (!c.PendingPositionUpdates.Contains(sub)) c.PendingPositionUpdates.Enqueue(sub);
                }

                foreach (Item item in Item.ItemList)
                {
                    if (!item.NeedsPositionUpdate) continue;
                    if (!c.PendingPositionUpdates.Contains(item)) c.PendingPositionUpdates.Enqueue(item);
                }
            }

            NetOutgoingMessage outmsg = server.CreateMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_INGAME);
            
            outmsg.Write((float)NetTime.Now);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);
            outmsg.Write(c.LastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server
            outmsg.Write(c.LastSentEntityEventID);

            entityEventManager.Write(c, outmsg);

            WriteChatMessages(outmsg, c);

            //write as many position updates as the message can fit
            while (outmsg.LengthBytes < config.MaximumTransmissionUnit - 20 && 
                c.PendingPositionUpdates.Count > 0)
            {
                var entity = c.PendingPositionUpdates.Dequeue();
                if (entity == null || entity.Removed) continue;

                outmsg.Write((byte)ServerNetObject.ENTITY_POSITION);
                if (entity is Item)
                {
                    ((Item)entity).ServerWritePosition(outmsg, c);
                }
                else
                {
                    ((IServerSerializable)entity).ServerWrite(outmsg, c);
                }
                outmsg.WritePadBits();
            }

            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            if (outmsg.LengthBytes > config.MaximumTransmissionUnit)
            {
                DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + config.MaximumTransmissionUnit + ")");
            }

            server.SendMessage(outmsg, c.Connection, NetDeliveryMethod.Unreliable);
        }

        private void ClientWriteLobby(Client c)
        {
            bool isInitialUpdate = false;

            NetOutgoingMessage outmsg = server.CreateMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_LOBBY);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);

            if (NetIdUtils.IdMoreRecent(GameMain.NetLobbyScreen.LastUpdateID, c.LastRecvGeneralUpdate))
            {
                outmsg.Write(true);
                outmsg.WritePadBits();

                outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
                outmsg.Write(GameMain.NetLobbyScreen.GetServerName());
                outmsg.Write(GameMain.NetLobbyScreen.ServerMessageText);
                
                outmsg.Write(c.LastRecvGeneralUpdate < 1);
                if (c.LastRecvGeneralUpdate < 1)
                {
                    isInitialUpdate = true;
                    ClientWriteInitial(c, outmsg);
                }
                outmsg.Write(GameMain.NetLobbyScreen.SelectedSub.Name);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedSub.MD5Hash.ToString());
                outmsg.Write(GameMain.NetLobbyScreen.UsingShuttle);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.ToString());

                outmsg.Write(Voting.AllowSubVoting);
                outmsg.Write(Voting.AllowModeVoting);

                outmsg.Write(AllowSpectating);

                outmsg.WriteRangedInteger(0, 2, (int)TraitorsEnabled);

                outmsg.WriteRangedInteger(0, MissionPrefab.MissionTypes.Count - 1, (GameMain.NetLobbyScreen.MissionTypeIndex));

                outmsg.Write((byte)GameMain.NetLobbyScreen.SelectedModeIndex);
                outmsg.Write(GameMain.NetLobbyScreen.LevelSeed);

                outmsg.Write(AutoRestart);
                if (autoRestart)
                {
                    outmsg.Write(AutoRestartTimer);
                }

                outmsg.Write((byte)connectedClients.Count);
                foreach (Client client in connectedClients)
                {
                    outmsg.Write(client.ID);
                    outmsg.Write(client.Name);
                    outmsg.Write(client.Character == null || !gameStarted ? (ushort)0 : client.Character.ID);
                }
            }
            else
            {
                outmsg.Write(false);
                outmsg.WritePadBits();
            }

            var campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            if (campaign != null)
            {
                if (NetIdUtils.IdMoreRecent(campaign.LastUpdateID, c.LastRecvCampaignUpdate))
                {
                    outmsg.Write(true);
                    outmsg.WritePadBits();
                    campaign.ServerWrite(outmsg, c);
                }
                else
                {
                    outmsg.Write(false);
                    outmsg.WritePadBits();
                }
            }
            else
            {
                outmsg.Write(false);
                outmsg.WritePadBits();
            }
            
            outmsg.Write(c.LastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server
            
            WriteChatMessages(outmsg, c);

            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            if (isInitialUpdate)
            {
                //the initial update may be very large if the host has a large number
                //of submarine files, so the message may have to be fragmented

                //unreliable messages don't play nicely with fragmenting, so we'll send the message reliably
                server.SendMessage(outmsg, c.Connection, NetDeliveryMethod.ReliableUnordered);

                //and assume the message was received, so we don't have to keep resending
                //these large initial messages until the client acknowledges receiving them
                c.LastRecvGeneralUpdate++;
                
                SendVoteStatus(new List<Client>() { c });
            }
            else
            {
                if (outmsg.LengthBytes > config.MaximumTransmissionUnit)
                {
                    DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + config.MaximumTransmissionUnit + ")");
                }

                server.SendMessage(outmsg, c.Connection, NetDeliveryMethod.Unreliable);
            }
        }

        private void WriteChatMessages(NetOutgoingMessage outmsg, Client c)
        {
            c.ChatMsgQueue.RemoveAll(cMsg => !NetIdUtils.IdMoreRecent(cMsg.NetStateID, c.LastRecvChatMsgID));
            for (int i = 0; i < c.ChatMsgQueue.Count && i < ChatMessage.MaxMessagesPerPacket; i++)
            {
                if (outmsg.LengthBytes + c.ChatMsgQueue[i].EstimateLengthBytesServer(c) > config.MaximumTransmissionUnit - 5)
                {
                    //not enough room in this packet
                    return;
                }
                c.ChatMsgQueue[i].ServerWrite(outmsg, c);
            }
        }
        
        public bool StartGame()
        {
            Submarine selectedSub = null;
            Submarine selectedShuttle = GameMain.NetLobbyScreen.SelectedShuttle;
            bool usingShuttle = GameMain.NetLobbyScreen.UsingShuttle;

            if (Voting.AllowSubVoting)
            {
                selectedSub = Voting.HighestVoted<Submarine>(VoteType.Sub, connectedClients);
                if (selectedSub == null) selectedSub = GameMain.NetLobbyScreen.SelectedSub;
            }
            else
            {
                selectedSub = GameMain.NetLobbyScreen.SelectedSub;
            }

            if (selectedSub == null)
            {
#if CLIENT
                GameMain.NetLobbyScreen.SubList.Flash();
#endif
                return false;
            }

            if (selectedShuttle == null)
            {
#if CLIENT
                GameMain.NetLobbyScreen.ShuttleList.Flash();
#endif
                return false;
            }

            GameModePreset selectedMode = Voting.HighestVoted<GameModePreset>(VoteType.Mode, connectedClients);
            if (selectedMode == null) selectedMode = GameMain.NetLobbyScreen.SelectedMode;

            if (selectedMode == null)
            {
#if CLIENT
                GameMain.NetLobbyScreen.ModeList.Flash();
#endif
                return false;
            }

            CoroutineManager.StartCoroutine(InitiateStartGame(selectedSub, selectedShuttle, usingShuttle, selectedMode), "InitiateStartGame");

            return true;
        }

        private IEnumerable<object> InitiateStartGame(Submarine selectedSub, Submarine selectedShuttle, bool usingShuttle, GameModePreset selectedMode)
        {
            initiatedStartGame = true;
            GameMain.NetLobbyScreen.StartButtonEnabled = false;

            if (connectedClients.Any())
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write((byte)ServerPacketHeader.QUERY_STARTGAME);

                msg.Write(selectedSub.Name);
                msg.Write(selectedSub.MD5Hash.Hash);

                msg.Write(usingShuttle);
                msg.Write(selectedShuttle.Name);
                msg.Write(selectedShuttle.MD5Hash.Hash);

                connectedClients.ForEach(c => c.ReadyToStart = false);

                server.SendMessage(msg, connectedClients.Select(c => c.Connection).ToList(), NetDeliveryMethod.ReliableUnordered, 0);

                //give the clients a few seconds to request missing sub/shuttle files before starting the round
                float waitForResponseTimer = 5.0f;
                while (connectedClients.Any(c => !c.ReadyToStart) && waitForResponseTimer > 0.0f)
                {
                    waitForResponseTimer -= CoroutineManager.UnscaledDeltaTime;
                    yield return CoroutineStatus.Running;
                }

                if (fileSender.ActiveTransfers.Count > 0)
                {
#if CLIENT
                    var msgBox = new GUIMessageBox("", "Waiting for file transfers to finish before starting the round...", new string[] { "Start now" });
                    msgBox.Buttons[0].OnClicked += msgBox.Close;
#endif

                    float waitForTransfersTimer = 20.0f;
                    while (fileSender.ActiveTransfers.Count > 0 && waitForTransfersTimer > 0.0f)
                    {
                        waitForTransfersTimer -= CoroutineManager.UnscaledDeltaTime;

#if CLIENT
                        //message box close, break and start the round immediately
                        if (!GUIMessageBox.MessageBoxes.Contains(msgBox))
                        {
                            break;
                        }
#endif

                        yield return CoroutineStatus.Running;
                    }
                }
            }

            startGameCoroutine = GameMain.Instance.ShowLoading(StartGame(selectedSub, selectedShuttle, usingShuttle, selectedMode), false);

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> StartGame(Submarine selectedSub, Submarine selectedShuttle, bool usingShuttle, GameModePreset selectedMode)
        {            
            entityEventManager.Clear();

            GameMain.NetLobbyScreen.StartButtonEnabled = false;

#if CLIENT
            GUIMessageBox.CloseAll();
#endif
            
            roundStartSeed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(roundStartSeed);
            
            int teamCount = 1;
            byte hostTeam = 1;
            
            MultiPlayerCampaign campaign = GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ? 
                GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;
        
            //don't instantiate a new gamesession if we're playing a campaign
            if (campaign == null || GameMain.GameSession == null)
            {
                GameMain.GameSession = new GameSession(selectedSub, "", selectedMode, MissionPrefab.MissionTypes[GameMain.NetLobbyScreen.MissionTypeIndex]);
            }

            if (GameMain.GameSession.GameMode.Mission != null &&
                GameMain.GameSession.GameMode.Mission.AssignTeamIDs(connectedClients, out hostTeam))
            {
                teamCount = 2;
            }
            else
            {
                connectedClients.ForEach(c => c.TeamID = hostTeam);
            }

            if (campaign != null)
            {
#if CLIENT
                if (GameMain.GameSession?.CrewManager != null) GameMain.GameSession.CrewManager.Reset();
#endif
                GameMain.GameSession.StartRound(campaign.Map.SelectedConnection.Level, true, teamCount > 1);
            }
            else
            {
                GameMain.GameSession.StartRound(GameMain.NetLobbyScreen.LevelSeed, teamCount > 1);
            }

            GameServer.Log("Starting a new round...", ServerLog.MessageType.ServerMessage);
            GameServer.Log("Submarine: " + selectedSub.Name, ServerLog.MessageType.ServerMessage);
            GameServer.Log("Game mode: " + selectedMode.Name, ServerLog.MessageType.ServerMessage);
            GameServer.Log("Level seed: " + GameMain.NetLobbyScreen.LevelSeed, ServerLog.MessageType.ServerMessage);

            bool missionAllowRespawn = campaign == null &&
                (!(GameMain.GameSession.GameMode is MissionMode) || 
                ((MissionMode)GameMain.GameSession.GameMode).Mission.AllowRespawn);

            if (AllowRespawn && missionAllowRespawn) respawnManager = new RespawnManager(this, usingShuttle ? selectedShuttle : null);

            //assign jobs and spawnpoints separately for each team
            for (int teamID = 1; teamID <= teamCount; teamID++)
            {
                //find the clients in this team
                List<Client> teamClients = teamCount == 1 ? new List<Client>(connectedClients) : connectedClients.FindAll(c => c.TeamID == teamID);
                if (AllowSpectating)
                {
                    teamClients.RemoveAll(c => c.SpectateOnly);
                }

                if (!teamClients.Any() && teamID > 1) continue;

                AssignJobs(teamClients, teamID == hostTeam);

                List<CharacterInfo> characterInfos = new List<CharacterInfo>();
                foreach (Client client in teamClients)
                {
                    client.NeedsMidRoundSync = false;

                    client.PendingPositionUpdates.Clear();
                    client.EntityEventLastSent.Clear();
                    client.LastSentEntityEventID = 0;
                    client.LastRecvEntityEventID = 0;
                    client.UnreceivedEntityEventCount = 0;

                    if (client.CharacterInfo == null)
                    {
                        client.CharacterInfo = new CharacterInfo(Character.HumanConfigFile, client.Name);
                    }
                    characterInfos.Add(client.CharacterInfo);
                    client.CharacterInfo.Job = new Job(client.AssignedJob);
                }

                //host's character
                if (characterInfo != null && hostTeam == teamID)
                {
                    characterInfo.Job = new Job(GameMain.NetLobbyScreen.JobPreferences[0]);
                    characterInfos.Add(characterInfo);
                }

                WayPoint[] assignedWayPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSubs[teamID - 1]);
                for (int i = 0; i < teamClients.Count; i++)
                {
                    Character spawnedCharacter = Character.Create(teamClients[i].CharacterInfo, assignedWayPoints[i].WorldPosition, true, false);
                    spawnedCharacter.AnimController.Frozen = true;
                    spawnedCharacter.GiveJobItems(assignedWayPoints[i]);
                    spawnedCharacter.TeamID = (byte)teamID;

                    teamClients[i].Character = spawnedCharacter;

#if CLIENT
                    GameMain.GameSession.CrewManager.AddCharacter(spawnedCharacter);
#endif
                }

#if CLIENT
                if (characterInfo != null && hostTeam == teamID)
                {
                    myCharacter = Character.Create(characterInfo, assignedWayPoints[assignedWayPoints.Length - 1].WorldPosition, false, false);
                    myCharacter.GiveJobItems(assignedWayPoints.Last());
                    myCharacter.TeamID = (byte)teamID;

                    Character.Controlled = myCharacter;

                    GameMain.GameSession.CrewManager.AddCharacter(myCharacter);
                }
#endif
            }

            foreach (Submarine sub in Submarine.MainSubs)
            {
                if (sub == null) continue;

                List<ItemPrefab> spawnList = new List<ItemPrefab>();
                foreach (KeyValuePair<ItemPrefab, int> kvp in extraCargo)
                {
                    for (int i = 0; i < kvp.Value; i++)
                    {
                        spawnList.Add(kvp.Key);
                    }
                }

                CargoManager.CreateItems(spawnList);
            }

            TraitorManager = null;
            if (TraitorsEnabled == YesNoMaybe.Yes ||
                (TraitorsEnabled == YesNoMaybe.Maybe && Rand.Range(0.0f, 1.0f) < 0.5f))
            {
                List<Character> characters = new List<Character>();
                foreach (Client client in ConnectedClients)
                {
                    if (client.Character != null)
                        characters.Add(client.Character);
                }
                var max = (int)Math.Round(characters.Count * 0.2f, 1);
                var traitorCount = Math.Max(1, TraitorsEnabled == YesNoMaybe.Maybe ? Rand.Int(max) + 1 : max);
                TraitorManager = new TraitorManager(this, traitorCount);

                if (TraitorManager.TraitorList.Count > 0)
                {
                    for (int i = 0; i < TraitorManager.TraitorList.Count; i++)
                    {
                        Log(TraitorManager.TraitorList[i].Character.Name + " is the traitor and the target is " + TraitorManager.TraitorList[i].TargetCharacter.Name, ServerLog.MessageType.ServerMessage);
                    }
                }
            }

            SendStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.GameMode.Preset, connectedClients);

            yield return CoroutineStatus.Running;
            
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.GameScreen.Select();

            AddChatMessage("Press TAB to chat. Use \"r;\" to talk through the radio.", ChatMessageType.Server);

            GameMain.NetLobbyScreen.StartButtonEnabled = true;

            gameStarted = true;
            initiatedStartGame = false;

            roundStartTime = DateTime.Now;

            yield return CoroutineStatus.Success;
        }

        private void SendStartMessage(int seed, Submarine selectedSub, GameModePreset selectedMode, List<Client> clients)
        {
            foreach (Client client in clients)
            {
                SendStartMessage(seed, selectedSub, selectedMode, client);
            }       
        }

        private void SendStartMessage(int seed, Submarine selectedSub, GameModePreset selectedMode, Client client)
        {
            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)ServerPacketHeader.STARTGAME);

            msg.Write(seed);

            msg.Write(GameMain.GameSession.Level.Seed);

            msg.Write((byte)GameMain.NetLobbyScreen.MissionTypeIndex);

            msg.Write(selectedSub.Name);
            msg.Write(selectedSub.MD5Hash.Hash);
            msg.Write(GameMain.NetLobbyScreen.UsingShuttle);
            msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
            msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.Hash);

            msg.Write(selectedMode.Name);

            MultiPlayerCampaign campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;

            bool missionAllowRespawn = campaign == null &&
                (!(GameMain.GameSession.GameMode is MissionMode) ||
                ((MissionMode)GameMain.GameSession.GameMode).Mission.AllowRespawn);

            msg.Write(AllowRespawn && missionAllowRespawn);
            msg.Write(Submarine.MainSubs[1] != null); //loadSecondSub

            Traitor traitor = null;
            if (TraitorManager != null && TraitorManager.TraitorList.Count > 0)
                traitor = TraitorManager.TraitorList.Find(t => t.Character == client.Character);
            if (traitor != null)
            {
                msg.Write(true);
                msg.Write(traitor.TargetCharacter.Name);
            }
            else
            {
                msg.Write(false);
            }

            //monster spawn settings
            List<string> monsterNames = monsterEnabled.Keys.ToList();
            foreach (string s in monsterNames)
            {
                msg.Write(monsterEnabled[s]);
            }
            msg.WritePadBits();

            server.SendMessage(msg, client.Connection, NetDeliveryMethod.ReliableUnordered);     
        }

        public void EndGame()
        {
            if (!gameStarted) return;

            string endMessage = "The round has ended." + '\n';

            if (TraitorManager != null)
            {
                endMessage += TraitorManager.GetEndMessage();
            }

            Mission mission = GameMain.GameSession.Mission;
            GameMain.GameSession.GameMode.End(endMessage);

            if (autoRestart)
            {
                AutoRestartTimer = AutoRestartInterval;
                //send a netlobby update to get the clients' autorestart timers up to date
                GameMain.NetLobbyScreen.LastUpdateID++;
            }

            if (SaveServerLogs) log.Save();
            
            Character.Controlled = null;
            
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
#if CLIENT
            myCharacter = null;
            GameMain.LightManager.LosEnabled = false;
#endif

            entityEventManager.Clear();
            foreach (Client c in connectedClients)
            {
                c.EntityEventLastSent.Clear();
                c.PendingPositionUpdates.Clear();
            }

#if DEBUG
            messageCount.Clear();
#endif

            respawnManager = null;
            gameStarted = false;

            if (connectedClients.Count > 0)
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write((byte)ServerPacketHeader.ENDGAME);
                msg.Write(endMessage);
                msg.Write(mission != null && mission.Completed);
                if (server.ConnectionsCount > 0)
                {
                    server.SendMessage(msg, server.Connections, NetDeliveryMethod.ReliableOrdered, 0);
                }

                foreach (Client client in connectedClients)
                {
                    client.Character = null;
                    client.InGame = false;
                }
            }

            CoroutineManager.StartCoroutine(EndCinematic(), "EndCinematic");

            GameMain.NetLobbyScreen.RandomizeSettings();
        }
        
        public IEnumerable<object> EndCinematic()
        {
            float endPreviewLength = 10.0f;
            
            var cinematic = new TransitionCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, endPreviewLength);

            do
            {
                yield return CoroutineStatus.Running;
            } while (cinematic.Running);
#if CLIENT
            SoundPlayer.OverrideMusicType = null;
#endif
            Submarine.Unload();
            entityEventManager.Clear();

            GameMain.NetLobbyScreen.Select();

            yield return CoroutineStatus.Success;
        }

        public override void KickPlayer(string playerName, string reason)
        {
            playerName = playerName.ToLowerInvariant();

            Client client = connectedClients.Find(c =>
                c.Name.ToLowerInvariant() == playerName ||
                (c.Character != null && c.Character.Name.ToLowerInvariant() == playerName));

            KickClient(client, reason);
        }
                
        public void KickClient(NetConnection conn, string reason)
        {
            Client client = connectedClients.Find(c => c.Connection == conn);
            KickClient(client, reason);            
        }
        
        public void KickClient(Client client, string reason)
        {
            if (client == null) return;
            
            string msg = "You have been kicked from the server.";
            if (!string.IsNullOrWhiteSpace(reason)) msg += "\nReason: " + reason;
            DisconnectClient(client, client.Name + " has been kicked from the server.", msg);            
        }

        public override void BanPlayer(string playerName, string reason, bool range = false, TimeSpan? duration = null)
        {
            playerName = playerName.ToLowerInvariant();

            Client client = connectedClients.Find(c =>
                c.Name.ToLowerInvariant() == playerName ||
                (c.Character != null && c.Character.Name.ToLowerInvariant() == playerName));

            if (client == null)
            {
                DebugConsole.ThrowError("Client \"" + playerName + "\" not found.");
                return;
            }

            BanClient(client, reason, range, duration);
        }

        public void BanClient(NetConnection conn, string reason, bool range = false, TimeSpan? duration = null)
        {
            Client client = connectedClients.Find(c => c.Connection == conn);
            if (client == null)
            {
                conn.Disconnect("You have been banned from the server");
                if (!banList.IsBanned(conn.RemoteEndPoint.Address.ToString()))
                {
                    banList.BanPlayer("Unnamed", conn.RemoteEndPoint.Address.ToString(), reason, duration);
                }                
            }
            else
            {
                BanClient(client, reason, range);
            }
        }

        public void BanClient(Client client, string reason, bool range = false, TimeSpan? duration = null)
        {
            if (client == null) return;
            
            string msg = "You have been banned from the server.";
            if (!string.IsNullOrWhiteSpace(reason)) msg += "\nReason: " + reason;
            DisconnectClient(client, client.Name + " has been banned from the server.", msg);
            string ip = client.Connection.RemoteEndPoint.Address.ToString();
            if (range) { ip = banList.ToRange(ip); }
            banList.BanPlayer(client.Name, ip, reason, duration);
        }

        public void DisconnectClient(NetConnection senderConnection, string msg = "", string targetmsg = "")
        {
            Client client = connectedClients.Find(x => x.Connection == senderConnection);
            if (client == null) return;

            DisconnectClient(client, msg, targetmsg);
        }

        public void DisconnectClient(Client client, string msg = "", string targetmsg = "")
        {
            if (client == null) return;

            if (gameStarted && client.Character != null)
            {
                client.Character.ClearInputs();
                client.Character.Kill(CauseOfDeathType.Disconnected, null, true);
            }

            client.Character = null;
            client.InGame = false;

            if (string.IsNullOrWhiteSpace(msg)) msg = client.Name + " has left the server";
            if (string.IsNullOrWhiteSpace(targetmsg)) targetmsg = "You have left the server";

            Log(msg, ServerLog.MessageType.ServerMessage);

            client.Connection.Disconnect(targetmsg);
            connectedClients.Remove(client);

#if CLIENT
            GameMain.NetLobbyScreen.RemovePlayer(client.Name);
            Voting.UpdateVoteTexts(connectedClients, VoteType.Sub);
            Voting.UpdateVoteTexts(connectedClients, VoteType.Mode);
#endif

            UpdateVoteStatus();

            SendChatMessage(msg, ChatMessageType.Server);

            UpdateCrewFrame();

            refreshMasterTimer = DateTime.Now;
        }

        private void UpdateCrewFrame()
        {
            foreach (Client c in connectedClients)
            {
                if (c.Character == null || !c.InGame) continue;
            }
        }

        public void SendDirectChatMessage(string txt, Client recipient)
        {
            ChatMessage msg = ChatMessage.Create("", txt, ChatMessageType.Server, null);
            SendDirectChatMessage(msg, recipient);
        }

        public void SendConsoleMessage(string txt, Client recipient)
        {
            ChatMessage msg = ChatMessage.Create("", txt, ChatMessageType.Console, null);
            SendDirectChatMessage(msg, recipient);
        }

        public void SendDirectChatMessage(ChatMessage msg, Client recipient)
        {
            msg.NetStateID = recipient.ChatMsgQueue.Count > 0 ?
                (ushort)(recipient.ChatMsgQueue.Last().NetStateID + 1) :
                (ushort)(recipient.LastRecvChatMsgID + 1);

            recipient.ChatMsgQueue.Add(msg);
            recipient.LastChatMsgQueueID = msg.NetStateID;
        }
        
        /// <summary>
        /// Add the message to the chatbox and pass it to all clients who can receive it
        /// </summary>
        public void SendChatMessage(string message, ChatMessageType? type = null, Client senderClient = null, Character senderCharacter = null)
        {
            string senderName = "";

            Client targetClient = null;

            if (type == null)
            {
                string tempStr;
                string command = ChatMessage.GetChatMessageCommand(message, out tempStr);
                switch (command.ToLowerInvariant())
                {
                    case "r":
                    case "radio":
                        type = ChatMessageType.Radio;
                        break;
                    case "d":
                    case "dead":
                        type = ChatMessageType.Dead;
                        break;
                    default:
                        if (command != "")
                        {
                            if (command == name.ToLowerInvariant())
                            {
                                //a private message to the host
                            }
                            else
                            {
                                targetClient = connectedClients.Find(c =>
                                    command == c.Name.ToLowerInvariant() ||
                                    (c.Character != null && command == c.Character.Name.ToLowerInvariant()));

                                if (targetClient == null)
                                {
                                    if (senderClient != null)
                                    {
                                        var chatMsg = ChatMessage.Create(
                                            "", "Player \"" + command + "\" not found!",
                                            ChatMessageType.Error, null);

                                        chatMsg.NetStateID = senderClient.ChatMsgQueue.Count > 0 ?
                                            (ushort)(senderClient.ChatMsgQueue.Last().NetStateID + 1) :
                                            (ushort)(senderClient.LastRecvChatMsgID + 1);

                                        senderClient.ChatMsgQueue.Add(chatMsg);
                                        senderClient.LastChatMsgQueueID = chatMsg.NetStateID;
                                    }
                                    else
                                    {
                                        AddChatMessage("Player \"" + command + "\" not found!", ChatMessageType.Error);
                                    }

                                    return;
                                }
                            }
                            
                            type = ChatMessageType.Private;
                        }
                        else
                        {
                            type = ChatMessageType.Default;
                        }
                        break;
                }

                message = tempStr;
            }

            if (gameStarted)
            {
                if (senderClient == null)
                {
                    //msg sent by the server
                    if (senderCharacter == null)
                    {
                        senderCharacter = myCharacter;
                        senderName = myCharacter == null ? name : myCharacter.Name;
                    }
                    else //msg sent by an AI character
                    {
                        senderName = senderCharacter.Name;
                    }
                }                
                else //msg sent by a client
                {
                    senderCharacter = senderClient.Character;
                    senderName = senderCharacter == null ? senderClient.Name : senderCharacter.Name;

                    //sender doesn't have an alive character -> only ChatMessageType.Dead allowed
                    if (senderCharacter == null || senderCharacter.IsDead)
                    {
                        type = ChatMessageType.Dead;
                    }
                    else if (type == ChatMessageType.Private)
                    {
                        //sender has an alive character, sending private messages not allowed
                        return;
                    }

                }
            }
            else
            {
                if (senderClient == null)
                {
                    //msg sent by the server
                    if (senderCharacter == null)
                    {
                        senderName = name;
                    }
                    else //sent by an AI character, not allowed when the game is not running
                    {
                        return;
                    }
                }                
                else //msg sent by a client          
                {
                    //game not started -> clients can only send normal and private chatmessages
                    if (type != ChatMessageType.Private) type = ChatMessageType.Default;
                    senderName = senderClient.Name;
                }
            }

            //check if the client is allowed to send the message
            WifiComponent senderRadio = null;
            switch (type)
            {
                case ChatMessageType.Radio:
                case ChatMessageType.Order:
                    if (senderCharacter == null) return;

                    //return if senderCharacter doesn't have a working radio
                    var radio = senderCharacter.Inventory?.Items.FirstOrDefault(i => i != null && i.GetComponent<WifiComponent>() != null);
                    if (radio == null || !senderCharacter.HasEquippedItem(radio)) return;

                    senderRadio = radio.GetComponent<WifiComponent>();
                    if (!senderRadio.CanTransmit()) return;                    
                    break;
                case ChatMessageType.Dead:
                    //character still alive -> not allowed
                    if (senderClient != null && senderCharacter != null && !senderCharacter.IsDead)
                    {
                        return;
                    }
                    break;
            }
            
            if (type == ChatMessageType.Server)
            {
                senderName = null;
                senderCharacter = null;
            }

            //check which clients can receive the message and apply distance effects
            foreach (Client client in ConnectedClients)
            {
                string modifiedMessage = message;

                switch (type)
                {
                    case ChatMessageType.Default:
                    case ChatMessageType.Radio:
                    case ChatMessageType.Order:
                        if (senderCharacter != null && 
                            client.Character != null && !client.Character.IsDead)
                        {
                            modifiedMessage = ChatMessage.ApplyDistanceEffect(message, (ChatMessageType)type, senderCharacter, client.Character);

                            //too far to hear the msg -> don't send
                            if (string.IsNullOrWhiteSpace(modifiedMessage)) continue;
                        }
                        break;
                    case ChatMessageType.Dead:
                        //character still alive -> don't send
                        if (client.Character != null && !client.Character.IsDead) continue;
                        break;
                    case ChatMessageType.Private:
                        //private msg sent to someone else than this client -> don't send
                        if (client != targetClient && client != senderClient) continue;
                        break;
                }
                
                var chatMsg = ChatMessage.Create(
                    senderName,
                    modifiedMessage, 
                    (ChatMessageType)type,
                    senderCharacter);

                SendDirectChatMessage(chatMsg, client);
            }

            string myReceivedMessage = message;
            if (gameStarted && myCharacter != null && senderCharacter != null)
            {
                myReceivedMessage = ChatMessage.ApplyDistanceEffect(message, (ChatMessageType)type, senderCharacter, myCharacter);
            }

            if (!string.IsNullOrWhiteSpace(myReceivedMessage) && 
                (targetClient == null || senderClient == null))
            {
                AddChatMessage(myReceivedMessage, (ChatMessageType)type, senderName, senderCharacter); 
            }       
        }
        
        public void SendOrderChatMessage(OrderChatMessage message, Client senderClient = null)
        {
            if (message.Sender == null || !message.Sender.CanSpeak) return;            

            //check which clients can receive the message and apply distance effects
            foreach (Client client in ConnectedClients)
            {
                string modifiedMessage = message.Text;
                
                if (message.Sender != null &&
                    client.Character != null && !client.Character.IsDead)
                {
                    modifiedMessage = ChatMessage.ApplyDistanceEffect(message.Text, ChatMessageType.Radio, message.Sender, client.Character);

                    //too far to hear the msg -> don't send
                    if (string.IsNullOrWhiteSpace(modifiedMessage)) continue;
                }

                var modifiedChatMsg = new OrderChatMessage(
                    message.Order, message.OrderOption, 
                    message.TargetEntity, message.TargetCharacter, message.Sender);
                    
                SendDirectChatMessage(modifiedChatMsg, client);
            }

            string myReceivedMessage = message.Text;
            if (gameStarted && myCharacter != null)
            {
                myReceivedMessage = ChatMessage.ApplyDistanceEffect(message.Text, ChatMessageType.Radio, message.Sender, myCharacter);
            }

            if (!string.IsNullOrWhiteSpace(myReceivedMessage))
            {
                AddChatMessage(myReceivedMessage, ChatMessageType.Order, message.SenderName, message.Sender);
            }
        }

        private void FileTransferChanged(FileSender.FileTransferOut transfer)
        {
            Client recipient = connectedClients.Find(c => c.Connection == transfer.Connection);
#if CLIENT
            UpdateFileTransferIndicator(recipient);
#endif
        }

        public void SendCancelTransferMsg(FileSender.FileTransferOut transfer)
        {
            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)ServerPacketHeader.FILE_TRANSFER);
            msg.Write((byte)FileTransferMessageType.Cancel);
            msg.Write((byte)transfer.SequenceChannel);
            server.SendMessage(msg, transfer.Connection, NetDeliveryMethod.ReliableOrdered, transfer.SequenceChannel);
        }

        public void UpdateVoteStatus()
        {
            if (server.Connections.Count == 0|| connectedClients.Count == 0) return;

            Client.UpdateKickVotes(connectedClients);

            var clientsToKick = connectedClients.FindAll(c => c.KickVoteCount >= connectedClients.Count * KickVoteRequiredRatio);
            foreach (Client c in clientsToKick)
            {
                SendChatMessage(c.Name + " has been kicked from the server.", ChatMessageType.Server, null);
                KickClient(c, "Kicked by vote");
            }

            GameMain.NetLobbyScreen.LastUpdateID++;
            
            SendVoteStatus(connectedClients);

            if (Voting.AllowEndVoting && EndVoteMax > 0 &&
                ((float)EndVoteCount / (float)EndVoteMax) >= EndVoteRequiredRatio)
            {
                Log("Ending round by votes (" + EndVoteCount + "/" + (EndVoteMax - EndVoteCount) + ")", ServerLog.MessageType.ServerMessage);
                EndGame();
            }
        }

        public void SendVoteStatus(List<Client> recipients)
        {
            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)ServerPacketHeader.UPDATE_LOBBY);
            msg.Write((byte)ServerNetObject.VOTE);
            Voting.ServerWrite(msg);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            server.SendMessage(msg, recipients.Select(c => c.Connection).ToList(), NetDeliveryMethod.ReliableUnordered, 0);
        }

        public void UpdateClientPermissions(Client client)
        {           
            clientPermissions.RemoveAll(cp => cp.IP == client.Connection.RemoteEndPoint.Address.ToString());

            if (client.Permissions != ClientPermissions.None)
            {
                clientPermissions.Add(new SavedClientPermission(
                    client.Name, 
                    client.Connection.RemoteEndPoint.Address.ToString(), 
                    client.Permissions,
                    client.PermittedConsoleCommands));
            }

            var msg = server.CreateMessage();
            msg.Write((byte)ServerPacketHeader.PERMISSIONS);
            WritePermissions(msg, client);

            server.SendMessage(msg, client.Connection, NetDeliveryMethod.ReliableUnordered);

            SaveClientPermissions();
        }

        private void WritePermissions(NetBuffer msg, Client client)
        {
            msg.Write((byte)client.Permissions);
            if (client.Permissions.HasFlag(ClientPermissions.ConsoleCommands))
            {
                msg.Write((UInt16)client.PermittedConsoleCommands.Sum(c => c.names.Length));
                foreach (DebugConsole.Command command in client.PermittedConsoleCommands)
                {
                    foreach (string commandName in command.names)
                    {
                        msg.Write(commandName);
                    }
                }
            }
        }
        
        public void SetClientCharacter(Client client, Character newCharacter)
        {
            if (client == null) return;

            //the client's previous character is no longer a remote player
            if (client.Character != null)
            {
                client.Character.IsRemotePlayer = false;
            }
            
            if (newCharacter == null)
            {
                if (client.Character != null) //removing control of the current character
                {
                    CreateEntityEvent(client.Character, new object[] { NetEntityEvent.Type.Control, null });
                    client.Character = null;
                }

            }
            else //taking control of a new character
            {
                newCharacter.ResetNetState();
                if (client.Character != null)
                {
                    newCharacter.LastNetworkUpdateID = client.Character.LastNetworkUpdateID;
                }

                newCharacter.IsRemotePlayer = true;
                newCharacter.Enabled = true;
                client.Character = newCharacter;
                CreateEntityEvent(newCharacter, new object[] { NetEntityEvent.Type.Control, client });
            }
        }

        private void UpdateCharacterInfo(NetIncomingMessage message, Client sender)
        {
            sender.SpectateOnly = message.ReadBoolean() && AllowSpectating;
            if (sender.SpectateOnly)
            {
                return;
            }

            Gender gender = Gender.Male;
            int headSpriteId = 0;
            try
            {
                gender = message.ReadBoolean() ? Gender.Male : Gender.Female;
                headSpriteId = message.ReadByte();
            }
            catch (Exception e)
            {
                gender = Gender.Male;
                headSpriteId = 0;

                DebugConsole.Log("Received invalid characterinfo from \"" + sender.Name + "\"! { " + e.Message + " }");
            }

            List<JobPrefab> jobPreferences = new List<JobPrefab>();
            int count = message.ReadByte();
            for (int i = 0; i < Math.Min(count, 3); i++)
            {
                string jobName = message.ReadString();

                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);
                if (jobPrefab != null) jobPreferences.Add(jobPrefab);
            }

            sender.CharacterInfo = new CharacterInfo(Character.HumanConfigFile, sender.Name, gender);
            sender.CharacterInfo.HeadSpriteId = headSpriteId;
            sender.JobPreferences = jobPreferences;
        }
        
        public void AssignJobs(List<Client> unassigned, bool assignHost)
        {
            unassigned = new List<Client>(unassigned);
            
            Dictionary<JobPrefab, int> assignedClientCount = new Dictionary<JobPrefab, int>();
            foreach (JobPrefab jp in JobPrefab.List)
            {
                assignedClientCount.Add(jp, 0);
            }

            int teamID = 0;
            if (unassigned.Count > 0) teamID = unassigned[0].TeamID;
            
            if (assignHost)
            {
                if (characterInfo != null)
                {
                    assignedClientCount[GameMain.NetLobbyScreen.JobPreferences[0]] = 1;                
                }
                else if (myCharacter?.Info?.Job != null && !myCharacter.IsDead)
                {
                    assignedClientCount[myCharacter.Info.Job.Prefab] = 1;  
                }
            }
            else if (myCharacter?.Info?.Job != null && !myCharacter.IsDead && myCharacter.TeamID == teamID)
            {
                assignedClientCount[myCharacter.Info.Job.Prefab]++;
            }

            //count the clients who already have characters with an assigned job
            foreach (Client c in connectedClients)
            {
                if (c.TeamID != teamID || unassigned.Contains(c)) continue;
                if (c.Character?.Info?.Job != null && !c.Character.IsDead)
                {
                    assignedClientCount[c.Character.Info.Job.Prefab]++;
                }
            }

            //if any of the players has chosen a job that is Always Allowed, give them that job
            for (int i = unassigned.Count - 1; i >= 0; i--)
            {
                if (!unassigned[i].JobPreferences[0].AllowAlways) continue;
                unassigned[i].AssignedJob = unassigned[i].JobPreferences[0];
                unassigned.RemoveAt(i);
            }

            //go throught the jobs whose MinNumber>0 (i.e. at least one crew member has to have the job)
            bool unassignedJobsFound = true;
            while (unassignedJobsFound && unassigned.Count > 0)
            {
                unassignedJobsFound = false;

                foreach (JobPrefab jobPrefab in JobPrefab.List)
                {
                    if (unassigned.Count == 0) break;
                    if (jobPrefab.MinNumber < 1 || assignedClientCount[jobPrefab] >= jobPrefab.MinNumber) continue;

                    //find the client that wants the job the most, or force it to random client if none of them want it
                    Client assignedClient = FindClientWithJobPreference(unassigned, jobPrefab, true);

                    assignedClient.AssignedJob = jobPrefab;
                    assignedClientCount[jobPrefab]++;
                    unassigned.Remove(assignedClient);

                    //the job still needs more crew members, set unassignedJobsFound to true to keep the while loop running
                    if (assignedClientCount[jobPrefab] < jobPrefab.MinNumber) unassignedJobsFound = true;
                }
            }

            //find a suitable job for the rest of the players
            foreach (Client c in unassigned)
            {
                foreach (JobPrefab preferredJob in c.JobPreferences)
                {
                    //the maximum number of players that can have this job hasn't been reached yet
                    // -> assign it to the client
                    if (assignedClientCount[preferredJob] < preferredJob.MaxNumber && c.Karma >= preferredJob.MinKarma)
                    {
                        c.AssignedJob = preferredJob;
                        assignedClientCount[preferredJob]++;
                        break;
                    }
                    //none of the jobs the client prefers are available anymore
                    else if (preferredJob == c.JobPreferences.Last())
                    {
                        //find all jobs that are still available
                        var remainingJobs = JobPrefab.List.FindAll(jp => assignedClientCount[preferredJob] < jp.MaxNumber && c.Karma >= jp.MinKarma);

                        //all jobs taken, give a random job
                        if (remainingJobs.Count == 0)
                        {
                            DebugConsole.ThrowError("Failed to assign a suitable job for \"" + c.Name + "\" (all jobs already have the maximum numbers of players). Assigning a random job...");
                            int jobIndex = Rand.Range(0,JobPrefab.List.Count);
                            int skips = 0;
                            while (c.Karma < JobPrefab.List[jobIndex].MinKarma)
                            {
                                jobIndex++;
                                skips++;
                                if (jobIndex >= JobPrefab.List.Count) jobIndex -= JobPrefab.List.Count;
                                if (skips >= JobPrefab.List.Count) break;
                            }
                            c.AssignedJob = JobPrefab.List[jobIndex];
                            assignedClientCount[c.AssignedJob]++;
                        }
                        else //some jobs still left, choose one of them by random
                        {
                            c.AssignedJob = remainingJobs[Rand.Range(0, remainingJobs.Count)];
                            assignedClientCount[c.AssignedJob]++;
                        }
                    }
                }
            }
        }

        private Client FindClientWithJobPreference(List<Client> clients, JobPrefab job, bool forceAssign = false)
        {
            int bestPreference = 0;
            Client preferredClient = null;
            foreach (Client c in clients)
            {
                if (c.Karma < job.MinKarma) continue;
                int index = c.JobPreferences.IndexOf(job);
                if (index == -1) index = 1000;

                if (preferredClient == null || index < bestPreference)
                {
                    bestPreference = index;
                    preferredClient = c;
                }
            }

            //none of the clients wants the job, assign it to random client
            if (forceAssign && preferredClient == null)
            {
                preferredClient = clients[Rand.Int(clients.Count)];
            }

            return preferredClient;
        }

        public static void Log(string line, ServerLog.MessageType messageType)
        {
            if (GameMain.Server == null || !GameMain.Server.SaveServerLogs) return;

            GameMain.Server.log.WriteLine(line, messageType);
        }

        public override void Disconnect()
        {
            banList.Save();
            SaveSettings();

            if (registeredToMaster && restClient != null)
            {
                var request = new RestRequest("masterserver2.php", Method.GET);
                request.AddParameter("action", "removeserver");
                request.AddParameter("serverport", Port);
                
                restClient.Execute(request);
                restClient = null;
            }

            if (SaveServerLogs)
            {
                Log("Shutting down the server...", ServerLog.MessageType.ServerMessage);
                log.Save();
            }
                        
            server.Shutdown("The server has been shut down");
        }
    }
}
