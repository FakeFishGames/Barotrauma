
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using RestSharp;
using Barotrauma.Items.Components;

namespace Barotrauma.Networking
{
    partial class GameServer : NetworkMember
    {
        private List<Client> connectedClients = new List<Client>();

        //for keeping track of disconnected clients in case the reconnect shortly after
        private List<Client> disconnectedClients = new List<Client>();

        private NetStats netStats;

        private int roundStartSeed;
        
        //is the server running
        private bool started;

        private NetServer server;
        private NetPeerConfiguration config;

        private int MaxPlayers;

        private DateTime sparseUpdateTimer;        
        private DateTime refreshMasterTimer;

        private RestClient restClient;
        private bool masterServerResponded;

        private ServerLog log;
        private GUIButton showLogButton;

        private GUIScrollBar clientListScrollBar;

        public TraitorManager TraitorManager;

        public override List<Client> ConnectedClients
        {
            get
            {
                return connectedClients;
            }
        }

        public GameServer(string name, int port, bool isPublic = false, string password = "", bool attemptUPnP = false, int maxPlayers = 10)
        {
            name = name.Replace(":", "");
            name = name.Replace(";", "");

            this.name = name;
            this.password = "";
            if (password.Length>0)
            {
                this.password = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password)));
            }
            
            config = new NetPeerConfiguration("barotrauma");

            netStats = new NetStats();

#if DEBUG
            config.SimulatedLoss = 0.05f;
            config.SimulatedRandomLatency = 0.2f;
            config.SimulatedDuplicatesChance = 0.05f;
            config.SimulatedMinimumLatency = 0.1f;
#endif 
            config.Port = port;
            Port = port;

            if (attemptUPnP)
            {
                config.EnableUPnP = true;
            }

            config.MaximumConnections = maxPlayers*2; //double the lidgren connections for unauthenticated players
            MaxPlayers = maxPlayers;

            config.DisableMessageType(NetIncomingMessageType.DebugMessage | 
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);
                                    
            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            //----------------------------------------

            var endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170, 20, 150, 20), "End round", Alignment.TopLeft, GUI.Style, inGameHUD);
            endRoundButton.OnClicked = (btn, userdata) => { EndGame(); return true; };

            log = new ServerLog(name);
            showLogButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170, 20, 150, 20), "Server Log", Alignment.TopLeft, GUI.Style, inGameHUD);
            showLogButton.OnClicked = (GUIButton button, object userData) =>
            {
                if (log.LogFrame == null)
                {
                    log.CreateLogFrame();
                }
                else
                {
                    log.LogFrame = null;
                    GUIComponent.KeyboardDispatcher.Subscriber = null;
                }
                return true;
            };

            GUIButton settingsButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170 - 170, 20, 150, 20), "Settings", Alignment.TopLeft, GUI.Style, inGameHUD);
            settingsButton.OnClicked = ToggleSettingsFrame;
            settingsButton.UserData = "settingsButton";

            whitelist = new WhiteList();
            banList = new BanList();

            LoadSettings();
            LoadClientPermissions();
            
            //----------------------------------------
            
            CoroutineManager.StartCoroutine(StartServer(isPublic));
        }

        private IEnumerable<object> StartServer(bool isPublic)
        {
            try
            {
                Log("Starting the server...", Color.Cyan);
                server = new NetServer(config);
                netPeer = server;
                server.Start();
            }
            catch (Exception e)
            {
                Log("Error while starting the server ("+e.Message+")", Color.Red);
                DebugConsole.ThrowError("Couldn't start the server", e);
            }
         

            if (config.EnableUPnP)
            {
                server.UPnP.ForwardPort(config.Port, "barotrauma");

                GUIMessageBox upnpBox = new GUIMessageBox("Please wait...", "Attempting UPnP port forwarding", new string[] {"Cancel"} );
                upnpBox.Buttons[0].OnClicked = upnpBox.Close;

                //DateTime upnpTimeout = DateTime.Now + new TimeSpan(0,0,5);
                while (server.UPnP.Status == UPnPStatus.Discovering 
                    && GUIMessageBox.VisibleBox == upnpBox)// && upnpTimeout>DateTime.Now)
                {
                    yield return null;
                }

                upnpBox.Close(null,null);
                
                if (server.UPnP.Status == UPnPStatus.NotAvailable)
                {
                    new GUIMessageBox("Error", "UPnP not available");
                }
                else if (server.UPnP.Status == UPnPStatus.Discovering)
                {
                    new GUIMessageBox("Error", "UPnP discovery timed out");
                }
            }

            if (isPublic)
            {
                RegisterToMasterServer();
            }
                        
            updateInterval = new TimeSpan(0, 0, 0, 0, 150);

            DebugConsole.NewMessage("Server started", Color.Green);
                        
            GameMain.NetLobbyScreen.Select();
            started = true;
            yield return CoroutineStatus.Success;
        }

        private void RegisterToMasterServer()
        {
            if (restClient==null)
            {
                restClient = new RestClient(NetConfig.MasterServerUrl);            
            }
                        
            var request = new RestRequest("masterserver2.php", Method.GET);            
            request.AddParameter("action", "addserver");
            request.AddParameter("servername", name);
            request.AddParameter("serverport", Port);
            request.AddParameter("currplayers", connectedClients.Count);
            request.AddParameter("maxplayers", config.MaximumConnections);
            request.AddParameter("password", string.IsNullOrWhiteSpace(password) ? 0 : 1);

            // execute the request
            restClient.ExecuteAsync(request, response =>
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    DebugConsole.ThrowError("Error while connecting to master server (" + response.StatusCode + ": " + response.StatusDescription + ")");
                    return;
                }

                if (response != null && !string.IsNullOrWhiteSpace(response.Content))
                {
                    DebugConsole.ThrowError("Error while connecting to master server (" + response.Content + ")");
                    return;
                }

                registeredToMaster = true;
                refreshMasterTimer = DateTime.Now + refreshMasterInterval;
            });
        }

        private IEnumerable<object> RefreshMaster()
        {
            if (restClient == null)
            {
                restClient = new RestClient(NetConfig.MasterServerUrl);
            }

            var request = new RestRequest("masterserver2.php", Method.GET);
            request.AddParameter("action", "refreshserver");
            request.AddParameter("gamestarted", gameStarted ? 1 : 0);
            request.AddParameter("currplayers", connectedClients.Count);
            request.AddParameter("maxplayers", config.MaximumConnections);

            Log("Refreshing connection with master server...", Color.Cyan);

            var sw = new Stopwatch();
            sw.Start();

            masterServerResponded = false;
            var restRequestHandle = restClient.ExecuteAsync(request, response => MasterServerCallBack(response));

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 15);
            while (!masterServerResponded)
            {
                if (DateTime.Now > timeOut)
                {
                    restRequestHandle.Abort();
                    DebugConsole.NewMessage("Couldn't connect to master server (request timed out)", Color.Red);

                    Log("Couldn't connect to master server (request timed out)", Color.Red);

                    break;
                    //registeredToMaster = false;
                }
                
                yield return CoroutineStatus.Running;
            }

            System.Diagnostics.Debug.WriteLine("took "+sw.ElapsedMilliseconds+" ms");

            yield return CoroutineStatus.Success;
        }

        private void MasterServerCallBack(IRestResponse response)
        {
            masterServerResponded = true;

            if (response.ErrorException != null)
            {
                DebugConsole.NewMessage("Error while registering to master server (" + response.ErrorException + ")", Color.Red);
                Log("Error while registering to master server (" + response.ErrorException + ")", Color.Red);
                return;
            }

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                DebugConsole.NewMessage("Error while reporting to master server (" + response.StatusCode + ": " + response.StatusDescription + ")", Color.Red);
                Log("Error while reporting to master server (" + response.StatusCode + ": " + response.StatusDescription + ")", Color.Red);
                return;
            }

            Log("Master server responded", Color.Cyan);
        }
        
        public override void Update(float deltaTime)
        {
            if (ShowNetStats) netStats.Update(deltaTime);
            if (settingsFrame != null) settingsFrame.Update(deltaTime);
            if (log.LogFrame != null) log.LogFrame.Update(deltaTime);
            

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
            
            if (gameStarted)
            {
                if (respawnManager != null) respawnManager.Update(deltaTime);

                bool isCrewDead =  
                    connectedClients.Find(c => c.Character != null && !c.Character.IsDead)==null &&
                   (myCharacter == null || myCharacter.IsDead);

                //restart if all characters are dead or submarine is at the end of the level
                if ((autoRestart && isCrewDead) 
                    || 
                    (EndRoundAtLevelEnd && Submarine.MainSub != null && Submarine.MainSub.AtEndPosition && Submarine.MainSubs[1]==null))
                {
                    if (AutoRestart && isCrewDead)
                    {
                        Log("Ending round (entire crew dead)", Color.Cyan);
                    }
                    else
                    {
                        Log("Ending round (submarine reached the end of the level)", Color.Cyan);
                    }

                    EndGame();               
                    UpdateNetLobby(null,null);
                    return;
                }
            }
            else if (autoRestart && Screen.Selected == GameMain.NetLobbyScreen && connectedClients.Count>0)
            {
                AutoRestartTimer -= deltaTime;
                if (AutoRestartTimer < 0.0f && GameMain.NetLobbyScreen.StartButton.Enabled)
                {
                    StartGameClicked(null,null);
                }
            }

            for (int i = disconnectedClients.Count - 1; i >= 0; i-- )
            {
                disconnectedClients[i].deleteDisconnectedTimer -= deltaTime;
                if (disconnectedClients[i].deleteDisconnectedTimer > 0.0f) continue;

                if (gameStarted && disconnectedClients[i].Character!=null)
                {
                    disconnectedClients[i].Character.Kill(CauseOfDeath.Damage, true);
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
                                        connectedClient != null ? connectedClient.name + " has disconnected" : "");
                                    break;
                            }
                            break;
                        case NetIncomingMessageType.ConnectionApproval:
                            if (banList.IsBanned(inc.SenderEndPoint.Address.ToString()))
                            {
                                inc.SenderConnection.Deny("You have been banned from the server");
                            }
                            else if (ConnectedClients.Count >= MaxPlayers)
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
#if DEBUG
                    DebugConsole.ThrowError("Failed to read incoming message", e);
#endif

                    continue;
                }
            }

            // if 30ms has passed
            if (updateTimer < DateTime.Now)
            {
                /*if (gameStarted)
                {

                    float ignoreDistance = FarseerPhysics.ConvertUnits.ToDisplayUnits(NetConfig.CharacterIgnoreDistance);

                    foreach (Character c in Character.CharacterList)
                    {
                        if (!(c is AICharacter) || c.IsDead) continue;

                        if (Character.CharacterList.Any(
                            c2 => c2.IsNetworkPlayer &&
                                Vector2.Distance(c2.WorldPosition, c.WorldPosition) < ignoreDistance))
                        {
                            
                        }

                        //todo: take multiple subs into account
                        //Vector2 diff = c.WorldPosition - Submarine.MainSub.WorldPosition;

                        //if (FarseerPhysics.ConvertUnits.ToSimUnits(diff.Length()) > NetConfig.CharacterIgnoreDistance) continue;                        
                    }
                }*/

                if (server.ConnectionsCount > 0)
                {
                    if (sparseUpdateTimer < DateTime.Now) SparseUpdate();

                    foreach (Client c in ConnectedClients)
                    {
                        if (gameStarted)
                        {
                            if (c.inGame)
                            {
                                ClientWriteIngame(c);
                            }
                            else
                            {
                                ClientWriteLobby(c);
                            }
                        }
                        else
                        {
                            ClientWriteLobby(c);
                        }
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
                KickClient(inc.SenderConnection, true);
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
                    }
                    break;
                case ClientPacketHeader.UPDATE_LOBBY:
                    ClientReadLobby(inc);
                    break;
                case ClientPacketHeader.UPDATE_INGAME:
                    ClientReadIngame(inc);
                    break;
            }            
        }

        private void SparseUpdate()
        {
            //if (gameStarted)
            //{
            //    foreach (Submarine sub in Submarine.Loaded)
            //    {
            //        //no need to send position updates for submarines that are docked to mainsub
            //        if (sub != Submarine.MainSub && sub.DockedTo.Contains(Submarine.MainSub)) continue;

            //        new NetworkEvent(sub.ID, false);
            //    }
            //}

            foreach (Character c in Character.CharacterList)
            {
                if (c.IsDead) continue;

                if (c is AICharacter)
                {
                    //todo: take multiple subs into account
                    //Vector2 diff = c.WorldPosition - Submarine.MainSub.WorldPosition;

                    //if (FarseerPhysics.ConvertUnits.ToSimUnits(diff.Length()) > NetConfig.CharacterIgnoreDistance) continue;
                }
                
            }

            sparseUpdateTimer = DateTime.Now + sparseUpdateInterval;
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
            while ((objHeader=(ClientNetObject)inc.ReadByte()) != ClientNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ClientNetObject.SYNC_IDS:
                        //TODO: might want to use a clever class for this
                        c.lastRecvGeneralUpdate = Math.Max(c.lastRecvGeneralUpdate, inc.ReadUInt32());
                        c.lastRecvChatMsgID     = Math.Max(c.lastRecvChatMsgID, inc.ReadUInt32());
                        break;
                    case ClientNetObject.CHAT_MESSAGE:
                        ChatMessage.ServerRead(inc, c);
                        break;
                    default:
                        return;
                        //break;
                }
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
            
            ClientNetObject objHeader;
            while ((objHeader = (ClientNetObject)inc.ReadByte()) != ClientNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ClientNetObject.SYNC_IDS:
                        //TODO: might want to use a clever class for this

                        c.lastRecvGeneralUpdate     = Math.Max(c.lastRecvGeneralUpdate, inc.ReadUInt32());
                        c.lastRecvChatMsgID         = Math.Max(c.lastRecvChatMsgID, inc.ReadUInt32());
                        c.lastRecvEntitySpawnID     = Math.Max(c.lastRecvEntitySpawnID, inc.ReadUInt32());
                        c.lastRecvEntityRemoveID    = Math.Max(c.lastRecvEntityRemoveID, inc.ReadUInt32());

                        break;
                    case ClientNetObject.CHAT_MESSAGE:
                        ChatMessage.ServerRead(inc, c);
                        break;
                    case ClientNetObject.CHARACTER_INPUT:
                        if (c.Character != null && !c.Character.IsDead && !c.Character.IsUnconscious)
                        {
                            c.Character.ServerRead(inc, c);
                        }
                        break;
                    default:
                        return;
                        //break;
                }
            }
        }

        private void ClientWriteIngame(Client c)
        {
            NetOutgoingMessage outmsg = server.CreateMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_INGAME);
            
            outmsg.Write((float)NetTime.Now);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);
            outmsg.Write(c.lastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server

            foreach (GUIComponent gc in GameMain.NetLobbyScreen.ChatBox.children)
            {
                if (gc is GUITextBlock)
                {
                    if (gc.UserData is ChatMessage)
                    {
                        ChatMessage cMsg = (ChatMessage)gc.UserData;
                        if (cMsg.NetStateID > c.lastRecvChatMsgID)
                        {
                            cMsg.ServerWrite(outmsg, c);
                        }
                    }
                }
            }

            if (Item.Spawner.NetStateID > c.lastRecvEntitySpawnID)
            {
                outmsg.Write((byte)ServerNetObject.ENTITY_SPAWN);
                Item.Spawner.ServerWrite(outmsg, c);
                outmsg.WritePadBits();
            }

            if (Item.Remover.NetStateID > c.lastRecvEntityRemoveID)
            {
                outmsg.Write((byte)ServerNetObject.ENTITY_REMOVE);
                Item.Remover.ServerWrite(outmsg, c);
                outmsg.WritePadBits();
            }

            foreach (Character character in Character.CharacterList)
            {
                if (character is AICharacter) continue;

                outmsg.Write((byte)ServerNetObject.CHARACTER_POSITION);
                character.ServerWrite(outmsg, c);
                outmsg.WritePadBits();
            }

            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);
            server.SendMessage(outmsg, c.Connection, NetDeliveryMethod.Unreliable);
        }

        private void ClientWriteLobby(Client c)
        {
            NetOutgoingMessage outmsg = server.CreateMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_LOBBY);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);
            
            if (c.lastRecvGeneralUpdate<GameMain.NetLobbyScreen.LastUpdateID)
            {
                outmsg.Write(true);
                outmsg.WritePadBits();
                outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
                outmsg.Write(GameMain.NetLobbyScreen.GetServerName());
                outmsg.Write(GameMain.NetLobbyScreen.ServerMessage);
                var subList = GameMain.NetLobbyScreen.GetSubList();
                if (c.lastRecvGeneralUpdate < 1)
                {
                    outmsg.Write((UInt16)subList.Count);
                    for (int i = 0; i < subList.Count; i++)
                    {
                        outmsg.Write(subList[i].Name);
                        outmsg.Write(subList[i].MD5Hash.ToString());
                    }
                }
                else
                {
                    outmsg.Write((UInt16)0);
                }
                outmsg.Write((GameMain.NetLobbyScreen.SubList.SelectedData as Submarine).Name);
                outmsg.Write((GameMain.NetLobbyScreen.SubList.SelectedData as Submarine).MD5Hash.ToString());
                outmsg.Write((GameMain.NetLobbyScreen.ShuttleList.SelectedData as Submarine).Name);
                outmsg.Write((GameMain.NetLobbyScreen.ShuttleList.SelectedData as Submarine).MD5Hash.ToString());

                outmsg.WriteRangedInteger(0, 2, (int)TraitorsEnabled);

                outmsg.WriteRangedInteger(0, Mission.MissionTypes.Count - 1, (GameMain.NetLobbyScreen.MissionTypeIndex));

                outmsg.Write((byte)GameMain.NetLobbyScreen.ModeList.SelectedIndex);
                outmsg.Write(GameMain.NetLobbyScreen.LevelSeed);

                outmsg.Write(AutoRestart);
                if (autoRestart)
                {
                    outmsg.Write(AutoRestartTimer);
                }
            }
            else
            {
                outmsg.Write(false);
                outmsg.WritePadBits();
            }

            outmsg.Write(c.lastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server

            foreach (GUIComponent gc in GameMain.NetLobbyScreen.ChatBox.children)
            {
                if (gc is GUITextBlock)
                {
                    if (gc.UserData is ChatMessage)
                    {
                        ChatMessage cMsg = (ChatMessage)gc.UserData;
                        if (cMsg.NetStateID > c.lastRecvChatMsgID)
                        {
                            cMsg.ServerWrite(outmsg,c);
                        }
                    }
                }
            }
            outmsg.Write((byte)ServerNetObject.END_OF_MESSAGE);
            server.SendMessage(outmsg, c.Connection, NetDeliveryMethod.Unreliable);
        }

        public bool StartGameClicked(GUIButton button, object obj)
        {
            Submarine selectedSub = null;
            Submarine selectedShuttle = GameMain.NetLobbyScreen.SelectedShuttle;

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
                GameMain.NetLobbyScreen.SubList.Flash();
                return false;
            }

            if (selectedShuttle == null)
            {
                GameMain.NetLobbyScreen.ShuttleList.Flash();
                return false;
            }

            GameModePreset selectedMode = Voting.HighestVoted<GameModePreset>(VoteType.Mode, connectedClients);
            if (selectedMode == null) selectedMode = GameMain.NetLobbyScreen.SelectedMode;

            if (selectedMode == null)
            {
                GameMain.NetLobbyScreen.ModeList.Flash();
                return false;
            }

            CoroutineManager.StartCoroutine(InitiateStartGame(selectedSub, selectedShuttle, selectedMode), "InitiateStartGame");

            return true;
        }

        private IEnumerable<object> InitiateStartGame(Submarine selectedSub, Submarine selectedShuttle, GameModePreset selectedMode)
        {
            GameMain.NetLobbyScreen.StartButton.Enabled = false;

            if (connectedClients.Any())
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write((byte)ServerPacketHeader.QUERY_STARTGAME);

                msg.Write(selectedSub.Name);
                msg.Write(selectedSub.MD5Hash.Hash);

                msg.Write(selectedShuttle.Name);
                msg.Write(selectedShuttle.MD5Hash.Hash);
            
                connectedClients.ForEach(c => c.ReadyToStart = false);

                server.SendMessage(msg, connectedClients.Select(c => c.Connection).ToList(), NetDeliveryMethod.ReliableUnordered, 0);

                //give the clients a few seconds to request missing sub/shuttle files before starting the round
                float waitForResponseTimer = 3.0f;
                while (connectedClients.Any(c => !c.ReadyToStart) && waitForResponseTimer > 0.0f)
                {
                    waitForResponseTimer -= CoroutineManager.UnscaledDeltaTime;
                    yield return CoroutineStatus.Running;
                }

                //todo: wait until file transfers are finished/cancelled
            }

            GameMain.ShowLoading(StartGame(selectedSub, selectedShuttle, selectedMode), false);

            yield return CoroutineStatus.Success;
        }

        private IEnumerable<object> StartGame(Submarine selectedSub, Submarine selectedShuttle, GameModePreset selectedMode)
        {
            Item.Spawner.Clear();
            Item.Remover.Clear();

            GameMain.NetLobbyScreen.StartButton.Enabled = false;

            GUIMessageBox.CloseAll();

            roundStartSeed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(roundStartSeed);
            
            GameMain.GameSession = new GameSession(selectedSub, "", selectedMode, Mission.MissionTypes[GameMain.NetLobbyScreen.MissionTypeIndex]);

            yield return CoroutineStatus.Running;

            int teamCount = 1;
            int hostTeam = 1;
            if (GameMain.GameSession.gameMode.Mission != null && 
                GameMain.GameSession.gameMode.Mission.AssignTeamIDs(connectedClients,out hostTeam))
            {
                teamCount = 2;
            }

            GameMain.GameSession.StartShift(GameMain.NetLobbyScreen.LevelSeed, teamCount > 1);

            GameServer.Log("Starting a new round...", Color.Cyan);
            GameServer.Log("Submarine: " + selectedSub.Name, Color.Cyan);
            GameServer.Log("Game mode: " + selectedMode.Name, Color.Cyan);
            GameServer.Log("Level seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);

            if (AllowRespawn) respawnManager = new RespawnManager(this, selectedShuttle);
            
            AssignJobs(connectedClients, characterInfo != null);
            
            List<CharacterInfo> characterInfos = new List<CharacterInfo>();
            foreach (Client client in connectedClients)
            {
                client.inGame = true;
                if (client.characterInfo == null)
                {
                    client.characterInfo = new CharacterInfo(Character.HumanConfigFile, client.name);
                }
                characterInfos.Add(client.characterInfo);
                client.characterInfo.Job = new Job(client.assignedJob);
            }

            //host's character
            if (characterInfo != null)
            {
                characterInfo.Job = new Job(GameMain.NetLobbyScreen.JobPreferences[0]);
                characterInfos.Add(characterInfo);
            }

            
            WayPoint[] assignedWayPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);
            for (int i = 0; i < connectedClients.Count; i++)
            {
                Character spawnedCharacter = Character.Create(connectedClients[i].characterInfo, assignedWayPoints[i].WorldPosition, true, false);
                spawnedCharacter.AnimController.Frozen = true;
                spawnedCharacter.GiveJobItems(assignedWayPoints[i]);

                connectedClients[i].Character = spawnedCharacter;                
                
                GameMain.GameSession.CrewManager.characters.Add(spawnedCharacter);
            }

            if (characterInfo != null)
            {
                myCharacter = Character.Create(characterInfo, assignedWayPoints[assignedWayPoints.Length - 1].WorldPosition, false, false);
                myCharacter.GiveJobItems(assignedWayPoints.Last());

                Character.Controlled = myCharacter;
                GameMain.GameSession.CrewManager.characters.Add(myCharacter);
            }

            foreach (Character c in GameMain.GameSession.CrewManager.characters)
            {
                Entity.Spawner.AddToSpawnedList(c);

                c.SpawnItems.ForEach(item => Entity.Spawner.AddToSpawnedList(item));
            }

            SendStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.gameMode.Preset, connectedClients);
            //var startMessage = CreateStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.gameMode.Preset);
            //server.SendMessage(startMessage, connectedClients.Select(c => c.Connection).ToList(), NetDeliveryMethod.ReliableUnordered, 0);

            yield return CoroutineStatus.Running;

            //UpdateCrewFrame();

            //TraitorManager = null;
            //if (TraitorsEnabled == YesNoMaybe.Yes ||
            //    (TraitorsEnabled == YesNoMaybe.Maybe && Rand.Range(0.0f, 1.0f) < 0.5f))
            //{
            //    TraitorManager = new TraitorManager(this);
            //}

            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.GameScreen.Select();

            AddChatMessage("Press TAB to chat. Use ''r;'' to talk through the radio.", ChatMessageType.Server);
            
            GameMain.NetLobbyScreen.StartButton.Enabled = true;

            gameStarted = true;

            yield return CoroutineStatus.Success;
        }

        private void SendStartMessage(int seed, Submarine selectedSub, GameModePreset selectedMode, List<Client> clients)
        {
            foreach (Client client in clients)
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write((byte)ServerPacketHeader.STARTGAME);

                msg.Write(seed);

                msg.Write(GameMain.NetLobbyScreen.LevelSeed);

                msg.Write((byte)GameMain.NetLobbyScreen.MissionTypeIndex);

                msg.Write(selectedSub.Name);
                msg.Write(selectedSub.MD5Hash.Hash);

                msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
                msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.Hash);

                msg.Write(selectedMode.Name);

                msg.Write(AllowRespawn);
                msg.Write(Submarine.MainSubs[1] != null); //loadSecondSub

                msg.Write(client.Character.ID);

                server.SendMessage(msg, client.Connection, NetDeliveryMethod.ReliableUnordered);     
            }
       
        }

        public void EndGame()
        {
            if (!gameStarted) return;

            string endMessage = "The round has ended." + '\n';

            if (TraitorManager != null)
            {
                endMessage += TraitorManager.GetEndMessage();
            }

            GameMain.GameSession.gameMode.End(endMessage);

            if (autoRestart) AutoRestartTimer = AutoRestartInterval;

            if (SaveServerLogs) log.Save();
            
            Character.Controlled = null;
            myCharacter = null;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;

            Item.Spawner.Clear();
            Item.Remover.Clear();

#if DEBUG
            messageCount.Clear();
#endif

            respawnManager = null;
            gameStarted = false;

            if (connectedClients.Count > 0)
            {
                foreach (Client client in connectedClients)
                {
                    client.Character = null;
                    client.inGame = false;
                }
            }

            CoroutineManager.StartCoroutine(EndCinematic());
        }

        public IEnumerable<object> EndCinematic()
        {
            float endPreviewLength = 10.0f;

            var cinematic = new TransitionCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, endPreviewLength);

            float secondsLeft = endPreviewLength;

            do
            {
                secondsLeft -= CoroutineManager.UnscaledDeltaTime;

                yield return CoroutineStatus.Running;
            } while (secondsLeft > 0.0f);

            Submarine.Unload();

            GameMain.NetLobbyScreen.Select();

            yield return CoroutineStatus.Success;
        }

        public override void KickPlayer(string playerName, bool ban, bool range=false)
        {
            playerName = playerName.ToLowerInvariant();

            Client client = connectedClients.Find(c =>
                c.name.ToLowerInvariant() == playerName ||
                (c.Character != null && c.Character.Name.ToLowerInvariant() == playerName));

            KickClient(client, ban, range);
        }

        public void KickClient(NetConnection conn, bool ban = false, bool range = false)
        {
            Client client = connectedClients.Find(c => c.Connection == conn);
            if (client == null)
            {
                conn.Disconnect(ban ? "You have been banned from the server" : "You have been kicked from the server");
                if (ban)
                {
                    if (!banList.IsBanned(conn.RemoteEndPoint.Address.ToString()))
                    {
                        banList.BanPlayer("Unnamed", conn.RemoteEndPoint.Address.ToString());
                    }
                }
            }
            else
            {
                KickClient(client, ban, range);
            }
        }

        public void KickClient(Client client, bool ban = false, bool range = false)
        {
            if (client == null) return;

            if (ban)
            {
                DisconnectClient(client, client.name + " has been banned from the server", "You have been banned from the server");
                string ip = client.Connection.RemoteEndPoint.Address.ToString();
                if (range) { ip = banList.ToRange(ip); }
                banList.BanPlayer(client.name, ip);
            }
            else
            {
                DisconnectClient(client, client.name + " has been kicked from the server", "You have been kicked from the server");
            }
        }

        private void DisconnectClient(NetConnection senderConnection, string msg = "", string targetmsg = "")
        {
            Client client = connectedClients.Find(x => x.Connection == senderConnection);
            if (client == null) return;

            DisconnectClient(client, msg, targetmsg);
        }

        private void DisconnectClient(Client client, string msg = "", string targetmsg = "")
        {
            if (client == null) return;

            if (gameStarted && client.Character != null)
            {
                client.Character.ClearInputs();
                client.Character.Kill(CauseOfDeath.Disconnected, true);
            }

            client.Character = null;
            client.inGame = false;

            if (string.IsNullOrWhiteSpace(msg)) msg = client.name + " has left the server";
            if (string.IsNullOrWhiteSpace(targetmsg)) targetmsg = "You have left the server";

            Log(msg, ChatMessage.MessageColor[(int)ChatMessageType.Server]);

            client.Connection.Disconnect(targetmsg);

            GameMain.NetLobbyScreen.RemovePlayer(client.name);
            
            connectedClients.Remove(client);

            AddChatMessage(msg, ChatMessageType.Server);

            UpdateCrewFrame();

            refreshMasterTimer = DateTime.Now;
        }

        private void UpdateCrewFrame()
        {
            List<Character> crew = new List<Character>();

            foreach (Client c in connectedClients)
            {
                if (c.Character == null || !c.inGame) continue;

                crew.Add(c.Character);
            }

            if (myCharacter != null) crew.Add(myCharacter);

            //if (GameMain.GameSession!=null) GameMain.GameSession.CrewManager.CreateCrewFrame(crew);
        }
        
        public void NewTraitor(Character traitor, Character target)
        {
            Log(traitor.Name + " is the traitor and the target is " + target.Name, Color.Cyan);

            Client traitorClient = null;
            foreach (Client c in connectedClients)
            {
                if (c.Character != traitor) continue;
                traitorClient = c;
                break;
            }
            
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);

            if (settingsFrame != null)
            {
                settingsFrame.Draw(spriteBatch);
            }
            else if (log.LogFrame!=null)
            {
                log.LogFrame.Draw(spriteBatch);
            }

            if (!ShowNetStats) return;

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight * 0.3f);


            if (clientListScrollBar == null)
            {
                clientListScrollBar = new GUIScrollBar(new Rectangle(x + width - 10, y, 10, height), GUI.Style, 1.0f);
            }


            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black * 0.7f, true);
            spriteBatch.DrawString(GUI.Font, "Network statistics:", new Vector2(x + 10, y + 10), Color.White);
                        
            spriteBatch.DrawString(GUI.SmallFont, "Connections: "+server.ConnectionsCount, new Vector2(x + 10, y + 30), Color.White);
            spriteBatch.DrawString(GUI.SmallFont, "Received bytes: " + MathUtils.GetBytesReadable(server.Statistics.ReceivedBytes), new Vector2(x + 10, y + 45), Color.White);
            spriteBatch.DrawString(GUI.SmallFont, "Received packets: " + server.Statistics.ReceivedPackets, new Vector2(x + 10, y + 60), Color.White);

            spriteBatch.DrawString(GUI.SmallFont, "Sent bytes: " + MathUtils.GetBytesReadable(server.Statistics.SentBytes), new Vector2(x + 10, y + 75), Color.White);
            spriteBatch.DrawString(GUI.SmallFont, "Sent packets: " + server.Statistics.SentPackets, new Vector2(x + 10, y + 90), Color.White);

            int resentMessages = 0;

            int clientListHeight = connectedClients.Count() * 40;
            float scrollBarHeight = (height - 110) / (float)Math.Max(clientListHeight, 110);

            if (clientListScrollBar.BarSize != scrollBarHeight)
            {
                clientListScrollBar.BarSize = scrollBarHeight;
            }

            int startY = y + 110;
            y = (startY - (int)(clientListScrollBar.BarScroll * (clientListHeight-(height - 110))));
            foreach (Client c in connectedClients)
            {
                Color clientColor = c.Connection.AverageRoundtripTime > 0.3f ? Color.Red : Color.White;

                if (y >= startY && y < startY + height - 120)
                {
                    spriteBatch.DrawString(GUI.SmallFont, c.name + " ("+c.Connection.RemoteEndPoint.Address.ToString()+")", new Vector2(x + 10, y), clientColor);
                    spriteBatch.DrawString(GUI.SmallFont, "Ping: " + (int)(c.Connection.AverageRoundtripTime * 1000.0f) + " ms", new Vector2(x+20, y+10), clientColor);
                }
                if (y + 25 >= startY && y < startY + height - 130) spriteBatch.DrawString(GUI.SmallFont, "Resent messages: " + c.Connection.Statistics.ResentMessages, new Vector2(x + 20, y + 20), clientColor);

                resentMessages += (int)c.Connection.Statistics.ResentMessages;

                y += 40;
            }

            clientListScrollBar.Update(1.0f / 60.0f);
            clientListScrollBar.Draw(spriteBatch);

            netStats.AddValue(NetStats.NetStatType.ResentMessages, Math.Max(resentMessages, 0));
            netStats.AddValue(NetStats.NetStatType.SentBytes, server.Statistics.SentBytes);
            netStats.AddValue(NetStats.NetStatType.ReceivedBytes, server.Statistics.ReceivedBytes);

            netStats.Draw(spriteBatch, new Rectangle(200,0,800,200), this);

        }

        public void UpdateVoteStatus()
        {
            if (server.Connections.Count == 0) return;

            var clientsToKick = connectedClients.FindAll(c => c.KickVoteCount > connectedClients.Count * KickVoteRequiredRatio);
            //clientsToKick.ForEach(c => KickClient(c));
            
        }

        public bool UpdateNetLobby(object obj)
        {
            return UpdateNetLobby(null, obj);
        }

        public bool UpdateNetLobby(GUIComponent component, object obj)
        {
            if (server.Connections.Count == 0) return true;
            
            return true;
        }

        public void UpdateClientPermissions(Client client)
        {
            
            clientPermissions.RemoveAll(cp => cp.IP == client.Connection.RemoteEndPoint.Address.ToString());

            if (client.Permissions != ClientPermissions.None)
            {
                clientPermissions.Add(new SavedClientPermission(
                    client.name, 
                    client.Connection.RemoteEndPoint.Address.ToString(), 
                    client.Permissions));
            }

            SaveClientPermissions();
        }

        public override bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            base.SelectCrewCharacter(component, obj);

            var characterFrame = component.Parent.Parent.FindChild("selectedcharacter");

            Character character = obj as Character;
            if (character == null) return false;

            if (character != myCharacter)
            {
                var banButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Ban", Alignment.BottomRight, GUI.Style, characterFrame);
                banButton.UserData = character.Name;
                banButton.OnClicked += GameMain.NetLobbyScreen.BanPlayer;

                var rangebanButton = new GUIButton(new Rectangle(0, -25, 100, 20), "Ban range", Alignment.BottomRight, GUI.Style, characterFrame);
                rangebanButton.UserData = character.Name;
                rangebanButton.OnClicked += GameMain.NetLobbyScreen.BanPlayerRange;

                var kickButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Kick", Alignment.BottomLeft, GUI.Style, characterFrame);
                kickButton.UserData = character.Name;
                kickButton.OnClicked += GameMain.NetLobbyScreen.KickPlayer;
            }

            return true;
        }

        private void UpdateCharacterInfo(NetIncomingMessage message, Client sender)
        {
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

                DebugConsole.Log("Received invalid characterinfo from \"" +sender.name+"\"! { "+e.Message+" }");
            }
            
            List<JobPrefab> jobPreferences = new List<JobPrefab>();
            int count = message.ReadByte();
            for (int i = 0; i < Math.Min(count, 3); i++)
            {
                string jobName = message.ReadString();

                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);
                if (jobPrefab != null) jobPreferences.Add(jobPrefab);
            }

            sender.characterInfo = new CharacterInfo(Character.HumanConfigFile, sender.name, gender);
            sender.characterInfo.HeadSpriteId = headSpriteId;
            sender.jobPreferences = jobPreferences;
        }
        
        public void AssignJobs(List<Client> unassigned, bool assignHost)
        {
            unassigned = new List<Client>(unassigned);
            
            int[] assignedClientCount = new int[JobPrefab.List.Count];

            if (characterInfo!=null && assignHost)
            {
                assignedClientCount[JobPrefab.List.FindIndex(jp => jp == GameMain.NetLobbyScreen.JobPreferences[0])]=1;
            }

            foreach (Client c in connectedClients)
            {
                if (unassigned.Contains(c)) continue;
                if (c.Character == null || !c.Character.IsDead) continue;

                assignedClientCount[JobPrefab.List.IndexOf(c.Character.Info.Job.Prefab)]++;
            }

            //if any of the players has chosen a job that is Always Allowed, give them that job
            for (int i = unassigned.Count - 1; i >= 0; i--)
            {
                if (!unassigned[i].jobPreferences[0].AllowAlways) continue;
                unassigned[i].assignedJob = unassigned[i].jobPreferences[0];
                unassigned.RemoveAt(i);
            }

            //go throught the jobs whose MinNumber>0 (i.e. at least one crew member has to have the job)
            bool unassignedJobsFound = true;
            while (unassignedJobsFound && unassigned.Count > 0)
            {
                unassignedJobsFound = false;
                for (int i = 0; i < JobPrefab.List.Count; i++)
                {
                    if (unassigned.Count == 0) break;
                    if (JobPrefab.List[i].MinNumber < 1 || assignedClientCount[i] >= JobPrefab.List[i].MinNumber) continue;

                    //find the client that wants the job the most, or force it to random client if none of them want it
                    Client assignedClient = FindClientWithJobPreference(unassigned, JobPrefab.List[i], true);

                    assignedClient.assignedJob = JobPrefab.List[i];

                    assignedClientCount[i]++;
                    unassigned.Remove(assignedClient);

                    //the job still needs more crew members, set unassignedJobsFound to true to keep the while loop running
                    if (assignedClientCount[i] < JobPrefab.List[i].MinNumber) unassignedJobsFound = true;
                }
            }
            
            //find a suitable job for the rest of the players
            for (int i = unassigned.Count - 1; i >= 0; i--)
            {
                for (int preferenceIndex = 0; preferenceIndex < 3; preferenceIndex++)
                {
                    int jobIndex = JobPrefab.List.FindIndex(jp => jp == unassigned[i].jobPreferences[preferenceIndex]);

                    //if there's enough crew members assigned to the job already, continue
                    if (assignedClientCount[jobIndex] >= JobPrefab.List[jobIndex].MaxNumber) continue;

                    unassigned[i].assignedJob = JobPrefab.List[jobIndex];

                    assignedClientCount[jobIndex]++;
                    unassigned.RemoveAt(i);
                    break;
                }
            }

            UpdateNetLobby(null);

        }

        private Client FindClientWithJobPreference(List<Client> clients, JobPrefab job, bool forceAssign = false)
        {
            int bestPreference = 0;
            Client preferredClient = null;
            foreach (Client c in clients)
            {
                int index = c.jobPreferences.IndexOf(job);
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

        public static void Log(string line, Color? color)
        {
            if (GameMain.Server == null || !GameMain.Server.SaveServerLogs) return;

            GameMain.Server.log.WriteLine(line, color);
        }

        public override void Disconnect()
        {
            banList.Save();

            if (registeredToMaster && restClient != null)
            {
                var request = new RestRequest("masterserver2.php", Method.GET);
                request.AddParameter("action", "removeserver");
                
                restClient.Execute(request);
                restClient = null;
            }

            if (SaveServerLogs)
            {
                Log("Shutting down server...", Color.Cyan);
                log.Save();
            }
            
            server.Shutdown("The server has been shut down");
        }
    }
}
