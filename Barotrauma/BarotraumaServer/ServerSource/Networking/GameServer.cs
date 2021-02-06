#define ALLOW_BOT_TRAITORS
using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Items.Components;
using Barotrauma.Steam;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        public override bool IsServer
        {
            get { return true; }
        }

        private string serverName;

        public string ServerName
        {
            get { return serverName; }
            set
            {
                if (string.IsNullOrEmpty(value)) { return; }
                serverName = value.Replace(":", "").Replace(";", "");
            }
        }

        public bool SubmarineSwitchLoad = false;

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

        private readonly ServerEntityEventManager entityEventManager;

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
        private readonly int? ownerKey;
        private readonly UInt64? ownerSteamId;

        public GameServer(string name, int port, int queryPort = 0, bool isPublic = false, string password = "", bool attemptUPnP = false, int maxPlayers = 10, int? ownKey = null, UInt64? steamId = null)
        {
            name = name.Replace(":", "");
            name = name.Replace(";", "");
            if (name.Length > NetConfig.ServerNameMaxLength)
            {
                name = name.Substring(0, NetConfig.ServerNameMaxLength);
            }

            this.serverName = name;

            LastClientListUpdateID = 0;

            serverSettings = new ServerSettings(this, name, port, queryPort, maxPlayers, isPublic, attemptUPnP);
            KarmaManager.SelectPreset(serverSettings.KarmaPreset);
            serverSettings.SetPassword(password);

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
                    Log("Using Lidgren networking. Manual port forwarding may be required. If players cannot connect to the server, you may want to use the in-game hosting menu (which uses SteamP2P networking and does not require port forwarding).", ServerLog.MessageType.ServerMessage);
                    serverPeer = new LidgrenServerPeer(ownerKey, serverSettings);
                }
                else
                {
                    Log("Using SteamP2P networking.", ServerLog.MessageType.ServerMessage);
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
#if USE_STEAM
                registeredToMaster = SteamManager.CreateServer(this, isPublic);
#endif
            }

            TickRate = serverSettings.TickRate;

            Log("Server started", ServerLog.MessageType.ServerMessage);

            GameMain.NetLobbyScreen.Select();
            GameMain.NetLobbyScreen.RandomizeSettings();
            if (!string.IsNullOrEmpty(serverSettings.SelectedSubmarine)) 
            {
                SubmarineInfo sub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == serverSettings.SelectedSubmarine);
                if (sub != null) { GameMain.NetLobbyScreen.SelectedSub = sub; }
            }
            if (!string.IsNullOrEmpty(serverSettings.SelectedShuttle))
            {
                SubmarineInfo shuttle = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == serverSettings.SelectedShuttle);
                if (shuttle != null) { GameMain.NetLobbyScreen.SelectedShuttle = shuttle; }
            }

            List<SubmarineInfo> campaignSubs = new List<SubmarineInfo>();
            if (serverSettings.CampaignSubmarines != null && serverSettings.CampaignSubmarines.Length > 0)
            {
                string[] submarines = serverSettings.CampaignSubmarines.Split(ServerSettings.SubmarineSeparatorChar);
                for (int i = 0; i < submarines.Length; i++)
                {
                    SubmarineInfo subInfo = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == submarines[i]);
                    if (subInfo != null && subInfo.IsCampaignCompatible)
                    {
                        campaignSubs.Add(subInfo);
                    }
                }
            }
            else
            {
                // Add vanilla submarines by default
                for (int i = 0; i < SubmarineInfo.SavedSubmarines.Count(); i++)
                {
                    SubmarineInfo subInfo = SubmarineInfo.SavedSubmarines.ElementAt(i);
                    if (subInfo.IsVanillaSubmarine() && subInfo.IsCampaignCompatible)
                    {
                        campaignSubs.Add(SubmarineInfo.SavedSubmarines.ElementAt(i));
                    }
                }
            }
            GameMain.NetLobbyScreen.CampaignSubmarines = campaignSubs;

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
            newClient.Connection.Status = NetworkConnectionStatus.Connected;
            newClient.SteamID = connection.SteamID;
            newClient.Language = connection.Language;
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

            SendChatMessage($"ServerMessage.JoinedServer~[client]={clName}", ChatMessageType.Server, null, changeType: PlayerConnectionChangeType.Joined);
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

        public override void Update(float deltaTime)
        {
#if CLIENT
            if (ShowNetStats) { netStats.Update(deltaTime); }
#endif
            if (!started) { return; }

            if (OwnerConnection != null && ChildServerRelay.HasShutDown)
            {
                Disconnect();
                return;
            }

            base.Update(deltaTime);

            fileSender.Update(deltaTime);
            KarmaManager.UpdateClients(ConnectedClients, deltaTime);

            UpdatePing();

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
                    if (character.IsDead || !character.ClientDisconnected) { continue; }

                    character.KillDisconnectedTimer += deltaTime;
                    character.SetStun(1.0f);

                    Client owner = connectedClients.Find(c => c.EndpointMatches(character.OwnerClientEndPoint));

                    if ((OwnerConnection == null || owner?.Connection != OwnerConnection) && character.KillDisconnectedTimer > serverSettings.KillDisconnectedTime)
                    {
                        character.Kill(CauseOfDeathType.Disconnected, null);
                        continue;
                    }

                    if (owner != null && owner.InGame && !owner.NeedsMidRoundSync &&
                        (!serverSettings.AllowSpectating || !owner.SpectateOnly))
                    {
                        SetClientCharacter(owner, character);
                    }
                }

                if (TraitorManager != null)
                {
                    TraitorManager.Update(deltaTime);
                }

                if (serverSettings.Voting.VoteRunning)
                {
                    Voting.SubVote.Timer += deltaTime;

                    if (Voting.SubVote.Timer >= serverSettings.SubmarineVoteTimeout)
                    {
                        // Do not take unanswered into account for total
                        if (SubmarineVoteYesCount / (float)(SubmarineVoteYesCount + SubmarineVoteNoCount) >= serverSettings.SubmarineVoteRequiredRatio)
                        {
                            SwitchSubmarine();
                        }
                        else
                        {
                            serverSettings.Voting.StopSubmarineVote(false);
                        }
                    }
                }

                bool isCrewDead =
                    connectedClients.All(c => c.Character == null || c.Character.IsDead || c.Character.IsIncapacitated);

                bool subAtLevelEnd = false;
                if (Submarine.MainSub != null && Submarine.MainSubs[1] == null)
                {
                    if (Level.Loaded?.EndOutpost != null)
                    {
                        int charactersInsideOutpost = connectedClients.Count(c =>
                            c.Character != null &&
                            !c.Character.IsDead && !c.Character.IsUnconscious &&
                            c.Character.Submarine == Level.Loaded.EndOutpost);
                        int charactersOutsideOutpost = connectedClients.Count(c =>
                            c.Character != null &&
                            !c.Character.IsDead && !c.Character.IsUnconscious &&
                            c.Character.Submarine != Level.Loaded.EndOutpost);

                        //level finished if the sub is docked to the outpost
                        //or very close and someone from the crew made it inside the outpost
                        subAtLevelEnd =
                            Submarine.MainSub.DockedTo.Contains(Level.Loaded.EndOutpost) ||
                            (Submarine.MainSub.AtEndPosition && charactersInsideOutpost > 0) ||
                            (charactersInsideOutpost > charactersOutsideOutpost);
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
                else if (subAtLevelEnd && !(GameMain.GameSession?.GameMode is CampaignMode))
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
                    else if (subAtLevelEnd)
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
                if (gameStarted && c.Character != null && !c.Character.IsDead && !c.Character.IsIncapacitated)
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

            //don't run the rest of the method if something in serverPeer.Update causes the server to shutdown
            if (!started) { return; }

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
                                + e.Message + "\n" + e.StackTrace.CleanupStackTrace();
                            if (e.InnerException != null)
                            {
                                errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace.CleanupStackTrace();
                            }

                            GameAnalyticsManager.AddErrorEventOnce(
                                "GameServer.Update:ClientWriteFailed" + e.StackTrace.CleanupStackTrace(),
                                GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                                errorMsg);
                        }
                    }

                    foreach (Character character in Character.CharacterList)
                    {
                        if (character.healthUpdateTimer <= 0.0f)
                        {
                            if (!character.HealthUpdatePending)
                            {
                                character.healthUpdateTimer = character.HealthUpdateInterval;
                            }
                            character.HealthUpdatePending = true;
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
                refreshMasterTimer = DateTime.Now + refreshMasterInterval;
                serverSettings.ServerDetailsChanged = false;
            }
        }


        private double lastPingTime;
        private byte[] lastPingData;
        private void UpdatePing()
        {
            if (Timing.TotalTime > lastPingTime + 1.0)
            {
                lastPingData ??= new byte[64];
                for (int i=0;i<lastPingData.Length;i++)
                {
                    lastPingData[i] = (byte)Rand.Range(33, 126);
                }
                lastPingTime = Timing.TotalTime;

                ConnectedClients.ForEach(c =>
                {
                    IWriteMessage pingReq = new WriteOnlyMessage();
                    pingReq.Write((byte)ServerPacketHeader.PING_REQUEST);
                    pingReq.Write((byte)lastPingData.Length);
                    pingReq.Write(lastPingData, 0, lastPingData.Length);
                    serverPeer.Send(pingReq, c.Connection, DeliveryMethod.Unreliable);

                    IWriteMessage pingInf = new WriteOnlyMessage();
                    pingInf.Write((byte)ServerPacketHeader.CLIENT_PINGS);
                    pingInf.Write((byte)ConnectedClients.Count);
                    ConnectedClients.ForEach(c2 =>
                    {
                        pingInf.Write(c2.ID);
                        pingInf.Write(c2.Ping);
                    });
                    serverPeer.Send(pingInf, c.Connection, DeliveryMethod.Unreliable);
                });
            }
        }

        private void ReadDataMessage(NetworkConnection sender, IReadMessage inc)
        {
            var connectedClient = connectedClients.Find(c => c.Connection == sender);

            ClientPacketHeader header = (ClientPacketHeader)inc.ReadByte();
            switch (header)
            {
                case ClientPacketHeader.PING_RESPONSE:
                    byte responseLen = inc.ReadByte();
                    if (responseLen != lastPingData.Length) { return; }
                    for (int i = 0; i < responseLen; i++)
                    {
                        byte b = inc.ReadByte();
                        if (b != lastPingData[i]) { return; }
                    }
                    connectedClient.Ping = (UInt16)((Timing.TotalTime - lastPingTime) * 1000);
                    break;
                case ClientPacketHeader.RESPONSE_STARTGAME:
                    if (connectedClient != null)
                    {
                        connectedClient.ReadyToStart = inc.ReadBoolean();
                        UpdateCharacterInfo(inc, connectedClient);

                        //game already started -> send start message immediately
                        if (gameStarted)
                        {
                            SendStartMessage(roundStartSeed, GameMain.GameSession.Level.Seed, GameMain.GameSession, connectedClient, true);
                        }
                    }
                    break;
                case ClientPacketHeader.REQUEST_STARTGAMEFINALIZE:
                    if (gameStarted && connectedClient != null)
                    {
                        SendRoundStartFinalize(connectedClient);
                    }
                    break;
                case ClientPacketHeader.UPDATE_LOBBY:
                    ClientReadLobby(inc);
                    break;
                case ClientPacketHeader.UPDATE_INGAME:
                    if (!gameStarted) { return; }
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

                        var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.Hash == subHash);

                        if (gameStarted)
                        {
                            SendDirectChatMessage(TextManager.Get("CampaignStartFailedRoundRunning"), connectedClient, ChatMessageType.MessageBox);
                            return;
                        }

                        if (matchingSub == null)
                        {
                            SendDirectChatMessage(
                                TextManager.GetWithVariable("CampaignStartFailedSubNotFound", "[subname]", subName),
                                connectedClient, ChatMessageType.MessageBox);
                        }
                        else
                        {
                            string localSavePath = SaveUtil.CreateSavePath(SaveUtil.SaveType.Multiplayer, saveName);
                            if (connectedClient.HasPermission(ClientPermissions.SelectMode) || connectedClient.HasPermission(ClientPermissions.ManageCampaign))
                            {
                                MultiPlayerCampaign.StartNewCampaign(localSavePath, matchingSub.FilePath, seed);
                            }
                        }
                     }
                    else
                    {
                        string saveName = inc.ReadString();
                        if (gameStarted)
                        {
                            SendDirectChatMessage(TextManager.Get("CampaignStartFailedRoundRunning"), connectedClient, ChatMessageType.MessageBox);
                            return;
                        }
                        if (connectedClient.HasPermission(ClientPermissions.SelectMode) || connectedClient.HasPermission(ClientPermissions.ManageCampaign)) { MultiPlayerCampaign.LoadCampaign(saveName); }
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
                case ClientPacketHeader.CREW:
                    ReadCrewMessage(inc, connectedClient);
                    break;
                case ClientPacketHeader.READY_CHECK:
                    ReadyCheck.ServerRead(inc, connectedClient);
                    break;
                case ClientPacketHeader.FILE_REQUEST:
                    if (serverSettings.AllowFileTransfers)
                    {
                        fileSender.ReadFileRequest(inc, connectedClient);
                    }
                    break;
                case ClientPacketHeader.EVENTMANAGER_RESPONSE:
                    GameMain.GameSession?.EventManager.ServerRead(inc, connectedClient);
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

            Log(GameServer.ClientLogName(c) + " has reported an error: " + errorStr, ServerLog.MessageType.Error);
            GameAnalyticsManager.AddErrorEventOnce("GameServer.HandleClientError:" + errorStr, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorStr);

            try
            {
                WriteEventErrorData(c, errorStr);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to write event error data", e);
            }

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

        private void WriteEventErrorData(Client client, string errorStr)
        {
            if (!Directory.Exists(ServerLog.SavePath))
            {
                Directory.CreateDirectory(ServerLog.SavePath);
            }

            string filePath = "event_error_log_server_" + client.Name + "_" + DateTime.UtcNow.ToShortTimeString() + ".log";
            filePath = Path.Combine(ServerLog.SavePath, ToolBox.RemoveInvalidFileNameChars(filePath));
            if (File.Exists(filePath)) { return; }

            List<string> errorLines = new List<string>
            {
                errorStr, ""
            };

            if (GameMain.GameSession?.GameMode != null)
            {
                errorLines.Add("Game mode: " + GameMain.GameSession.GameMode.Name);
                if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                {
                    errorLines.Add("Campaign ID: " + campaign.CampaignID);
                    errorLines.Add("Campaign save ID: " + campaign.LastSaveID);
                }
                errorLines.Add("Mission: " + (GameMain.GameSession?.Mission?.Prefab.Identifier ?? "none"));                
            }
            if (GameMain.GameSession?.Submarine != null)
            {
                errorLines.Add("Submarine: " + GameMain.GameSession.Submarine.Info.Name);
            }
            if (Level.Loaded != null)
            {
                errorLines.Add("Level: " + Level.Loaded.Seed + ", " + string.Join(", ", Level.Loaded.EqualityCheckValues.Select(cv => cv.ToString("X"))));
                errorLines.Add("Entity count before generating level: " + Level.Loaded.EntityCountBeforeGenerate);
                errorLines.Add("Entities:");
                foreach (Entity e in Level.Loaded.EntitiesBeforeGenerate)
                {
                    errorLines.Add("    " + e.ID + ": " + e.ToString());
                }
                errorLines.Add("Entity count after generating level: " + Level.Loaded.EntityCountAfterGenerate);
            }

            errorLines.Add("Entity IDs:");
            List<Entity> sortedEntities = Entity.GetEntities().ToList();
            sortedEntities.Sort((e1, e2) => e1.ID.CompareTo(e2.ID));
            foreach (Entity e in sortedEntities)
            {
                errorLines.Add(e.ID + ": " + e.ToString());
            }

            errorLines.Add("");
            errorLines.Add("EntitySpawner events:");
            foreach (var entityEvent in entityEventManager.UniqueEvents)
            {
                if (entityEvent.Entity is EntitySpawner)
                {
                    var spawnData = entityEvent.Data[0] as EntitySpawner.SpawnOrRemove;
                    errorLines.Add(
                        entityEvent.ID + ": " + 
                        (spawnData.Remove ? "Remove " : "Create ") + 
                        spawnData.Entity.ToString() + 
                        " (" + spawnData.OriginalID + ", " + spawnData.Entity.ID + ")");
                }
            }

            errorLines.Add("");
            errorLines.Add("Last debug messages:");
            for (int i = DebugConsole.Messages.Count - 1; i > 0 && i > DebugConsole.Messages.Count - 15; i--)
            {
                errorLines.Add("   " + DebugConsole.Messages[i].Time + " - " + DebugConsole.Messages[i].Text);
            }

            File.WriteAllLines(filePath, errorLines);
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

                        TryChangeClientName(c, inc);

                        c.LastRecvCampaignSave = inc.ReadUInt16();
                        if (c.LastRecvCampaignSave > 0)
                        {
                            byte campaignID = inc.ReadByte();
                            c.LastRecvCampaignUpdate = inc.ReadUInt16();
                            bool characterDiscarded = inc.ReadBoolean();

                            if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                            {
                                if (characterDiscarded) { campaign.DiscardClientCharacterData(c); }
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

            bool midroundSyncingDone = inc.ReadBoolean();
            inc.ReadPadBits();
            if (gameStarted)
            {
                if (!c.InGame)
                {
                    //check if midround syncing is needed due to missed unique events
                    if (!midroundSyncingDone) { entityEventManager.InitClientMidRoundSync(c); }
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

                        c.LastRecvCampaignSave = inc.ReadUInt16();
                        if (c.LastRecvCampaignSave > 0)
                        {
                            byte campaignID = inc.ReadByte();
                            c.LastRecvCampaignUpdate = inc.ReadUInt16();
                            bool characterDiscarded = inc.ReadBoolean();

                            if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                            {
                                if (characterDiscarded) { campaign.DiscardClientCharacterData(c); }
                                //the client has a campaign save for another campaign
                                //(the server started a new campaign and the client isn't aware of it yet?)
                                if (campaign.CampaignID != campaignID)
                                {
                                    c.LastRecvCampaignSave = (ushort)(campaign.LastSaveID - 1);
                                    c.LastRecvCampaignUpdate = (ushort)(campaign.LastUpdateID - 1);
                                }
                            }
                        }

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

                        if (NetIdUtils.IsValidId(lastRecvChatMsgID, c.LastRecvChatMsgID, c.LastChatMsgQueueID))
                        {
                            c.LastRecvChatMsgID = lastRecvChatMsgID;
                        }
                        else if (lastRecvChatMsgID != c.LastRecvChatMsgID && GameSettings.VerboseLogging)
                        {
                            DebugConsole.ThrowError(
                                "Invalid lastRecvChatMsgID  " + lastRecvChatMsgID +
                                " (previous: " + c.LastChatMsgQueueID + ", latest: " + c.LastChatMsgQueueID + ")");
                        }

                        if (NetIdUtils.IsValidId(lastRecvEntityEventID, c.LastRecvEntityEventID, lastEntityEventID))
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

        private void ReadCrewMessage(IReadMessage inc, Client sender)
        {
            if (GameMain.GameSession?.Campaign is MultiPlayerCampaign mpCampaign)
            {
                mpCampaign.ServerReadCrew(inc, sender);
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

            var mpCampaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            if (command == ClientPermissions.ManageRound && mpCampaign != null)
            {
                if (!mpCampaign.AllowedToEndRound(sender))
                {
                    return;
                }
            }
            else if (command == ClientPermissions.ManageCampaign && mpCampaign != null)
            {
                if (!mpCampaign.AllowedToManageCampaign(sender))
                {
                    return;
                }
            }
            else if (!sender.HasPermission(command))
            {
                Log("Client \"" + GameServer.ClientLogName(sender) + "\" sent a server command \"" + command + "\". Permission denied.", ServerLog.MessageType.ServerMessage);
                return;
            }

            switch (command)
            {
                case ClientPermissions.Kick:
                    string kickedName = inc.ReadString().ToLowerInvariant();
                    string kickReason = inc.ReadString();
                    var kickedClient = connectedClients.Find(cl => cl != sender && cl.Name.Equals(kickedName, StringComparison.OrdinalIgnoreCase) && cl.Connection != OwnerConnection);
                    if (kickedClient != null)
                    {
                        Log("Client \"" + GameServer.ClientLogName(sender) + "\" kicked \"" + GameServer.ClientLogName(kickedClient) + "\".", ServerLog.MessageType.ServerMessage);
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

                    var bannedClient = connectedClients.Find(cl => cl != sender && cl.Name.Equals(bannedName, StringComparison.OrdinalIgnoreCase) && cl.Connection != OwnerConnection);
                    if (bannedClient != null)
                    {
                        Log("Client \"" + ClientLogName(sender) + "\" banned \"" + ClientLogName(bannedClient) + "\".", ServerLog.MessageType.ServerMessage);
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
                    if (end)
                    {
                        if (gameStarted)
                        {
                            Log("Client \"" + GameServer.ClientLogName(sender) + "\" ended the round.", ServerLog.MessageType.ServerMessage);
                            EndGame();                            
                        }
                    }
                    else
                    {
                        bool continueCampaign = inc.ReadBoolean();
                        if (mpCampaign != null && mpCampaign.GameOver || continueCampaign)
                        {
                            MultiPlayerCampaign.LoadCampaign(GameMain.GameSession.SavePath);
                        }
                        else if (!gameStarted && !initiatedStartGame)
                        {
                            Log("Client \"" + ClientLogName(sender) + "\" started the round.", ServerLog.MessageType.ServerMessage);
                            StartGame();
                        }
                        else if (mpCampaign != null)
                        {
                            var availableTransition = mpCampaign.GetAvailableTransition(out _, out _);
                            switch (availableTransition)
                            {
                                case CampaignMode.TransitionType.ReturnToPreviousEmptyLocation:
                                    mpCampaign.Map.SelectLocation(
                                        mpCampaign.Map.CurrentLocation.Connections.Find(c => c.LevelData == Level.Loaded?.LevelData).OtherLocation(mpCampaign.Map.CurrentLocation));
                                    mpCampaign.LoadNewLevel();
                                    break;
                                case CampaignMode.TransitionType.ProgressToNextEmptyLocation:
                                    mpCampaign.Map.SetLocation(mpCampaign.Map.Locations.IndexOf(Level.Loaded.EndLocation));
                                    mpCampaign.LoadNewLevel();
                                    break;
                                case CampaignMode.TransitionType.None:
#if DEBUG || UNSTABLE
                                    DebugConsole.ThrowError($"Client \"{sender.Name}\" attempted to trigger a level transition. No transitions available.");
#endif
                                    return;
                                default:
                                    Log("Client \"" + ClientLogName(sender) + "\" ended the round.", ServerLog.MessageType.ServerMessage);
                                    mpCampaign.LoadNewLevel();
                                    break;
                            }
                        }
                    }
                    break;
                case ClientPermissions.SelectSub:
                    bool isCampaign = inc.ReadBoolean();
                    if (!isCampaign)
                    {
                        bool isShuttle = inc.ReadBoolean();
                        inc.ReadPadBits();
                        UInt16 subIndex = inc.ReadUInt16();
                        var subList = GameMain.NetLobbyScreen.GetSubList();
                        if (subIndex >= subList.Count)
                        {
                            DebugConsole.NewMessage("Client \"" + GameServer.ClientLogName(sender) + "\" attempted to select a sub, index out of bounds (" + subIndex + ")", Color.Red);
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
                    }
                    else
                    {
                        int subEqualityCheckVal = inc.ReadInt32();
                        bool add = inc.ReadBoolean();
                        SubmarineInfo sub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.EqualityCheckVal == subEqualityCheckVal);

                        if (sub == null)
                        {
                            DebugConsole.NewMessage("Client \"" + GameServer.ClientLogName(sender) + "\" attempted to select a sub that does not exist on the server!", Color.Red);
                        }
                        else
                        {
                            if (add)
                            {
                                GameMain.NetLobbyScreen.AddCampaignSubmarine(sub);
                            }
                            else
                            {
                                GameMain.NetLobbyScreen.RemoveCampaignSubmarine(sub);
                            }
                        }
                    }
                    break;
                case ClientPermissions.SelectMode:
                    UInt16 modeIndex = inc.ReadUInt16();
                    GameMain.NetLobbyScreen.SelectedModeIndex = modeIndex;
                    Log("Gamemode changed to " + GameMain.NetLobbyScreen.GameModes[GameMain.NetLobbyScreen.SelectedModeIndex].Name, ServerLog.MessageType.ServerMessage);

                    if (GameMain.NetLobbyScreen.GameModes[modeIndex].Identifier.Equals("multiplayercampaign", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] saveFiles = SaveUtil.GetSaveFiles(SaveUtil.SaveType.Multiplayer, includeInCompatible: false).ToArray();
                        for (int i = 0; i < saveFiles.Length; i++)
                        {
                            XDocument doc = SaveUtil.LoadGameSessionDoc(saveFiles[i]);
                            if (doc?.Root != null)
                            {
                                saveFiles[i] =
                                    string.Join(";",
                                        saveFiles[i].Replace(';', ' '),
                                        doc.Root.GetAttributeString("submarine", ""),
                                        doc.Root.GetAttributeString("savetime", ""),
                                        doc.Root.GetAttributeString("selectedcontentpackages", ""));
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
                    break;
                case ClientPermissions.ManageCampaign:
                    (GameMain.GameSession.GameMode as MultiPlayerCampaign)?.ServerRead(inc, sender);                    
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
                        logMsg = "Client \"" + GameServer.ClientLogName(sender) + "\" set the permissions of the client \"" + GameServer.ClientLogName(targetClient) + "\" to "
                            + string.Join(", ", permissionNames);
                    }
                    else
                    {
                        logMsg = "Client \"" + GameServer.ClientLogName(sender) + "\" removed all permissions from the client \"" + GameServer.ClientLogName(targetClient) + ".";
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

            }
            if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign &&
                GameMain.NetLobbyScreen.SelectedMode == campaign.Preset &&
                NetIdUtils.IdMoreRecent(campaign.LastSaveID, c.LastRecvCampaignSave))
            {
                //already sent an up-to-date campaign save
                if (c.LastCampaignSaveSendTime != null && campaign.LastSaveID == c.LastCampaignSaveSendTime.First)
                {
                    //the save was sent less than 5 second ago, don't attempt to resend yet
                    //(the client may have received it but hasn't acked us yet)
                    if (c.LastCampaignSaveSendTime.Second > NetTime.Now - 5.0f)
                    {
                        return;
                    }
                }

                if (!fileSender.ActiveTransfers.Any(t => t.Connection == c.Connection && t.FileType == FileTransferType.CampaignSave))
                {
                    fileSender.StartTransfer(c.Connection, FileTransferType.CampaignSave, GameMain.GameSession.SavePath);
                    c.LastCampaignSaveSendTime = new Pair<ushort, float>(campaign.LastSaveID, (float)NetTime.Now);
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
                outmsg.Write((byte)subList[i].SubmarineClass);
                outmsg.Write(subList[i].RequiredContentPackagesInstalled);
            }

            outmsg.Write(GameStarted);
            outmsg.Write(serverSettings.AllowSpectating);

            c.WritePermissions(outmsg);

            if (gameStarted)
            {
                string ownedSubmarineIndexes = string.Empty;
                for (int i = 0; i < subList.Count; i++)
                {
                    if (GameMain.GameSession.OwnedSubmarines.Contains(subList[i]))
                    {
                        ownedSubmarineIndexes += i.ToString();
                        ownedSubmarineIndexes += ";";
                    }
                }

                if (ownedSubmarineIndexes.Length > 0)
                {
                    ownedSubmarineIndexes.Trim(';');
                }
                outmsg.Write(ownedSubmarineIndexes);
            }
        }

        private void ClientWriteIngame(Client c)
        {
            //don't send position updates to characters who are still midround syncing
            //characters or items spawned mid-round don't necessarily exist at the client's end yet
            if (!c.NeedsMidRoundSync)
            {
                foreach (Character character in Character.CharacterList)
                {
                    if (!character.Enabled) { continue; }
                    if (c.SpectatePos == null)
                    {
                        float distSqr = Vector2.DistanceSquared(character.WorldPosition, c.Character.WorldPosition);
                        if (c.Character.ViewTarget != null)
                        {
                            distSqr = Math.Min(distSqr, Vector2.DistanceSquared(character.WorldPosition, c.Character.ViewTarget.WorldPosition));
                        }
                        if (distSqr >= NetConfig.DisableCharacterDistSqr) { continue; }
                    }
                    else
                    {
                        if (character != c.Character && Vector2.DistanceSquared(character.WorldPosition, c.SpectatePos.Value) >= NetConfig.DisableCharacterDistSqr)
                        {
                            continue;
                        }
                    }

                    float updateInterval = character.GetPositionUpdateInterval(c);
                    c.PositionUpdateLastSent.TryGetValue(character, out float lastSent);
                    if (lastSent > NetTime.Now)
                    {
                        //sent in the future -> can't be right, remove
                        c.PositionUpdateLastSent.Remove(character);
                    }
                    else
                    {
                        if (lastSent > NetTime.Now - updateInterval) { continue; }
                    }
                    if (!c.PendingPositionUpdates.Contains(character)) { c.PendingPositionUpdates.Enqueue(character); }
                }

                foreach (Submarine sub in Submarine.Loaded)
                {
                    //if docked to a sub with a smaller ID, don't send an update
                    //  (= update is only sent for the docked sub that has the smallest ID, doesn't matter if it's the main sub or a shuttle)
                    if (sub.Info.IsOutpost || sub.DockedTo.Any(s => s.ID < sub.ID)) { continue; }
                    if (sub.PhysicsBody == null || sub.PhysicsBody.BodyType == FarseerPhysics.BodyType.Static) { continue; }
                    if (!c.PendingPositionUpdates.Contains(sub)) { c.PendingPositionUpdates.Enqueue(sub); }
                }

                foreach (Item item in Item.ItemList)
                {
                    if (item.PositionUpdateInterval == float.PositiveInfinity) { continue; }
                    float updateInterval = item.GetPositionUpdateInterval(c);
                    c.PositionUpdateLastSent.TryGetValue(item, out float lastSent);
                    if (lastSent > NetTime.Now)
                    {
                        //sent in the future -> can't be right, remove
                        c.PositionUpdateLastSent.Remove(item);
                    }
                    else
                    {
                        if (lastSent > NetTime.Now - updateInterval) { continue; }
                    }
                    if (!c.PendingPositionUpdates.Contains(item)) { c.PendingPositionUpdates.Enqueue(item); }
                }
            }

            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_INGAME);

            outmsg.Write((float)NetTime.Now);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);
            outmsg.Write(c.LastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server
            outmsg.Write(c.LastSentEntityEventID);

            if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign && campaign.Preset == GameMain.NetLobbyScreen.SelectedMode &&
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
                if (outmsg.LengthBytes + tempBuffer.LengthBytes > MsgConstants.MTU - 100)
                {
                    break;
                }

                outmsg.Write(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
                outmsg.WritePadBits();

                c.PositionUpdateLastSent[entity] = (float)NetTime.Now;
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
                outmsg.Write(client.NameID);
                outmsg.Write(client.Name);
                outmsg.Write(client.Character?.Info?.Job != null && gameStarted ? client.Character.Info.Job.Prefab.Identifier : (client.PreferredJob ?? ""));
                outmsg.Write(client.Character == null || !gameStarted ? (ushort)0 : client.Character.ID);
                if (c.HasPermission(ClientPermissions.ServerLog))
                {
                    outmsg.Write(client.Karma);
                }
                else
                {
                    outmsg.Write(100.0f);
                }
                outmsg.Write(client.Muted);
                outmsg.Write(client.InGame);
                outmsg.Write(client.Permissions != ClientPermissions.None);
                outmsg.Write(client.Connection == OwnerConnection);
                outmsg.Write(client.Connection != OwnerConnection && 
                    !client.HasPermission(ClientPermissions.Ban) &&
                    !client.HasPermission(ClientPermissions.Kick) &&
                    !client.HasPermission(ClientPermissions.Unban)); //is kicking the player allowed
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
            int initialUpdateBytes = 0;

            IWriteMessage settingsBuf = null;
            if (NetIdUtils.IdMoreRecent(GameMain.NetLobbyScreen.LastUpdateID, c.LastRecvLobbyUpdate))
            {
                outmsg.Write(true);
                outmsg.WritePadBits();

                outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);

                settingsBuf = new ReadWriteMessage();
                serverSettings.ServerWrite(settingsBuf, c);
                outmsg.Write((UInt16)settingsBuf.LengthBytes);
                outmsg.Write(settingsBuf.Buffer, 0, settingsBuf.LengthBytes);

                outmsg.Write(c.LastRecvLobbyUpdate < 1);
                if (c.LastRecvLobbyUpdate < 1)
                {
                    isInitialUpdate = true;
                    initialUpdateBytes = outmsg.LengthBytes;
                    ClientWriteInitial(c, outmsg);
                    initialUpdateBytes = outmsg.LengthBytes - initialUpdateBytes;
                }
                outmsg.Write(GameMain.NetLobbyScreen.SelectedSub.Name);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedSub.MD5Hash.ToString());
                outmsg.Write(serverSettings.UseRespawnShuttle);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
                outmsg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.ToString());

                List<int> campaignSubIndices = new List<int>();
                if (GameMain.NetLobbyScreen.SelectedMode == GameModePreset.MultiPlayerCampaign)
                {
                    List<SubmarineInfo> subList = GameMain.NetLobbyScreen.GetSubList();
                    for (int i = 0; i < subList.Count; i++)
                    {
                        if (GameMain.NetLobbyScreen.CampaignSubmarines.Contains(subList[i]))
                        {
                            campaignSubIndices.Add(i);
                        }
                    }
                }
                outmsg.Write((UInt16)campaignSubIndices.Count);
                foreach (int campaignSubIndex in campaignSubIndices)
                {
                    outmsg.Write((UInt16)campaignSubIndex);
                }

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
                    string errorMsg = "Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU + ")\n";
                    errorMsg +=
                        "  Client list size: " + clientListBytes + " bytes\n" +
                        "  Chat message size: " + chatMessageBytes + " bytes\n" +
                        "  Campaign size: " + campaignBytes + " bytes\n" +
                        "  Settings size: " + settingsBytes + " bytes\n";
                    if (initialUpdateBytes > 0)
                    {
                        errorMsg +=
                            "    Initial update size: " + settingsBuf.LengthBytes + " bytes\n";
                    }
                    if (settingsBuf != null)
                    {
                        errorMsg +=
                            "    Settings buffer size: " + settingsBuf.LengthBytes + " bytes\n";
                    }
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
            SubmarineInfo selectedSub = null;
            SubmarineInfo selectedShuttle = GameMain.NetLobbyScreen.SelectedShuttle;

            if (serverSettings.Voting.AllowSubVoting)
            {
                selectedSub = serverSettings.Voting.HighestVoted<SubmarineInfo>(VoteType.Sub, connectedClients);
                if (selectedSub == null) { selectedSub = GameMain.NetLobbyScreen.SelectedSub; }
            }
            else
            {
                selectedSub = GameMain.NetLobbyScreen.SelectedSub;
            }

            if (selectedSub == null || selectedShuttle == null)
            {
                return false;
            }

            GameModePreset selectedMode = serverSettings.Voting.HighestVoted<GameModePreset>(VoteType.Mode, connectedClients);
            if (selectedMode == null) { selectedMode = GameMain.NetLobbyScreen.SelectedMode; }

            if (selectedMode == null)
            {
                return false;
            }

            initiatedStartGame = true;
            startGameCoroutine = CoroutineManager.StartCoroutine(InitiateStartGame(selectedSub, selectedShuttle, selectedMode), "InitiateStartGame");

            return true;
        }

        private IEnumerable<object> InitiateStartGame(SubmarineInfo selectedSub, SubmarineInfo selectedShuttle, GameModePreset selectedMode)
        {
            initiatedStartGame = true;

            if (connectedClients.Any())
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ServerPacketHeader.QUERY_STARTGAME);

                msg.Write(selectedSub.Name);
                msg.Write(selectedSub.MD5Hash.Hash);

                msg.Write(serverSettings.UseRespawnShuttle);
                msg.Write(selectedShuttle.Name);
                msg.Write(selectedShuttle.MD5Hash.Hash);

                var campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
                msg.Write(campaign == null ? (byte)0 : campaign.CampaignID);
                msg.Write(campaign == null ? (UInt16)0 : campaign.LastSaveID);
                msg.Write(campaign == null ? (UInt16)0 : campaign.LastUpdateID);

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

            startGameCoroutine = GameMain.Instance.ShowLoading(StartGame(selectedSub, selectedShuttle, selectedMode), false);

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> StartGame(SubmarineInfo selectedSub, SubmarineInfo selectedShuttle, GameModePreset selectedMode)
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
                GameMain.GameSession = new GameSession(selectedSub, "", selectedMode, GameMain.NetLobbyScreen.LevelSeed, missionType: GameMain.NetLobbyScreen.MissionType);
            }

            List<Client> playingClients = new List<Client>(connectedClients);
            if (serverSettings.AllowSpectating)
            {
                playingClients.RemoveAll(c => c.SpectateOnly);
            }
            //always allow the server owner to spectate even if it's disallowed in server settings
            playingClients.RemoveAll(c => c.Connection == OwnerConnection && c.SpectateOnly);

            if (GameMain.GameSession.GameMode.Mission != null)
            {
                GameMain.GameSession.GameMode.Mission.AssignTeamIDs(playingClients);
                teamCount = GameMain.GameSession.GameMode.Mission.TeamCount;
            }
            else
            {
                connectedClients.ForEach(c => c.TeamID = Character.TeamType.Team1);
            }

            if (campaign != null)
            {
                if (campaign.Map == null)
                {
                    throw new Exception("Campaign map was null.");
                }
                if (campaign.NextLevel == null)
                {
                    string errorMsg = "Failed to start a campaign round (next level not set).";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameServer.StartGame:InvalidCampaignState", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    if (OwnerConnection != null)
                    {
                        SendDirectChatMessage(errorMsg, connectedClients.Find(c => c.Connection == OwnerConnection), ChatMessageType.Error);
                    }
                    yield return CoroutineStatus.Failure;
                }

                SendStartMessage(roundStartSeed, campaign.NextLevel.Seed, GameMain.GameSession, connectedClients, includesFinalize: false);
                GameMain.GameSession.StartRound(campaign.NextLevel, mirrorLevel: campaign.MirrorLevel);
                SubmarineSwitchLoad = false;
                campaign.AssignClientCharacterInfos(connectedClients);
                Log("Game mode: " + selectedMode.Name, ServerLog.MessageType.ServerMessage);
                Log("Submarine: " + GameMain.GameSession.SubmarineInfo.Name, ServerLog.MessageType.ServerMessage);
                Log("Level seed: " + campaign.NextLevel.Seed, ServerLog.MessageType.ServerMessage);
                if (GameMain.GameSession.Mission != null) { Log("Mission: " + GameMain.GameSession.Mission.Prefab.Name, ServerLog.MessageType.ServerMessage); }
            }
            else
            {
                SendStartMessage(roundStartSeed, GameMain.NetLobbyScreen.LevelSeed, GameMain.GameSession, connectedClients, false);

                GameMain.GameSession.StartRound(GameMain.NetLobbyScreen.LevelSeed, serverSettings.SelectedLevelDifficulty);
                Log("Game mode: " + selectedMode.Name, ServerLog.MessageType.ServerMessage);
                Log("Submarine: " + selectedSub.Name, ServerLog.MessageType.ServerMessage);
                Log("Level seed: " + GameMain.NetLobbyScreen.LevelSeed, ServerLog.MessageType.ServerMessage);
                if (GameMain.GameSession.Mission != null) { Log("Mission: " + GameMain.GameSession.Mission.Prefab.Name, ServerLog.MessageType.ServerMessage); }
            }

            if (GameMain.GameSession.SubmarineInfo.IsFileCorrupted)
            {
                CoroutineManager.StopCoroutines(startGameCoroutine);
                initiatedStartGame = false;
                SendChatMessage(TextManager.FormatServerMessage($"SubLoadError~[subname]={GameMain.GameSession.SubmarineInfo.Name}"), ChatMessageType.Error);
                yield return CoroutineStatus.Failure;
            }

            MissionMode missionMode = GameMain.GameSession.GameMode as MissionMode;
            bool missionAllowRespawn = GameMain.GameSession.Campaign == null && (missionMode?.Mission == null || missionMode.Mission.AllowRespawn);
            bool outpostAllowRespawn = GameMain.GameSession.Campaign != null && Level.Loaded?.Type == LevelData.LevelType.Outpost;

            if (serverSettings.AllowRespawn && (missionAllowRespawn || outpostAllowRespawn))
            { 
                respawnManager = new RespawnManager(this, serverSettings.UseRespawnShuttle && !outpostAllowRespawn ? selectedShuttle : null); 
            }

            Level.Loaded?.SpawnNPCs();
            Level.Loaded?.SpawnCorpses();
            Level.Loaded?.PrepareBeaconStation();
            AutoItemPlacer.PlaceIfNeeded();

            CrewManager crewManager = campaign?.CrewManager;

            entityEventManager.RefreshEntityIDs();

            bool hadBots = true;

            //assign jobs and spawnpoints separately for each team
            for (int n = 0; n < teamCount; n++)
            {
                var teamID = n == 0 ? Character.TeamType.Team1 : Character.TeamType.Team2;

                Submarine.MainSubs[n].TeamID = teamID;
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == null) { continue; }
                    if (item.Submarine != Submarine.MainSubs[n] && !Submarine.MainSubs[n].DockedTo.Contains(item.Submarine)) { continue; }
                    foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                    {
                        wifiComponent.TeamID = Submarine.MainSubs[n].TeamID;
                    }
                }
                foreach (Submarine sub in Submarine.MainSubs[n].DockedTo)
                {
                    if (sub.Info.Type != SubmarineType.Player) { continue; }
                    sub.TeamID = teamID;
                }

                //find the clients in this team
                List<Client> teamClients = teamCount == 1 ? new List<Client>(playingClients) : playingClients.FindAll(c => c.TeamID == teamID);
                if (serverSettings.AllowSpectating)
                {
                    teamClients.RemoveAll(c => c.SpectateOnly);
                }
                //always allow the server owner to spectate even if it's disallowed in server settings
                teamClients.RemoveAll(c => c.Connection == OwnerConnection && c.SpectateOnly);

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
                        client.CharacterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, client.Name);
                    }
                    characterInfos.Add(client.CharacterInfo);
                    if (client.CharacterInfo.Job == null || client.CharacterInfo.Job.Prefab != client.AssignedJob.First)
                    {
                        client.CharacterInfo.Job = new Job(client.AssignedJob.First, client.AssignedJob.Second);
                    }
                }

                List<CharacterInfo> bots = new List<CharacterInfo>();

                // do not load new bots if we already have them
                if (crewManager == null || !crewManager.HasBots)
                {
                    int botsToSpawn = serverSettings.BotSpawnMode == BotSpawnMode.Fill ? serverSettings.BotCount - characterInfos.Count : serverSettings.BotCount;
                    for (int i = 0; i < botsToSpawn; i++)
                    {
                        var botInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName)
                        {
                            TeamID = teamID
                        };
                        characterInfos.Add(botInfo);
                        bots.Add(botInfo);
                    }

                    AssignBotJobs(bots, teamID);
                    if (campaign != null) 
                    {
                        foreach (CharacterInfo bot in bots)
                        {
                            crewManager?.AddCharacterInfo(bot);
                        }
                    }

                    if (crewManager != null)
                    {
                        crewManager.HasBots = true;
                        hadBots = false;
                    }
                }

                List<WayPoint> spawnWaypoints = null;
                List<WayPoint> mainSubWaypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSubs[n]).ToList();
                if (Level.Loaded?.StartOutpost != null && Level.Loaded.Type == LevelData.LevelType.Outpost && 
                    Level.Loaded.StartOutpost.GetConnectedSubs().Any(s => s.Info.Type == SubmarineType.Player))
                {
                    spawnWaypoints = WayPoint.WayPointList.FindAll(wp =>
                        wp.SpawnType == SpawnType.Human &&
                        wp.Submarine == Level.Loaded.StartOutpost &&
                        wp.CurrentHull?.OutpostModuleTags != null &&
                        wp.CurrentHull.OutpostModuleTags.Contains("airlock"));
                    while (spawnWaypoints.Count > characterInfos.Count)
                    {
                        spawnWaypoints.RemoveAt(Rand.Int(spawnWaypoints.Count));
                    }
                    while (spawnWaypoints.Any() && spawnWaypoints.Count < characterInfos.Count)
                    {
                        spawnWaypoints.Add(spawnWaypoints[Rand.Int(spawnWaypoints.Count)]);
                    }
                }
                if (spawnWaypoints == null || !spawnWaypoints.Any())
                {
                    spawnWaypoints = mainSubWaypoints;
                }
                Debug.Assert(spawnWaypoints.Count == mainSubWaypoints.Count);

                for (int i = 0; i < teamClients.Count; i++)
                {
                    Character spawnedCharacter = Character.Create(teamClients[i].CharacterInfo, spawnWaypoints[i].WorldPosition, teamClients[i].CharacterInfo.Name, isRemotePlayer: true, hasAi: false);
                    spawnedCharacter.AnimController.Frozen = true;
                    spawnedCharacter.TeamID = teamID;
                    teamClients[i].Character = spawnedCharacter;
                    var characterData = campaign?.GetClientCharacterData(teamClients[i]);
                    if (characterData == null)
                    {
                        spawnedCharacter.GiveJobItems(mainSubWaypoints[i]);
                        if (campaign != null)
                        {
                            characterData = campaign.SetClientCharacterData(teamClients[i]);
                            characterData.HasSpawned = true;
                        }
                    }
                    else
                    {
                        characterData.SpawnInventoryItems(spawnedCharacter, spawnedCharacter.Inventory);
                        characterData.ApplyHealthData(spawnedCharacter.Info, spawnedCharacter);
                        spawnedCharacter.GiveIdCardTags(mainSubWaypoints[i]);
                        characterData.HasSpawned = true;
                    }
                    spawnedCharacter.OwnerClientEndPoint = teamClients[i].Connection.EndPointString;
                    spawnedCharacter.OwnerClientName = teamClients[i].Name;
                }

                for (int i = teamClients.Count; i < teamClients.Count + bots.Count; i++)
                {
                    Character spawnedCharacter = Character.Create(characterInfos[i], spawnWaypoints[i].WorldPosition, characterInfos[i].Name, isRemotePlayer: false, hasAi: true);
                    spawnedCharacter.TeamID = teamID;
                    spawnedCharacter.GiveJobItems(mainSubWaypoints[i]);
                    spawnedCharacter.GiveIdCardTags(mainSubWaypoints[i]);
                }
            }

            if (crewManager != null && crewManager.HasBots)
            {
                if (hadBots)
                {
                    //loaded existing bots -> init them
                    crewManager?.InitRound();
                }
                else
                {
                    //created new bots -> save them
                    SaveUtil.SaveGame(GameMain.GameSession.SavePath);
                }
            }

            campaign?.LoadPets();

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
                    TraitorManager = new TraitorManager();
                    TraitorManager.Start(this);
                }
            }

            GameAnalyticsManager.AddDesignEvent("Traitors:" + (TraitorManager == null ? "Disabled" : "Enabled"));

            yield return CoroutineStatus.Running;

            if (GameMain.Server?.ServerSettings?.Voting != null)
            {
                GameMain.Server.ServerSettings.Voting.ResetVotes(GameMain.Server.ConnectedClients);
            }

            GameMain.GameScreen.Select();

            Log("Round started.", ServerLog.MessageType.ServerMessage);

            gameStarted = true;
            initiatedStartGame = false;
            GameMain.ResetFrameTime();

            LastClientListUpdateID++;

            roundStartTime = DateTime.Now;

            yield return CoroutineStatus.Success;
        }

        private void SendStartMessage(int seed, string levelSeed, GameSession gameSession, List<Client> clients, bool includesFinalize)
        {
            foreach (Client client in clients)
            {
                SendStartMessage(seed, levelSeed, gameSession, client, includesFinalize);
            }
        }

        private void SendStartMessage(int seed, string levelSeed, GameSession gameSession, Client client, bool includesFinalize)
        {
            MultiPlayerCampaign campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            MissionMode missionMode = GameMain.GameSession.GameMode as MissionMode;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.STARTGAME);
            msg.Write(seed);
            msg.Write(gameSession.GameMode.Preset.Identifier);

            bool missionAllowRespawn = campaign == null && (missionMode?.Mission == null || missionMode.Mission.AllowRespawn);
            bool outpostAllowRespawn = campaign != null && campaign.NextLevel?.Type == LevelData.LevelType.Outpost;
            msg.Write(serverSettings.AllowRespawn && (missionAllowRespawn || outpostAllowRespawn));
            msg.Write(serverSettings.AllowDisguises);
            msg.Write(serverSettings.AllowRewiring);
            msg.Write(serverSettings.AllowRagdollButton);
            msg.Write(serverSettings.UseRespawnShuttle);
            msg.Write((byte)GameMain.Config.LosMode);
            msg.Write(includesFinalize); msg.WritePadBits();

            serverSettings.WriteMonsterEnabled(msg);

            if (campaign == null)
            {
                msg.Write(levelSeed);
                msg.Write(serverSettings.SelectedLevelDifficulty);
                msg.Write(gameSession.SubmarineInfo.Name);
                msg.Write(gameSession.SubmarineInfo.MD5Hash.Hash);
                msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
                msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.Hash);
                msg.Write((short)(GameMain.GameSession.GameMode?.Mission == null ? -1 : MissionPrefab.List.IndexOf(GameMain.GameSession.GameMode.Mission.Prefab)));
            }
            else
            {
                int nextLocationIndex = campaign.Map.Locations.FindIndex(l => l.LevelData == campaign.NextLevel);
                int nextConnectionIndex = campaign.Map.Connections.FindIndex(c => c.LevelData == campaign.NextLevel);
                msg.Write(campaign.CampaignID);
                msg.Write(nextLocationIndex);
                msg.Write(nextConnectionIndex);
                msg.Write(campaign.Map.SelectedLocationIndex);
                msg.Write(campaign.MirrorLevel);
            }

            if (includesFinalize)
            {
                WriteRoundStartFinalize(msg, client);
            }

            serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
        }

        private void SendRoundStartFinalize(Client client)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ServerPacketHeader.STARTGAMEFINALIZE);
            WriteRoundStartFinalize(msg, client);
            serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
        }

        private void WriteRoundStartFinalize(IWriteMessage msg, Client client)
        {
            //tell the client what content files they should preload
            var contentToPreload = GameMain.GameSession.EventManager.GetFilesToPreload();
            msg.Write((ushort)contentToPreload.Count());
            foreach (ContentFile contentFile in contentToPreload)
            {
                msg.Write((byte)contentFile.Type);
                msg.Write(contentFile.Path);
            }
            msg.Write(Submarine.MainSub?.Info.EqualityCheckVal ?? 0);
            msg.Write(GameMain.GameSession.Mission?.Prefab.Identifier ?? "");
            msg.Write((byte)GameMain.GameSession.Level.EqualityCheckValues.Count);
            foreach (int equalityCheckValue in GameMain.GameSession.Level.EqualityCheckValues)
            {
                msg.Write(equalityCheckValue);
            }
            GameMain.GameSession.Mission?.ServerWriteInitial(msg, client);
        }

        public void EndGame(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            if (!gameStarted)
            {
                return;
            }

            if (GameSettings.VerboseLogging)
            {
                Log("Ending the round...\n" + Environment.StackTrace.CleanupStackTrace(), ServerLog.MessageType.ServerMessage);

            }
            else
            {
                Log("Ending the round...", ServerLog.MessageType.ServerMessage);
            }

            string endMessage = TextManager.FormatServerMessage("RoundSummaryRoundHasEnded");
            var traitorResults = TraitorManager?.GetEndResults() ?? new List<TraitorMissionResult>();

            Mission mission = GameMain.GameSession.Mission;
            if (GameMain.GameSession.IsRunning)
            {
                GameMain.GameSession.EndRound(endMessage, traitorResults);
            }

            endRoundTimer = 0.0f;

            if (serverSettings.AutoRestart)
            {
                serverSettings.AutoRestartTimer = serverSettings.AutoRestartInterval;
                //send a netlobby update to get the clients' autorestart timers up to date
                GameMain.NetLobbyScreen.LastUpdateID++;
            }

            if (serverSettings.SaveServerLogs) { serverSettings.ServerLog.Save(); }

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
                msg.Write((byte)transitionType);
                msg.Write(endMessage);
                msg.Write(mission != null && mission.Completed);
                msg.Write(GameMain.GameSession?.WinningTeam == null ? (byte)0 : (byte)GameMain.GameSession.WinningTeam);

                msg.Write((byte)traitorResults.Count);
                foreach (var traitorResult in traitorResults)
                {
                    traitorResult.ServerWrite(msg);
                }

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
            string logMsg;
            if (message.SenderClient != null)
            {
                logMsg = GameServer.ClientLogName(message.SenderClient) + ": " + message.TranslatedText;
            }
            else
            {
                logMsg = message.TextWithSender;
            }
            Log(logMsg, ServerLog.MessageType.Chat);

            base.AddChatMessage(message);
        }

        private bool TryChangeClientName(Client c, IReadMessage inc)
        {
            UInt16 nameId = inc.ReadUInt16();
            string newName = inc.ReadString();
            string newJob = inc.ReadString();

            if (c == null || string.IsNullOrEmpty(newName) || !NetIdUtils.IdMoreRecent(nameId, c.NameID)) { return false; }

            c.NameID = nameId;
            newName = Client.SanitizeName(newName);
            if (newName == c.Name && newJob == c.PreferredJob) { return false; }
            c.PreferredJob = newJob;

            //update client list even if the name cannot be changed to the one sent by the client,
            //so the client will be informed what their actual name is
            LastClientListUpdateID++;

            if (newName == c.Name) { return false; }

            if (c.Connection != OwnerConnection)
            {
                if (!Client.IsValidName(newName, serverSettings))
                {
                    SendDirectChatMessage($"ServerMessage.NameChangeFailedSymbols~[newname]={newName}", c, ChatMessageType.ServerMessageBox);
                    return false;
                }
                if (Homoglyphs.Compare(newName.ToLower(), ServerName.ToLower()))
                {
                    SendDirectChatMessage($"ServerMessage.NameChangeFailedServerTooSimilar~[newname]={newName}", c, ChatMessageType.ServerMessageBox);
                    return false;
                }

                if (c.KickVoteCount > 0)
                {
                    SendDirectChatMessage($"ServerMessage.NameChangeFailedVoteKick~[newname]={newName}", c, ChatMessageType.ServerMessageBox);
                    return false;
                }
            }

            Client nameTaken = ConnectedClients.Find(c2 => c != c2 && Homoglyphs.Compare(c2.Name.ToLower(), newName.ToLower()));
            if (nameTaken != null)
            {
                SendDirectChatMessage($"ServerMessage.NameChangeFailedClientTooSimilar~[newname]={newName}~[takenname]={nameTaken.Name}", c, ChatMessageType.ServerMessageBox);
                return false;
            }

            SendChatMessage($"ServerMessage.NameChangeSuccessful~[oldname]={c.Name}~[newname]={newName}", ChatMessageType.Server);
            c.Name = newName;
            c.Connection.Name = newName;
            return true;
        }

        public override void KickPlayer(string playerName, string reason)
        {
            Client client = connectedClients.Find(c =>
                c.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase) ||
                (c.Character != null && c.Character.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)));

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
            DisconnectClient(client, logMsg, msg, reason, PlayerConnectionChangeType.Kicked);
        }

        public override void BanPlayer(string playerName, string reason, bool range = false, TimeSpan? duration = null)
        {
            Client client = connectedClients.Find(c =>
                c.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase) ||
                (c.Character != null && c.Character.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)));

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
            DisconnectClient(client, $"ServerMessage.BannedFromServer~[client]={client.Name}", targetMsg, reason, PlayerConnectionChangeType.Banned);

            if (client.Connection is LidgrenConnection lidgrenConn && (client.SteamID == 0 || range))
            {
                string ip = "";
                ip = lidgrenConn.IPEndPoint.Address.IsIPv4MappedToIPv6 ?
                lidgrenConn.IPEndPoint.Address.MapToIPv4NoThrow().ToString() :
                lidgrenConn.IPEndPoint.Address.ToString();
                if (range) { ip = BanList.ToRange(ip); }

                serverSettings.BanList.BanPlayer(client.Name, ip, reason, duration);
            }
            if (client.SteamID > 0)
            {
                serverSettings.BanList.BanPlayer(client.Name, client.SteamID, reason, duration);
            }
        }

        public override void UnbanPlayer(string playerName, string playerEndPoint)
        {
            if (!string.IsNullOrEmpty(playerEndPoint))
            {
                serverSettings.BanList.UnbanEndPoint(playerEndPoint);
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

            DisconnectClient(client, msg, targetmsg, string.Empty, PlayerConnectionChangeType.Disconnected);
        }

        public void DisconnectClient(Client client, string msg = "", string targetmsg = "", string reason = "", PlayerConnectionChangeType changeType = PlayerConnectionChangeType.Disconnected)
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

            SendChatMessage(msg, ChatMessageType.Server, changeType: changeType);

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
                string errorMsg = "Attempted to send a chat message to a null client.\n" + Environment.StackTrace.CleanupStackTrace();
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
        public void SendChatMessage(string message, ChatMessageType? type = null, Client senderClient = null, Character senderCharacter = null, PlayerConnectionChangeType changeType = PlayerConnectionChangeType.None)
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
                            if (command.ToLower() == serverName.ToLower())
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
                        senderName = serverName;
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

                    if (type == ChatMessageType.Private)
                    {
                        if (senderCharacter != null && !senderCharacter.IsDead || targetClient.Character != null && !targetClient.Character.IsDead)
                        {
                            //sender or target has an alive character, sending private messages not allowed
                            SendDirectChatMessage(ChatMessage.Create("", $"ServerMessage.PrivateMessagesNotAllowed", ChatMessageType.Error, null), senderClient);
                            return;
                        }
                    }
                    //sender doesn't have a character or the character can't speak -> only ChatMessageType.Dead allowed
                    else if (senderCharacter == null || senderCharacter.IsDead || senderCharacter.SpeechImpediment >= 100.0f)
                    {
                        type = ChatMessageType.Dead;
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
                        senderName = serverName;
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

            if (type == ChatMessageType.Server || type == ChatMessageType.Error)
            {
                senderName = null;
                senderCharacter = null;
            }
            else if (type == ChatMessageType.Radio)
            {
                //send to chat-linked wifi components
                Signal s = new Signal(0, message, null, senderCharacter, senderRadio?.Item);
                senderRadio?.TransmitSignal(s, true);
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
                    senderCharacter,
                    senderClient, 
                    changeType);

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
            if (message.Sender == null || message.Sender.SpeechImpediment >= 100.0f) { return; }
            //ChatMessageType messageType = ChatMessage.CanUseRadio(message.Sender) ? ChatMessageType.Radio : ChatMessageType.Default;

            //check which clients can receive the message and apply distance effects
            foreach (Client client in ConnectedClients)
            {
                string modifiedMessage = message.Text;

                if (message.Sender != null &&
                    client.Character != null && !client.Character.IsDead)
                {
                    //too far to hear the msg -> don't send
                    if (!client.Character.CanHearCharacter(message.Sender)) { continue; }
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

            if (serverSettings.Voting.VoteRunning)
            {
                // Required ratio cannot be met
                if (SubmarineVoteNoCount / (float)SubmarineVoteMax > 1f - serverSettings.SubmarineVoteRequiredRatio)
                {
                    serverSettings.Voting.StopSubmarineVote(false);
                    return; // Update will be re-sent via StopSubmarineVote
                }
                else if (SubmarineVoteYesCount / (float)SubmarineVoteMax >= serverSettings.SubmarineVoteRequiredRatio)
                {
                    SwitchSubmarine();
                    return; // Update will be re-sent via StopSubmarineVote
                }
            }

            Client.UpdateKickVotes(connectedClients);

            int minimumKickVotes = Math.Max(1, (int)(connectedClients.Count * serverSettings.KickVoteRequiredRatio));
            var clientsToKick = connectedClients.FindAll(c =>
                c.Connection != OwnerConnection &&
                !c.HasPermission(ClientPermissions.Kick) &&
                !c.HasPermission(ClientPermissions.Ban) &&
                !c.HasPermission(ClientPermissions.Unban) &&
                c.KickVoteCount >= minimumKickVotes);
            foreach (Client c in clientsToKick)
            {
                var previousPlayer = previousPlayers.Find(p => p.MatchesClient(c));
                if (previousPlayer != null)
                {
                    //reset the client's kick votes (they can rejoin after their ban expires)
                    previousPlayer.KickVoters.Clear();
                }

                SendChatMessage($"ServerMessage.KickedFromServer~[client]={c.Name}", ChatMessageType.Server, null, changeType: PlayerConnectionChangeType.Kicked);
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

        private void SwitchSubmarine()
        {
            SubmarineInfo targetSubmarine = Voting.SubVote.Sub;
            VoteType voteType = Voting.SubVote.VoteType;
            int deliveryFee = 0;

            switch (voteType)
            {
                case VoteType.PurchaseAndSwitchSub:
                case VoteType.PurchaseSub:
                    // Pay for submarine
                    GameMain.GameSession.PurchaseSubmarine(targetSubmarine);
                    break;
                case VoteType.SwitchSub:
                    deliveryFee = Voting.SubVote.DeliveryFee;
                    break;
                default:
                    return;
            }

            if (voteType != VoteType.PurchaseSub)
            {
                GameMain.GameSession.SwitchSubmarine(targetSubmarine, deliveryFee);
                GameMain.GameSession.Campaign.UpgradeManager.RefundResetAndReload(targetSubmarine, true);
            }

            serverSettings.Voting.StopSubmarineVote(true);
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

            foreach (Client recipient in connectedClients)
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
                
                if (newCharacter.Info != null && newCharacter.Info.Character == null)
                {
                    newCharacter.Info.Character = newCharacter;
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
                if (JobPrefab.Prefabs.ContainsKey(jobIdentifier))
                {
                    jobPreferences.Add(new Pair<JobPrefab, int>(JobPrefab.Prefabs[jobIdentifier], variant));
                }
            }

            sender.CharacterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, sender.Name);
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
            var jobList = JobPrefab.Prefabs.ToList();
            unassigned = new List<Client>(unassigned);
            unassigned = unassigned.OrderBy(sp => Rand.Int(int.MaxValue)).ToList();

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
                if (c.TeamID != teamID || unassigned.Contains(c)) { continue; }
                if (c.Character?.Info?.Job != null && !c.Character.IsDead)
                {
                    assignedClientCount[c.Character.Info.Job.Prefab]++;
                }
            }

            //if any of the players has chosen a job that is Always Allowed, give them that job
            for (int i = unassigned.Count - 1; i >= 0; i--)
            {
                if (unassigned[i].JobPreferences.Count == 0) { continue; }
                if (!unassigned[i].JobPreferences[0].First.AllowAlways) { continue; }
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
                    if (unassigned.Count == 0) { break; }
                    if (jobPrefab.MinNumber < 1 || assignedClientCount[jobPrefab] >= jobPrefab.MinNumber) { continue; }

                    //find the client that wants the job the most, or force it to random client if none of them want it
                    Client assignedClient = FindClientWithJobPreference(unassigned, jobPrefab, true);

                    assignedClient.AssignedJob = 
                        assignedClient.JobPreferences.FirstOrDefault(jp => jp.First == jobPrefab) ??
                        new Pair<JobPrefab, int>(jobPrefab, 0);

                    assignedClientCount[jobPrefab]++;
                    unassigned.Remove(assignedClient);

                    //the job still needs more crew members, set unassignedJobsFound to true to keep the while loop running
                    if (assignedClientCount[jobPrefab] < jobPrefab.MinNumber) { unassignedJobsFound = true; }
                }
            }

            List<WayPoint> availableSpawnPoints = WayPoint.WayPointList.FindAll(wp =>
                wp.SpawnType == SpawnType.Human &&
                wp.Submarine != null && wp.Submarine.TeamID == teamID);
            List<WayPoint> unassignedSpawnPoints = new List<WayPoint>(availableSpawnPoints);

            /*bool canAssign = false;
            do
            {
                canAssign = false;
                foreach (WayPoint spawnPoint in unassignedSpawnPoints)
                {
                    if (unassigned.Count == 0) { break; }

                    JobPrefab job = spawnPoint.AssignedJob ?? JobPrefab.List.Values.GetRandom();
                    if (assignedClientCount[job] >= job.MaxNumber) { continue; }

                    Client assignedClient = FindClientWithJobPreference(unassigned, job, true);
                    if (assignedClient != null)
                    {
                        assignedClient.AssignedJob = job;
                        assignedClientCount[job]++;
                        unassigned.Remove(assignedClient);
                        canAssign = true;
                    }
                }
            } while (unassigned.Count > 0 && canAssign);*/

            //attempt to give the clients a job they have in their job preferences
            for (int i = unassigned.Count - 1; i >= 0; i--)
            {
                if (unassignedSpawnPoints.Count == 0) { break; }
                foreach (Pair<JobPrefab, int> preferredJob in unassigned[i].JobPreferences)
                {
                    //can't assign this job if maximum number has reached or the clien't karma is too low
                    if (assignedClientCount[preferredJob.First] >= preferredJob.First.MaxNumber || unassigned[i].Karma < preferredJob.First.MinKarma)
                    {
                        continue;
                    }
                    //give the client their preferred job if there's a spawnpoint available for that job
                    var matchingSpawnPoint = unassignedSpawnPoints.Find(s => s.AssignedJob == preferredJob.First);
                    //if the job is not available in any spawnpoint (custom job?), treat empty spawnpoints
                    //as a matching ones
                    if (matchingSpawnPoint == null && !availableSpawnPoints.Any(s => s.AssignedJob == preferredJob.First))
                    {
                        matchingSpawnPoint = unassignedSpawnPoints.Find(s => s.AssignedJob == null);
                    }
                    if (matchingSpawnPoint != null)
                    {
                        unassignedSpawnPoints.Remove(matchingSpawnPoint);
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
                        if (jobIndex >= jobList.Count) { jobIndex -= jobList.Count; }
                        if (skips >= jobList.Count) { break; }
                    }
                    c.AssignedJob =
                        c.JobPreferences.FirstOrDefault(jp => jp.First == jobList[jobIndex]) ??
                        new Pair<JobPrefab, int>(jobList[jobIndex], 0);
                    assignedClientCount[c.AssignedJob.First]++;
                }
                //if one of the client's preferences is still available, give them that job
                else if (c.JobPreferences.Any(jp => remainingJobs.Contains(jp.First)))
                {
                    foreach (Pair<JobPrefab, int> preferredJob in c.JobPreferences)
                    {
                        c.AssignedJob = preferredJob;
                        assignedClientCount[preferredJob.First]++;
                        break;                        
                    }
                }
                else //none of the client's preferred jobs available, choose a random job
                {
                    c.AssignedJob = new Pair<JobPrefab, int>(remainingJobs[Rand.Range(0, remainingJobs.Count)], 0);
                    assignedClientCount[c.AssignedJob.First]++;
                }
            }
        }

        public void AssignBotJobs(List<CharacterInfo> bots, Character.TeamType teamID)
        {
            Dictionary<JobPrefab, int> assignedPlayerCount = new Dictionary<JobPrefab, int>();
            foreach (JobPrefab jp in JobPrefab.Prefabs)
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

            List<WayPoint> spawnPoints = WayPoint.WayPointList.FindAll(wp =>
                wp.SpawnType == SpawnType.Human &&
                wp.Submarine != null && wp.Submarine.TeamID == teamID)
                    .OrderBy(sp => Rand.Int(int.MaxValue))
                    .OrderBy(sp => sp.AssignedJob == null ? 0 : 1)
                        .ToList();

            bool canAssign = false;
            do
            {
                canAssign = false;
                foreach (WayPoint spawnPoint in spawnPoints)
                {
                    if (unassignedBots.Count == 0) { break; }

                    JobPrefab jobPrefab = spawnPoint.AssignedJob ?? JobPrefab.Prefabs.GetRandom();
                    if (assignedPlayerCount[jobPrefab] >= jobPrefab.MaxNumber) { continue; }

                    var variant = Rand.Range(0, jobPrefab.Variants, Rand.RandSync.Server);
                    unassignedBots[0].Job = new Job(jobPrefab, variant);
                    assignedPlayerCount[jobPrefab]++;
                    unassignedBots.Remove(unassignedBots[0]);
                    canAssign = true;                    
                }
            } while (unassignedBots.Count > 0 && canAssign);
            
            //find a suitable job for the rest of the bots
            foreach (CharacterInfo c in unassignedBots)
            {
                //find all jobs that are still available
                var remainingJobs = JobPrefab.Prefabs.Where(jp => assignedPlayerCount[jp] < jp.MaxNumber);
                //all jobs taken, give a random job
                if (remainingJobs.Count() == 0)
                {
                    DebugConsole.ThrowError("Failed to assign a suitable job for bot \"" + c.Name + "\" (all jobs already have the maximum numbers of players). Assigning a random job...");
                    c.Job = Job.Random();
                    assignedPlayerCount[c.Job.Prefab]++;
                }
                else //some jobs still left, choose one of them by random
                {
                    var job = remainingJobs.GetRandom();
                    var variant = Rand.Range(0, job.Variants);
                    c.Job = new Job(job, variant);
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

        public void UpdateMissionState(int state)
        {
            foreach (var client in connectedClients)
            {
                IWriteMessage msg = new WriteOnlyMessage();
                msg.Write((byte)ServerPacketHeader.MISSION);
                msg.Write((ushort)state);
                serverPeer.Send(msg, client.Connection, DeliveryMethod.Reliable);
            }
        }

        public static string ClientLogName(Client client, string name = null)
        {
            if (client == null) { return name; }
            string retVal = "‖";
            if (client.Karma < 40.0f)
            {
                retVal += "color:#ff9900;";
            }
            retVal += "metadata:" + (client.SteamID!=0 ? client.SteamID.ToString() : client.ID.ToString()) + "‖" + (name ?? client.Name) + "‖end‖";
            return retVal;
        }

        public static string CharacterLogName(Character character)
        {
            if (character == null) { return "[NULL]"; }
            Client client = GameMain.Server.ConnectedClients.Find(c => c.Character == character);
            return ClientLogName(client, character.LogName);
        }

        public static void Log(string line, ServerLog.MessageType messageType)
        {
            if (GameMain.Server == null || !GameMain.Server.ServerSettings.SaveServerLogs) { return; }

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

                if (GameMain.NetLobbyScreen.SelectedSub != null) { serverSettings.SelectedSubmarine = GameMain.NetLobbyScreen.SelectedSub.Name; }
                if (GameMain.NetLobbyScreen.SelectedShuttle != null) { serverSettings.SelectedShuttle = GameMain.NetLobbyScreen.SelectedShuttle.Name; }
                if (GameMain.NetLobbyScreen.CampaignSubmarines != null)
                {
                    string submarinesString = string.Empty;
                    for (int i = 0; i < GameMain.NetLobbyScreen.CampaignSubmarines.Count; i++)
                    {
                        submarinesString += GameMain.NetLobbyScreen.CampaignSubmarines[i].Name + ServerSettings.SubmarineSeparatorChar;                       
                    }
                    submarinesString.Trim(ServerSettings.SubmarineSeparatorChar);
                    serverSettings.CampaignSubmarines = submarinesString;
                }

                serverSettings.SaveSettings();

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
