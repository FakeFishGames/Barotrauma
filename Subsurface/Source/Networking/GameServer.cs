
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using RestSharp;
using Barotrauma.Networking.ReliableMessages;
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
            this.password = password;
            
            config = new NetPeerConfiguration("barotrauma");

            netStats = new NetStats();

#if DEBUG
            config.SimulatedLoss = 0.05f;
            config.SimulatedRandomLatency = 0.3f;
            config.SimulatedDuplicatesChance = 0.05f;
            config.SimulatedMinimumLatency = 0.1f;
#endif 
            config.Port = port;
            Port = port;

            if (attemptUPnP)
            {
                config.EnableUPnP = true;
            }

            config.MaximumConnections = maxPlayers;

            config.DisableMessageType(NetIncomingMessageType.DebugMessage | 
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);
                                    
            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            //----------------------------------------

            var endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170, 20, 150, 20), "End round", Alignment.TopLeft, GUI.Style, inGameHUD);
            endRoundButton.OnClicked = EndButtonHit;

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
                }
                return true;
            };

            banList = new BanList();

            LoadSettings();

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
            RestResponse response = (RestResponse)restClient.Execute(request);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                DebugConsole.ThrowError("Error while connecting to master server (" +response.StatusCode+": "+response.StatusDescription+")");
                return;
            }

            if (response != null && !string.IsNullOrWhiteSpace(response.Content))
            {
                DebugConsole.ThrowError("Error while connecting to master server (" +response.Content+")");
                return;
            }

            registeredToMaster = true;
            refreshMasterTimer = DateTime.Now + refreshMasterInterval;
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
            
            if (gameStarted)
            {
                inGameHUD.Update((float)Physics.step);

                bool isCrewDead =  
                    connectedClients.Find(c => c.Character != null && !c.Character.IsDead)==null &&
                   (myCharacter == null || myCharacter.IsDead);

                //restart if all characters are dead or submarine is at the end of the level
                if ((autoRestart && isCrewDead) 
                    || 
                    (endRoundAtLevelEnd && Submarine.Loaded!=null && Submarine.Loaded.AtEndPosition))
                {
                    if (AutoRestart && isCrewDead)
                    {
                        Log("Ending round (entire crew dead)", Color.Cyan);
                    }
                    else
                    {
                        Log("Ending round (submarine reached the end of the level)", Color.Cyan);
                    }
                    
                    EndButtonHit(null, null);                    
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
                if (c.FileStreamSender != null) UpdateFileTransfer(c, deltaTime);                

                c.ReliableChannel.Update(deltaTime);
            }

            NetIncomingMessage inc = null; 
            while ((inc = server.ReadMessage()) != null)
            {
                try
                {
                    ReadMessage(inc);
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
                if (gameStarted)
                {
                    if (myCharacter != null && !myCharacter.IsDead) new NetworkEvent(NetworkEventType.EntityUpdate, myCharacter.ID, false);

                    foreach (Character c in Character.CharacterList)
                    {
                        if (!(c is AICharacter) || c.IsDead) continue;

                        Vector2 diff = c.WorldPosition-Submarine.Loaded.WorldPosition;

                        if (FarseerPhysics.ConvertUnits.ToSimUnits(diff.Length()) > NetConfig.CharacterIgnoreDistance) continue;

                        new NetworkEvent(NetworkEventType.EntityUpdate, c.ID, false);
                    }
                }

                if (server.ConnectionsCount > 0)
                {
                    if (sparseUpdateTimer < DateTime.Now) SparseUpdate();

                    SendNetworkEvents();
                }

                updateTimer = DateTime.Now + updateInterval;
            }

            if (!registeredToMaster || refreshMasterTimer >= DateTime.Now) return;

            CoroutineManager.StartCoroutine(RefreshMaster());
            refreshMasterTimer = DateTime.Now + refreshMasterInterval;
        }

        private void SparseUpdate()
        {
            if (gameStarted) new NetworkEvent(Submarine.Loaded.ID, false);

            foreach (Character c in Character.CharacterList)
            {
                if (c.IsDead) continue;

                if (c is AICharacter)
                {
                    Vector2 diff = c.WorldPosition - Submarine.Loaded.WorldPosition;

                    if (FarseerPhysics.ConvertUnits.ToSimUnits(diff.Length()) > NetConfig.CharacterIgnoreDistance) continue;
                }

                new NetworkEvent(NetworkEventType.ImportantEntityUpdate, c.ID, false);
            }

            sparseUpdateTimer = DateTime.Now + sparseUpdateInterval;
        }

        private void ReadMessage(NetIncomingMessage inc)
        {
            switch (inc.MessageType)
            {
                case NetIncomingMessageType.ConnectionApproval:
                    HandleConnectionApproval(inc);
                    break;
                case NetIncomingMessageType.StatusChanged:
                    Debug.WriteLine(inc.SenderConnection + " status changed. " + (NetConnectionStatus)inc.SenderConnection.Status);
                    if (inc.SenderConnection.Status == NetConnectionStatus.Connected)
                    {
                        Client sender = connectedClients.Find(x => x.Connection == inc.SenderConnection);

                        if (sender == null) break;

                        if (sender.version != GameMain.Version.ToString())
                        {
                            DisconnectClient(sender, sender.name+" was unable to connect to the server (nonmatching game version)", 
                                "Version " + GameMain.Version + " required to connect to the server (Your version: " + sender.version + ")");
                        }
                        else if (connectedClients.Find(x => x.name == sender.name && x != sender)!=null)
                        {
                            DisconnectClient(sender, sender.name + " was unable to connect to the server (name already in use)",
                                "The name ''"+sender.name+"'' is already in use. Please choose another name.");
                        }
                        else
                        {
                            //AssignJobs();

                            GameMain.NetLobbyScreen.AddPlayer(sender.name);

                            // Notify the client that they have logged in
                            var outmsg = server.CreateMessage();

                            outmsg.Write((byte)PacketTypes.LoggedIn);
                            outmsg.Write(sender.ID);
                            outmsg.Write(gameStarted);
                            outmsg.Write(gameStarted && sender.Character != null);
                            outmsg.Write(allowSpectating);

                            //notify the client about other clients already logged in
                            outmsg.Write((byte)((characterInfo == null) ? connectedClients.Count - 1 : connectedClients.Count));
                            foreach (Client c in connectedClients)
                            {
                                if (c.Connection == inc.SenderConnection) continue;
                                outmsg.Write(c.name);
                                outmsg.Write(c.ID);
                            }

                            if (characterInfo != null)
                            {
                                outmsg.Write(characterInfo.Name);
                                outmsg.Write((byte)0);
                            }

                            var subs = GameMain.NetLobbyScreen.GetSubList();
                            outmsg.Write((byte)subs.Count);
                            foreach (Submarine sub in subs)
                            {
                                outmsg.Write(sub.Name);
                                outmsg.Write(sub.MD5Hash.Hash);
                            }

                            server.SendMessage(outmsg, inc.SenderConnection, NetDeliveryMethod.ReliableUnordered, 0);
                            
                            //notify other clients about the new client
                            outmsg = server.CreateMessage();
                            outmsg.Write((byte)PacketTypes.PlayerJoined);
                            outmsg.Write(sender.name);
                            outmsg.Write(sender.ID);
                        
                            //send the message to everyone except the client who just logged in
                            SendMessage(outmsg, NetDeliveryMethod.ReliableUnordered, inc.SenderConnection);

                            AddChatMessage(sender.name + " has joined the server", ChatMessageType.Server);
                        }
                    }
                    else if (inc.SenderConnection.Status == NetConnectionStatus.Disconnected)
                    {
                        var connectedClient = connectedClients.Find(c => c.Connection == inc.SenderConnection);
                        if (connectedClient != null && !disconnectedClients.Contains(connectedClient))
                        {
                            connectedClient.deleteDisconnectedTimer = NetConfig.DeleteDisconnectedTime;
                            disconnectedClients.Add(connectedClient);
                        }

                        DisconnectClient(inc.SenderConnection, 
                            connectedClient != null ? connectedClient.name+" has disconnected" : "");
                    }
                    
                    break;
                case NetIncomingMessageType.Data:

                    Client dataSender = connectedClients.Find(c => c.Connection == inc.SenderConnection);
                    if (dataSender == null) return;

                    byte packetType = inc.ReadByte();

                    if (packetType == (byte)PacketTypes.ReliableMessage)
                    {
                        if (!dataSender.ReliableChannel.CheckMessage(inc)) return;
                        packetType = inc.ReadByte();
                    }

                    switch (packetType)
                    {
                        case (byte)PacketTypes.NetworkEvent:
                            if (!gameStarted) break;
                            NetworkEvent.ReadMessage(inc, true);

                            break;
                        case (byte)PacketTypes.Chatmessage:
                            //SendChatMessage(ChatMessage.ReadNetworkMessage(inc));
                            //!!!!!!!!!!!

                            ReadChatMessage(inc);

                            break;
                        case (byte)PacketTypes.PlayerLeft:
                            DisconnectClient(inc.SenderConnection);
                            break;
                        case (byte)PacketTypes.StartGame:
                            dataSender.ReadyToStart = true;
                            break;
                        case (byte)PacketTypes.CharacterInfo:
                            ReadCharacterData(inc);
                            break;
                        case (byte)PacketTypes.RequestFile:
                            
                            if (!allowFileTransfers)
                            {
                                SendCancelTransferMessage(dataSender, "File transfers have been disabled by the server.");
                                break;
                            }

                            byte fileType = inc.ReadByte();
                            string fileName = fileType == (byte)FileTransferMessageType.Cancel ? "" : inc.ReadString();

                            switch (fileType)
                            {
                                case (byte)FileTransferMessageType.Submarine:
                                    
                                    var requestedSubmarine = Submarine.SavedSubmarines.Find(s => s.Name == fileName);

                                    if (requestedSubmarine==null)
                                    {
                                        //todo: ei voi ladata
                                    }
                                    else
                                    {
                                        if (dataSender.FileStreamSender != null) dataSender.FileStreamSender.CancelTransfer();

                                        var fileStreamSender = FileStreamSender.Create(dataSender.Connection, requestedSubmarine.FilePath, FileTransferMessageType.Submarine);
                                        if (fileStreamSender != null) dataSender.FileStreamSender = fileStreamSender;
                                    }
                                    break;
                                case (byte)FileTransferMessageType.Cancel:
                                    if (dataSender.FileStreamSender != null)
                                    {
                                        dataSender.FileStreamSender.CancelTransfer();
                                    }
                                    break;
                                default:
                                    DebugConsole.ThrowError("Unknown file type was requested ("+fileType+")");
                                    break;
                            }


                            break;
                        case (byte)PacketTypes.ResendRequest:
                            
                            dataSender.ReliableChannel.HandleResendRequest(inc);
                            break;
                        case (byte)PacketTypes.LatestMessageID:
                            dataSender.ReliableChannel.HandleLatestMessageID(inc);
                            break;
                        case (byte)PacketTypes.Vote:
                            Voting.RegisterVote(inc, connectedClients);

                            if (Voting.AllowEndVoting && EndVoteMax > 0 &&
                                ((float)EndVoteCount / (float)EndVoteMax) >= EndVoteRequiredRatio)
                            {
                                Log("Ending round by votes (" + EndVoteCount + "/" + (EndVoteMax - EndVoteCount) + ")", Color.Cyan);
                                EndButtonHit(null, null);
                            }
                            break;
                        case (byte)PacketTypes.RequestNetLobbyUpdate:
                            UpdateNetLobby(null, null);
                            UpdateVoteStatus();
                            break;
                        case (byte)PacketTypes.SpectateRequest:
                            if (gameStarted && allowSpectating)
                            {
                                var startMessage = CreateStartMessage(roundStartSeed, Submarine.Loaded, GameMain.GameSession.gameMode.Preset);
                                server.SendMessage(startMessage, inc.SenderConnection, NetDeliveryMethod.ReliableUnordered);

                                dataSender.Spectating = true;
                                CoroutineManager.StartCoroutine(SyncSpectator(dataSender));
                            }
                            break;
                    }
                    break;
                case NetIncomingMessageType.WarningMessage:
                    Debug.WriteLine(inc.ReadString());
                    break;
            }
        }

        private void HandleConnectionApproval(NetIncomingMessage inc)
        {
            if ((PacketTypes)inc.ReadByte() != PacketTypes.Login) return;

            DebugConsole.NewMessage("New player has joined the server", Color.White);

            if (banList.IsBanned(inc.SenderEndPoint.Address.ToString()))
            {
                inc.SenderConnection.Deny("You have been banned from the server");
                DebugConsole.NewMessage("Banned player tried to join the server", Color.Red);
                return;                
            }
            
            if (connectedClients.Find(c => c.Connection == inc.SenderConnection)!=null)
            {
                inc.SenderConnection.Deny("Connection error - already joined");
                return;
            }

            byte userID;
            string userPassword = "", version = "", packageName = "", packageHash = "", name = "";
            try
            {
                userID = inc.ReadByte();
                userPassword = inc.ReadString();
                version = inc.ReadString();
                packageName = inc.ReadString();
                packageHash = inc.ReadString();
                name = inc.ReadString();
            }
            catch
            {
                inc.SenderConnection.Deny("Connection error - server failed to read your ConnectionApproval message");
                DebugConsole.NewMessage("Connection error - server failed to read the ConnectionApproval message", Color.Red);
                return;
            }

#if !DEBUG
            if (!string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(userPassword))
            {
                inc.SenderConnection.Deny("Password required!");
                DebugConsole.NewMessage(name + " couldn't join the server (no password)", Color.Red);
                return;
            }
            else if (userPassword != password)
            {
                inc.SenderConnection.Deny("Wrong password!");
                DebugConsole.NewMessage(name + " couldn't join the server (wrong password)", Color.Red);
                return;
            }
            else if (version != GameMain.Version.ToString())
            {
                inc.SenderConnection.Deny("Version " + GameMain.Version + " required to connect to the server (Your version: " + version + ")");
                DebugConsole.NewMessage(name + " couldn't join the server (wrong game version)", Color.Red);
                return;
            }
            else if (packageName != GameMain.SelectedPackage.Name)
            {
                inc.SenderConnection.Deny("Your content package (" + packageName + ") doesn't match the server's version (" + GameMain.SelectedPackage.Name + ")");
                DebugConsole.NewMessage(name + " couldn't join the server (wrong content package name)", Color.Red);
                return;
            }
            else if (packageHash != GameMain.SelectedPackage.MD5hash.Hash)
            {
                inc.SenderConnection.Deny("Your content package (MD5: " + packageHash + ") doesn't match the server's version (MD5: " + GameMain.SelectedPackage.MD5hash.Hash + ")");
                DebugConsole.NewMessage(name + " couldn't join the server (wrong content package hash)", Color.Red);
                return;
            }
            else if (connectedClients.Find(c => c.name.ToLower() == name.ToLower() && c.ID != userID) != null)
            {
                inc.SenderConnection.Deny("The name ''" + name + "'' is already in use. Please choose another name.");
                DebugConsole.NewMessage(name + " couldn't join the server (name already in use)", Color.Red);
                return;
            }

#endif

            //existing user re-joining
            if (userID > 0)
            {
                Client existingClient = connectedClients.Find(c => c.ID == userID);
                if (existingClient == null)
                {
                    existingClient = disconnectedClients.Find(c => c.ID == userID);
                    if (existingClient != null)
                    {
                        disconnectedClients.Remove(existingClient);
                        connectedClients.Add(existingClient);

                        UpdateCrewFrame();
                    }
                }
                if (existingClient != null)
                {
                    existingClient.Connection = inc.SenderConnection;
                    existingClient.ReliableChannel = new ReliableChannel(server);
                    inc.SenderConnection.Approve();
                    return;
                }
            }

            userID = 1;
            while (connectedClients.Any(c => c.ID == userID))
            {
                userID++;
            }

            Client newClient = new Client(server, name, userID);
            newClient.Connection = inc.SenderConnection;
            newClient.version = version;

            connectedClients.Add(newClient);

            UpdateCrewFrame();

            inc.SenderConnection.Approve();

            refreshMasterTimer = DateTime.Now;
        }


        private void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod deliveryMethod, NetConnection excludedConnection = null)
        {
            List<NetConnection> recipients = new List<NetConnection>();

            foreach (Client client in connectedClients)
            {
                if (client.Connection != excludedConnection) recipients.Add(client.Connection);                
            }

            if (recipients.Count == 0) return;

            server.SendMessage(msg, recipients, deliveryMethod, 0);  
            
        }

        private void SendNetworkEvents(List<Client> recipients = null)
        {
            if (NetworkEvent.Events.Count == 0) return;

            if (recipients == null)
            {
                recipients = connectedClients.FindAll(c => c.Character != null || c.Spectating);
            }

            if (recipients.Count == 0) return;

            foreach (Client c in recipients)
            {
                var message = ComposeNetworkEventMessage(NetworkEventDeliveryMethod.ReliableChannel, c.Connection);
                if (message != null)
                {
                    ReliableMessage reliableMessage = c.ReliableChannel.CreateMessage();
                    message.Position = 0;
                    reliableMessage.InnerMessage.Write(message.ReadBytes(message.LengthBytes));

                    c.ReliableChannel.SendMessage(reliableMessage, c.Connection);
                }

                message = ComposeNetworkEventMessage(NetworkEventDeliveryMethod.ReliableLidgren, c.Connection);
                if (message!=null)
                {
                    server.SendMessage(message, c.Connection, NetDeliveryMethod.ReliableUnordered);
                }

                message = ComposeNetworkEventMessage(NetworkEventDeliveryMethod.Unreliable, c.Connection);
                if (message != null)
                {
                    server.SendMessage(message, c.Connection, NetDeliveryMethod.Unreliable, 0);                        
                }                
            }

            NetworkEvent.Events.Clear();
        }

        private IEnumerable<object> SyncSpectator(Client sender)
        {
            yield return new WaitForSeconds(3.0f);

            //save all the current events to a list and clear them
            var existingEvents = new List<NetworkEvent>(NetworkEvent.Events);
            NetworkEvent.Events.Clear();

            foreach (Hull hull in Hull.hullList)
            {
                if (!hull.FireSources.Any() && hull.Volume < 0.01f) continue;
                new NetworkEvent(NetworkEventType.ImportantEntityUpdate, hull.ID, false);
            }

            foreach (Character c in Character.CharacterList)
            {
                new NetworkEvent(NetworkEventType.EntityUpdate, c.ID, false);
                if (c.Inventory != null) new NetworkEvent(NetworkEventType.InventoryUpdate, c.ID, false);
                if (c.IsDead) new NetworkEvent(NetworkEventType.KillCharacter, c.ID, false);
            }

            foreach (Item item in Item.ItemList)
            {
                for (int i = 0; i < item.components.Count; i++)
                {
                    if (!item.components[i].NetworkUpdateSent) continue;
                    item.NewComponentEvent(item.components[i], false, true);
                }

                if (item.body == null || !item.body.Enabled || item.ParentInventory!=null) continue;
                new NetworkEvent(NetworkEventType.DropItem, item.ID, false);
            }

            List<NetworkEvent> syncMessages = new List<NetworkEvent>(NetworkEvent.Events);
            while (syncMessages.Any())
            {
                //put 5 events in the message and send them to the spectator
                NetworkEvent.Events = syncMessages.GetRange(0, Math.Min(syncMessages.Count, 5));
                SendNetworkEvents(new List<Client>() { sender });
                syncMessages.RemoveRange(0, Math.Min(syncMessages.Count, 5));

                //restore "normal" events
                NetworkEvent.Events = existingEvents;

                yield return new WaitForSeconds(0.1f);

                //save "normal" events again
                existingEvents = new List<NetworkEvent>(NetworkEvent.Events);
            }
            
            yield return CoroutineStatus.Success;
        }


        public bool StartGameClicked(GUIButton button, object obj)
        {
            Submarine selectedSub = null;

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

            GameModePreset selectedMode = Voting.HighestVoted<GameModePreset>(VoteType.Mode, connectedClients);
            if (selectedMode == null) selectedMode = GameMain.NetLobbyScreen.SelectedMode;   

            if (selectedMode==null)
            {
                GameMain.NetLobbyScreen.ModeList.Flash();
                return false;
            }

            CoroutineManager.StartCoroutine(WaitForPlayersReady(selectedSub, selectedMode), "WaitForPlayersReady");
 
            return true;
        }

        private IEnumerable<object> WaitForPlayersReady(Submarine selectedSub, GameModePreset selectedMode)
        {
            GameMain.NetLobbyScreen.StartButton.Enabled = false;

            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.CanStartGame);
            msg.Write(selectedSub.Name);
            msg.Write(selectedSub.MD5Hash.Hash);

            SendMessage(msg, NetDeliveryMethod.ReliableUnordered);

            connectedClients.ForEach(c => c.ReadyToStart = false);

            float waitForResponseTimer = 5.0f;
            while (connectedClients.Any(c => !c.ReadyToStart) && waitForResponseTimer > 0.0f)
            {
                waitForResponseTimer -= CoroutineManager.UnscaledDeltaTime;
                yield return CoroutineStatus.Running;
            }

            float fileTransferTimeOut = 60.0f;
            while (connectedClients.Any(c => c.FileStreamSender != null && c.FileStreamSender.FilePath == selectedSub.FilePath) && fileTransferTimeOut>0.0f)
            {
                fileTransferTimeOut -= CoroutineManager.UnscaledDeltaTime;

                if (GUIMessageBox.MessageBoxes.Count==0)
                {
                    var messageBox = new GUIMessageBox("File transfer in progress",
                        "The round will be started after the submarine file has been sent to all players.", new string[] {"Cancel transfer"}, 400, 400);
                    messageBox.Buttons[0].UserData = connectedClients.Find(c => c.FileStreamSender != null && c.FileStreamSender.FilePath == selectedSub.FilePath);
                    messageBox.Buttons[0].OnClicked = (button, obj) =>
                        {
                            (button.UserData as Client).CancelTransfer();
                            return true;
                        };
                }
            }

            GameMain.ShowLoading(StartGame(selectedSub, selectedMode), false);

            yield return CoroutineStatus.Success;
        }
        
        private IEnumerable<object> StartGame(Submarine selectedSub, GameModePreset selectedMode)
        {
            GameMain.NetLobbyScreen.StartButton.Enabled = false;

            GUIMessageBox.CloseAll();

            AssignJobs();

            roundStartSeed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(roundStartSeed);
       
            GameMain.GameSession = new GameSession(selectedSub, "", selectedMode, Mission.MissionTypes[GameMain.NetLobbyScreen.MissionTypeIndex]);
            GameMain.GameSession.StartShift(GameMain.NetLobbyScreen.LevelSeed);

            GameServer.Log("Starting a new round...", Color.Cyan);
            GameServer.Log("Submarine: " + selectedSub.Name, Color.Cyan);
            GameServer.Log("Game mode: " + selectedMode.Name, Color.Cyan);
            GameServer.Log("Level seed: " + GameMain.NetLobbyScreen.LevelSeed, Color.Cyan);

            yield return CoroutineStatus.Running;

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

            if (characterInfo != null)
            {
                characterInfo.Job = new Job(GameMain.NetLobbyScreen.JobPreferences[0]);
                characterInfos.Add(characterInfo);
            }

            WayPoint[] assignedWayPoints = WayPoint.SelectCrewSpawnPoints(characterInfos);
            
            for (int i = 0; i < connectedClients.Count; i++)
            {
                connectedClients[i].Character = Character.Create(
                    connectedClients[i].characterInfo, assignedWayPoints[i].WorldPosition, true, false);
                connectedClients[i].Character.GiveJobItems(assignedWayPoints[i]);

                GameMain.GameSession.CrewManager.characters.Add(connectedClients[i].Character);
            }

            if (characterInfo != null)
            {
                myCharacter = Character.Create(characterInfo, assignedWayPoints[assignedWayPoints.Length - 1].WorldPosition, false, false);
                Character.Controlled = myCharacter;

                myCharacter.GiveJobItems(assignedWayPoints[assignedWayPoints.Length - 1]);

                GameMain.GameSession.CrewManager.characters.Add(myCharacter);
            }

            var startMessage = CreateStartMessage(roundStartSeed, Submarine.Loaded, GameMain.GameSession.gameMode.Preset);
            SendMessage(startMessage, NetDeliveryMethod.ReliableUnordered);


            yield return CoroutineStatus.Running;
            
            UpdateCrewFrame();

            if (TraitorsEnabled == YesNoMaybe.Yes ||
                (TraitorsEnabled == YesNoMaybe.Maybe && Rand.Range(0.0f, 1.0f) < 0.5f))
            {
                TraitorManager = new TraitorManager(this);
            }
            else
            {
                TraitorManager = null;
            }

            //give some time for the clients to load the map
            yield return new WaitForSeconds(2.0f);

            gameStarted = true;

            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            GameMain.GameScreen.Select();

            if (myCharacter == null)
            {
                AddChatMessage("Press TAB to chat. Use ''d;'' to talk to dead players and spectators, "
                    + "and ''player name;'' to only send the message to a specific player.", ChatMessageType.Server);
            }
            else
            {
                AddChatMessage("Press TAB to chat. Use ''r;'' to talk through the radio.", ChatMessageType.Server);
            }

            GameMain.NetLobbyScreen.StartButton.Enabled = true;
            
            yield return CoroutineStatus.Success;
        }

        private NetOutgoingMessage CreateStartMessage(int seed, Submarine selectedSub, GameModePreset selectedMode)
        {
            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.StartGame);

            msg.Write(seed);

            msg.Write(GameMain.NetLobbyScreen.LevelSeed);

            msg.Write((byte)GameMain.NetLobbyScreen.MissionTypeIndex);

            msg.Write(selectedSub.Name);
            msg.Write(selectedSub.MD5Hash.Hash);

            msg.Write(selectedMode.Name);

            //msg.Write(GameMain.NetLobbyScreen.GameDuration.TotalMinutes);

            List<Client> playingClients = connectedClients.FindAll(c => c.Character != null);

            msg.Write((myCharacter == null) ? (byte)playingClients.Count : (byte)(playingClients.Count + 1));
            foreach (Client client in playingClients)
            {
                msg.Write(client.ID);
                WriteCharacterData(msg, client.Character.Name, client.Character);
            }

            if (myCharacter != null)
            {
                msg.Write((byte)0);
                WriteCharacterData(msg, myCharacter.Info.Name, myCharacter);
            }

            return msg;
        }

        private bool EndButtonHit(GUIButton button, object obj)
        {
            if (!gameStarted) return false;

            string endMessage = "The round has ended." + '\n';

            if (TraitorManager != null)
            {
                endMessage += TraitorManager.GetEndMessage();
            }

            GameMain.GameSession.gameMode.End(endMessage);

            if (autoRestart) AutoRestartTimer = 20.0f;

            if (saveServerLogs) log.Save();

            return true;
        }

        public IEnumerable<object> EndGame(string endMessage)
        {
            Character.Controlled = null;
            myCharacter = null;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;

            gameStarted = false;

            if (connectedClients.Count > 0)
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write((byte)PacketTypes.EndGame);
                msg.Write(endMessage);

                if (server.ConnectionsCount > 0)
                {
                    server.SendMessage(msg, server.Connections, NetDeliveryMethod.ReliableOrdered, 0);
                }

                foreach (Client client in connectedClients)
                {
                    client.Spectating = false;
                    client.Character = null;
                    client.inGame = false;
                }
            }

            float endPreviewLength = 10.0f;

            var cinematic = new TransitionCinematic(Submarine.Loaded, GameMain.GameScreen.Cam, endPreviewLength);
            
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
            }

            if (string.IsNullOrWhiteSpace(msg)) msg = client.name + " has left the server";
            if (string.IsNullOrWhiteSpace(targetmsg)) targetmsg = "You have left the server";

            Log(msg, ChatMessage.MessageColor[(int)ChatMessageType.Server]);
            
            NetOutgoingMessage outmsg = server.CreateMessage();
            outmsg.Write((byte)PacketTypes.KickedOut);
            outmsg.Write(targetmsg);
            server.SendMessage(outmsg, client.Connection, NetDeliveryMethod.ReliableUnordered, 0);

            connectedClients.Remove(client);

            outmsg = server.CreateMessage();
            outmsg.Write((byte)PacketTypes.PlayerLeft);
            outmsg.Write(client.ID);
            outmsg.Write(msg);

            GameMain.NetLobbyScreen.RemovePlayer(client.name);

            if (server.Connections.Count > 0)
            {
                server.SendMessage(outmsg, server.Connections, NetDeliveryMethod.ReliableUnordered, 0);
            }

            if (client.FileStreamSender != null)
            {
                client.FileStreamSender.Dispose();
                client.FileStreamSender = null;
            }

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

        public void KickPlayer(string playerName, bool ban = false)
        {
            playerName = playerName.ToLowerInvariant();

            Client client = connectedClients.Find(c => c.name.ToLowerInvariant() == playerName ||
                    (c.Character != null && c.Character.Name.ToLowerInvariant() == playerName));

            if (client == null) return;

            KickClient(client, ban);
        }

        private void KickClient(Client client, bool ban = false)
        {
            if (client == null) return;

            if (ban)
            {
                DisconnectClient(client, client.name + " has been banned from the server", "You have been banned from the server");
                banList.BanPlayer(client.name, client.Connection.RemoteEndPoint.Address.ToString());
            }
            else
            {
                DisconnectClient(client, client.name + " has been kicked from the server", "You have been kicked from the server");
            }
        }

        private void UpdateFileTransfer(Client client, float deltaTime)
        {
            if (client.FileStreamSender == null) return;

            var clientNameBox = GameMain.NetLobbyScreen.PlayerList.FindChild(client.name);
            var clientInfo = clientNameBox.FindChild(client.FileStreamSender);

            if (clientInfo == null)
            {
                clientNameBox.ClearChildren();

                clientInfo = new GUIFrame(new Rectangle(0, 0, 180, 0), Color.Transparent, Alignment.TopRight, null, clientNameBox);
                clientInfo.UserData = client.FileStreamSender;
                new GUIProgressBar(new Rectangle(0, 4, 160, clientInfo.Rect.Height - 8), Color.Green, GUI.Style, 0.0f, Alignment.Left, clientInfo).IsHorizontal = true;
                new GUITextBlock(new Rectangle(0, 2, 160, 0), "", GUI.Style, Alignment.TopLeft, Alignment.Left | Alignment.CenterY, clientInfo, true, GUI.SmallFont);

                var cancelButton = new GUIButton(new Rectangle(20, 0, 14, 0), "X", Alignment.Right, GUI.Style, clientInfo);
                cancelButton.OnClicked = (GUIButton button, object userdata) =>
                {
                    (cancelButton.Parent.UserData as FileStreamSender).CancelTransfer();
                    return true;
                };
            }
            else
            {
                var progressBar = clientInfo.GetChild<GUIProgressBar>();
                progressBar.BarSize = client.FileStreamSender.Progress;

                var progressText = clientInfo.GetChild<GUITextBlock>();
                progressText.Text = client.FileStreamSender.FileName + "  " +
                    MathUtils.GetBytesReadable(client.FileStreamSender.Sent) + " / " + MathUtils.GetBytesReadable(client.FileStreamSender.FileSize);
            }

            client.FileStreamSender.Update(deltaTime);

            if (client.FileStreamSender.Status != FileTransferStatus.Sending &&
                client.FileStreamSender.Status != FileTransferStatus.NotStarted)
            {
                if (client.FileStreamSender.Status == FileTransferStatus.Canceled)
                {
                    SendCancelTransferMessage(client, "File transfer was canceled by the server.");
                }

                clientNameBox.RemoveChild(clientInfo);

                client.FileStreamSender.Dispose();
                client.FileStreamSender = null;
            }
        }

        private void SendCancelTransferMessage(Client client, string message)
        {
            var outmsg = server.CreateMessage();
            outmsg.Write((byte)PacketTypes.RequestFile);
            outmsg.Write(false);
            outmsg.Write(message);
            server.SendMessage(outmsg, client.Connection, NetDeliveryMethod.ReliableUnordered);
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

            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.Traitor);
            msg.Write(target.Info.Name);
            if (server.Connections.Count > 0)
            {
                server.SendMessage(msg, traitorClient.Connection, NetDeliveryMethod.ReliableUnordered, 0);
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
                    spriteBatch.DrawString(GUI.SmallFont, c.name + ":", new Vector2(x + 10, y), clientColor);
                    spriteBatch.DrawString(GUI.SmallFont, "Ping: " + (int)(c.Connection.AverageRoundtripTime * 1000.0f) + " ms", new Vector2(x + width - 100, y), clientColor);
                }
                if (y + 10 >= startY && y < startY + height - 130) spriteBatch.DrawString(GUI.SmallFont, "Resent messages: " + c.Connection.Statistics.ResentMessages, new Vector2(x + 10, y + 10), clientColor);

                resentMessages += (int)c.Connection.Statistics.ResentMessages;

                y += 40;
            }

            clientListScrollBar.Update(1.0f / 60.0f);
            clientListScrollBar.Draw(spriteBatch);

            netStats.AddValue(NetStats.NetStatType.ResentMessages, Math.Max(resentMessages, 0));
            netStats.AddValue(NetStats.NetStatType.SentBytes, server.Statistics.SentBytes);
            netStats.AddValue(NetStats.NetStatType.ReceivedBytes, server.Statistics.ReceivedBytes);

            netStats.Draw(spriteBatch, new Rectangle(200,0,800,200));

        }

        public void UpdateVoteStatus()
        {
            if (server.Connections.Count == 0) return;

            var clientsToKick = connectedClients.FindAll(c => c.KickVoteCount > connectedClients.Count * KickVoteRequiredRatio);
            clientsToKick.ForEach(c => KickClient(c));

            try
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write((byte)PacketTypes.VoteStatus);
                Voting.WriteData(msg, connectedClients);

                server.SendMessage(msg, server.Connections, NetDeliveryMethod.ReliableUnordered, 0); 
            }
            catch (Exception e)
            {
#if DEBUG   
                DebugConsole.ThrowError("Failed to update vote status", e);
#endif
            }

        }

        public bool UpdateNetLobby(object obj)
        {
            return UpdateNetLobby(null, obj);
        }

        public bool UpdateNetLobby(GUIComponent component, object obj)
        {
            if (server.Connections.Count == 0) return true;

            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.UpdateNetLobby);
            GameMain.NetLobbyScreen.WriteData(msg);

            server.SendMessage(msg, server.Connections, NetDeliveryMethod.ReliableUnordered, 0);            

            return true;
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

                var kickButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Kick", Alignment.BottomLeft, GUI.Style, characterFrame);
                kickButton.UserData = character.Name;
                kickButton.OnClicked += GameMain.NetLobbyScreen.KickPlayer;
            }

            return true;
        }

        private void ReadChatMessage(NetIncomingMessage inc)
        {
            ChatMessage message = ChatMessage.ReadNetworkMessage(inc);

            List<Client> recipients = new List<Client>();

            foreach (Client c in connectedClients)
            {
                switch (message.Type)
                {
                    case ChatMessageType.Dead:
                        if (c.Character != null && !c.Character.IsDead) continue;
                        break;
                    case ChatMessageType.Default:
                        if (message.Sender != null && c.Character != null && message.Sender != c.Character)
                        {
                            if (Vector2.Distance(message.Sender.WorldPosition, c.Character.WorldPosition) > ChatMessage.SpeakRange) continue;
                        }
                        break;
                    case ChatMessageType.Radio:
                        if (message.Sender == null) return;
                        var radio = message.Sender.Inventory.Items.First(i => i != null && i.GetComponent<WifiComponent>() != null);
                        if (radio == null) message.Type = ChatMessageType.Default;
                        break;
                }

                recipients.Add(c);
            }

            AddChatMessage(message);

            foreach (Client c in recipients)
            {
                ReliableMessage msg = c.ReliableChannel.CreateMessage();
                msg.InnerMessage.Write((byte)PacketTypes.Chatmessage);
                //msg.InnerMessage.Write((byte)type);
                //msg.InnerMessage.Write(message);  

                message.WriteNetworkMessage(msg.InnerMessage);

                c.ReliableChannel.SendMessage(msg, c.Connection);
            }   
        }

        public override void SendChatMessage(string message, ChatMessageType? type = null)
        {
            List<Client> recipients = new List<Client>();
            Client targetClient = null;

            if (type == null)
            {
                type = gameStarted && myCharacter != null ? ChatMessageType.Default : ChatMessageType.Server;
            }

            string command = ChatMessage.GetChatMessageCommand(message, out message).ToLowerInvariant();
                
            if (command=="dead" || command=="d")
            {
                type = ChatMessageType.Dead;
            }
            else if (command=="radio" || command=="r")
            {
                if (CanUseRadio(Character.Controlled)) type = ChatMessageType.Radio;
            }
            else if (command != "")
            {
                targetClient = connectedClients.Find(c =>
                    command == c.name.ToLowerInvariant() ||
                    (c.Character != null && command == c.Character.Name.ToLowerInvariant()));

                if (targetClient == null)
                {
                    AddChatMessage("Player ''" + command + "'' not found!", ChatMessageType.Error);
                    return;
                }
            }

            if (targetClient != null)
            {
                recipients.Add(targetClient);
            }
            else
            {
                foreach (Client c in connectedClients)
                {
                    if (type != ChatMessageType.Dead || (c.Character == null || c.Character.IsDead)) recipients.Add(c);
                }
            }

            var chatMessage = ChatMessage.Create(
                gameStarted && myCharacter != null ? myCharacter.Name : name,
                message, (ChatMessageType)type, gameStarted ? myCharacter : null);

            AddChatMessage(chatMessage);

            if (!server.Connections.Any()) return;

            SendChatMessage(chatMessage, recipients);
        }

        public void SendChatMessage(ChatMessage chatMessage, List<Client> recipients)
        {
            foreach (Client c in recipients)
            {
                ReliableMessage msg = c.ReliableChannel.CreateMessage();
                msg.InnerMessage.Write((byte)PacketTypes.Chatmessage);
                //msg.InnerMessage.Write((byte)type);
                //msg.InnerMessage.Write(message);  

                chatMessage.WriteNetworkMessage(msg.InnerMessage);

                c.ReliableChannel.SendMessage(msg, c.Connection);
            }  
        }

        private void ReadCharacterData(NetIncomingMessage message)
        {
            string name = "";
            Gender gender = Gender.Male;
            int headSpriteId = 0;

            try
            {
                name            = message.ReadString();
                gender          = message.ReadBoolean() ? Gender.Male : Gender.Female;
                headSpriteId    = message.ReadByte();
            }
            catch
            {
                name = "";
                gender = Gender.Male;
                headSpriteId = 0;
            }


            List<JobPrefab> jobPreferences = new List<JobPrefab>();
            int count = message.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                string jobName = message.ReadString();
                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);
                if (jobPrefab != null) jobPreferences.Add(jobPrefab);
            }

            foreach (Client c in connectedClients)
            {
                if (c.Connection != message.SenderConnection) continue;

                c.characterInfo = new CharacterInfo(Character.HumanConfigFile, name, gender);
                c.characterInfo.HeadSpriteId = headSpriteId;
                c.jobPreferences = jobPreferences;
                break;
            }
        }

        private void WriteCharacterData(NetOutgoingMessage message, string name, Character character)
        {
            message.Write(name);
            message.Write(character.ID);
            message.Write(character.Info.Gender == Gender.Female);

            message.Write((byte)character.Info.HeadSpriteId);

            message.Write(character.WorldPosition.X);
            message.Write(character.WorldPosition.Y);

            message.Write(character.Info.Job.Name);
        }

        public void SendCharacterSpawnMessage(Character character)
        {
            NetOutgoingMessage message = server.CreateMessage();
            message.Write((byte)PacketTypes.NewCharacter);

            message.Write(character.ConfigPath);

            message.Write(character.ID);

            message.Write(character.Position.X);
            message.Write(character.Position.Y);
            
            SendMessage(message, NetDeliveryMethod.ReliableUnordered);
        }

        public void SendItemSpawnMessage(List<Item> items, List<Inventory> inventories = null)
        {
            if (items == null || !items.Any()) return;

            NetOutgoingMessage message = server.CreateMessage();
            message.Write((byte)PacketTypes.NewItem);

            Item.Spawner.FillNetworkData(message, items, inventories);

            SendMessage(message, NetDeliveryMethod.ReliableUnordered);
        }

        public void SendItemRemoveMessage(List<Item> items)
        {
            if (items == null || !items.Any()) return;

            NetOutgoingMessage message = server.CreateMessage();

            Item.Remover.FillNetworkData(message, items);

            SendMessage(message, NetDeliveryMethod.ReliableUnordered);
        }

        private void AssignJobs()
        {
            List<Client> unassigned = new List<Client>(connectedClients);
            
            int[] assignedClientCount = new int[JobPrefab.List.Count];

            if (characterInfo!=null)
            {
                assignedClientCount[JobPrefab.List.FindIndex(jp => jp == GameMain.NetLobbyScreen.JobPreferences[0])]=1;
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
            if (GameMain.Server == null || !GameMain.Server.saveServerLogs) return;

            GameMain.Server.log.WriteLine(line, color);
        }

        /// <summary>
        /// sends some random data to the clients
        /// use for debugging purposes
        /// </summary>
        public void SendRandomData()
        {
            NetOutgoingMessage msg = server.CreateMessage();
            switch (Rand.Int(5))
            {
                case 0:
                    msg.Write((byte)PacketTypes.NetworkEvent);
                    msg.Write((byte)Rand.Int(Enum.GetNames(typeof(NetworkEventType)).Length));
                    msg.Write((ushort)Rand.Int(MapEntity.mapEntityList.Count));
                    break;
                case 1:
                    msg.Write((byte)PacketTypes.NetworkEvent);
                    msg.Write((byte)NetworkEventType.ComponentUpdate);
                    msg.Write((int)Item.ItemList[Rand.Int(Item.ItemList.Count)].ID);
                    msg.Write(Rand.Int(8));
                    break;
                case 2:
                    msg.Write((byte)Enum.GetNames(typeof(PacketTypes)).Length);
                    break;
                case 3:
                    msg.Write((byte)PacketTypes.UpdateNetLobby);
                    break;
            }

            int bitCount = Rand.Int(100);
            for (int i = 0; i < bitCount; i++)
            {
                msg.Write(Rand.Int(2) == 0);
            }
            SendMessage(msg, (Rand.Int(2) == 0) ? NetDeliveryMethod.ReliableOrdered : NetDeliveryMethod.Unreliable, null);

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

            if (saveServerLogs)
            {
                Log("Shutting down server...", Color.Cyan);
                log.Save();
            }

            foreach (Client client in connectedClients)
            {
                if (client.FileStreamSender != null) client.FileStreamSender.Dispose();
            }

            server.Shutdown("The server has been shut down");
        }
    }

    class Client
    {
        public string name;
        public byte ID;

        public Character Character;
        public CharacterInfo characterInfo;
        public NetConnection Connection { get; set; }
        public string version;
        public bool inGame;



        private List<Client> kickVoters;

        public bool ReadyToStart;

        private object[] votes;

        public List<JobPrefab> jobPreferences;
        public JobPrefab assignedJob;

        public FileStreamSender FileStreamSender;

        public bool Spectating;

        public ReliableChannel ReliableChannel;

        public float deleteDisconnectedTimer;

        public int KickVoteCount
        {
            get { return kickVoters.Count; }
        }


        public Client(NetPeer server, string name, byte ID)
            : this(name, ID)
        {
            ReliableChannel = new ReliableChannel(server);
        }

        public Client(string name, byte ID)
        {
            this.name = name;
            this.ID = ID;

            kickVoters = new List<Client>();

            votes = new object[Enum.GetNames(typeof(VoteType)).Length];
            
            jobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0,3));
        }

        public T GetVote<T>(VoteType voteType)
        {
            return (votes[(int)voteType] is T) ? (T)votes[(int)voteType] : default(T);
        }

        public void SetVote(VoteType voteType, object value)
        {
            votes[(int)voteType] = value;
        }

        public void ResetVotes()
        {
            for (int i = 0; i<votes.Length; i++)
            {
                votes[i] = null;
            }
        }

        public void AddKickVote(Client voter)
        {
            if (!kickVoters.Contains(voter)) kickVoters.Add(voter);
        }


        public void RemoveKickVote(Client voter)
        {
            kickVoters.Remove(voter);
        }
        
        public bool HasKickVoteFromID(int id)
        {
            return kickVoters.Any(k => k.ID == id);
        }


        public static void UpdateKickVotes(List<Client> connectedClients)
        {
            foreach (Client client in connectedClients)
            {
                client.kickVoters.RemoveAll(voter => !connectedClients.Contains(voter));
            }
        }

        public void CancelTransfer()
        {
            if (FileStreamSender == null) return;

            FileStreamSender.CancelTransfer();
            FileStreamSender.Dispose();

            FileStreamSender = null;
        }
    }
}
