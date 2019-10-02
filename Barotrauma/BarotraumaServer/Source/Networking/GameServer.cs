#define ALLOW_BOT_TRAITORS
using Barotrauma.Items.Components;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO.Compression;
using System.IO;
using Barotrauma.Steam;
using System.Xml.Linq;
using System.Threading;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        public override bool IsServer
        {
            get { return true; }
        }

        private List<Client> connectedClients = new List<Client>();

        //for keeping track of disconnected clients in case the reconnect shortly after
        private List<Client> disconnectedClients = new List<Client>();

        //keeps track of players who've previously been playing on the server
        //so kick votes persist during the session and the server can let the clients know what name this client used previously
        private readonly List<PreviousPlayer> previousPlayers = new List<PreviousPlayer>();

        private int roundStartSeed;

        //is the server running
        private bool started;

        private ServerPeer serverPeer;
        public ServerPeer ServerPeer { get { return serverPeer; } }

        private DateTime refreshMasterTimer;
        private TimeSpan refreshMasterInterval = new TimeSpan(0, 0, 60);
        private bool registeredToMaster;

        private DateTime roundStartTime;

        private RestClient restClient;
        private bool masterServerResponded;
        private IRestResponse masterServerResponse;

        private bool autoRestartTimerRunning;
        private float endRoundTimer;

        public VoipServer VoipServer
        {
            get;
            private set;
        }

        private bool initiatedStartGame;
        private CoroutineHandle startGameCoroutine;

        public TraitorManager TraitorManager;

        private ServerEntityEventManager entityEventManager;

        private FileSender fileSender;
#if DEBUG
        public void PrintSenderTransters()
        {
            foreach (var transfer in fileSender.ActiveTransfers)
            {
                DebugConsole.NewMessage(transfer.FileName + " " + transfer.Progress.ToString());
            }
        }
#endif

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

        public TimeSpan UpdateInterval
        {
            get { return updateInterval; }
        }

        public int Port => serverSettings?.Port ?? 0;

        //only used when connected to steam
        public int QueryPort => serverSettings?.QueryPort ?? 0;

        public NetworkConnection OwnerConnection { get; private set; }
        private int? ownerKey;
        private UInt64? ownerSteamId;

        public GameServer(string name, int port, int queryPort = 0, bool isPublic = false, string password = "", bool attemptUPnP = false, int maxPlayers = 10, int? ownKey = null, UInt64? steamId = null)
        {
            name = name.Replace(":", "");
            name = name.Replace(";", "");
            if (name.Length > NetConfig.ServerNameMaxLength)
            {
                name = name.Substring(0, NetConfig.ServerNameMaxLength);
            }

            this.name = name;

            LastClientListUpdateID = 0;

            serverSettings = new ServerSettings(this, name, port, queryPort, maxPlayers, isPublic, attemptUPnP);
            if (!string.IsNullOrEmpty(password))
            {
                serverSettings.SetPassword(password);
            }

            ownerKey = ownKey;

            ownerSteamId = steamId;

            entityEventManager = new ServerEntityEventManager(this);

            CoroutineManager.StartCoroutine(StartServer(isPublic));
        }

        private IEnumerable<object> StartServer(bool isPublic)
        {
            bool error = false;
            try
            {
                Log("Starting the server...", ServerLog.MessageType.ServerMessage);
                if (!ownerSteamId.HasValue || ownerSteamId.Value == 0)
                {
                    Log("Using Lidgren networking", ServerLog.MessageType.ServerMessage);
                    serverPeer = new LidgrenServerPeer(ownerKey, serverSettings);
                }
                else
                {
                    Log("Using SteamP2P", ServerLog.MessageType.ServerMessage);
                    serverPeer = new SteamP2PServerPeer(ownerSteamId.Value, serverSettings);
                }

                serverPeer.OnInitializationComplete = OnInitializationComplete;
                serverPeer.OnMessageReceived = ReadDataMessage;
                serverPeer.OnDisconnect = OnClientDisconnect;
                serverPeer.OnShutdown = GameMain.Instance.CloseServer;
                serverPeer.OnOwnerDetermined = OnOwnerDetermined;

                fileSender = new FileSender(serverPeer, MsgConstants.MTU);
                fileSender.OnEnded += FileTransferChanged;
                fileSender.OnStarted += FileTransferChanged;

                serverPeer.Start();

                VoipServer = new VoipServer(serverPeer);
            }
            catch (Exception e)
            {
                Log("Error while starting the server (" + e.Message + ")", ServerLog.MessageType.Error);

                System.Net.Sockets.SocketException socketException = e as System.Net.Sockets.SocketException;

                error = true;
            }

            if (error)
            {
                if (serverPeer != null) serverPeer.Close("Error while starting the server");

                Environment.Exit(-1);

                yield return CoroutineStatus.Success;
            }


            if (serverPeer is LidgrenServerPeer)
            {
                if (SteamManager.USE_STEAM)
                {
                    registeredToMaster = SteamManager.CreateServer(this, isPublic);
                }
                if (isPublic && !GameMain.Config.UseSteamMatchmaking)
                {
                    CoroutineManager.StartCoroutine(RegisterToMasterServer());
                }
            }

            TickRate = serverSettings.TickRate;

            Log("Server started", ServerLog.MessageType.ServerMessage);

            GameMain.NetLobbyScreen.Select();
            GameMain.NetLobbyScreen.RandomizeSettings();
            started = true;

            GameAnalyticsManager.AddDesignEvent("GameServer:Start");

            yield return CoroutineStatus.Success;
        }

        private void OnOwnerDetermined(NetworkConnection connection)
        {
            OwnerConnection = connection;

            var ownerClient = ConnectedClients.Find(c => c.Connection == connection);
            if (ownerClient == null)
            {
                DebugConsole.ThrowError("Owner client not found! Can't set permissions");
                return;
            }
            ownerClient.SetPermissions(ClientPermissions.All, DebugConsole.Commands);
            UpdateClientPermissions(ownerClient);
        }

        public void NotifyCrash()
        {
            var tempList = ConnectedClients.Where(c => c.Connection != OwnerConnection).ToList();
            foreach (var c in tempList)
            {
                DisconnectClient(c.Connection, DisconnectReason.ServerCrashed.ToString(), DisconnectReason.ServerCrashed.ToString());
            }
            if (OwnerConnection != null)
            {
                var conn = OwnerConnection; OwnerConnection = null;
                DisconnectClient(conn, DisconnectReason.ServerCrashed.ToString(), DisconnectReason.ServerCrashed.ToString());
            }
            Thread.Sleep(500);
        }

        private void OnInitializationComplete(NetworkConnection connection)
        {
            string clName = connection.Name;
            Client newClient = new Client(clName, GetNewClientID());
            newClient.InitClientSync();
            newClient.Connection = connection;
            newClient.SteamID = connection.SteamID;
            ConnectedClients.Add(newClient);

            var previousPlayer = previousPlayers.Find(p => p.MatchesClient(newClient));
            if (previousPlayer != null)
            {
                newClient.Karma = previousPlayer.Karma;
                newClient.KarmaKickCount = previousPlayer.KarmaKickCount;
                foreach (Client c in previousPlayer.KickVoters)
                {
                    if (!connectedClients.Contains(c)) { continue; }
                    newClient.AddKickVote(c);
                }
            }

            LastClientListUpdateID++;

            if (newClient.Connection == OwnerConnection)
            {
                newClient.GivePermission(ClientPermissions.All);
                newClient.PermittedConsoleCommands.AddRange(DebugConsole.Commands);
                SendConsoleMessage("Granted all permissions to " + newClient.Name + ".", newClient);
            }

            SendChatMessage($"ServerMessage.JoinedServer~[client]={clName}", ChatMessageType.Server, null);
            serverSettings.ServerDetailsChanged = true;

            if (previousPlayer != null && previousPlayer.Name != newClient.Name)
            {
                SendChatMessage($"ServerMessage.PreviousClientName~[client]={clName}~[previousname]={previousPlayer.Name}", ChatMessageType.Server, null);
                previousPlayer.Name = newClient.Name;
            }

            var savedPermissions = serverSettings.ClientPermissions.Find(cp =>
                cp.SteamID > 0 ?
                cp.SteamID == newClient.SteamID :
                newClient.EndpointMatches(cp.EndPoint));

            if (savedPermissions != null)
            {
                newClient.SetPermissions(savedPermissions.Permissions, savedPermissions.PermittedCommands);
            }
            else
            {
                var defaultPerms = PermissionPreset.List.Find(p => p.Name == "None");
                if (defaultPerms != null)
                {
                    newClient.SetPermissions(defaultPerms.Permissions, defaultPerms.PermittedCommands);
                }
                else
                {
                    newClient.SetPermissions(ClientPermissions.None, new List<DebugConsole.Command>());
                }
            }

            UpdateClientPermissions(newClient);
        }

        private void OnClientDisconnect(NetworkConnection connection, string disconnectMsg)
        {
            Client connectedClient = connectedClients.Find(c => c.Connection == connection);

            DisconnectClient(connectedClient, reason: disconnectMsg);
        }

        private IEnumerable<object> RegisterToMasterServer()
        {
            if (restClient == null)
            {
                restClient = new RestClient(NetConfig.MasterServerUrl);
            }

            var request = new RestRequest("masterserver3.php", Method.GET);
            request.AddParameter("action", "addserver");
            request.AddParameter("servername", name);
            request.AddParameter("serverport", Port);
            request.AddParameter("currplayers", connectedClients.Count);
            request.AddParameter("maxplayers", serverSettings.MaxPlayers);
            request.AddParameter("password", serverSettings.HasPassword ? 0 : 1);
            request.AddParameter("version", GameMain.Version.ToString());
            if (GameMain.Config.SelectedContentPackages.Count > 0)
            {
                request.AddParameter("contentpackages", string.Join(",", GameMain.Config.SelectedContentPackages.Select(cp => cp.Name)));
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
            request.AddParameter("maxplayers", serverSettings.MaxPlayers);

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

            System.Diagnostics.Debug.WriteLine("took " + sw.ElapsedMilliseconds + " ms");

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
#endif
            if (!started) return;

            base.Update(deltaTime);

            fileSender.Update(deltaTime);
            KarmaManager.UpdateClients(ConnectedClients, deltaTime);

            if (serverSettings.VoiceChatEnabled)
            {
                VoipServer.SendToClients(connectedClients);
            }

            if (gameStarted)
            {
                if (respawnManager != null) { respawnManager.Update(deltaTime); }

                entityEventManager.Update(connectedClients);

                //go through the characters backwards to give rejoining clients control of the latest created character
                for (int i = Character.CharacterList.Count - 1; i >= 0; i--)
                {
                    Character character = Character.CharacterList[i];
                    if (character.IsDead || !character.ClientDisconnected) continue;

                    character.KillDisconnectedTimer += deltaTime;
                    character.SetStun(1.0f);
                    if (character.KillDisconnectedTimer > serverSettings.KillDisconnectedTime)
                    {
                        character.Kill(CauseOfDeathType.Disconnected, null);
                        continue;
                    }

                    Client owner = connectedClients.Find(c =>
                        c.InGame && !c.NeedsMidRoundSync &&
                        c.Name == character.OwnerClientName &&
                        c.EndpointMatches(character.OwnerClientEndPoint));

                    if (owner != null && (!serverSettings.AllowSpectating || !owner.SpectateOnly))
                    {
                        SetClientCharacter(owner, character);
                    }
                }

                if (TraitorManager != null)
                {
                    TraitorManager.Update(deltaTime);
                }

                bool isCrewDead =
                    connectedClients.All(c => c.Character == null || c.Character.IsDead || c.Character.IsUnconscious);

                bool subAtLevelEnd = false;
                if (Submarine.MainSub != null && Submarine.MainSubs[1] == null)
                {
                    if (Level.Loaded?.EndOutpost != null)
                    {
                        bool charactersInsideOutpost = connectedClients.Any(c =>
                            c.Character != null &&
                            !c.Character.IsDead &&
                            c.Character.Submarine == Level.Loaded.EndOutpost);

                        //level finished if the sub is docked to the outpost
                        //or very close and someone from the crew made it inside the outpost
                        subAtLevelEnd =
                            Submarine.MainSub.DockedTo.Contains(Level.Loaded.EndOutpost) ||
                            (Submarine.MainSub.AtEndPosition && charactersInsideOutpost);
                    }
                    else
                    {
                        subAtLevelEnd = Submarine.MainSub.AtEndPosition;
                    }
                }

                float endRoundDelay = 1.0f;
                if (TraitorManager?.ShouldEndRound ?? false)
                {
                    endRoundDelay = 5.0f;
                    endRoundTimer += deltaTime;
                }
                else if (serverSettings.AutoRestart && isCrewDead)
                {
                    endRoundDelay = 5.0f;
                    endRoundTimer += deltaTime;
                }
                else if (serverSettings.EndRoundAtLevelEnd && subAtLevelEnd)
                {
                    endRoundDelay = 5.0f;
                    endRoundTimer += deltaTime;
                }
                else if (isCrewDead && respawnManager == null)
                {
                    if (endRoundTimer <= 0.0f)
                    {
                        SendChatMessage(TextManager.GetWithVariable("CrewDeadNoRespawns", "[time]", "60"), ChatMessageType.Server);
                    }
                    endRoundDelay = 60.0f;
                    endRoundTimer += deltaTime;
                }
                else
                {
                    endRoundTimer = 0.0f;
                }

                if (endRoundTimer >= endRoundDelay)
                {
                    if (TraitorManager?.ShouldEndRound ?? false)
                    {
                        Log("Ending round (a traitor completed their mission)", ServerLog.MessageType.ServerMessage);
                    }
                    else if (serverSettings.AutoRestart && isCrewDead)
                    {
                        Log("Ending round (entire crew dead)", ServerLog.MessageType.ServerMessage);
                    }
                    else if (serverSettings.EndRoundAtLevelEnd && subAtLevelEnd)
                    {
                        Log("Ending round (submarine reached the end of the level)", ServerLog.MessageType.ServerMessage);
                    }
                    else
                    {
                        Log("Ending round (no living players left and respawning is not enabled during this round)", ServerLog.MessageType.ServerMessage);
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
                    if (serverSettings.AutoRestart) serverSettings.AutoRestartTimer = Math.Max(serverSettings.AutoRestartInterval, 5.0f);
                    //GameMain.NetLobbyScreen.StartButtonEnabled = true;

                    GameMain.NetLobbyScreen.LastUpdateID++;

                    startGameCoroutine = null;
                    initiatedStartGame = false;
                }
            }
            else if (Screen.Selected == GameMain.NetLobbyScreen && !gameStarted && !initiatedStartGame)
            {
                if (serverSettings.AutoRestart)
                {
                    //autorestart if there are any non-spectators on the server (ignoring the server owner)
                    bool shouldAutoRestart = connectedClients.Any(c =>
                        c.Connection != OwnerConnection &&
                        (!c.SpectateOnly || !serverSettings.AllowSpectating));

                    if (shouldAutoRestart != autoRestartTimerRunning)
                    {
                        autoRestartTimerRunning = shouldAutoRestart;
                        GameMain.NetLobbyScreen.LastUpdateID++;
                    }

                    if (autoRestartTimerRunning)
                    {
                        serverSettings.AutoRestartTimer -= deltaTime;
                    }
                }

                if (serverSettings.AutoRestart && autoRestartTimerRunning && serverSettings.AutoRestartTimer < 0.0f)
                {
                    StartGame();
                }
                else if (serverSettings.StartWhenClientsReady)
                {
                    int clientsReady = connectedClients.Count(c => c.GetVote<bool>(VoteType.StartRound));
                    if (clientsReady / (float)connectedClients.Count >= serverSettings.StartWhenClientsReadyRatio)
                    {
                        StartGame();
                    }
                }
            }

            for (int i = disconnectedClients.Count - 1; i >= 0; i--)
            {
                disconnectedClients[i].DeleteDisconnectedTimer -= deltaTime;
                if (disconnectedClients[i].DeleteDisconnectedTimer > 0.0f) continue;

                if (gameStarted && disconnectedClients[i].Character != null)
                {
                    disconnectedClients[i].Character.Kill(CauseOfDeathType.Disconnected, null);
                    disconnectedClients[i].Character = null;
                }

                disconnectedClients.RemoveAt(i);
            }

            foreach (Client c in connectedClients)
            {
                //slowly reset spam timers
                c.ChatSpamTimer = Math.Max(0.0f, c.ChatSpamTimer - deltaTime);
                c.ChatSpamSpeed = Math.Max(0.0f, c.ChatSpamSpeed - deltaTime);

                //constantly increase AFK timer if the client is controlling a character (gets reset to zero every time an input is received)
                if (gameStarted && c.Character != null && !c.Character.IsDead && !c.Character.IsUnconscious)
                {
                    if (c.Connection != OwnerConnection) c.KickAFKTimer += deltaTime;
                }
            }

            IEnumerable<Client> kickAFK = connectedClients.FindAll(c =>
                c.KickAFKTimer >= serverSettings.KickAFKTime &&
                (OwnerConnection == null || c.Connection != OwnerConnection));
            foreach (Client c in kickAFK)
            {
                KickClient(c, "DisconnectMessage.AFK");
            }

            serverPeer.Update(deltaTime);

            // if update interval has passed
            if (updateTimer < DateTime.Now)
            {
                if (ConnectedClients.Count > 0)
                {
                    foreach (Client c in ConnectedClients)
                    {
                        try
                        {
                            ClientWrite(c);
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError("Failed to write a network message for the client \"" + c.Name + "\"!", e);

                            string errorMsg = "Failed to write a network message for the client \"" + c.Name + "\"! (MidRoundSyncing: " + c.NeedsMidRoundSync + ")\n"
                                + e.Message + "\n" + e.StackTrace;
                            if (e.InnerException != null)
                            {
                                errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace;
                            }

                            GameAnalyticsManager.AddErrorEventOnce(
                                "GameServer.Update:ClientWriteFailed" + e.StackTrace,
                                GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                                errorMsg);
                        }
                    }

                    foreach (Character character in Character.CharacterList)
                    {
                        if (character.healthUpdateTimer <= 0.0f)
                        {
                            character.healthUpdateTimer = character.HealthUpdateInterval;
                        }
                        else
                        {
                            character.healthUpdateTimer -= (float)UpdateInterval.TotalSeconds;
                        }
                        character.HealthUpdateInterval += (float)UpdateInterval.TotalSeconds;
                    }
                }

                updateTimer = DateTime.Now + updateInterval;
            }

            if (registeredToMaster && (DateTime.Now > refreshMasterTimer || serverSettings.ServerDetailsChanged))
            {
                if (GameMain.Config.UseSteamMatchmaking)
                {
                    bool refreshSuccessful = SteamManager.RefreshServerDetails(this);
                    if (GameSettings.VerboseLogging)
                    {
                        Log(refreshSuccessful ?
                            "Refreshed server info on the server list." :
                            "Refreshing server info on the server list failed.", ServerLog.MessageType.ServerMessage);
                    }
                }
                else
                {
                    CoroutineManager.StartCoroutine(RefreshMaster());
                }
                refreshMasterTimer = DateTime.Now + refreshMasterInterval;
                serverSettings.ServerDetailsChanged = false;
            }
        }

        private void ReadDataMessage(NetworkConnection sender, IReadMessage inc)
        {
            var connectedClient = connectedClients.Find(c => c.Connection == sender);

            ClientPacketHeader header = (ClientPacketHeader)inc.ReadByte();
            switch (header)
            {
                case ClientPacketHeader.RESPONSE_STARTGAME:
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
                case ClientPacketHeader.CAMPAIGN_SETUP_INFO:
                    bool isNew = inc.ReadBoolean(); inc.ReadPadBits();
                    if (isNew)
                    {
                        string saveName = inc.ReadString();
                        string seed = inc.ReadString();
                        string subName = inc.ReadString();
                        string subHash = inc.ReadString();

                        var matchingSub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.Hash == subHash);

                        if (matchingSub == null)
                        {
                            SendDirectChatMessage(
                                TextManager.GetWithVariable("CampaignStartFailedSubNotFound", "[subname]", subName),
                                connectedClient, ChatMessageType.MessageBox);
                        }
                        else
                        {
                            string localSavePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveName);
                            if (connectedClient.HasPermission(ClientPermissions.SelectMode))
                            {
                                MultiPlayerCampaign.StartNewCampaign(localSavePath, matchingSub.FilePath, seed);
                            }
                        }
                     }
                    else
                    {
                        string saveName = inc.ReadString();
                        if (connectedClient.HasPermission(ClientPermissions.SelectMode)) MultiPlayerCampaign.LoadCampaign(saveName);
                    }
                    break;
                case ClientPacketHeader.VOICE:
                    if (serverSettings.VoiceChatEnabled && !connectedClient.Muted)
                    {
                        byte id = inc.ReadByte();
                        if (connectedClient.ID != id)
                        {
#if DEBUG
                            DebugConsole.ThrowError(
                                "Client \"" + connectedClient.Name + "\" sent a VOIP update that didn't match its ID (" + id.ToString() + "!=" + connectedClient.ID.ToString() + ")");
#endif
                            return;
                        }
                        connectedClient.VoipQueue.Read(inc);
                    }
                    break;
                case ClientPacketHeader.SERVER_SETTINGS:
                    serverSettings.ServerRead(inc, connectedClient);
                    break;
                case ClientPacketHeader.SERVER_COMMAND:
                    ClientReadServerCommand(inc);
                    break;
                case ClientPacketHeader.FILE_REQUEST:
                    if (serverSettings.AllowFileTransfers)
                    {
                        fileSender.ReadFileRequest(inc, connectedClient);
                    }
                    break;
                case ClientPacketHeader.ERROR:
                    HandleClientError(inc, connectedClient);
                    break;
            }
        }

        private void HandleClientError(IReadMessage inc, Client c)
        {
            string errorStr = "Unhandled error report";

            ClientNetError error = (ClientNetError)inc.ReadByte();
            int levelEqualityCheckVal = inc.ReadInt32();
            switch (error)
            {
                case ClientNetError.MISSING_EVENT:
                    UInt16 expectedID = inc.ReadUInt16();
                    UInt16 receivedID = inc.ReadUInt16();
                    errorStr = "Expecting event id " + expectedID.ToString() + ", received " + receivedID.ToString();
                    break;
                case ClientNetError.MISSING_ENTITY:
                    UInt16 eventID = inc.ReadUInt16();
                    UInt16 entityID = inc.ReadUInt16();
                    Entity entity = Entity.FindEntityByID(entityID);
                    if (entity == null)
                    {
                        errorStr = "Received an update for an entity that doesn't exist (event id " + eventID.ToString() + ", entity id " + entityID.ToString() + ").";
                    }
                    else if (entity is Character character)
                    {
                        errorStr = "Missing character " + character.Name + " (event id " + eventID.ToString() + ", entity id " + entityID.ToString() + ").";
                    }
                    else if (entity is Item item)
                    {
                        errorStr = "Missing item " + item.Name + " (event id " + eventID.ToString() + ", entity id " + entityID.ToString() + ").";
                    }
                    else
                    {
                        errorStr = "Missing entity " + entity.ToString() + " (event id " + eventID.ToString() + ", entity id " + entityID.ToString() + ").";
                    }
                    break;
            }

            if (Level.Loaded != null && levelEqualityCheckVal != Level.Loaded.EqualityCheckVal)
            {
                errorStr += " Level equality check failed. The level generated at your end doesn't match the level generated by the server (seed " + Level.Loaded.Seed + ").";
            }

            Log(c.Name + " has reported an error: " + errorStr, ServerLog.MessageType.Error);
            GameAnalyticsManager.AddErrorEventOnce("GameServer.HandleClientError:" + errorStr, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorStr);

            if (c.Connection == OwnerConnection)
            {
                SendDirectChatMessage(errorStr, c, ChatMessageType.MessageBox);
                EndGame();
            }
            else
            {
                KickClient(c, errorStr);
            }
        }

        public override void CreateEntityEvent(INetSerializable entity, object[] extraData = null)
        {
            if (!(entity is IServerSerializable)) throw new InvalidCastException("entity is not IServerSerializable");
            entityEventManager.CreateEvent(entity as IServerSerializable, extraData);
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

        private void ClientReadLobby(IReadMessage inc)
        {
            Client c = ConnectedClients.Find(x => x.Connection == inc.Sender);
            if (c == null)
            {
                //TODO: remove?
                //inc.Sender.Disconnect("You're not a connected client.");
                return;
            }

            ClientNetObject objHeader;
            while ((objHeader = (ClientNetObject)inc.ReadByte()) != ClientNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ClientNetObject.SYNC_IDS:
                        //TODO: might want to use a clever class for this
                        c.LastRecvLobbyUpdate = NetIdUtils.Clamp(inc.ReadUInt16(), c.LastRecvLobbyUpdate, GameMain.NetLobbyScreen.LastUpdateID);
                        c.LastRecvChatMsgID = NetIdUtils.Clamp(inc.ReadUInt16(), c.LastRecvChatMsgID, c.LastChatMsgQueueID);
                        c.LastRecvClientListUpdate = NetIdUtils.Clamp(inc.ReadUInt16(), c.LastRecvClientListUpdate, LastClientListUpdateID);

                        TryChangeClientName(c, inc.ReadString());

                        c.LastRecvCampaignSave = inc.ReadUInt16();
                        if (c.LastRecvCampaignSave > 0)
                        {
                            byte campaignID = inc.ReadByte();
                            c.LastRecvCampaignUpdate = inc.ReadUInt16();
                            bool characterDiscarded = inc.ReadBoolean();

                            if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                            {
                                if (characterDiscarded)
                                {
                                    campaign.DiscardClientCharacterData(c);
                                }

                                //the client has a campaign save for another campaign
                                //(the server started a new campaign and the client isn't aware of it yet?)
                                if (campaign.CampaignID != campaignID)
                                {
                                    c.LastRecvCampaignSave = (ushort)(campaign.LastSaveID - 1);
                                    c.LastRecvCampaignUpdate = (ushort)(campaign.LastUpdateID - 1);
                                }
                            }
                        }
                        break;
                    case ClientNetObject.CHAT_MESSAGE:
                        ChatMessage.ServerRead(inc, c);
                        break;
                    case ClientNetObject.VOTE:
                        serverSettings.Voting.ServerRead(inc, c);
                        break;
                    default:
                        return;
                }

                //don't read further messages if the client has been disconnected (kicked due to spam for example)
                if (!connectedClients.Contains(c)) break;
            }
        }

        private void ClientReadIngame(IReadMessage inc)
        {
            Client c = ConnectedClients.Find(x => x.Connection == inc.Sender);
            if (c == null)
            {
                //TODO: remove?
                //inc.SenderConnection.Disconnect("You're not a connected client.");
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

                        UInt16 lastRecvChatMsgID = inc.ReadUInt16();
                        UInt16 lastRecvEntityEventID = inc.ReadUInt16();
                        UInt16 lastRecvClientListUpdate = inc.ReadUInt16();

                        //last msgs we've created/sent, the client IDs should never be higher than these
                        UInt16 lastEntityEventID = entityEventManager.Events.Count == 0 ? (UInt16)0 : entityEventManager.Events.Last().ID;

                        if (c.NeedsMidRoundSync)
                        {
                            //received all the old events -> client in sync, we can switch to normal behavior
                            if (lastRecvEntityEventID >= c.UnreceivedEntityEventCount - 1 ||
                                c.UnreceivedEntityEventCount == 0)
                            {
                                ushort prevID = lastRecvEntityEventID;
                                c.NeedsMidRoundSync = false;
                                lastRecvEntityEventID = (UInt16)(c.FirstNewEventID - 1);
                                c.LastRecvEntityEventID = lastRecvEntityEventID;
                                DebugConsole.Log("Finished midround syncing " + c.Name + " - switching from ID " + prevID + " to " + c.LastRecvEntityEventID);
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
                                " (previous: " + c.LastChatMsgQueueID + ", latest: " + c.LastChatMsgQueueID + ")");
                        }

                        if (NetIdUtils.IdMoreRecent(lastRecvEntityEventID, c.LastRecvEntityEventID) &&
                            !NetIdUtils.IdMoreRecent(lastRecvEntityEventID, lastEntityEventID))
                        {
                            if (c.NeedsMidRoundSync)
                            {
                                //give midround-joining clients a bit more time to get in sync if they keep receiving messages
                                int receivedEventCount = lastRecvEntityEventID - c.LastRecvEntityEventID;
                                if (receivedEventCount < 0) receivedEventCount += ushort.MaxValue;
                                c.MidRoundSyncTimeOut += receivedEventCount * 0.01f;
                                DebugConsole.Log("Midround sync timeout " + c.MidRoundSyncTimeOut.ToString("0.##") + "/" + Timing.TotalTime.ToString("0.##"));
                            }

                            c.LastRecvEntityEventID = lastRecvEntityEventID;
                        }
                        else if (lastRecvEntityEventID != c.LastRecvEntityEventID && GameSettings.VerboseLogging)
                        {
                            DebugConsole.ThrowError(
                                "Invalid lastRecvEntityEventID  " + lastRecvEntityEventID +
                                " (previous: " + c.LastRecvEntityEventID + ", latest: " + lastEntityEventID + ")");
                        }

                        if (NetIdUtils.IdMoreRecent(lastRecvClientListUpdate, c.LastRecvClientListUpdate))
                        {
                            c.LastRecvClientListUpdate = lastRecvClientListUpdate;
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
                        serverSettings.Voting.ServerRead(inc, c);
                        break;
                    case ClientNetObject.SPECTATING_POS:
                        c.SpectatePos = new Vector2(inc.ReadSingle(), inc.ReadSingle());
                        break;
                    default:
                        return;
                }

                //don't read further messages if the client has been disconnected (kicked due to spam for example)
                if (!connectedClients.Contains(c)) break;
            }
        }

        private void ClientReadServerCommand(IReadMessage inc)
        {
            Client sender = ConnectedClients.Find(x => x.Connection == inc.Sender);
            if (sender == null)
            {
                //TODO: remove?
                //inc.SenderConnection.Disconnect("You're not a connected client.");
                return;
            }

            ClientPermissions command = ClientPermissions.None;
            try
            {
                command = (ClientPermissions)inc.ReadUInt16();
            }
            catch
            {
                return;
            }

            //clients are allowed to end the round by talking with the watchman in multiplayer
            //campaign even if they don't have the special permission
            bool peekBool = inc.ReadBoolean(); inc.BitPosition--;
            if (command == ClientPermissions.ManageRound && peekBool &&
                GameMain.GameSession?.GameMode is MultiPlayerCampaign mpCampaign)
            {
                if (!mpCampaign.AllowedToEndRound(sender.Character) && !sender.HasPermission(command))
                {
                    return;
                }
            }
            else if (!sender.HasPermission(command))
            {
                Log("Client \"" + sender.Name + "\" sent a server command \"" + command + "\". Permission denied.", ServerLog.MessageType.ServerMessage);
                return;
            }

            switch (command)
            {
                case ClientPermissions.Kick:
                    string kickedName = inc.ReadString().ToLowerInvariant();
                    string kickReason = inc.ReadString();
                    var kickedClient = connectedClients.Find(cl => cl != sender && cl.Name.ToLowerInvariant() == kickedName && cl.Connection != OwnerConnection);
                    if (kickedClient != null)
                    {
                        Log("Client \"" + sender.Name + "\" kicked \"" + kickedClient.Name + "\".", ServerLog.MessageType.ServerMessage);
                        KickClient(kickedClient, string.IsNullOrEmpty(kickReason) ? $"ServerMessage.KickedBy~[initiator]={sender.Name}" : kickReason);
                    }
                    else
                    {
                        SendDirectChatMessage(TextManager.GetServerMessage($"ServerMessage.PlayerNotFound~[player]={kickedName}"), sender, ChatMessageType.Console);
                    }
                    break;
                case ClientPermissions.Ban:
                    string bannedName = inc.ReadString().ToLowerInvariant();
                    string banReason = inc.ReadString();
                    bool range = inc.ReadBoolean();
                    double durationSeconds = inc.ReadDouble();

                    var bannedClient = connectedClients.Find(cl => cl != sender && cl.Name.ToLowerInvariant() == bannedName && cl.Connection != OwnerConnection);
                    if (bannedClient != null)
                    {
                        Log("Client \"" + sender.Name + "\" banned \"" + bannedClient.Name + "\".", ServerLog.MessageType.ServerMessage);
                        if (durationSeconds > 0)
                        {
                            BanClient(bannedClient, string.IsNullOrEmpty(banReason) ? $"ServerMessage.BannedBy~[initiator]={sender.Name}" : banReason, range, TimeSpan.FromSeconds(durationSeconds));
                        }
                        else
                        {
                            BanClient(bannedClient, string.IsNullOrEmpty(banReason) ? $"ServerMessage.BannedBy~[initiator]={sender.Name}" : banReason, range);
                        }
                    }
                    else
                    {
                        SendDirectChatMessage(TextManager.GetServerMessage($"ServerMessage.PlayerNotFound~[player]={bannedName}"), sender, ChatMessageType.Console);
                    }
                    break;
                case ClientPermissions.Unban:
                    string unbannedName = inc.ReadString();
                    string unbannedIP = inc.ReadString();
                    UnbanPlayer(unbannedName, unbannedIP);
                    break;
                case ClientPermissions.ManageRound:
                    bool end = inc.ReadBoolean();
                    if (gameStarted && end)
                    {
                        Log("Client \"" + sender.Name + "\" ended the round.", ServerLog.MessageType.ServerMessage);
                        EndGame();
                    }
                    else if (!gameStarted && !end && !initiatedStartGame)
                    {
                        Log("Client \"" + sender.Name + "\" started the round.", ServerLog.MessageType.ServerMessage);
                        StartGame();
                    }
                    break;
                case ClientPermissions.SelectSub:
                    bool isShuttle = inc.ReadBoolean();
                    inc.ReadPadBits();
                    UInt16 subIndex = inc.ReadUInt16();
                    var subList = GameMain.NetLobbyScreen.GetSubList();
                    if (subIndex >= subList.Count)
                    {
                        DebugConsole.NewMessage("Client \"" + sender.Name + "\" attempted to select a sub, index out of bounds (" + subIndex + ")", Color.Red);
                    }
                    else
                    {
                        if (isShuttle)
                        {
                            GameMain.NetLobbyScreen.SelectedShuttle = subList[subIndex];
                        }
                        else
                        {
                            GameMain.NetLobbyScreen.SelectedSub = subList[subIndex];
                        }
                    }
                    break;
                case ClientPermissions.SelectMode:
                    UInt16 modeIndex = inc.ReadUInt16();
                    if (GameMain.NetLobbyScreen.GameModes[modeIndex].Identifier.ToLowerInvariant() == "multiplayercampaign")
                    {
                        string[] saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer).ToArray();
                        for (int i = 0; i < saveFiles.Length; i++)
                        {
                            XDocument doc = SaveUtil.LoadGameSessionDoc(saveFiles[i]);
                            if (doc?.Root != null)
                            {
                                saveFiles[i] =
                                    string.Join(";",
                                        saveFiles[i].Replace(';', ' '),
                                        doc.Root.GetAttributeString("submarine", ""),
                                        doc.Root.GetAttributeString("savetime", ""));
                            }
                        }

                        IWriteMessage msg = new WriteOnlyMessage();
                        msg.Write((byte)ServerPacketHeader.CAMPAIGN_SETUP_INFO);
                        msg.Write((UInt16)saveFiles.Count());
                        foreach (string saveFile in saveFiles)
                        {
                            msg.Write(saveFile);
                        }

                        serverPeer.Send(msg, sender.Connection, DeliveryMethod.Reliable);
                    }
                    else
                    {
                        GameMain.NetLobbyScreen.SelectedModeIndex = modeIndex;
                        Log("Gamemode changed to " + GameMain.NetLobbyScreen.GameModes[GameMain.NetLobbyScreen.SelectedModeIndex].Name, ServerLog.MessageType.ServerMessage);
                    }
                    break;
                case ClientPermissions.ManageCampaign:
                    MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
                    if (campaign != null)
                    {
                        campaign.ServerRead(inc, sender);
                    }
                    break;
                case ClientPermissions.ConsoleCommands:
                    {
                        string consoleCommand = inc.ReadString();
                        Vector2 clientCursorPos = new Vector2(inc.ReadSingle(), inc.ReadSingle());
                        DebugConsole.ExecuteClientCommand(sender, clientCursorPos, consoleCommand);
                    }
                    break;
                case ClientPermissions.ManagePermissions:
                    byte targetClientID = inc.ReadByte();
                    Client targetClient = connectedClients.Find(c => c.ID == targetClientID);
                    if (targetClient == null || targetClient == sender || targetClient.Connection == OwnerConnection) { return; }

                    targetClient.ReadPermissions(inc);

                    List<string> permissionNames = new List<string>();
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        if (permission == ClientPermissions.None || permission == ClientPermissions.All)
                        {
                            continue;
                        }
                        if (targetClient.Permissions.HasFlag(permission)) { permissionNames.Add(permission.ToString()); }
                    }

                    string logMsg;
                    if (permissionNames.Any())
                    {
                        logMsg = "Client \"" + sender.Name + "\" set the permissions of the client \"" + targetClient.Name + "\" to "
                            + string.Join(", ", permissionNames);
                    }
                    else
                    {
                        logMsg = "Client \"" + sender.Name + "\" removed all permissions from the client \"" + targetClient.Name + ".";
                    }
                    Log(logMsg, ServerLog.MessageType.ServerMessage);

                    UpdateClientPermissions(targetClient);

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
                //consider the client's character disconnected (causing it to die if the client does not join soon)
                if (gameStarted && c.Character != null && (DateTime.Now - roundStartTime).Seconds > 30.0f)
                {
                    c.Character.ClientDisconnected = true;
                }

                ClientWriteLobby(c);

                if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign &&
                    GameMain.NetLobbyScreen.SelectedMode == campaign.Preset &&
                    NetIdUtils.IdMoreRecent(campaign.LastSaveID, c.LastRecvCampaignSave))
                {
                    //already sent an up-to-date campaign save
                    if (c.LastCampaignSaveSendTime != null && campaign.LastSaveID == c.LastCampaignSaveSendTime.First)
                    {
                        //the save was sent less than 5 second ago, don't attempt to resend yet
                        //(the client may have received it but hasn't acked us yet)
                        if (c.LastCampaignSaveSendTime.Second > Lidgren.Network.NetTime.Now - 5.0f)
                        {
                            return;
                        }
                    }

                    if (!fileSender.ActiveTransfers.Any(t => t.Connection == c.Connection && t.FileType == FileTransferType.CampaignSave))
                    {
                        fileSender.StartTransfer(c.Connection, FileTransferType.CampaignSave, GameMain.GameSession.SavePath);
                        c.LastCampaignSaveSendTime = new Pair<ushort, float>(campaign.LastSaveID, (float)Lidgren.Network.NetTime.Now);
                    }
                }
            }
        }

        /// <summary>
        /// Write info that the client needs when joining the server
        /// </summary>
        private void ClientWriteInitial(Client c, IWriteMessage outmsg)
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
                outmsg.Write(subList[i].RequiredContentPackagesInstalled);
            }

            outmsg.Write(GameStarted);
            outmsg.Write(serverSettings.AllowSpectating);

            c.WritePermissions(outmsg);
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

                    if (c.SpectatePos == null)
                    {
                        if (c.Character != null && Vector2.DistanceSquared(character.WorldPosition, c.Character.WorldPosition) >= NetConfig.DisableCharacterDistSqr)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (Vector2.DistanceSquared(character.WorldPosition, c.SpectatePos.Value) >= NetConfig.DisableCharacterDistSqr)
                        {
                            continue;
                        }
                    }

                    float updateInterval = character.GetPositionUpdateInterval(c);
                    c.PositionUpdateLastSent.TryGetValue(character.ID, out float lastSent);
                    if (lastSent > Lidgren.Network.NetTime.Now - updateInterval) { continue; }

                    if (!c.PendingPositionUpdates.Contains(character)) c.PendingPositionUpdates.Enqueue(character);
                }

                foreach (Submarine sub in Submarine.Loaded)
                {
                    //if docked to a sub with a smaller ID, don't send an update
                    //  (= update is only sent for the docked sub that has the smallest ID, doesn't matter if it's the main sub or a shuttle)
                    if (sub.IsOutpost || sub.DockedTo.Any(s => s.ID < sub.ID)) continue;
                    if (!c.PendingPositionUpdates.Contains(sub)) c.PendingPositionUpdates.Enqueue(sub);
                }

                foreach (Item item in Item.ItemList)
                {
                    if (item.PositionUpdateInterval == float.PositiveInfinity) { continue; }
                    float updateInterval = item.GetPositionUpdateInterval(c);
                    c.PositionUpdateLastSent.TryGetValue(item.ID, out float lastSent);
                    if (lastSent > Lidgren.Network.NetTime.Now - item.PositionUpdateInterval) { continue; }
                    if (!c.PendingPositionUpdates.Contains(item)) c.PendingPositionUpdates.Enqueue(item);
                }
            }

            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_INGAME);

            outmsg.Write((float)Lidgren.Network.NetTime.Now);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);
            outmsg.Write(c.LastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server
            outmsg.Write(c.LastSentEntityEventID);

            int clientListBytes = outmsg.LengthBytes;
            WriteClientList(c, outmsg);
            clientListBytes = outmsg.LengthBytes - clientListBytes;

            int chatMessageBytes = outmsg.LengthBytes;
            WriteChatMessages(outmsg, c);
            chatMessageBytes = outmsg.LengthBytes - chatMessageBytes;

            //write as many position updates as the message can fit (only after midround syncing is done)
            int positionUpdateBytes = outmsg.LengthBytes;
            while (!c.NeedsMidRoundSync && c.PendingPositionUpdates.Count > 0)
            {
                var entity = c.PendingPositionUpdates.Peek();
                if (entity == null || entity.Removed ||
                    (entity is Item item && item.PositionUpdateInterval == float.PositiveInfinity))
                {
                    c.PendingPositionUpdates.Dequeue();
                    continue;
                }

                IWriteMessage tempBuffer = new ReadWriteMessage();
                tempBuffer.Write((byte)ServerNetObject.ENTITY_POSITION);
                if (entity is Item)
                {
                    ((Item)entity).ServerWritePosition(tempBuffer, c);
                }
                else
                {
                    ((IServerSerializable)entity).ServerWrite(tempBuffer, c);
                }

                //no more room in this packet
                if (outmsg.LengthBytes + tempBuffer.LengthBytes > MsgConstants.MTU - 20)
                {
                    break;
                }

                outmsg.Write(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
                outmsg.WritePadBits();

                c.PositionUpdateLastSent[entity.ID] = (float)Lidgren.Network.NetTime.Now;
                c.PendingPositionUpdates.Dequeue();
            }
            positionUpdateBytes = outmsg.LengthBytes - positionUpdateBytes;

            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            if (outmsg.LengthBytes > MsgConstants.MTU)
            {
                string errorMsg = "Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU + ")\n";
                errorMsg +=
                    "  Client list size: " + clientListBytes + " bytes\n" +
                    "  Chat message size: " + chatMessageBytes + " bytes\n" +
                    "  Position update size: " + positionUpdateBytes + " bytes\n\n";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("GameServer.ClientWriteIngame1:PacketSizeExceeded" + outmsg.LengthBytes, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
            }

            serverPeer.Send(outmsg, c.Connection, DeliveryMethod.Unreliable);

            //---------------------------------------------------------------------------

            for (int i = 0; i < NetConfig.MaxEventPacketsPerUpdate; i++)
            {
                outmsg = new WriteOnlyMessage();
                outmsg.Write((byte)ServerPacketHeader.UPDATE_INGAME);
                outmsg.Write((float)Lidgren.Network.NetTime.Now);

                int eventManagerBytes = outmsg.LengthBytes;
                entityEventManager.Write(c, outmsg, out List<NetEntityEvent> sentEvents);
                eventManagerBytes = outmsg.LengthBytes - eventManagerBytes;

                if (sentEvents.Count == 0)
                {
                    break;
                }

                outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);

                if (outmsg.LengthBytes > MsgConstants.MTU)
                {
                    string errorMsg = "Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU + ")\n";
                    errorMsg +=
                        "  Event size: " + eventManagerBytes + " bytes\n";

                    if (sentEvents != null && sentEvents.Count > 0)
                    {
                        errorMsg += "Sent events: \n";
                        foreach (var entityEvent in sentEvents)
                        {
                            errorMsg += "  - " + (entityEvent.Entity?.ToString() ?? "null") + "\n";
                        }
                    }

                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameServer.ClientWriteIngame2:PacketSizeExceeded" + outmsg.LengthBytes, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                }

                serverPeer.Send(outmsg, c.Connection, DeliveryMethod.Unreliable);
            }
        }

        private void WriteClientList(Client c, IWriteMessage outmsg)
        {
            bool hasChanged = NetIdUtils.IdMoreRecent(LastClientListUpdateID, c.LastRecvClientListUpdate);
            if (!hasChanged) { return; }

            outmsg.Write((byte)ServerNetObject.CLIENT_LIST);
            outmsg.Write(LastClientListUpdateID);

            outmsg.Write((byte)connectedClients.Count);
            foreach (Client client in connectedClients)
            {
                outmsg.Write(client.ID);
                outmsg.Write(client.SteamID);
                outmsg.Write(client.Name);
                outmsg.Write(client.Character == null || !gameStarted ? (ushort)0 : client.Character.ID);
                outmsg.Write(client.Muted);
                outmsg.Write(client.Connection != OwnerConnection); //is kicking the player allowed
                outmsg.WritePadBits();
            }
        }

        private void ClientWriteLobby(Client c)
        {
            bool isInitialUpdate = false;

            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_LOBBY);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);

            int settingsBytes = outmsg.LengthBytes;

            if (NetIdUtils.IdMoreRecent(GameMain.NetLobbyScreen.LastUpdateID, c.LastRecvLobbyUpdate))
            {
                outmsg.Write(true);
                outmsg.WritePadBits();

                outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);

                IWriteMessage settingsBuf = new ReadWriteMessage();
                serverSettings.ServerWrite(settingsBuf, c);

                outmsg.Write((UInt16)settingsBuf.LengthBytes);
                outmsg.Write(settingsBuf.Buffer,0,settingsBuf.LengthBytes);

                outmsg.Write(c.LastRecvLobbyUpdate < 1);
                if (c.LastRecvLobbyUpdate < 1)
                {
                    isInitialUpdate = true;
                    ClientWriteInitial(c, outmsg);
                }
                outmsg.Write(GameMain.NetLobbyScreen.SelectedSub.Name);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedSub.MD5Hash.ToString());
                outmsg.Write(serverSettings.UseRespawnShuttle);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.ToString());

                outmsg.Write(serverSettings.Voting.AllowSubVoting);
                outmsg.Write(serverSettings.Voting.AllowModeVoting);

                outmsg.Write(serverSettings.VoiceChatEnabled);

                outmsg.Write(serverSettings.AllowSpectating);

                outmsg.WriteRangedInteger((int)serverSettings.TraitorsEnabled, 0, 2);

                outmsg.WriteRangedInteger((int)GameMain.NetLobbyScreen.MissionType, 0, (int)MissionType.All);

                outmsg.Write((byte)GameMain.NetLobbyScreen.SelectedModeIndex);
                outmsg.Write(GameMain.NetLobbyScreen.LevelSeed);
                outmsg.Write(serverSettings.SelectedLevelDifficulty);

                outmsg.Write((byte)serverSettings.BotCount);
                outmsg.Write(serverSettings.BotSpawnMode == BotSpawnMode.Fill);

                outmsg.Write(serverSettings.AutoRestart);
                if (serverSettings.AutoRestart)
                {
                    outmsg.Write(autoRestartTimerRunning ? serverSettings.AutoRestartTimer : 0.0f);
                }
            }
            else
            {
                outmsg.Write(false);
                outmsg.WritePadBits();
            }
            settingsBytes = outmsg.LengthBytes - settingsBytes;

            int campaignBytes = outmsg.LengthBytes;
            var campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            if (outmsg.LengthBytes < MsgConstants.MTU - 500 &&
                campaign != null && campaign.Preset == GameMain.NetLobbyScreen.SelectedMode &&
                NetIdUtils.IdMoreRecent(campaign.LastUpdateID, c.LastRecvCampaignUpdate))
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
            campaignBytes = outmsg.LengthBytes - campaignBytes;

            outmsg.Write(c.LastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server

            int clientListBytes = outmsg.LengthBytes;
            if (outmsg.LengthBytes < MsgConstants.MTU - 500)
            {
                WriteClientList(c, outmsg);
            }
            clientListBytes = outmsg.LengthBytes - clientListBytes;

            int chatMessageBytes = outmsg.LengthBytes;
            WriteChatMessages(outmsg, c);
            chatMessageBytes = outmsg.LengthBytes - outmsg.LengthBytes;

            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            if (isInitialUpdate)
            {
                //the initial update may be very large if the host has a large number
                //of submarine files, so the message may have to be fragmented

                //unreliable messages don't play nicely with fragmenting, so we'll send the message reliably
                serverPeer.Send(outmsg, c.Connection, DeliveryMethod.Reliable);

                //and assume the message was received, so we don't have to keep resending
                //these large initial messages until the client acknowledges receiving them
                c.LastRecvLobbyUpdate++;

                SendVoteStatus(new List<Client>() { c });
            }
            else
            {
                if (outmsg.LengthBytes > MsgConstants.MTU)
                {
                    string errorMsg = "Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU + ")";
                    errorMsg +=
                        "  Client list size: " + clientListBytes + " bytes\n" +
                        "  Chat message size: " + chatMessageBytes + " bytes\n" +
                        "  Campaign size: " + campaignBytes + " bytes\n" +
                        "  Settings size: " + settingsBytes + " bytes\n\n";
                        DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameServer.ClientWriteIngame1:ClientWriteLobby" + outmsg.LengthBytes, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                }

                serverPeer.Send(outmsg, c.Connection, DeliveryMethod.Unreliable);
            }
        }

        private void WriteChatMessages(IWriteMessage outmsg, Client c)
        {
            c.ChatMsgQueue.RemoveAll(cMsg => !NetIdUtils.IdMoreRecent(cMsg.NetStateID, c.LastRecvChatMsgID));
            for (int i = 0; i < c.ChatMsgQueue.Count && i < ChatMessage.MaxMessagesPerPacket; i++)
            {
                if (outmsg.LengthBytes + c.ChatMsgQueue[i].EstimateLengthBytesServer(c) > MsgConstants.MTU - 5)
                {
                    //not enough room in this packet
                    return;
                }
                c.ChatMsgQueue[i].ServerWrite(outmsg, c);
            }
        }

        public bool StartGame()
        {
            if (initiatedStartGame || gameStarted) { return false; }

            Log("Starting a new round...", ServerLog.MessageType.ServerMessage);

            Submarine selectedSub = null;
            Submarine selectedShuttle = GameMain.NetLobbyScreen.SelectedShuttle;

            if (serverSettings.Voting.AllowSubVoting)
            {
                selectedSub = serverSettings.Voting.HighestVoted<Submarine>(VoteType.Sub, connectedClients);
                if (selectedSub == null) selectedSub = GameMain.NetLobbyScreen.SelectedSub;
            }
            else
            {
                selectedSub = GameMain.NetLobbyScreen.SelectedSub;
            }

            if (selectedSub == null)
            {
                return false;
            }

            if (selectedShuttle == null)
            {
                return false;
            }

            GameModePreset selectedMode = serverSettings.Voting.HighestVoted<GameModePreset>(VoteType.Mode, connectedClients);
            if (selectedMode == null) selectedMode = GameMain.NetLobbyScreen.SelectedMode;

            if (selectedMode == null)
            {
                return false;
            }

            initiatedStartGame = true;
            startGameCoroutine = CoroutineManager.StartCoroutine(InitiateStartGame(selectedSub, selectedShuttle, serverSettings.UseRespawnShuttle, selectedMode), "InitiateStartGame");

            return true;
        }

        private IEnumerable<object> InitiateStartGame(Submarine selectedSub, Submarine selectedShuttle, bool usingShuttle, GameModePreset selectedMode)
        {
            initiatedStartGame = true;

            if (connectedClients.Any())
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ServerPacketHeader.QUERY_STARTGAME);

                msg.Write(selectedSub.Name);
                msg.Write(selectedSub.MD5Hash.Hash);

                msg.Write(usingShuttle);
                msg.Write(selectedShuttle.Name);
                msg.Write(selectedShuttle.MD5Hash.Hash);

                connectedClients.ForEach(c => c.ReadyToStart = false);

                foreach (NetworkConnection conn in connectedClients.Select(c => c.Connection))
                {
                    serverPeer.Send(msg, conn, DeliveryMethod.Reliable);
                }

                //give the clients a few seconds to request missing sub/shuttle files before starting the round
                float waitForResponseTimer = 5.0f;
                while (connectedClients.Any(c => !c.ReadyToStart) && waitForResponseTimer > 0.0f)
                {
                    waitForResponseTimer -= CoroutineManager.UnscaledDeltaTime;
                    yield return CoroutineStatus.Running;
                }

                if (fileSender.ActiveTransfers.Count > 0)
                {
                    float waitForTransfersTimer = 20.0f;
                    while (fileSender.ActiveTransfers.Count > 0 && waitForTransfersTimer > 0.0f)
                    {
                        waitForTransfersTimer -= CoroutineManager.UnscaledDeltaTime;

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

            roundStartSeed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(roundStartSeed);

            int teamCount = 1;
            MultiPlayerCampaign campaign = selectedMode == GameMain.GameSession?.GameMode.Preset ?
                GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;

            if (campaign != null && campaign.Map == null)
            {
                initiatedStartGame = false;
                startGameCoroutine = null;
                string errorMsg = "Starting the round failed. Campaign was still active, but the map has been disposed. Try selecting another game mode.";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("GameServer.StartGame:InvalidCampaignState", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                if (OwnerConnection != null)
                {
                    SendDirectChatMessage(errorMsg, connectedClients.Find(c => c.Connection == OwnerConnection), ChatMessageType.Error);
                }
                yield return CoroutineStatus.Failure;
            }

            //don't instantiate a new gamesession if we're playing a campaign
            if (campaign == null || GameMain.GameSession == null)
            {
                GameMain.GameSession = new GameSession(selectedSub, "", selectedMode, GameMain.NetLobbyScreen.MissionType);
            }

            List<Client> playingClients = new List<Client>(connectedClients);
            if (serverSettings.AllowSpectating)
            {
                playingClients.RemoveAll(c => c.SpectateOnly);
            }
            //always allow the server owner to spectate even if it's disallowed in server settings
            playingClients.RemoveAll(c => c.Connection == OwnerConnection && c.SpectateOnly);

            if (GameMain.GameSession.GameMode.Mission != null &&
                GameMain.GameSession.GameMode.Mission.AssignTeamIDs(playingClients))
            {
                teamCount = 2;
            }
            else
            {
                connectedClients.ForEach(c => c.TeamID = Character.TeamType.Team1);
            }

            if (campaign != null)
            {
                GameMain.GameSession.StartRound(campaign.Map.SelectedConnection.Level,
                    reloadSub: true,
                    loadSecondSub: teamCount > 1,
                    mirrorLevel: campaign.Map.CurrentLocation != campaign.Map.SelectedConnection.Locations[0]);

                campaign.AssignClientCharacterInfos(connectedClients);
                Log("Game mode: " + selectedMode.Name, ServerLog.MessageType.ServerMessage);
                Log("Submarine: " + GameMain.GameSession.Submarine.Name, ServerLog.MessageType.ServerMessage);
                Log("Level seed: " + campaign.Map.SelectedConnection.Level.Seed, ServerLog.MessageType.ServerMessage);
            }
            else
            {
                GameMain.GameSession.StartRound(GameMain.NetLobbyScreen.LevelSeed, serverSettings.SelectedLevelDifficulty, teamCount > 1);
                Log("Game mode: " + selectedMode.Name, ServerLog.MessageType.ServerMessage);
                Log("Submarine: " + selectedSub.Name, ServerLog.MessageType.ServerMessage);
                Log("Level seed: " + GameMain.NetLobbyScreen.LevelSeed, ServerLog.MessageType.ServerMessage);
            }

            MissionMode missionMode = GameMain.GameSession.GameMode as MissionMode;
            bool missionAllowRespawn = campaign == null && (missionMode?.Mission == null || missionMode.Mission.AllowRespawn);

            if (serverSettings.AllowRespawn && missionAllowRespawn) respawnManager = new RespawnManager(this, usingShuttle ? selectedShuttle : null);

            entityEventManager.RefreshEntityIDs();

            //assign jobs and spawnpoints separately for each team
            for (int n = 0; n < teamCount; n++)
            {
                var teamID = n == 0 ? Character.TeamType.Team1 : Character.TeamType.Team2;

                Submarine.MainSubs[n].TeamID = teamID;
                foreach (Submarine sub in Submarine.MainSubs[n].DockedTo)
                {
                    sub.TeamID = teamID;
                }

                //find the clients in this team
                List<Client> teamClients = teamCount == 1 ?
                    new List<Client>(connectedClients) : 
                    playingClients.FindAll(c => c.TeamID == teamID);

                if (!teamClients.Any() && n > 0) { continue; }

                AssignJobs(teamClients);

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
                        client.CharacterInfo = new CharacterInfo(Character.HumanSpeciesName, client.Name);
                    }
                    characterInfos.Add(client.CharacterInfo);
                    if (client.CharacterInfo.Job == null || client.CharacterInfo.Job.Prefab != client.AssignedJob.First)
                    {
                        client.CharacterInfo.Job = new Job(client.AssignedJob.First, client.AssignedJob.Second);
                    }
                }

                List<CharacterInfo> bots = new List<CharacterInfo>();
                int botsToSpawn = serverSettings.BotSpawnMode == BotSpawnMode.Fill ? serverSettings.BotCount - characterInfos.Count : serverSettings.BotCount;
                for (int i = 0; i < botsToSpawn; i++)
                {
                    var botInfo = new CharacterInfo(Character.HumanSpeciesName);
                    characterInfos.Add(botInfo);
                    bots.Add(botInfo);
                }
                AssignBotJobs(bots, teamID);

                WayPoint[] assignedWayPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSubs[n]);
                for (int i = 0; i < teamClients.Count; i++)
                {
                    Character spawnedCharacter = Character.Create(teamClients[i].CharacterInfo, assignedWayPoints[i].WorldPosition, teamClients[i].CharacterInfo.Name, true, false);
                    spawnedCharacter.AnimController.Frozen = true;
                    spawnedCharacter.TeamID = teamID;
                    var characterData = campaign?.GetClientCharacterData(teamClients[i]);
                    if (characterData == null)
                    {
                        spawnedCharacter.GiveJobItems(assignedWayPoints[i]);
                    }
                    else
                    {
                        characterData.HasSpawned = true;
                        characterData.SpawnInventoryItems(spawnedCharacter.Info, spawnedCharacter.Inventory);
                    }

                    teamClients[i].Character = spawnedCharacter;
                    spawnedCharacter.OwnerClientEndPoint = teamClients[i].Connection.EndPointString;
                    spawnedCharacter.OwnerClientName = teamClients[i].Name;
                }

                for (int i = teamClients.Count; i < teamClients.Count + bots.Count; i++)
                {
                    Character spawnedCharacter = Character.Create(characterInfos[i], assignedWayPoints[i].WorldPosition, characterInfos[i].Name, false, true);
                    spawnedCharacter.TeamID = teamID;
                    spawnedCharacter.GiveJobItems(assignedWayPoints[i]);
                }
            }

            foreach (Submarine sub in Submarine.MainSubs)
            {
                if (sub == null) continue;

                List<PurchasedItem> spawnList = new List<PurchasedItem>();
                foreach (KeyValuePair<ItemPrefab, int> kvp in serverSettings.ExtraCargo)
                {
                    spawnList.Add(new PurchasedItem(kvp.Key, kvp.Value));
                }

                CargoManager.CreateItems(spawnList);
            }

            TraitorManager = null;
            if (serverSettings.TraitorsEnabled == YesNoMaybe.Yes ||
                (serverSettings.TraitorsEnabled == YesNoMaybe.Maybe && Rand.Range(0.0f, 1.0f) < 0.5f))
            {
                if (!(GameMain.GameSession?.GameMode is CampaignMode))
                {
                    List<Character> characters = new List<Character>();
                    foreach (Client client in ConnectedClients)
                    {
                        if (client.Character != null) characters.Add(client.Character);
                    }
                    TraitorManager = new TraitorManager();
                    TraitorManager.Start(this);
                }
            }

            GameAnalyticsManager.AddDesignEvent("Traitors:" + (TraitorManager == null ? "Disabled" : "Enabled"));

            SendStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.GameMode.Preset, connectedClients);

            yield return CoroutineStatus.Running;

            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.GameScreen.Select();

            Log("Round started.", ServerLog.MessageType.ServerMessage);

            gameStarted = true;
            initiatedStartGame = false;
            GameMain.ResetFrameTime();

            LastClientListUpdateID++;

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
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.STARTGAME);

            msg.Write(seed);
            msg.Write(GameMain.GameSession.Level.Seed);
            msg.Write(GameMain.GameSession.Level.EqualityCheckVal);
            msg.Write(serverSettings.SelectedLevelDifficulty);

            msg.Write((byte)GameMain.Config.LosMode);

            msg.Write((byte)GameMain.NetLobbyScreen.MissionType);

            msg.Write(selectedSub.Name);
            msg.Write(selectedSub.MD5Hash.Hash);
            msg.Write(serverSettings.UseRespawnShuttle);
            msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
            msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.Hash);

            msg.Write(selectedMode.Identifier);
            msg.Write((short)(GameMain.GameSession.GameMode?.Mission == null ?
                -1 : MissionPrefab.List.IndexOf(GameMain.GameSession.GameMode.Mission.Prefab)));

            MultiPlayerCampaign campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;

            MissionMode missionMode = GameMain.GameSession.GameMode as MissionMode;
            bool missionAllowRespawn = campaign == null && (missionMode?.Mission == null || missionMode.Mission.AllowRespawn);
            msg.Write(serverSettings.AllowRespawn && missionAllowRespawn);
            msg.Write(Submarine.MainSubs[1] != null); //loadSecondSub

            msg.Write(serverSettings.AllowDisguises);
            msg.Write(serverSettings.AllowRewiring);

            msg.Write(serverSettings.AllowRagdollButton);

            serverSettings.WriteMonsterEnabled(msg);

            serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
        }

        public void EndGame()
        {
            if (!gameStarted)
            {
                return;
            }

            if (GameSettings.VerboseLogging)
            {
                Log("Ending the round...\n" + Environment.StackTrace, ServerLog.MessageType.ServerMessage);

            }
            else
            {
                Log("Ending the round...", ServerLog.MessageType.ServerMessage);
            }

            var traitorEndMessage = TraitorManager?.GetEndMessage() ?? "";
            var traitorEndMessageStart = traitorEndMessage.LastIndexOf('/') + 1;

            var roundSummary = TextManager.FormatServerMessage("RoundSummaryRoundHasEnded", new string[] {"[traitorinfo]"}, new string[] {"[endsummary.traitorinfo]" /*TraitorManager != null ? TraitorManager.GetEndMessage() : ""*/});
            var roundSummaryStart = roundSummary.LastIndexOf('/') + 1;

            string endMessage = string.Join("/",  new[] {
                traitorEndMessage.Substring(0, traitorEndMessageStart),
                "[endsummary.traitorinfo]=" + traitorEndMessage.Substring(traitorEndMessageStart),
                roundSummary.Substring(0, roundSummaryStart),
                "[endsummary]=" + roundSummary.Substring(roundSummaryStart),
                "[endsummary]\n\n[endsummary.traitorinfo]"
            }.Where(s => !string.IsNullOrEmpty(s)));

            Mission mission = GameMain.GameSession.Mission;
            GameMain.GameSession.GameMode.End(endMessage);

            endRoundTimer = 0.0f;

            if (serverSettings.AutoRestart)
            {
                serverSettings.AutoRestartTimer = serverSettings.AutoRestartInterval;
                //send a netlobby update to get the clients' autorestart timers up to date
                GameMain.NetLobbyScreen.LastUpdateID++;
            }

            if (serverSettings.SaveServerLogs) serverSettings.ServerLog.Save();

            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            entityEventManager.Clear();
            foreach (Client c in connectedClients)
            {
                c.EntityEventLastSent.Clear();
                c.PendingPositionUpdates.Clear();
                c.PositionUpdateLastSent.Clear();
            }

            KarmaManager.OnRoundEnded();

#if DEBUG
            messageCount.Clear();
#endif

            respawnManager = null;
            gameStarted = false;

            if (connectedClients.Count > 0)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ServerPacketHeader.ENDGAME);
                msg.Write(endMessage);
                msg.Write(mission != null && mission.Completed);
                msg.Write(GameMain.GameSession?.WinningTeam == null ? (byte)0 : (byte)GameMain.GameSession.WinningTeam);

                foreach (Client client in connectedClients)
                {
                    serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
                    client.Character = null;
                    client.HasSpawned = false;
                    client.InGame = false;
                }
            }

            Submarine.Unload();
            entityEventManager.Clear();
            GameMain.NetLobbyScreen.Select();
            Log("Round ended.", ServerLog.MessageType.ServerMessage);

            GameMain.NetLobbyScreen.RandomizeSettings();
        }

        public override void AddChatMessage(ChatMessage message)
        {
            if (string.IsNullOrEmpty(message.Text)) { return; }
            Log(message.TextWithSender, ServerLog.MessageType.Chat);

            base.AddChatMessage(message);
        }

        private bool TryChangeClientName(Client c, string newName)
        {
            if (c == null || string.IsNullOrEmpty(newName)) { return false; }

            newName = Client.SanitizeName(newName);
            if (newName == c.Name) { return false; }

            //update client list even if the name cannot be changed to the one sent by the client,
            //so the client will be informed what their actual name is
            LastClientListUpdateID++;

            if (c.Connection != OwnerConnection)
            {
                if (!Client.IsValidName(newName, serverSettings))
                {
                    SendDirectChatMessage("Could not change your name to \"" + newName + "\" (the name contains disallowed symbols).", c, ChatMessageType.MessageBox);
                    return false;
                }
                if (Homoglyphs.Compare(newName.ToLower(), Name.ToLower()))
                {
                    SendDirectChatMessage("Could not change your name to \"" + newName + "\" (too similar to the server's name).", c, ChatMessageType.MessageBox);
                    return false;
                }
            }

            Client nameTaken = ConnectedClients.Find(c2 => c != c2 && Homoglyphs.Compare(c2.Name.ToLower(), newName.ToLower()));
            if (nameTaken != null)
            {
                SendDirectChatMessage("Could not change your name to \"" + newName + "\" (too similar to the name of the client \"" + nameTaken.Name + "\").", c, ChatMessageType.MessageBox);
                return false;
            }

            SendChatMessage("Player \"" + c.Name + "\" has changed their name to \"" + newName + "\".", ChatMessageType.Server);
            c.Name = newName;
            c.Connection.Name = newName;
            return true;
        }

        public override void KickPlayer(string playerName, string reason)
        {
            playerName = playerName.ToLowerInvariant();

            Client client = connectedClients.Find(c =>
                c.Name.ToLowerInvariant() == playerName ||
                (c.Character != null && c.Character.Name.ToLowerInvariant() == playerName));

            KickClient(client, reason);
        }

        public void KickClient(NetworkConnection conn, string reason)
        {
            if (conn == OwnerConnection) return;

            Client client = connectedClients.Find(c => c.Connection == conn);
            KickClient(client, reason);
        }

        public void KickClient(Client client, string reason, bool resetKarma = false)
        {
            if (client == null || client.Connection == OwnerConnection) return;

            if (resetKarma)
            {
                var previousPlayer = previousPlayers.Find(p => p.MatchesClient(client));
                if (previousPlayer != null)
                {
                    previousPlayer.Karma = Math.Max(previousPlayer.Karma, 50.0f);
                }
                client.Karma = Math.Max(client.Karma, 50.0f);
            }

            string msg = DisconnectReason.Kicked.ToString();
            string logMsg = $"ServerMessage.KickedFromServer~[client]={client.Name}";
            DisconnectClient(client, logMsg, msg, reason);
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

        public void BanClient(Client client, string reason, bool range = false, TimeSpan? duration = null)
        {
            if (client == null || client.Connection == OwnerConnection) { return; }

            var previousPlayer = previousPlayers.Find(p => p.MatchesClient(client));
            if (previousPlayer != null)
            {
                //reset karma to a neutral value, so if/when the ban is revoked the client wont get immediately punished by low karma again
                previousPlayer.Karma = Math.Max(previousPlayer.Karma, 50.0f);
            }
            client.Karma = Math.Max(client.Karma, 50.0f);

            string targetMsg = DisconnectReason.Banned.ToString();
            DisconnectClient(client, $"ServerMessage.BannedFromServer~[client]={client.Name}", targetMsg, reason);

            if (client.SteamID == 0 || range)
            {
                string ip = "";
                if (client.Connection is LidgrenConnection lidgrenConn)
                {
                    ip = lidgrenConn.IPEndPoint.Address.IsIPv4MappedToIPv6 ?
                    lidgrenConn.IPEndPoint.Address.MapToIPv4().ToString() :
                    lidgrenConn.IPEndPoint.Address.ToString();
                    if (range) { ip = serverSettings.BanList.ToRange(ip); }
                }

                serverSettings.BanList.BanPlayer(client.Name, ip, reason, duration);
            }
            if (client.SteamID > 0)
            {
                serverSettings.BanList.BanPlayer(client.Name, client.SteamID, reason, duration);
            }
        }

        public override void UnbanPlayer(string playerName, string playerIP)
        {
            if (!string.IsNullOrEmpty(playerIP))
            {
                serverSettings.BanList.UnbanIP(playerIP);
            }
            else if (!string.IsNullOrEmpty(playerName))
            {
                serverSettings.BanList.UnbanPlayer(playerName);
            }
        }

        public void DisconnectClient(NetworkConnection senderConnection, string msg = "", string targetmsg = "")
        {
            if (senderConnection == OwnerConnection)
            {
                DebugConsole.NewMessage("Owner disconnected: closing the server...", Color.Yellow);
                Log("Owner disconnected: closing the server...", ServerLog.MessageType.ServerMessage);
                GameMain.ShouldRun = false;
            }
            Client client = connectedClients.Find(x => x.Connection == senderConnection);
            if (client == null) return;

            DisconnectClient(client, msg, targetmsg, string.Empty);
        }

        public void DisconnectClient(Client client, string msg = "", string targetmsg = "", string reason = "")
        {
            if (client == null) return;

            if (gameStarted && client.Character != null)
            {
                client.Character.ClientDisconnected = true;
                client.Character.ClearInputs();
            }

            client.Character = null;
            client.HasSpawned = false;
            client.InGame = false;

            if (string.IsNullOrWhiteSpace(msg)) msg = $"ServerMessage.ClientLeftServer~[client]={client.Name}";
            if (string.IsNullOrWhiteSpace(targetmsg)) targetmsg = "ServerMessage.YouLeftServer";
            if (!string.IsNullOrWhiteSpace(reason))
            {
                msg += $"/ /ServerMessage.Reason/: /{reason}";
                targetmsg += $"/\n/ServerMessage.Reason/: /{reason}";
            }

            if (client.SteamID != 0) { SteamManager.StopAuthSession(client.SteamID); }

            var previousPlayer = previousPlayers.Find(p => p.MatchesClient(client));
            if (previousPlayer == null)
            {
                previousPlayer = new PreviousPlayer(client);
                previousPlayers.Add(previousPlayer);
            }
            previousPlayer.Name = client.Name;
            previousPlayer.Karma = client.Karma;
            previousPlayer.KarmaKickCount = client.KarmaKickCount;
            previousPlayer.KickVoters.Clear();
            foreach (Client c in connectedClients)
            {
                if (client.HasKickVoteFrom(c)) { previousPlayer.KickVoters.Add(c); }
            }

            serverPeer.Disconnect(client.Connection, targetmsg);
            client.Dispose();
            connectedClients.Remove(client);

            KarmaManager.OnClientDisconnected(client);

            UpdateVoteStatus();

            SendChatMessage(msg, ChatMessageType.Server);

            UpdateCrewFrame();

            serverSettings.ServerDetailsChanged = true;
            refreshMasterTimer = DateTime.Now;
        }

        private void UpdateCrewFrame()
        {
            foreach (Client c in connectedClients)
            {
                if (c.Character == null || !c.InGame) continue;
            }
        }

        public void SendDirectChatMessage(string txt, Client recipient, ChatMessageType messageType = ChatMessageType.Server)
        {
            ChatMessage msg = ChatMessage.Create("", txt, messageType, null);
            SendDirectChatMessage(msg, recipient);
        }

        public void SendConsoleMessage(string txt, Client recipient)
        {
            ChatMessage msg = ChatMessage.Create("", txt, ChatMessageType.Console, null);
            SendDirectChatMessage(msg, recipient);
        }

        public void SendDirectChatMessage(ChatMessage msg, Client recipient)
        {
            if (recipient == null)
            {
                string errorMsg = "Attempted to send a chat message to a null client.\n" + Environment.StackTrace;
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("GameServer.SendDirectChatMessage:ClientNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

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
                string command = ChatMessage.GetChatMessageCommand(message, out string tempStr);
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
                            if (command.ToLower() == name.ToLower())
                            {
                                //a private message to the host
                                if (OwnerConnection != null)
                                {
                                    targetClient = connectedClients.Find(c => c.Connection == OwnerConnection);
                                }
                            }
                            else
                            {
                                targetClient = connectedClients.Find(c =>
                                    command.ToLower() == c.Name.ToLower() ||
                                    command.ToLower() == c.Character?.Name?.ToLower());

                                if (targetClient == null)
                                {
                                    if (senderClient != null)
                                    {
                                        var chatMsg = ChatMessage.Create(
                                            "", $"ServerMessage.PlayerNotFound~[player]={command}",
                                            ChatMessageType.Error, null);
                                        SendDirectChatMessage(chatMsg, senderClient);
                                    }
                                    else
                                    {
                                        AddChatMessage($"ServerMessage.PlayerNotFound~[player]={command}", ChatMessageType.Error);
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
                        senderName = name;
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

                    //sender doesn't have a character or the character can't speak -> only ChatMessageType.Dead allowed
                    if (senderCharacter == null || senderCharacter.IsDead || senderCharacter.SpeechImpediment >= 100.0f)
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
                    //character still alive and capable of speaking -> dead chat not allowed
                    if (senderClient != null && senderCharacter != null && !senderCharacter.IsDead && senderCharacter.SpeechImpediment < 100.0f)
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
            else if (type == ChatMessageType.Radio)
            {
                //send to chat-linked wifi components
                senderRadio.TransmitSignal(0, message, senderRadio.Item, senderCharacter, false);
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
                            if (senderCharacter != client.Character)
                            {
                                modifiedMessage = ChatMessage.ApplyDistanceEffect(message, (ChatMessageType)type, senderCharacter, client.Character);
                            }

                            //too far to hear the msg -> don't send
                            if (string.IsNullOrWhiteSpace(modifiedMessage)) continue;
                        }
                        break;
                    case ChatMessageType.Dead:
                        //character still alive -> don't send
                        if (client != senderClient && client.Character != null && !client.Character.IsDead) continue;
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

            if (type.Value != ChatMessageType.MessageBox)
            {
                string myReceivedMessage = type == ChatMessageType.Server || type == ChatMessageType.Error ? TextManager.GetServerMessage(message) : message;
                if (!string.IsNullOrWhiteSpace(myReceivedMessage))
                {
                    AddChatMessage(myReceivedMessage, (ChatMessageType)type, senderName, senderCharacter);
                }
            }
        }

        public void SendOrderChatMessage(OrderChatMessage message)
        {
            if (message.Sender == null || message.Sender.SpeechImpediment >= 100.0f) return;
            ChatMessageType messageType = ChatMessage.CanUseRadio(message.Sender) ? ChatMessageType.Radio : ChatMessageType.Default;

            //check which clients can receive the message and apply distance effects
            foreach (Client client in ConnectedClients)
            {
                string modifiedMessage = message.Text;

                if (message.Sender != null &&
                    client.Character != null && !client.Character.IsDead)
                {
                    if (message.Sender != client.Character)
                    {
                        modifiedMessage = ChatMessage.ApplyDistanceEffect(message.Text, messageType, message.Sender, client.Character);
                    }

                    //too far to hear the msg -> don't send
                    if (string.IsNullOrWhiteSpace(modifiedMessage)) continue;
                }

                SendDirectChatMessage(new OrderChatMessage(message.Order, message.OrderOption, message.TargetEntity, message.TargetCharacter, message.Sender), client);
            }

            string myReceivedMessage = message.Text;

            if (!string.IsNullOrWhiteSpace(myReceivedMessage))
            {
                AddChatMessage(new OrderChatMessage(message.Order, message.OrderOption, myReceivedMessage, message.TargetEntity, message.TargetCharacter, message.Sender));
            }
        }

        private void FileTransferChanged(FileSender.FileTransferOut transfer)
        {
            Client recipient = connectedClients.Find(c => c.Connection == transfer.Connection);
            if (transfer.FileType == FileTransferType.CampaignSave &&
                (transfer.Status == FileTransferStatus.Sending || transfer.Status == FileTransferStatus.Finished) &&
                recipient.LastCampaignSaveSendTime != null)
            {
                recipient.LastCampaignSaveSendTime.Second = (float)Lidgren.Network.NetTime.Now;
            }
        }

        public void SendCancelTransferMsg(FileSender.FileTransferOut transfer)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.FILE_TRANSFER);
            msg.Write((byte)FileTransferMessageType.Cancel);
            msg.Write((byte)transfer.ID);
            serverPeer.Send(msg, transfer.Connection, DeliveryMethod.ReliableOrdered);
        }

        public void UpdateVoteStatus()
        {
            if (connectedClients.Count == 0) return;

            Client.UpdateKickVotes(connectedClients);

            var clientsToKick = connectedClients.FindAll(c =>
                c.Connection != OwnerConnection &&
                c.KickVoteCount >= connectedClients.Count * serverSettings.KickVoteRequiredRatio);
            foreach (Client c in clientsToKick)
            {
                var previousPlayer = previousPlayers.Find(p => p.MatchesClient(c));
                if (previousPlayer != null)
                {
                    //reset the client's kick votes (they can rejoin after their ban expires)
                    previousPlayer.KickVoters.Clear();
                }

                SendChatMessage($"ServerMessage.KickedFromServer~[client]={c.Name}", ChatMessageType.Server, null);
                KickClient(c, "ServerMessage.KickedByVote");
                BanClient(c, "ServerMessage.KickedByVoteAutoBan", duration: TimeSpan.FromSeconds(serverSettings.AutoBanTime));
            }

            GameMain.NetLobbyScreen.LastUpdateID++;

            SendVoteStatus(connectedClients);

            if (serverSettings.Voting.AllowEndVoting && EndVoteMax > 0 &&
                ((float)EndVoteCount / (float)EndVoteMax) >= serverSettings.EndVoteRequiredRatio)
            {
                Log("Ending round by votes (" + EndVoteCount + "/" + (EndVoteMax - EndVoteCount) + ")", ServerLog.MessageType.ServerMessage);
                EndGame();
            }
        }

        public void SendVoteStatus(List<Client> recipients)
        {
            if (!recipients.Any()) { return; }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.UPDATE_LOBBY);
            msg.Write((byte)ServerNetObject.VOTE);
            serverSettings.Voting.ServerWrite(msg);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            foreach (var c in recipients)
            {
                serverPeer.Send(msg, c.Connection, DeliveryMethod.Reliable);
            }
        }

        public void UpdateClientPermissions(Client client)
        {
            if (client.SteamID > 0)
            {
                serverSettings.ClientPermissions.RemoveAll(cp => cp.SteamID == client.SteamID);
                if (client.Permissions != ClientPermissions.None)
                {
                    serverSettings.ClientPermissions.Add(new ServerSettings.SavedClientPermission(
                        client.Name,
                        client.SteamID,
                        client.Permissions,
                        client.PermittedConsoleCommands));
                }
            }
            else
            {
                serverSettings.ClientPermissions.RemoveAll(cp => client.EndpointMatches(cp.EndPoint));
                if (client.Permissions != ClientPermissions.None)
                {
                    serverSettings.ClientPermissions.Add(new ServerSettings.SavedClientPermission(
                        client.Name,
                        client.Connection.EndPointString,
                        client.Permissions,
                        client.PermittedConsoleCommands));
                }
            }

            //send the message to the client whose permissions are being modified and the clients who are allowed to modify permissions
            List<Client> recipients = new List<Client>() { client };
            foreach (Client otherClient in connectedClients)
            {
                if (otherClient.HasPermission(ClientPermissions.ManagePermissions) && !recipients.Contains(otherClient))
                {
                    recipients.Add(otherClient);
                }
            }
            foreach (Client recipient in recipients)
            {
                CoroutineManager.StartCoroutine(SendClientPermissionsAfterClientListSynced(recipient, client));
            }
            serverSettings.SaveClientPermissions();
        }


        private IEnumerable<object> SendClientPermissionsAfterClientListSynced(Client recipient, Client client)
        {
            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 10);
            while (recipient.LastRecvClientListUpdate < LastClientListUpdateID)
            {
                if (DateTime.Now > timeOut || GameMain.Server == null || !connectedClients.Contains(recipient))
                {
                    yield return CoroutineStatus.Success;
                }
                yield return null;
            }

            SendClientPermissions(recipient, client);
            yield return CoroutineStatus.Success;
        }


        private void SendClientPermissions(Client recipient, Client client)
        {
            if (recipient?.Connection == null) { return; }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.PERMISSIONS);
            client.WritePermissions(msg);
            serverPeer.Send(msg, recipient.Connection, DeliveryMethod.Reliable);
        }

        public void GiveAchievement(Character character, string achievementIdentifier)
        {
            achievementIdentifier = achievementIdentifier.ToLowerInvariant();
            foreach (Client client in connectedClients)
            {
                if (client.Character == character)
                {
                    GiveAchievement(client, achievementIdentifier);
                    return;
                }
            }
        }

        public void GiveAchievement(Client client, string achievementIdentifier)
        {
            if (client.GivenAchievements.Contains(achievementIdentifier)) return;
            client.GivenAchievements.Add(achievementIdentifier);

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.ACHIEVEMENT);
            msg.Write(achievementIdentifier);

            serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
        }

        public void SendTraitorMessage(Client client, string message, string missionIdentifier, TraitorMessageType messageType)
        {
            if (client == null) { return; }
            var msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.TRAITOR_MESSAGE);
            msg.Write((byte)messageType);
            msg.Write(missionIdentifier ?? "");
            msg.Write(message);
            serverPeer.Send(msg, client.Connection, DeliveryMethod.ReliableOrdered);
        }

        public void UpdateCheatsEnabled()
        {
            if (!connectedClients.Any()) { return; }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.CHEATS_ENABLED);
            msg.Write(DebugConsole.CheatsEnabled);
            msg.WritePadBits();

            foreach (Client c in connectedClients)
            {
                serverPeer.Send(msg, c.Connection, DeliveryMethod.Reliable);
            }
        }

        public void SetClientCharacter(Client client, Character newCharacter)
        {
            if (client == null) return;

            //the client's previous character is no longer a remote player
            if (client.Character != null)
            {
                client.Character.IsRemotePlayer = false;
                client.Character.OwnerClientEndPoint = null;
                client.Character.OwnerClientName = null;
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
                newCharacter.ClientDisconnected = false;
                newCharacter.KillDisconnectedTimer = 0.0f;
                newCharacter.ResetNetState();
                if (client.Character != null)
                {
                    newCharacter.LastNetworkUpdateID = client.Character.LastNetworkUpdateID;
                }

                newCharacter.OwnerClientEndPoint = client.Connection.EndPointString;
                newCharacter.OwnerClientName = client.Name;
                newCharacter.IsRemotePlayer = true;
                newCharacter.Enabled = true;
                client.Character = newCharacter;
                CreateEntityEvent(newCharacter, new object[] { NetEntityEvent.Type.Control, client });
            }
        }

        private void UpdateCharacterInfo(IReadMessage message, Client sender)
        {
            sender.SpectateOnly = message.ReadBoolean() && (serverSettings.AllowSpectating || sender.Connection == OwnerConnection);
            if (sender.SpectateOnly)
            {
                return;
            }

            Gender gender = Gender.Male;
            Race race = Race.White;
            int headSpriteId = 0;
            try
            {
                gender = (Gender)message.ReadByte();
                race = (Race)message.ReadByte();
                headSpriteId = message.ReadByte();
            }
            catch (Exception e)
            {
                //gender = Gender.Male;
                //race = Race.White;
                //headSpriteId = 0;
                DebugConsole.Log("Received invalid characterinfo from \"" + sender.Name + "\"! { " + e.Message + " }");
            }
            int hairIndex = message.ReadByte();
            int beardIndex = message.ReadByte();
            int moustacheIndex = message.ReadByte();
            int faceAttachmentIndex = message.ReadByte();

            List<Pair<JobPrefab, int>> jobPreferences = new List<Pair<JobPrefab, int>>();
            int count = message.ReadByte();
            // TODO: modding support?
            for (int i = 0; i < Math.Min(count, 3); i++)
            {
                string jobIdentifier = message.ReadString();
                int variant = message.ReadByte();
                if (JobPrefab.List.TryGetValue(jobIdentifier, out JobPrefab jobPrefab))
                {
                    jobPreferences.Add(new Pair<JobPrefab, int>(jobPrefab, variant));
                }
            }

            sender.CharacterInfo = new CharacterInfo(Character.HumanSpeciesName, sender.Name);
            sender.CharacterInfo.RecreateHead(headSpriteId, race, gender, hairIndex, beardIndex, moustacheIndex, faceAttachmentIndex);

            //if the client didn't provide job preferences, we'll use the preferences that are randomly assigned in the Client constructor
            Debug.Assert(sender.JobPreferences.Count > 0);
            if (jobPreferences.Count > 0)
            {
                sender.JobPreferences = jobPreferences;
            }
        }

        public void AssignJobs(List<Client> unassigned)
        {
            var jobList = JobPrefab.List.Values.ToList();
            unassigned = new List<Client>(unassigned);

            Dictionary<JobPrefab, int> assignedClientCount = new Dictionary<JobPrefab, int>();
            foreach (JobPrefab jp in jobList)
            {
                assignedClientCount.Add(jp, 0);
            }

            Character.TeamType teamID = Character.TeamType.None;
            if (unassigned.Count > 0) { teamID = unassigned[0].TeamID; }

            //if we're playing a multiplayer campaign, check which clients already have a character and a job
            //(characters are persistent in campaigns)
            if (GameMain.GameSession.GameMode is MultiPlayerCampaign multiplayerCampaign)
            {
                var campaignAssigned = multiplayerCampaign.GetAssignedJobs(connectedClients);
                //remove already assigned clients from unassigned
                unassigned.RemoveAll(u => campaignAssigned.ContainsKey(u));
                //add up to assigned client count
                foreach (KeyValuePair<Client, Job> clientJob in campaignAssigned)
                {
                    assignedClientCount[clientJob.Value.Prefab]++;
                    clientJob.Key.AssignedJob = new Pair<JobPrefab, int>(clientJob.Value.Prefab, clientJob.Value.Variant);
                }
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
                if (unassigned[i].JobPreferences.Count == 0) continue;
                if (!unassigned[i].JobPreferences[0].First.AllowAlways) continue;
                unassigned[i].AssignedJob = unassigned[i].JobPreferences[0];
                unassigned.RemoveAt(i);
            }

            //go throught the jobs whose MinNumber>0 (i.e. at least one crew member has to have the job)
            bool unassignedJobsFound = true;
            while (unassignedJobsFound && unassigned.Count > 0)
            {
                unassignedJobsFound = false;

                foreach (JobPrefab jobPrefab in jobList)
                {
                    if (unassigned.Count == 0) break;
                    if (jobPrefab.MinNumber < 1 || assignedClientCount[jobPrefab] >= jobPrefab.MinNumber) continue;

                    //find the client that wants the job the most, or force it to random client if none of them want it
                    Client assignedClient = FindClientWithJobPreference(unassigned, jobPrefab, true);

                    assignedClient.AssignedJob = new Pair<JobPrefab, int>(jobPrefab, 0);
                    assignedClientCount[jobPrefab]++;
                    unassigned.Remove(assignedClient);

                    //the job still needs more crew members, set unassignedJobsFound to true to keep the while loop running
                    if (assignedClientCount[jobPrefab] < jobPrefab.MinNumber) unassignedJobsFound = true;
                }
            }

            //attempt to give the clients a job they have in their job preferences
            for (int i = unassigned.Count - 1; i >= 0; i--)
            {
                foreach (Pair<JobPrefab, int> preferredJob in unassigned[i].JobPreferences)
                {
                    //the maximum number of players that can have this job hasn't been reached yet
                    // -> assign it to the client
                    if (assignedClientCount[preferredJob.First] < preferredJob.First.MaxNumber && unassigned[i].Karma >= preferredJob.First.MinKarma)
                    {
                        unassigned[i].AssignedJob = preferredJob;
                        assignedClientCount[preferredJob.First]++;
                        unassigned.RemoveAt(i);
                        break;
                    }
                }
            }

            //give random jobs to rest of the clients
            foreach (Client c in unassigned)
            {
                //find all jobs that are still available
                var remainingJobs = jobList.FindAll(jp => assignedClientCount[jp] < jp.MaxNumber && c.Karma >= jp.MinKarma);

                //all jobs taken, give a random job
                if (remainingJobs.Count == 0)
                {
                    DebugConsole.ThrowError("Failed to assign a suitable job for \"" + c.Name + "\" (all jobs already have the maximum numbers of players). Assigning a random job...");
                    int jobIndex = Rand.Range(0, jobList.Count);
                    int skips = 0;
                    while (c.Karma < jobList[jobIndex].MinKarma)
                    {
                        jobIndex++;
                        skips++;
                        if (jobIndex >= jobList.Count) jobIndex -= jobList.Count;
                        if (skips >= jobList.Count) break;
                    }
                    c.AssignedJob = new Pair<JobPrefab, int>(jobList[jobIndex], 0);
                    assignedClientCount[c.AssignedJob.First]++;
                }
                else //some jobs still left, choose one of them by random
                {
                    c.AssignedJob = new Pair<JobPrefab, int>(remainingJobs[Rand.Range(0, remainingJobs.Count)], 0);
                    assignedClientCount[c.AssignedJob.First]++;
                }
            }
        }

        public void AssignBotJobs(List<CharacterInfo> bots, Character.TeamType teamID)
        {
            var jobList = JobPrefab.List.Values.ToList();
            Dictionary<JobPrefab, int> assignedPlayerCount = new Dictionary<JobPrefab, int>();
            foreach (JobPrefab jp in jobList)
            {
                assignedPlayerCount.Add(jp, 0);
            }

            //count the clients who already have characters with an assigned job
            foreach (Client c in connectedClients)
            {
                if (c.TeamID != teamID) continue;
                if (c.Character?.Info?.Job != null && !c.Character.IsDead)
                {
                    assignedPlayerCount[c.Character.Info.Job.Prefab]++;
                }
                else if (c.CharacterInfo?.Job != null)
                {
                    assignedPlayerCount[c.CharacterInfo?.Job.Prefab]++;
                }
            }

            List<CharacterInfo> unassignedBots = new List<CharacterInfo>(bots);
            foreach (CharacterInfo bot in bots)
            {
                foreach (JobPrefab jobPrefab in jobList)
                {
                    if (jobPrefab.MinNumber < 1 || assignedPlayerCount[jobPrefab] >= jobPrefab.MinNumber) continue;
                    bot.Job = new Job(jobPrefab);
                    assignedPlayerCount[jobPrefab]++;
                    unassignedBots.Remove(bot);
                    break;
                }
            }

            //find a suitable job for the rest of the players
            foreach (CharacterInfo c in unassignedBots)
            {
                //find all jobs that are still available
                var remainingJobs = jobList.FindAll(jp => assignedPlayerCount[jp] < jp.MaxNumber);
                //all jobs taken, give a random job
                if (remainingJobs.Count == 0)
                {
                    DebugConsole.ThrowError("Failed to assign a suitable job for bot \"" + c.Name + "\" (all jobs already have the maximum numbers of players). Assigning a random job...");
                    c.Job = Job.Random();
                    assignedPlayerCount[c.Job.Prefab]++;
                }
                else //some jobs still left, choose one of them by random
                {
                    c.Job = new Job(remainingJobs[Rand.Range(0, remainingJobs.Count)]);
                    assignedPlayerCount[c.Job.Prefab]++;
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
                int index = c.JobPreferences.IndexOf(c.JobPreferences.Find(j => j.First == job));
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
            if (GameMain.Server == null || !GameMain.Server.ServerSettings.SaveServerLogs) return;

            GameMain.Server.ServerSettings.ServerLog.WriteLine(line, messageType);

            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                if (!client.HasPermission(ClientPermissions.ServerLog)) continue;
                //use sendername as the message type
                GameMain.Server.SendDirectChatMessage(
                    ChatMessage.Create(messageType.ToString(), line, ChatMessageType.ServerLog, null),
                    client);
            }
        }

        public Tuple<ulong, string> FindPreviousClientData(Client client)
        {
            var player = previousPlayers.Find(p => p.MatchesClient(client));
            if (player != null)
            {
                return Tuple.Create(player.SteamID, player.EndPoint);
            }
            return null;
        }

        public override void Disconnect()
        {
            if (started)
            {
                started = false;

                serverSettings.BanList.Save();
                serverSettings.SaveSettings();

                if (registeredToMaster)
                {
                    if (restClient != null)
                    {
                        var request = new RestRequest("masterserver2.php", Method.GET);
                        request.AddParameter("action", "removeserver");
                        request.AddParameter("serverport", Port);
                        restClient.Execute(request);
                        restClient = null;
                    }
                }

                if (serverSettings.SaveServerLogs)
                {
                    Log("Shutting down the server...", ServerLog.MessageType.ServerMessage);
                    serverSettings.ServerLog.Save();
                }

                GameAnalyticsManager.AddDesignEvent("GameServer:ShutDown");
                serverPeer?.Close(DisconnectReason.ServerShutdown.ToString());

                SteamManager.CloseServer();
            }
        }
    }

    partial class PreviousPlayer
    {
        public string Name;
        public string EndPoint;
        public UInt64 SteamID;
        public float Karma;
        public int KarmaKickCount;
        public readonly List<Client> KickVoters = new List<Client>();

        public PreviousPlayer(Client c)
        {
            Name = c.Name;
            EndPoint = c.Connection?.EndPointString ?? "";
            SteamID = c.SteamID;
        }

        public bool MatchesClient(Client c)
        {
            if (c.SteamID > 0 && SteamID > 0) { return c.SteamID == SteamID; }
            return c.EndpointMatches(EndPoint);
        }
    }
}
