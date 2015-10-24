
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using RestSharp;
using Barotrauma.Networking.ReliableMessages;

namespace Barotrauma.Networking
{
    class GameServer : NetworkMember
    {
        public bool ShowNetStats;

        public List<Client> connectedClients = new List<Client>();

        //for keeping track of disconnected clients in case the reconnect shortly after
        private List<Client> disconnectedClients = new List<Client>();

        private NetStats netStats;

        //is the server running
        bool started;

        private NetServer server;
        private NetPeerConfiguration config;
        
        private TimeSpan sparseUpdateInterval = new TimeSpan(0, 0, 0, 3);
        private DateTime sparseUpdateTimer;

        private TimeSpan refreshMasterInterval = new TimeSpan(0, 0, 40);
        private DateTime refreshMasterTimer;

        private BanList banList;
        
        private bool masterServerResponded;

        private bool registeredToMaster;

        private string password;

        private bool autoRestart;

        public bool AutoRestart
        {
            get { return (connectedClients.Count==0) ? false : autoRestart; }
            set
            {
                autoRestart = value;

                AutoRestartTimer = autoRestart ? 20.0f : 0.0f;
            }
        }
        public float AutoRestartTimer;

        public BanList BanList
        {
            get { return banList; }
        }

        public GameServer(string name, int port, bool isPublic = false, string password = "", bool attemptUPnP = false, int maxPlayers = 10)
        {
            var endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170, 20, 150, 25), "End round", Alignment.TopLeft, GUI.Style, inGameHUD);
            endRoundButton.OnClicked = EndButtonHit;

            banList = new BanList();

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
            
            CoroutineManager.StartCoroutine(StartServer(isPublic));
        }

        private IEnumerable<object> StartServer(bool isPublic)
        {
            try
            {
                server = new NetServer(config);
                server.Start();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't start the server", e);
            }
         

            if (config.EnableUPnP)
            {
                server.UPnP.ForwardPort(config.Port, "barotrauma");

                GUIMessageBox upnpBox = new GUIMessageBox("Please wait...", "Attempting UPnP port forwarding", new string[] {"Cancel"} );
                upnpBox.Buttons[0].OnClicked = upnpBox.Close;

                DateTime upnpTimeout = DateTime.Now + new TimeSpan(0,0,5);
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
            var client = new RestClient(NetConfig.MasterServerUrl);
            
            var request = new RestRequest("masterserver.php", Method.GET);            
            request.AddParameter("action", "addserver");
            request.AddParameter("servername", name);
            request.AddParameter("serverport", Port);
            request.AddParameter("playercount", PlayerCountToByte(connectedClients.Count, config.MaximumConnections));
            request.AddParameter("password", string.IsNullOrWhiteSpace(password) ? 0 : 1);

            // execute the request
            RestResponse response = (RestResponse)client.Execute(request);

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                DebugConsole.ThrowError("Error while connecting to master server (" +response.StatusCode+": "+response.StatusDescription+")");
                return;
            }

            if (response!=null && !string.IsNullOrWhiteSpace(response.Content))
            {
                DebugConsole.ThrowError("Error while connecting to master server (" +response.Content+")");
                return;
            }

            registeredToMaster = true;
            refreshMasterTimer = DateTime.Now + refreshMasterInterval;
        }

        private IEnumerable<object> RefreshMaster()
        {
            var client = new RestClient(NetConfig.MasterServerUrl);

            var request = new RestRequest("masterserver.php", Method.GET);
            request.AddParameter("action", "refreshserver");
            request.AddParameter("gamestarted", gameStarted ? 1 : 0);
            request.AddParameter("playercount", PlayerCountToByte(connectedClients.Count, config.MaximumConnections));
            
            System.Diagnostics.Debug.WriteLine("refreshing master");

            var sw = new Stopwatch();
            sw.Start();

            masterServerResponded = false;
            var restRequestHandle = client.ExecuteAsync(request, response => MasterServerCallBack(response));

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 15);
            while (!masterServerResponded)
            {
                if (DateTime.Now > timeOut)
                {
                    restRequestHandle.Abort();
                    DebugConsole.ThrowError("Couldn't connect to master server (request timed out)");
                    registeredToMaster = false;
                }
            System.Diagnostics.Debug.WriteLine("took "+sw.ElapsedMilliseconds+" ms");
                
                yield return CoroutineStatus.Running;
            }

            yield return CoroutineStatus.Success;
        }

        private void MasterServerCallBack(IRestResponse response)
        {
            masterServerResponded = true;

            if (response.ErrorException != null)
            {
                DebugConsole.ThrowError("Error while registering to master server", response.ErrorException);
                registeredToMaster = false;
                return;
            }

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                DebugConsole.NewMessage("Error while reporting to master server (" + response.StatusCode + ": " + response.StatusDescription + ")", Color.Red);
                //registeredToMaster = false;
                return;
            }
        }
        
        public override void Update(float deltaTime)
        {
            if (ShowNetStats) netStats.Update(deltaTime);

            if (!started) return;

            base.Update(deltaTime);
            
            if (gameStarted)
            {
                inGameHUD.Update((float)Physics.step);

                //if all characters dead
                if (AutoRestart &&
                    connectedClients.Find(c => c.character != null && !c.character.IsDead)==null &&
                   (myCharacter == null || myCharacter.IsDead))
                {
                    EndButtonHit(null, null);
                    AutoRestartTimer = 20.0f;
                    UpdateNetLobby(null,null);
                    return;
                }
            }
            else if (autoRestart && Screen.Selected == GameMain.NetLobbyScreen && connectedClients.Count>0)
            {
                AutoRestartTimer -= deltaTime;
                if (AutoRestartTimer < 0.0f)
                {
                    StartGameClicked(null,null);
                }
            }

            for (int i = disconnectedClients.Count - 1; i >= 0; i-- )
            {
                disconnectedClients[i].deleteDisconnectedTimer -= deltaTime;
                if (disconnectedClients[i].deleteDisconnectedTimer > 0.0f) continue;

                if (gameStarted && disconnectedClients[i].character!=null)
                {
                    disconnectedClients[i].character.Remove();
                    disconnectedClients[i].character = null;
                }

                disconnectedClients.RemoveAt(i);
            }

            foreach (Client c in connectedClients)
            {
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
                    if (myCharacter != null) myCharacter.CreateUpdateNetworkEvent(false);  

                    foreach (Character c in Character.CharacterList)
                    {
                        if (c as AICharacter == null) continue;

                        if (c.SimPosition.Length() > 100.0f) continue;

                        c.CreateUpdateNetworkEvent(false);  
                    }
                }

                if (server.ConnectionsCount > 0)
                {
                    if (sparseUpdateTimer < DateTime.Now) SparseUpdate();

                    SendNetworkEvents();
                }

                updateTimer = DateTime.Now + updateInterval;
            }

            if (registeredToMaster && refreshMasterTimer < DateTime.Now)
            {
                CoroutineManager.StartCoroutine(RefreshMaster());

                refreshMasterTimer = DateTime.Now + refreshMasterInterval;
            }
        }

        private void SparseUpdate()
        {
            if (gameStarted) new NetworkEvent(Submarine.Loaded.ID, false);

            sparseUpdateTimer = DateTime.Now + sparseUpdateInterval;
        }

        private void ReadMessage(NetIncomingMessage inc)
        {
            NetOutgoingMessage outmsg;

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
                            outmsg = server.CreateMessage();

                            outmsg.Write((byte)PacketTypes.LoggedIn);

                            outmsg.Write(sender.ID);

                            outmsg.Write(gameStarted);

                            outmsg.Write(gameStarted && sender.character!=null);

                            //notify the client about other clients already logged in
                            outmsg.Write((characterInfo == null) ? connectedClients.Count - 1 : connectedClients.Count);
                            foreach (Client c in connectedClients)
                            {
                                if (c.Connection == inc.SenderConnection) continue;
                                outmsg.Write(c.name);
                                outmsg.Write(c.ID);
                            }

                            if (characterInfo != null)
                            {
                                outmsg.Write(characterInfo.Name);
                                outmsg.Write(-1);
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

                            UpdateNetLobby(null, null);
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

                    byte packetType = 0;
                    try
                    {
                        packetType = inc.ReadByte();
                    }
                    catch
                    {
                        return;
                    }

                    bool isReliable = false;
                    if (packetType == (byte)PacketTypes.ReliableMessage)
                    {
                        if (!dataSender.ReliableChannel.CheckMessage(inc)) return;
                        packetType = inc.ReadByte();

                        isReliable = true;
                    }

                    switch (packetType)
                    {
                        case (byte)PacketTypes.NetworkEvent:
                            if (!gameStarted) break;
                            if (!NetworkEvent.ReadData(inc)) break;

                            List<Client> recipients = connectedClients.FindAll(c => c.Connection != inc.SenderConnection && c.inGame);
                            if (recipients.Count == 0) break;

                            if (isReliable)
                            {
                                Debug.WriteLine("receiver reliable networkevent");
                                foreach (Client c in recipients)
                                {
                                    var reliableMessage = c.ReliableChannel.CreateMessage();
                                    inc.Position = 8+16;
                                    byte[] messageBytes = inc.ReadBytes(inc.LengthBytes-3);
                                    reliableMessage.InnerMessage.Write(messageBytes);

                                    c.ReliableChannel.SendMessage(reliableMessage, c.Connection);
                                }
                            }
                            else
                            {
                                outmsg = server.CreateMessage();
                                outmsg.Write(inc);

                                List<NetConnection> recipientConnections = new List<NetConnection>();
                                foreach (Client c in recipients) recipientConnections.Add(c.Connection);

                                server.SendMessage(outmsg, recipientConnections, inc.DeliveryMethod, 0);
                            }
                                                        
                            break;
                        case (byte)PacketTypes.Chatmessage:
                            ChatMessageType messageType = (ChatMessageType)inc.ReadByte();
                            string message = inc.ReadString();
                            
                            SendChatMessage(message, messageType);

                            break;
                        case (byte)PacketTypes.PlayerLeft:
                            DisconnectClient(inc.SenderConnection);
                            break;
                        case (byte)PacketTypes.CharacterInfo:
                            ReadCharacterData(inc);
                            break;
                        case (byte)PacketTypes.ResendRequest:
                            
                            dataSender.ReliableChannel.HandleResendRequest(inc);
                            break;
                        case (byte)PacketTypes.LatestMessageID:
                            dataSender.ReliableChannel.HandleLatestMessageID(inc);
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
            if (inc.ReadByte() != (byte)PacketTypes.Login) return;

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

            int userID;
            string userPassword = "", version = "", packageName = "", packageHash = "", name = "";
            try
            {
                userID = inc.ReadInt32();
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

            if (userPassword != password)
            {
                inc.SenderConnection.Deny("Wrong password!");
                DebugConsole.NewMessage(name +" couldn't join the server (wrong password)", Color.Red);
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
            else if (connectedClients.Find(c => c.name.ToLower() == name.ToLower() && c.ID!=userID) != null)
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
                    inc.SenderConnection.Approve();
                    return;
                }
            }

            userID = Rand.Range(1, 1000000);
            while (connectedClients.Find(c => c.ID == userID) != null)
            {
                userID++;
            }

            Client newClient = new Client(server, name, userID);
            newClient.Connection = inc.SenderConnection;
            newClient.version = version;

            connectedClients.Add(newClient);

            UpdateCrewFrame();

            inc.SenderConnection.Approve();
        }


        private void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod deliveryMethod, NetConnection excludedConnection)
        {
            List<NetConnection> recipients = new List<NetConnection>();

            foreach (Client client in connectedClients)
            {
                if (client.Connection != excludedConnection) recipients.Add(client.Connection);                
            }

            if (recipients.Count == 0) return;

            server.SendMessage(msg, recipients, deliveryMethod, 0);  
            
        }

        private void SendNetworkEvents()
        {
            if (NetworkEvent.events.Count == 0) return;

            List<Client> recipients = connectedClients.FindAll(c => c.character != null);

            List<NetConnection> recipientConnections = new List<NetConnection>();
            foreach (Client c in recipients)
            {
                recipientConnections.Add(c.Connection);
            }

            if (recipients.Count == 0) return;



            foreach (NetworkEvent networkEvent in NetworkEvent.events)  
            {
                NetOutgoingMessage message = server.CreateMessage();
                message.Write((byte)PacketTypes.NetworkEvent);
                //if (!networkEvent.IsClient) continue;

                if (!networkEvent.FillData(message))
                {
                    continue;
                }

                //Entity e = Entity.FindEntityByID(networkEvent.ID);
                //if (e == null) continue;
                if (networkEvent.IsImportant)
                {
                    foreach (Client c in recipients)
                    {
                        ReliableMessage reliableMessage = c.ReliableChannel.CreateMessage();
                        message.Position = 0;
                        reliableMessage.InnerMessage.Write(message.ReadBytes(message.LengthBytes));

                        c.ReliableChannel.SendMessage(reliableMessage, c.Connection);
                    }
                }
                else
                {
                    if (server.ConnectionsCount>0)
                    {
                        server.SendMessage(message, recipientConnections, NetDeliveryMethod.Unreliable, 0);  
                    }  
                }                          
            }
            NetworkEvent.events.Clear();
        }


        public bool StartGameClicked(GUIButton button, object obj)
        {
            Submarine selectedSub = GameMain.NetLobbyScreen.SelectedMap as Submarine;

            if (selectedSub == null)
            {
                GameMain.NetLobbyScreen.SubList.Flash();
                return false;
            }

            GameMain.ShowLoading(StartGame(selectedSub), false);
 
            return true;
        }

        private IEnumerable<object> StartGame(Submarine selectedSub)
        {
            AssignJobs();

            //selectedMap.Load();

            int seed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(seed);
            GameMain.GameSession = new GameSession(selectedSub, "", GameMain.NetLobbyScreen.SelectedMode);
            GameMain.GameSession.StartShift(GameMain.NetLobbyScreen.LevelSeed);

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
                connectedClients[i].character = new Character(
                    connectedClients[i].characterInfo, assignedWayPoints[i], true);
                connectedClients[i].character.GiveJobItems(assignedWayPoints[i]);
            }

            if (characterInfo != null)
            {
                myCharacter = new Character(characterInfo, assignedWayPoints[assignedWayPoints.Length - 1]);
                Character.Controlled = myCharacter;

                myCharacter.GiveJobItems(assignedWayPoints[assignedWayPoints.Length - 1]);
            }

            yield return CoroutineStatus.Running;

            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.StartGame);

            msg.Write(seed);

            msg.Write(GameMain.NetLobbyScreen.LevelSeed);

            msg.Write(GameMain.NetLobbyScreen.SelectedMap.Name);
            msg.Write(GameMain.NetLobbyScreen.SelectedMap.MD5Hash.Hash);

            msg.Write(GameMain.NetLobbyScreen.SelectedMode.Name);

            //msg.Write(GameMain.NetLobbyScreen.GameDuration.TotalMinutes);

            msg.Write((myCharacter == null) ? (byte)connectedClients.Count : (byte)(connectedClients.Count + 1));
            foreach (Client client in connectedClients)
            {
                msg.Write(client.ID);
                WriteCharacterData(msg, client.character.Name, client.character);
            }

            if (myCharacter != null)
            {
                msg.Write(-1);
                WriteCharacterData(msg, myCharacter.Info.Name, Character.Controlled);
            }

            SendMessage(msg, NetDeliveryMethod.ReliableUnordered, null);

            UpdateCrewFrame();

            //give some time for the clients to load the map
            yield return new WaitForSeconds(2.0f);

            gameStarted = true;

            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            GameMain.GameScreen.Select();

            yield return CoroutineStatus.Success;

        }

        private bool EndButtonHit(GUIButton button, object obj)
        {
            GameMain.GameSession.gameMode.End("Server admin has ended the round");

            return true;
        }

        public IEnumerable<object> EndGame(string endMessage)
        {
            var messageBox = new GUIMessageBox("The round has ended", endMessage, 400, 300);
            
            Character.Controlled = null;
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
                    client.character = null;
                    client.inGame = false;
                }
            }

            float endPreviewLength = 10.0f;

            DateTime endTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, (int)(1000.0f * endPreviewLength));
            float secondsLeft = endPreviewLength;

            do
            {
                secondsLeft = (float)(endTime - DateTime.Now).TotalSeconds;

                float camAngle = (float)((DateTime.Now - endTime).TotalSeconds / endPreviewLength) * MathHelper.TwoPi;
                Vector2 offset = (new Vector2(
                    (float)Math.Cos(camAngle) * (Submarine.Borders.Width / 2.0f),
                    (float)Math.Sin(camAngle) * (Submarine.Borders.Height / 2.0f)));

                GameMain.GameScreen.Cam.TargetPos = offset * 0.8f;
                //Game1.GameScreen.Cam.MoveCamera((float)deltaTime);

                messageBox.Text = endMessage + "\nReturning to lobby in " + (int)secondsLeft + " s";

                yield return CoroutineStatus.Running;
            } while (secondsLeft > 0.0f);

            Submarine.Unload();

            messageBox.Close(null, null);

            GameMain.NetLobbyScreen.Select();

            yield return CoroutineStatus.Success;

        }

        private void DisconnectClient(NetConnection senderConnection, string msg = "", string targetmsg = "")
        {
            Client client = connectedClients.Find(x => x.Connection == senderConnection);
            if (client != null) DisconnectClient(client, msg, targetmsg);
        }

        private void DisconnectClient(Client client, string msg = "", string targetmsg = "")
        {
            if (client == null) return;

            if (gameStarted && client.character != null)
            {
                if (GameMain.GameSession!=null && GameMain.GameSession.gameMode!=null)
                {
                    TraitorMode traitorMode = GameMain.GameSession.gameMode as TraitorMode;
                    if (traitorMode!=null)
                    {
                        traitorMode.CharacterLeft(client.character);
                    }
                }

                client.character.ClearInputs();
            }

            if (string.IsNullOrWhiteSpace(msg)) msg = client.name + " has left the server";
            if (string.IsNullOrWhiteSpace(targetmsg)) targetmsg = "You have left the server";
            
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

            AddChatMessage(msg, ChatMessageType.Server);

            UpdateCrewFrame();
        }

        private void UpdateCrewFrame()
        {
            List<Character> crew = new List<Character>();

            foreach (Client c in connectedClients)
            {
                if (c.character == null || !c.inGame) continue;

                crew.Add(c.character);
            }

            if (myCharacter != null) crew.Add(myCharacter);

            CreateCrewFrame(crew);
        }

        public void KickPlayer(string playerName, bool ban = false)
        {
            playerName = playerName.ToLower();

            Client client = connectedClients.Find( c => c.name.ToLower() == playerName ||
                    (c.character != null && c.character.Name.ToLower() == playerName));

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

        public void NewTraitor(out Character traitor, out Character target)
        {
            List<Character> characters = new List<Character>();
            foreach (Client client in connectedClients)
            {
                if (!client.inGame || client.character==null) continue;
                characters.Add(client.character);
            }
            if (myCharacter!= null) characters.Add(myCharacter);

            if (characters.Count < 2)
            {
                traitor = null;
                target = null;
                return;
            }

            int traitorIndex = Rand.Range(0, characters.Count);

            int targetIndex = Rand.Range(0, characters.Count);
            while (targetIndex==traitorIndex)
            {
                targetIndex = Rand.Range(0, characters.Count);
            }


            traitor = characters[traitorIndex];
            target = characters[targetIndex];

            if (myCharacter==null)
            {               
                new GUIMessageBox("New traitor", traitor.Info.Name + " is the traitor and the target is " + target.Info.Name+".");
            }
            else if (myCharacter == traitor)
            {
                new GUIMessageBox("You are the traitor!", "Your task is to assassinate " + target.Info.Name+".");
                return;
            }

            Client traitorClient = null;
            foreach (Client c in connectedClients)
            {
                if (c.character != traitor) continue;
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
            
            if (!ShowNetStats) return;

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight*0.3f);

            GUI.DrawRectangle(spriteBatch, new Rectangle(x,y,width,height), Color.Black*0.7f, true);
            spriteBatch.DrawString(GUI.Font, "Network statistics:", new Vector2(x+10, y+10), Color.White);
                        
            spriteBatch.DrawString(GUI.SmallFont, "Connections: "+server.ConnectionsCount, new Vector2(x + 10, y + 30), Color.White);
            spriteBatch.DrawString(GUI.SmallFont, "Received bytes: " + server.Statistics.ReceivedBytes, new Vector2(x + 10, y + 45), Color.White);
            spriteBatch.DrawString(GUI.SmallFont, "Received packets: " + server.Statistics.ReceivedPackets, new Vector2(x + 10, y + 60), Color.White);

            spriteBatch.DrawString(GUI.SmallFont, "Sent bytes: " + server.Statistics.SentBytes, new Vector2(x + 10, y + 75), Color.White);
            spriteBatch.DrawString(GUI.SmallFont, "Sent packets: " + server.Statistics.SentPackets, new Vector2(x + 10, y + 90), Color.White);

            int resentMessages = 0;

            y += 110;
            foreach (Client c in connectedClients)
            {
                spriteBatch.DrawString(GUI.SmallFont, c.name + ":", new Vector2(x + 10, y), Color.White);
                spriteBatch.DrawString(GUI.SmallFont, "- avg roundtrip " + c.Connection.AverageRoundtripTime+" s", new Vector2(x + 20, y + 15), Color.White);
                spriteBatch.DrawString(GUI.SmallFont, "- resent messages " + c.Connection.Statistics.ResentMessages, new Vector2(x + 20, y + 30), Color.White);

                resentMessages += (int)c.Connection.Statistics.ResentMessages;
                
                y += 50;            
            }

            netStats.AddValue(NetStats.NetStatType.ResentMessages, resentMessages);
            netStats.AddValue(NetStats.NetStatType.SentBytes, server.Statistics.SentBytes);
            netStats.AddValue(NetStats.NetStatType.ReceivedBytes, server.Statistics.ReceivedBytes);

            netStats.Draw(spriteBatch, new Rectangle(200,0,800,200));

        }

        public bool UpdateNetLobby(object obj)
        {
            return UpdateNetLobby(null, obj);
        }

        public bool UpdateNetLobby(GUIComponent component, object obj)
        {
            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.UpdateNetLobby);
            GameMain.NetLobbyScreen.WriteData(msg);

            if (server.Connections.Count > 0)
            {
                server.SendMessage(msg, server.Connections, NetDeliveryMethod.ReliableUnordered, 0);
            }

            return true;
        }

        protected override bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            base.SelectCrewCharacter(component, obj);

            var characterFrame = crewFrame.FindChild("selectedcharacter");

            Character character = obj as Character;
            if (obj == null) return false;

            if (character != myCharacter)
            {
                var kickButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Kick", Alignment.BottomLeft, GUI.Style, characterFrame);
                kickButton.UserData = character.Name;
                kickButton.OnClicked += GameMain.NetLobbyScreen.KickPlayer;

                var banButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Ban", Alignment.BottomRight, GUI.Style, characterFrame);
                banButton.UserData = character.Name;
                banButton.OnClicked += GameMain.NetLobbyScreen.BanPlayer;
            }



            return true;

        }

        public override void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Server)
        {
            AddChatMessage(message, type);

            if (server.Connections.Count == 0) return;

            List<Client> recipients = new List<Client>();

            foreach (Client c in connectedClients)
            {
                if (type!=ChatMessageType.Dead || (c.character != null && c.character.IsDead)) recipients.Add(c);
            }

            foreach (Client c in recipients)
            {
                ReliableMessage msg = c.ReliableChannel.CreateMessage();
                msg.InnerMessage.Write((byte)PacketTypes.Chatmessage);
                msg.InnerMessage.Write((byte)type);
                msg.InnerMessage.Write(message);            

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
                name         = message.ReadString();
                gender       = message.ReadBoolean() ? Gender.Male : Gender.Female;
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

            message.Write(character.SimPosition.X);
            message.Write(character.SimPosition.Y);

            message.Write(character.Info.Job.Name);
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

            //share the rest of the jobs according to the ''commonness'' of the job
            //float totalCommonness = 0.0f;
            //for (int i = 0; i < JobPrefab.List.Count; i++)
            //{
            //    if (JobPrefab.List[i].AllowAlways || JobPrefab.List[i].MaxNumber == 0) continue;

            //    totalCommonness += JobPrefab.List[i].Commonness;
            //}

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
                int index = c.jobPreferences.FindIndex(jp => jp == job);
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


        private byte PlayerCountToByte(int playerCount, int maxPlayers)
        {
            byte byteVal = (byte)playerCount;

            byteVal |= (byte)((maxPlayers-1) << 4);

            return byteVal;
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
                    msg.Write((byte)Enum.GetNames(typeof(NetworkEventType)).Length);
                    msg.Write(Rand.Int(MapEntity.mapEntityList.Count));
                    break;
                case 1:
                    msg.Write((byte)PacketTypes.NetworkEvent);
                    msg.Write((byte)NetworkEventType.ComponentUpdate);
                    msg.Write((int)Item.itemList[Rand.Int(Item.itemList.Count)].ID);
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
                msg.Write((Rand.Int(2) == 0) ? true : false);
            }
            SendMessage(msg, (Rand.Int(2) == 0) ? NetDeliveryMethod.ReliableOrdered : NetDeliveryMethod.Unreliable, null);

        }

        public override void Disconnect()
        {
            banList.Save();
            server.Shutdown("The server has shut down");
        }
    }

    class Client
    {
        public string name;
        public int ID;

        public Character character;
        public CharacterInfo characterInfo;
        public NetConnection Connection { get; set; }
        public string version;
        public bool inGame;

        public List<JobPrefab> jobPreferences;
        public JobPrefab assignedJob;

        public ReliableChannel ReliableChannel;

        public float deleteDisconnectedTimer;

        public Client(NetPeer server, string name, int ID)
            : this(name, ID)
        {
            ReliableChannel = new ReliableChannel(server);
        }

        public Client(string name, int ID)
        {
            this.name = name;
            this.ID = ID;
            
            jobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0,3));
        }
    }
}
