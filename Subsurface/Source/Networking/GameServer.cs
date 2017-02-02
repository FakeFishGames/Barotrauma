
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

        private ServerEntityEventManager entityEventManager;

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

            AdminAuthPass = "";

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
            config.SimulatedRandomLatency = 0.05f;
            config.SimulatedDuplicatesChance = 0.05f;
            config.SimulatedMinimumLatency = 0.1f;

            config.ConnectionTimeout = 60.0f;
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

            entityEventManager = new ServerEntityEventManager(this);

            whitelist = new WhiteList();
            banList = new BanList();

            LoadSettings();
            LoadClientPermissions();
            
            //----------------------------------------
            
            CoroutineManager.StartCoroutine(StartServer(isPublic));
        }

        private IEnumerable<object> StartServer(bool isPublic)
        {
            bool error = false;
            try
            {
                Log("Starting the server...", Color.Cyan);
                server = new NetServer(config);
                netPeer = server;
                server.Start();
            }
            catch (Exception e)
            {
                Log("Error while starting the server (" + e.Message + ")", Color.Red);

                System.Net.Sockets.SocketException socketException = e as System.Net.Sockets.SocketException;

                if (socketException != null && socketException.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
                {
                    new GUIMessageBox("Starting the server failed", e.Message + ". Are you trying to run multiple servers on the same port?");
                }
                else
                {
                    new GUIMessageBox("Starting the server failed", e.Message);
                }

                error = true;
            }                  
      
            if (error)
            {
                if (server != null) server.Shutdown("Error while starting the server");

                GameMain.NetworkMember = null;
                yield return CoroutineStatus.Success;
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
                
            var request = new RestRequest("masterserver3.php", Method.GET);            
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

            var request = new RestRequest("masterserver3.php", Method.GET);
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

            if (response.Content=="Error: server not found")
            {
                Log("Not registered to master server, re-registering...", Color.Red);

                RegisterToMasterServer();
                return;
            }

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
        
        public override void AddToGUIUpdateList()
        {
            if (started) base.AddToGUIUpdateList();

            if (settingsFrame != null) settingsFrame.AddToGUIUpdateList();
            if (log.LogFrame != null) log.LogFrame.AddToGUIUpdateList();
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
                            c2 => c2.IsRemotePlayer &&
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
                        if (gameStarted && c.inGame)
                        {
                            ClientWriteIngame(c);                         
                        }
                        else
                        {
                            ClientWriteLobby(c);
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

                        //game already started -> send start message immediately
                        if (gameStarted)
                        {
                            SendStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.gameMode.Preset, connectedClients);
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

            /*foreach (Character c in Character.CharacterList)
            {
                if (c.IsDead) continue;

                if (c is AICharacter)
                {
                    //todo: take multiple subs into account
                    //Vector2 diff = c.WorldPosition - Submarine.MainSub.WorldPosition;

                    //if (FarseerPhysics.ConvertUnits.ToSimUnits(diff.Length()) > NetConfig.CharacterIgnoreDistance) continue;
                }
                
            }*/

            sparseUpdateTimer = DateTime.Now + sparseUpdateInterval;
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
            while ((objHeader=(ClientNetObject)inc.ReadByte()) != ClientNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ClientNetObject.SYNC_IDS:
                        //TODO: might want to use a clever class for this
                        c.lastRecvGeneralUpdate = Math.Min(Math.Max(c.lastRecvGeneralUpdate, inc.ReadUInt32()), GameMain.NetLobbyScreen.LastUpdateID);
                        c.lastRecvChatMsgID     = Math.Min(Math.Max(c.lastRecvChatMsgID, inc.ReadUInt32()), c.lastChatMsgQueueID);
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

            if (gameStarted) c.inGame = true;
            
            ClientNetObject objHeader;
            while ((objHeader = (ClientNetObject)inc.ReadByte()) != ClientNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ClientNetObject.SYNC_IDS:
                        //TODO: might want to use a clever class for this
                        
                        UInt32 lastRecvChatMsgID        = inc.ReadUInt32();
                        UInt32 lastRecvEntitySpawnID    = inc.ReadUInt32();
                        UInt32 lastRecvEntityEventID    = inc.ReadUInt32();

                        //last msgs we've created/sent, the client IDs should never be higher than these
                        UInt32 lastEntitySpawnID = Entity.Spawner.NetStateID;
                        UInt32 lastEntityEventID = entityEventManager.Events.Count() == 0 ? 0 : entityEventManager.Events.Last().ID;

#if DEBUG
                        //client thinks they've received a msg we haven't sent yet (corrupted packet, msg read/written incorrectly?)
                        if (lastRecvChatMsgID > c.lastChatMsgQueueID)
                            DebugConsole.ThrowError("client.lastRecvChatMsgID > lastChatMsgQueueID");

                        if (lastRecvEntitySpawnID > lastEntitySpawnID)
                            DebugConsole.ThrowError("client.lastRecvEntitySpawnID > lastEntitySpawnID");
                        
                        if (lastRecvEntityEventID > lastEntityEventID)
                            DebugConsole.ThrowError("client.lastRecvEntityEventID > lastEntityEventID");                        
#endif

                        c.lastRecvChatMsgID     = Math.Min(Math.Max(c.lastRecvChatMsgID, lastRecvChatMsgID), c.lastChatMsgQueueID);
                        c.lastRecvEntitySpawnID = Math.Min(Math.Max(c.lastRecvEntitySpawnID, lastRecvEntitySpawnID), lastEntitySpawnID);
                        c.lastRecvEntityEventID = Math.Min(Math.Max(c.lastRecvEntityEventID, lastRecvEntityEventID), lastEntityEventID);


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
                        Voting.RegisterVote(inc, c);
                        break;
                    default:
                        return;
                }
            }
        }

        /// <summary>
        /// Write info that the client needs when joining the server
        /// </summary>
        private void ClientWriteInitial(Client c, NetBuffer outmsg)
        {
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
        }

        private void ClientWriteIngame(Client c)
        {
            NetOutgoingMessage outmsg = server.CreateMessage();
            outmsg.Write((byte)ServerPacketHeader.UPDATE_INGAME);
            
            outmsg.Write((float)NetTime.Now);

            outmsg.Write((byte)ServerNetObject.SYNC_IDS);
            outmsg.Write(c.lastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server
            outmsg.Write(c.lastSentEntityEventID);

            c.chatMsgQueue.RemoveAll(cMsg => cMsg.NetStateID <= c.lastRecvChatMsgID);
            foreach (ChatMessage cMsg in c.chatMsgQueue)
            {
                cMsg.ServerWrite(outmsg, c);
            }            

            if (Item.Spawner.NetStateID > c.lastRecvEntitySpawnID)
            {
                outmsg.Write((byte)ServerNetObject.ENTITY_SPAWN);
                Item.Spawner.ServerWrite(outmsg, c);
                outmsg.WritePadBits();
            }
            
            foreach (Character character in Character.CharacterList)
            {
                if (character is AICharacter)
                {
                    //TODO: don't send if the ai character is far from the client 
                    //(some sort of distance-based culling might be a good idea for player-controlled characters as well)
                    outmsg.Write((byte)ServerNetObject.ENTITY_POSITION);
                    character.ServerWrite(outmsg, c);
                    outmsg.WritePadBits();
                }
                else
                {
                    outmsg.Write((byte)ServerNetObject.ENTITY_POSITION);
                    character.ServerWrite(outmsg, c);
                    outmsg.WritePadBits();
                }
            }

            foreach (Submarine sub in Submarine.Loaded)
            {
                //if docked to a sub with a smaller ID, don't send an update
                //  (= update is only sent for the docked sub that has the smallest ID, doesn't matter if it's the main sub or a shuttle)
                if (sub.DockedTo.Any(s => s.ID < sub.ID)) continue;

                outmsg.Write((byte)ServerNetObject.ENTITY_POSITION);
                sub.ServerWrite(outmsg, c);
                outmsg.WritePadBits();
            }

            foreach (Item item in Item.ItemList)
            {
                if (!item.NeedsPositionUpdate) continue;

                outmsg.Write((byte)ServerNetObject.ENTITY_POSITION);
                item.ServerWritePosition(outmsg, c);
                outmsg.WritePadBits();
            }

            entityEventManager.Write(c, outmsg);

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
                outmsg.Write(GameMain.NetLobbyScreen.ServerMessage.Text);
                
                outmsg.Write(c.lastRecvGeneralUpdate < 1);
                if (c.lastRecvGeneralUpdate < 1)
                {
                    ClientWriteInitial(c, outmsg);
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

                outmsg.Write((byte)connectedClients.Count);
                foreach (Client client in connectedClients)
                {
                    outmsg.Write(client.name);
                }
            }
            else
            {
                outmsg.Write(false);
                outmsg.WritePadBits();
            }

            outmsg.Write(c.lastSentChatMsgID); //send this to client so they know which chat messages weren't received by the server

            c.chatMsgQueue.RemoveAll(cMsg => cMsg.NetStateID <= c.lastRecvChatMsgID);
            foreach (ChatMessage cMsg in c.chatMsgQueue)
            {
                cMsg.ServerWrite(outmsg, c);
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
            entityEventManager.Clear();

            GameMain.NetLobbyScreen.StartButton.Enabled = false;

            GUIMessageBox.CloseAll();
            
            roundStartSeed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(roundStartSeed);

            bool couldNotStart = false;

            int teamCount = 1;
            int hostTeam = 1;

            try
            {            
                GameMain.GameSession = new GameSession(selectedSub, "", selectedMode, Mission.MissionTypes[GameMain.NetLobbyScreen.MissionTypeIndex]);

                if (GameMain.GameSession.gameMode.Mission != null && 
                    GameMain.GameSession.gameMode.Mission.AssignTeamIDs(connectedClients,out hostTeam))
                {
                    teamCount = 2;
                }

                GameMain.GameSession.StartShift(GameMain.NetLobbyScreen.LevelSeed, teamCount > 1);
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start a new round", e);

                //try again in >5 seconds
                if (autoRestart) AutoRestartTimer = Math.Max(AutoRestartInterval, 5.0f);
                GameMain.NetLobbyScreen.StartButton.Enabled = true;

                UpdateNetLobby(null, null);

                couldNotStart = true;
            }

            if (couldNotStart) yield return CoroutineStatus.Failure;

            GameServer.Log("Starting a new round...", Color.Cyan);
            GameServer.Log("Submarine: " + selectedSub.Name, Color.Cyan);
            GameServer.Log("Game mode: " + selectedMode.Name, Color.Cyan);
            GameServer.Log("Level seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);

            bool missionAllowRespawn = 
                !(GameMain.GameSession.gameMode is MissionMode) || 
                ((MissionMode)GameMain.GameSession.gameMode).Mission.AllowRespawn;

            if (AllowRespawn && missionAllowRespawn) respawnManager = new RespawnManager(this, selectedShuttle);

            //assign jobs and spawnpoints separately for each team
            for (int teamID = 1; teamID <= teamCount; teamID++)
            {
                //find the clients in this team
                List<Client> teamClients = teamCount == 1 ? connectedClients : connectedClients.FindAll(c => c.TeamID == teamID);
                
                if (!teamClients.Any() && teamID > 1) continue;

                AssignJobs(teamClients, teamID == hostTeam);

                List<CharacterInfo> characterInfos = new List<CharacterInfo>();
                foreach (Client client in teamClients)
                {
                    client.lastRecvEntitySpawnID = 0;

                    client.entityEventLastSent.Clear();
                    client.lastSentEntityEventID = 0;
                    client.lastRecvEntityEventID = 0;

                    if (client.characterInfo == null)
                    {
                        client.characterInfo = new CharacterInfo(Character.HumanConfigFile, client.name);
                    }
                    characterInfos.Add(client.characterInfo);
                    client.characterInfo.Job = new Job(client.assignedJob);
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
                    Character spawnedCharacter = Character.Create(teamClients[i].characterInfo, assignedWayPoints[i].WorldPosition, true, false);
                    spawnedCharacter.AnimController.Frozen = true;
                    spawnedCharacter.GiveJobItems(assignedWayPoints[i]);
                    spawnedCharacter.TeamID = (byte)teamID;

                    teamClients[i].Character = spawnedCharacter;

                    GameMain.GameSession.CrewManager.characters.Add(spawnedCharacter);
                }

                if (characterInfo != null && hostTeam == teamID)
                {
                    myCharacter = Character.Create(characterInfo, assignedWayPoints[assignedWayPoints.Length - 1].WorldPosition, false, false);
                    myCharacter.GiveJobItems(assignedWayPoints.Last());
                    myCharacter.TeamID = (byte)teamID;

                    Character.Controlled = myCharacter;
                    GameMain.GameSession.CrewManager.characters.Add(myCharacter);
                }
            }
            
            foreach (Character c in GameMain.GameSession.CrewManager.characters)
            {
                Entity.Spawner.AddToSpawnedList(c);
                Entity.Spawner.AddToSpawnedList(c.SpawnItems);
            }
            
            TraitorManager = null;
            if (TraitorsEnabled == YesNoMaybe.Yes ||
                (TraitorsEnabled == YesNoMaybe.Maybe && Rand.Range(0.0f, 1.0f) < 0.5f))
            {
                TraitorManager = new TraitorManager(this);

                if (TraitorManager.TraitorCharacter!=null && TraitorManager.TargetCharacter != null)
                {
                    Log(TraitorManager.TraitorCharacter.Name + " is the traitor and the target is " + TraitorManager.TargetCharacter.Name, Color.Cyan);
                }
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

                bool missionAllowRespawn =
                    !(GameMain.GameSession.gameMode is MissionMode) ||
                    ((MissionMode)GameMain.GameSession.gameMode).Mission.AllowRespawn;

                msg.Write(AllowRespawn && missionAllowRespawn);
                msg.Write(Submarine.MainSubs[1] != null); //loadSecondSub

                if (TraitorManager != null &&
                    TraitorManager.TraitorCharacter != null &&
                    TraitorManager.TargetCharacter != null && 
                    TraitorManager.TraitorCharacter == client.Character)
                {
                    msg.Write(true);
                    msg.Write(TraitorManager.TargetCharacter.Name);
                }
                else
                {
                    msg.Write(false);
                }
                
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

            Mission mission = GameMain.GameSession.Mission;
            GameMain.GameSession.gameMode.End(endMessage);

            if (autoRestart) AutoRestartTimer = AutoRestartInterval;

            if (SaveServerLogs) log.Save();
            
            Character.Controlled = null;
            myCharacter = null;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;

            Item.Spawner.Clear();
            entityEventManager.Clear();
            foreach (Client c in connectedClients)
            {
                c.entityEventLastSent.Clear();
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
                    client.inGame = false;
                }
            }

            CoroutineManager.StartCoroutine(EndCinematic(),"EndCinematic");
        }

        public IEnumerable<object> EndCinematic()
        {
            float endPreviewLength = 10.0f;

            var cinematic = new TransitionCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, endPreviewLength);

            new TransitionCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, endPreviewLength);
            float secondsLeft = endPreviewLength;

            do
            {
                secondsLeft -= CoroutineManager.UnscaledDeltaTime;

                yield return CoroutineStatus.Running;
            } while (secondsLeft > 0.0f);

            Submarine.Unload();
            entityEventManager.Clear();

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
            foreach (Client c in connectedClients)
            {
                if (c.Character == null || !c.inGame) continue;
            }
        }
        
        /// <summary>
        /// Add the message to the chatbox and pass it to all clients who can receive it
        /// </summary>
        public void SendChatMessage(string message, ChatMessageType? type = null, Client senderClient = null)
        {
            Character senderCharacter = null;
            string senderName = "";

            Client targetClient = null;
            
            if (type==null)
            {
                string tempStr;
                string command = ChatMessage.GetChatMessageCommand(message, out tempStr);
                switch (command)
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
                                    command == c.name.ToLowerInvariant() ||
                                    (c.Character != null && command == c.Character.Name.ToLowerInvariant()));

                                if (targetClient == null)
                                {
                                    AddChatMessage("Player \"" + command + "\" not found!", ChatMessageType.Error);
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
                //msg sent by the server
                if (senderClient == null)
                {
                    senderCharacter = myCharacter;
                    senderName = myCharacter == null ? name : myCharacter.Name;
                }                
                else //msg sent by a client
                {
                    senderCharacter = senderClient.Character;
                    senderName = senderCharacter == null ? senderClient.name : senderCharacter.Name;

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
                //msg sent by the server
                if (senderClient == null)
                {
                    senderName = name;
                }                
                else //msg sent by a client          
                {
                    //game not started -> clients can only send normal and private chatmessages
                    if (type != ChatMessageType.Private) type = ChatMessageType.Default;
                    senderName = senderClient.name;
                }
            }

            //check if the client is allowed to send the message
            WifiComponent senderRadio = null;
            switch (type)
            {
                case ChatMessageType.Radio:
                    if (senderCharacter == null) return;

                    //return if senderCharacter doesn't have a working radio
                    var radio = senderCharacter.Inventory.Items.First(i => i != null && i.GetComponent<WifiComponent>() != null);
                    if (radio == null) return;

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
            
            //check which clients can receive the message and apply distance effects
            foreach (Client client in ConnectedClients)
            {
                string modifiedMessage = message;

                switch (type)
                {
                    case ChatMessageType.Default:
                    case ChatMessageType.Radio:
                        if (senderCharacter != null && 
                            client.Character != null && !client.Character.IsDead)
                        {
                            modifiedMessage = ApplyChatMsgDistanceEffects(message, (ChatMessageType)type, senderCharacter, client.Character);

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
                  
                chatMsg.NetStateID = client.chatMsgQueue.Count > 0 ? 
                    client.chatMsgQueue.Last().NetStateID + 1 : 
                    client.lastRecvChatMsgID+1;

                client.chatMsgQueue.Add(chatMsg);
                client.lastChatMsgQueueID = chatMsg.NetStateID;
            }

            string myReceivedMessage = message;

            if (gameStarted && myCharacter != null)
            {
                myReceivedMessage = ApplyChatMsgDistanceEffects(message, (ChatMessageType)type, senderCharacter, myCharacter);
            }

            if (!string.IsNullOrWhiteSpace(myReceivedMessage) && 
                (targetClient == null || senderClient == null))
            {
                AddChatMessage(myReceivedMessage, (ChatMessageType)type, senderName, senderCharacter); 
            }       
        }

        private string ApplyChatMsgDistanceEffects(string message, ChatMessageType type, Character sender, Character receiver)
        {
            if (sender == null) return "";

            switch (type)
            {
                case ChatMessageType.Default:
                    if (!receiver.IsDead)
                    {
                        return ChatMessage.ApplyDistanceEffect(receiver, sender, message, ChatMessage.SpeakRange);
                    }
                    break;
                case ChatMessageType.Radio:
                    if (!receiver.IsDead)
                    {
                        var receiverItem = receiver.Inventory.Items.First(i => i != null && i.GetComponent<WifiComponent>() != null);
                        //client doesn't have a radio -> don't send
                        if (receiverItem == null) return "";

                        var senderItem = sender.Inventory.Items.First(i => i != null && i.GetComponent<WifiComponent>() != null);
                        if (senderItem == null) return "";

                        var receiverRadio   = receiverItem.GetComponent<WifiComponent>();
                        var senderRadio     = senderItem.GetComponent<WifiComponent>();

                        if (!receiverRadio.CanReceive(senderRadio)) return "";

                        return ChatMessage.ApplyDistanceEffect(receiverItem, senderItem, message, senderRadio.Range);
                    }
                    break;
            }

            return message;
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

            int clientListHeight = connectedClients.Count * 40;
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
            SaveSettings();

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
