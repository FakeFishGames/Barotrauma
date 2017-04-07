
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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

        private bool initiatedStartGame;
        private CoroutineHandle startGameCoroutine;

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

            config.MaximumConnections = maxPlayers;

            config.DisableMessageType(NetIncomingMessageType.DebugMessage | 
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);
                                    
            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            //----------------------------------------

            var endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170, 20, 150, 20), "End round", Alignment.TopLeft, "", inGameHUD);
            endRoundButton.OnClicked = (btn, userdata) => { EndGame(); return true; };

            log = new ServerLog(name);
            showLogButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170, 20, 150, 20), "Server Log", Alignment.TopLeft, "", inGameHUD);
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

            GUIButton settingsButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170 - 170, 20, 150, 20), "Settings", Alignment.TopLeft, "", inGameHUD);
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
            request.AddParameter("serverport", Port);
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
            else if (initiatedStartGame)
            {
                //tried to start up the game and StartGame coroutine is not running anymore
                // -> something wen't wrong during startup, re-enable start button and reset AutoRestartTimer
                if (startGameCoroutine != null && !CoroutineManager.IsCoroutineRunning(startGameCoroutine))
                {
                    if (autoRestart) AutoRestartTimer = Math.Max(AutoRestartInterval, 5.0f);
                    GameMain.NetLobbyScreen.StartButton.Enabled = true;

                    UpdateNetLobby(null, null);
                    startGameCoroutine = null;
                    initiatedStartGame = false;
                }
            }
            else if (autoRestart && Screen.Selected == GameMain.NetLobbyScreen && connectedClients.Count > 0)
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

                //slowly reset spam timers
                c.ChatSpamTimer = Math.Max(0.0f, c.ChatSpamTimer - deltaTime);
                c.ChatSpamSpeed = Math.Max(0.0f, c.ChatSpamSpeed - deltaTime);
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

                    float ignoreDistance = FarseerPhysics.ConvertUnits.ToDisplayUnits(NetConfig.CharacterIgnoreDistance);

                    foreach (Character c in Character.CharacterList)
                    {
                        if (!(c is AICharacter) || c.IsDead) continue;

                        if (Character.CharacterList.Any(
                            c2 => c2.IsRemotePlayer &&
                                Vector2.Distance(c2.WorldPosition, c.WorldPosition) < ignoreDistance))
                        {
                            new NetworkEvent(NetworkEventType.EntityUpdate, c.ID, false);
                        }

                        //todo: take multiple subs into account
                        //Vector2 diff = c.WorldPosition - Submarine.MainSub.WorldPosition;

                        //if (FarseerPhysics.ConvertUnits.ToSimUnits(diff.Length()) > NetConfig.CharacterIgnoreDistance) continue;                        
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
            //if (gameStarted)
            //{
            //    foreach (Submarine sub in Submarine.Loaded)
            //    {
            //        //no need to send position updates for submarines that are docked to mainsub
            //        if (sub != Submarine.MainSub && sub.DockedTo.Contains(Submarine.MainSub)) continue;

            //        new NetworkEvent(sub.ID, false);
            //    }
            //}

            float ignoreDistance = FarseerPhysics.ConvertUnits.ToDisplayUnits(NetConfig.CharacterIgnoreDistance);

            foreach (Character c in Character.CharacterList)
            {
                if (c is AICharacter)
                {
                    c.Enabled = 
                       (myCharacter != null && Vector2.Distance(myCharacter.WorldPosition, c.WorldPosition) < ignoreDistance) ||
                        Character.CharacterList.Any(c2 => c2.IsRemotePlayer && Vector2.Distance(c2.WorldPosition, c.WorldPosition) < ignoreDistance);
                }

                if (c.IsDead) continue;

                new NetworkEvent(NetworkEventType.ImportantEntityUpdate, c.ID, false);
            }

            sparseUpdateTimer = DateTime.Now + sparseUpdateInterval;
        }

        private void ReadMessage(NetIncomingMessage inc)
        {
            Client sender = connectedClients.Find(x => x.Connection == inc.SenderConnection);
            
            switch (inc.MessageType)
            {
                case NetIncomingMessageType.ConnectionApproval:
                    HandleConnectionApproval(inc);
                    break;
                case NetIncomingMessageType.StatusChanged:
                    Debug.WriteLine(inc.SenderConnection + " status changed. " + (NetConnectionStatus)inc.SenderConnection.Status);
                    if (inc.SenderConnection.Status == NetConnectionStatus.Disconnected)
                    {
                        var connectedClient = connectedClients.Find(c => c.Connection == inc.SenderConnection);
                        if (connectedClient != null && !disconnectedClients.Contains(connectedClient))
                        {
                            connectedClient.deleteDisconnectedTimer = NetConfig.DeleteDisconnectedTime;
                            disconnectedClients.Add(connectedClient);
                        }

                        DisconnectClient(inc.SenderConnection,
                            connectedClient != null ? connectedClient.name + " has disconnected" : "");
                    }
                    break;
                case NetIncomingMessageType.Data:
                    if (banList.IsBanned(inc.SenderEndPoint.Address.ToString()))
                    {
                        inc.SenderConnection.Disconnect("You have been banned from the server");
                        return;
                    }

                    byte packetType = inc.ReadByte();

                    if (sender == null)
                    {
                        var authUser = unauthenticatedClients.Find(c => c.Connection == inc.SenderConnection);
                        if (authUser == null)
                        {
                            unauthenticatedClients.Remove(authUser);
                            inc.SenderConnection.Disconnect("Disconnected");
                        }
                        else
                        {
                            CheckAuthentication(inc);
                        }
                        return;
                    }

                    if (packetType == (byte)PacketTypes.ReliableMessage)
                    {
                        if (!sender.ReliableChannel.CheckMessage(inc)) return;
                        packetType = inc.ReadByte();
                    }

                    switch (packetType)
                    {
                        case (byte)PacketTypes.NetworkEvent:
                            if (!gameStarted) break;
                            NetworkEvent.ReadMessage(inc, true);

                            break;
                        case (byte)PacketTypes.Chatmessage:
                            ReadChatMessage(inc);
                            break;
                        case (byte)PacketTypes.PlayerLeft:
                            DisconnectClient(inc.SenderConnection);
                            break;
                        case (byte)PacketTypes.StartGame:
                            sender.ReadyToStart = true;
                            break;
                        case (byte)PacketTypes.EndGame:
                            if (!sender.HasPermission(ClientPermissions.EndRound))
                            {
                                Log(sender.name+" attempted to end the round (insufficient permissions)", Color.Red);
                            }
                            else
                            {
                                Log("Round ended by " + sender.name, Color.Red);
                                EndGame();
                            }
                            break;
                        case (byte)PacketTypes.RequestAdminAuth:
                            string pass = inc.ReadString();
                            if (adminAuthPass.Length == 0)
                            {
                                Log(sender.name + " tried to become admin!", Color.Red);
                                return;
                            }
                            if (adminAuthPass==pass)
                            {
                                if (sender.Permissions == ClientPermissions.None)
                                {
                                    Log(sender.name + " is now an admin.", Color.Yellow);
                                    sender.SetPermissions(ClientPermissions.Kick | ClientPermissions.Ban | ClientPermissions.EndRound);
                                }
                                else
                                {
                                    Log(sender.name + " is no longer an admin.", Color.Yellow);
                                    sender.SetPermissions(ClientPermissions.None);
                                }
                                UpdateClientPermissions(sender);
                            }
                            else
                            {
                                Log(sender.name + " has failed admin authentication!", Color.Red);
                            }
                            break;
                        case (byte)PacketTypes.KickPlayer:                            
                            bool ban = inc.ReadBoolean();
                            string kickedName = inc.ReadString();

                            var kickedClient = connectedClients.Find(c => c.name.ToLowerInvariant() == kickedName.ToLowerInvariant());
                            if (kickedClient == null || kickedClient == sender) return;

                            if (ban && !sender.HasPermission(ClientPermissions.Ban))
                            {
                                Log(sender.name + " attempted to ban " + kickedClient.name + " (insufficient permissions)", Color.Red);
                            }
                            else if (!sender.HasPermission(ClientPermissions.Kick))
                            {
                                Log(sender.name + " attempted to kick " + kickedClient.name + " (insufficient permissions)", Color.Red);
                            }
                            else
                            {
                                KickClient(kickedClient, ban);
                            }
                            break;
                        case (byte)PacketTypes.CharacterInfo:
                            ReadCharacterData(inc);
                            break;
                        case (byte)PacketTypes.RequestFile:
                            
                            if (!AllowFileTransfers)
                            {
                                SendCancelTransferMessage(sender, "File transfers have been disabled by the server.");
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
                                        if (sender.FileStreamSender != null) sender.FileStreamSender.CancelTransfer();

                                        var fileStreamSender = FileStreamSender.Create(sender.Connection, requestedSubmarine.FilePath, FileTransferMessageType.Submarine);
                                        if (fileStreamSender != null) sender.FileStreamSender = fileStreamSender;
                                    }
                                    break;
                                case (byte)FileTransferMessageType.Cancel:
                                    if (sender.FileStreamSender != null)
                                    {
                                        sender.FileStreamSender.CancelTransfer();
                                    }
                                    break;
                                default:
                                    DebugConsole.ThrowError("Unknown file type was requested ("+fileType+")");
                                    break;
                            }

                            break;
                        case (byte)PacketTypes.ResendRequest:
                            sender.ReliableChannel.HandleResendRequest(inc);
                            break;
                        case (byte)PacketTypes.LatestMessageID:
                            sender.ReliableChannel.HandleLatestMessageID(inc);
                            break;
                        case (byte)PacketTypes.Vote:
                            Voting.RegisterVote(inc, connectedClients);

                            if (Voting.AllowEndVoting && EndVoteMax > 0 &&
                                ((float)EndVoteCount / (float)EndVoteMax) >= EndVoteRequiredRatio)
                            {
                                Log("Ending round by votes (" + EndVoteCount + "/" + (EndVoteMax - EndVoteCount) + ")", Color.Cyan);
                                EndGame();
                            }
                            break;
                        case (byte)PacketTypes.RequestNetLobbyUpdate:
                            UpdateNetLobby(null, null);
                            UpdateVoteStatus();
                            break;
                        case (byte)PacketTypes.SpectateRequest:
                            if (gameStarted && AllowSpectating)
                            {
                                var startMessage = CreateStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.gameMode.Preset);
                                server.SendMessage(startMessage, inc.SenderConnection, NetDeliveryMethod.ReliableOrdered);

                                CoroutineManager.StartCoroutine(SyncSpectator(sender));
                            }
                            break;
                    }
                    break;
                case NetIncomingMessageType.WarningMessage:
                    Debug.WriteLine(inc.ReadString());
                    break;
            }
        }

        private void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod deliveryMethod, NetConnection excludedConnection = null)
        {
            List<NetConnection> recipients = new List<NetConnection>();

            foreach (Client client in connectedClients)
            {
                if (client.Connection != excludedConnection) recipients.Add(client.Connection);                
            }

            if (recipients.Count == 0) return;

            SendMessage(msg, deliveryMethod, recipients);              
        }

        private void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod deliveryMethod, List<NetConnection> recipients)
        {
            if (recipients == null) recipients = connectedClients.Select(c => c.Connection).ToList();

            if (recipients.Count == 0) return;

            server.SendMessage(msg, recipients, deliveryMethod, 0);
        }

        private void SendNetworkEvents(List<Client> recipients = null)
        {
            if (NetworkEvent.Events.Count == 0) return;

            if (recipients == null)
            {
                recipients = connectedClients.FindAll(c => c.inGame);
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

            foreach (Item item in Item.Remover.removedItems)
            {
                Item.Spawner.spawnItems.Remove(item);
            }

            SendItemRemoveMessage(Item.Remover.removedItems, new List<NetConnection>() { sender.Connection });
            SendItemSpawnMessage(Item.Spawner.spawnItems, new List<NetConnection>() { sender.Connection });

            yield return new WaitForSeconds(1.0f);

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

            foreach (Structure wall in Structure.WallList)
            {
                bool takenDamage = false;
                for (int i = 0; i<wall.SectionCount; i++)
                {
                    if (wall.SectionDamage(i) < wall.Health)
                    {
                        takenDamage = true;
                        break;
                    }
                }

                if (takenDamage) new NetworkEvent(NetworkEventType.ImportantEntityUpdate, wall.ID, false);
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

            yield return new WaitForSeconds(0.1f);

            SendRespawnManagerMsg(null, null, new List<NetConnection>() { sender.Connection });

            yield return new WaitForSeconds(0.1f);


            sender.inGame = true;
            
            yield return CoroutineStatus.Success;
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

            if (selectedMode==null)
            {
                GameMain.NetLobbyScreen.ModeList.Flash();
                return false;
            }

            CoroutineManager.StartCoroutine(WaitForPlayersReady(selectedSub, selectedShuttle, selectedMode), "WaitForPlayersReady");
 
            return true;
        }

        private IEnumerable<object> WaitForPlayersReady(Submarine selectedSub, Submarine selectedShuttle, GameModePreset selectedMode)
        {
            GameMain.NetLobbyScreen.StartButton.Enabled = false;

            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.CanStartGame);
            msg.Write(selectedSub.Name);
            msg.Write(selectedSub.MD5Hash.Hash);

            msg.Write(selectedShuttle.Name);
            msg.Write(selectedShuttle.MD5Hash.Hash);

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

            startGameCoroutine = GameMain.ShowLoading(StartGame(selectedSub, selectedShuttle, selectedMode), false);

            yield return CoroutineStatus.Success;
        }
        
        private IEnumerable<object> StartGame(Submarine selectedSub, Submarine selectedShuttle, GameModePreset selectedMode)
        {
            initiatedStartGame = true;

            Item.Spawner.Clear();
            Item.Remover.Clear();

            GameMain.NetLobbyScreen.StartButton.Enabled = false;

            GUIMessageBox.CloseAll();

            roundStartSeed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(roundStartSeed);

            bool couldNotStart = false;

            int teamCount = 1;
            int hostTeam = 1;
       
            GameMain.GameSession = new GameSession(selectedSub, "", selectedMode, Mission.MissionTypes[GameMain.NetLobbyScreen.MissionTypeIndex]);

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

            bool missionAllowRespawn = 
                !(GameMain.GameSession.gameMode is MissionMode) || 
                ((MissionMode)GameMain.GameSession.gameMode).Mission.AllowRespawn;

            if (AllowRespawn && missionAllowRespawn) respawnManager = new RespawnManager(this, selectedShuttle);
            
            for (int teamID = 1; teamID <= teamCount; teamID++)
            {
                List<Client> teamClients = teamCount == 1 ? connectedClients : connectedClients.FindAll(c => c.TeamID == teamID);

                if (!teamClients.Any() && teamID > 1) continue;

                AssignJobs(teamClients, teamID==hostTeam);

                List<CharacterInfo> characterInfos = new List<CharacterInfo>();

                foreach (Client client in teamClients)
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
                if (characterInfo != null && teamID == hostTeam)
                {
                    characterInfo.Job = new Job(GameMain.NetLobbyScreen.JobPreferences[0]);
                    characterInfos.Add(characterInfo);
                }

                WayPoint[] assignedWayPoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSubs[teamID - 1]);

                for (int i = 0; i < teamClients.Count; i++)
                {
                    teamClients[i].Character = Character.Create(
                        teamClients[i].characterInfo, assignedWayPoints[i].WorldPosition, true, false);
                    teamClients[i].Character.GiveJobItems(assignedWayPoints[i]);

                    GameMain.GameSession.CrewManager.characters.Add(teamClients[i].Character);

                    teamClients[i].Character.TeamID = (byte)teamID;
                }
                
                if (characterInfo != null && teamID == hostTeam)
                {
                    myCharacter = Character.Create(characterInfo, assignedWayPoints[assignedWayPoints.Length - 1].WorldPosition, false, false); 
                    myCharacter.GiveJobItems(assignedWayPoints[assignedWayPoints.Length - 1]);
                    myCharacter.TeamID = (byte)teamID;

                    Character.Controlled = myCharacter;
                    GameMain.GameSession.CrewManager.characters.Add(myCharacter);
                }
            }

            foreach (Submarine sub in Submarine.MainSubs)
            {
                if (sub == null) continue;

                WayPoint cargoSpawnPos = WayPoint.GetRandom(SpawnType.Cargo, null, sub);

                if (cargoSpawnPos == null || cargoSpawnPos.CurrentHull == null)
                {
                    DebugConsole.ThrowError("Couldn't spawn additional cargo (no cargo spawnpoint inside any of the hulls)");
                    continue;
                }

                var cargoRoom = cargoSpawnPos.CurrentHull;
                Vector2 position = new Vector2(
                    cargoSpawnPos.Position.X + Rand.Range(-20.0f, 20.0f),
                    cargoRoom.Rect.Y - cargoRoom.Rect.Height);

                foreach (string s in extraCargo.Keys)
                {
                    ItemPrefab itemPrefab = ItemPrefab.list.Find(ip => ip.Name == s) as ItemPrefab;
                    if (itemPrefab == null) continue;

                    for (int i = 0; i < extraCargo[s]; i++)
                    {
                        Item.Spawner.QueueItem(itemPrefab, position + (Vector2.UnitX * itemPrefab.Size.Y/2), sub, false);
                    }
                }                
            }
            
           
            var startMessage = CreateStartMessage(roundStartSeed, Submarine.MainSub, GameMain.GameSession.gameMode.Preset);

            SendMessage(startMessage, NetDeliveryMethod.ReliableOrdered);
            
            //SendItemSpawnMessage(allItems, inventories);

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


            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;

            GameMain.GameScreen.Select();

            if (myCharacter == null)
            {
                AddChatMessage("Press TAB to chat. Use \"d;\" to talk to dead players and spectators, "
                    + "and \"player name;\" to only send the message to a specific player.", ChatMessageType.Server);
            }
            else
            {
                AddChatMessage("Press TAB to chat. Use \"r;\" to talk through the radio.", ChatMessageType.Server);
            }

            GameMain.NetLobbyScreen.StartButton.Enabled = true;

            gameStarted = true;
            initiatedStartGame = false;

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

            msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.Name);
            msg.Write(GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash.Hash);

            msg.Write(selectedMode.Name);


            bool missionAllowRespawn =
                !(GameMain.GameSession.gameMode is MissionMode) ||
                ((MissionMode)GameMain.GameSession.gameMode).Mission.AllowRespawn;

            msg.Write(AllowRespawn && missionAllowRespawn);
            msg.Write(Submarine.MainSubs[1] != null); //loadSecondSub

            //msg.Write(GameMain.NetLobbyScreen.GameDuration.TotalMinutes);

            var characters = Character.CharacterList.FindAll(c => !(c is AICharacter) || c.SpawnedMidRound);

            msg.Write((byte)characters.Count);
            foreach (Character c in characters)
            {
                WriteCharacterData(msg, c.Name, c);
            }

            return msg;
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
            Item.Remover.Clear();

#if DEBUG
            messageCount.Clear();
#endif

            respawnManager = null;
            gameStarted = false;

            if (connectedClients.Count > 0)
            {
                NetOutgoingMessage msg = server.CreateMessage();
                msg.Write((byte)PacketTypes.EndGame);
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

            new TransitionCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, endPreviewLength);
            
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

        public void SendRespawnManagerMsg(List<Character> spawnedCharacters = null, List<Item> spawnedItems = null, List<NetConnection> recipients = null)
        {
            if (respawnManager == null) return;

            NetOutgoingMessage msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.Respawn);

            respawnManager.WriteNetworkEvent(msg, spawnedCharacters, spawnedItems);

            SendMessage(msg, NetDeliveryMethod.ReliableUnordered, recipients);
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
            
            //notify other players about the disconnected client
            NetOutgoingMessage outmsg = server.CreateMessage();
            outmsg.Write((byte)PacketTypes.PlayerLeft);
            outmsg.Write(client.ID);
            outmsg.Write(msg);

            GameMain.NetLobbyScreen.RemovePlayer(client.name);

            if (server.Connections.Count > 0)
            {
                server.SendMessage(outmsg, server.Connections, NetDeliveryMethod.ReliableUnordered, 0);
            }

            connectedClients.Remove(client);
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
            foreach (Client c in connectedClients)
            {
                if (c.Character == null || !c.inGame) continue;
            }
        }

        public override void KickPlayer(string playerName, bool ban, bool range = false)
        {
            playerName = playerName.ToLowerInvariant();

            Client client = connectedClients.Find(c => 
                c.name.ToLowerInvariant() == playerName ||
                (c.Character != null && c.Character.Name.ToLowerInvariant() == playerName));

            KickClient(client, ban, range);
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
                new GUIProgressBar(new Rectangle(0, 4, 160, clientInfo.Rect.Height - 8), Color.Green, "", 0.0f, Alignment.Left, clientInfo).IsHorizontal = true;
                new GUITextBlock(new Rectangle(0, 2, 160, 0), "", "", Alignment.TopLeft, Alignment.Left | Alignment.CenterY, clientInfo, true, GUI.SmallFont);

                var cancelButton = new GUIButton(new Rectangle(20, 0, 14, 0), "X", Alignment.Right, "", clientInfo);
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
                clientListScrollBar = new GUIScrollBar(new Rectangle(x + width - 10, y, 10, height), "", 1.0f);
            }


            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black * 0.7f, true);
            GUI.Font.DrawString(spriteBatch, "Network statistics:", new Vector2(x + 10, y + 10), Color.White);
                        
            GUI.SmallFont.DrawString(spriteBatch, "Connections: "+server.ConnectionsCount, new Vector2(x + 10, y + 30), Color.White);
            GUI.SmallFont.DrawString(spriteBatch, "Received bytes: " + MathUtils.GetBytesReadable(server.Statistics.ReceivedBytes), new Vector2(x + 10, y + 45), Color.White);
            GUI.SmallFont.DrawString(spriteBatch, "Received packets: " + server.Statistics.ReceivedPackets, new Vector2(x + 10, y + 60), Color.White);

            GUI.SmallFont.DrawString(spriteBatch, "Sent bytes: " + MathUtils.GetBytesReadable(server.Statistics.SentBytes), new Vector2(x + 10, y + 75), Color.White);
            GUI.SmallFont.DrawString(spriteBatch, "Sent packets: " + server.Statistics.SentPackets, new Vector2(x + 10, y + 90), Color.White);

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
                    GUI.SmallFont.DrawString(spriteBatch, c.name + " ("+c.Connection.RemoteEndPoint.Address.ToString()+")", new Vector2(x + 10, y), clientColor);
                    GUI.SmallFont.DrawString(spriteBatch, "Ping: " + (int)(c.Connection.AverageRoundtripTime * 1000.0f) + " ms", new Vector2(x+20, y+10), clientColor);
                }
                if (y + 25 >= startY && y < startY + height - 130) GUI.SmallFont.DrawString(spriteBatch, "Resent messages: " + c.Connection.Statistics.ResentMessages, new Vector2(x + 20, y + 20), clientColor);

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

        public void UpdateClientPermissions(Client client)
        {
            var msg = server.CreateMessage();
            msg.Write((byte)PacketTypes.Permissions);
            msg.Write((int)client.Permissions);

            server.SendMessage(msg, client.Connection, NetDeliveryMethod.ReliableUnordered);

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
                var banButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Ban", Alignment.BottomRight, "", characterFrame);
                banButton.UserData = character.Name;
                banButton.OnClicked += GameMain.NetLobbyScreen.BanPlayer;

                var rangebanButton = new GUIButton(new Rectangle(0, -25, 100, 20), "Ban range", Alignment.BottomRight, "", characterFrame);
                rangebanButton.UserData = character.Name;
                rangebanButton.OnClicked += GameMain.NetLobbyScreen.BanPlayerRange;

                var kickButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Kick", Alignment.BottomLeft, "", characterFrame);
                kickButton.UserData = character.Name;
                kickButton.OnClicked += GameMain.NetLobbyScreen.KickPlayer;
            }

            return true;
        }

        private void ReadChatMessage(NetIncomingMessage inc)
        {
            Client sender = connectedClients.Find(x => x.Connection == inc.SenderConnection);
            ChatMessage message = ChatMessage.ReadNetworkMessage(inc);
            if (message == null) return;

            List<Client> recipients = new List<Client>();

            foreach (Client c in connectedClients)
            {
                if (!sender.inGame && c.inGame) continue; //people in lobby can't talk to people ingame
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
                        if (!CanUseRadio(sender.Character)) message.Type = ChatMessageType.Default;
                        break;
                }

                recipients.Add(c);
            }

            //SPAM FILTER
            if (sender.ChatSpamTimer > 0.0f)
            {
                //player has already been spamming, stop again
                ChatMessage denyMsg = ChatMessage.Create("", "You have been blocked by the spam filter. Try again after 10 seconds.", ChatMessageType.Server, null);
                sender.ChatSpamTimer = 10.0f;
                SendChatMessage(denyMsg, sender);

                return;
            }
           
            float similarity = 0;
            similarity += sender.ChatSpamSpeed * 0.05f; //the faster messages are being sent, the faster the filter will block
            for (int i = 0; i < sender.ChatMessages.Count; i++)
            {
                float closeFactor = 1.0f / (20.0f - i);

                int levenshteinDist = ToolBox.LevenshteinDistance(message.Text, sender.ChatMessages[i]);
                similarity += Math.Max((message.Text.Length - levenshteinDist) / message.Text.Length * closeFactor, 0.0f);
            }
            
            if (similarity > 5.0f)
            {
                sender.ChatSpamCount++;
                
                if (sender.ChatSpamCount > 3)
                {
                    //kick for spamming too much
                    KickClient(sender, false);
                }
                else
                {
                    ChatMessage denyMsg = ChatMessage.Create("", "You have been blocked by the spam filter. Try again after 10 seconds.", ChatMessageType.Server, null);
                    sender.ChatSpamTimer = 10.0f;
                    SendChatMessage(denyMsg, sender);
                }
                return;
            }

            sender.ChatMessages.Add(message.Text);
            if (sender.ChatMessages.Count > 20)
            {
                sender.ChatMessages.RemoveAt(0);
            }

            if (sender.inGame || (Screen.Selected == GameMain.NetLobbyScreen))
            {
                AddChatMessage(message);
            }
            else
            {
                GameServer.Log(message.TextWithSender, message.Color);
            }
            sender.ChatSpamSpeed += 5.0f;

            foreach (Client c in recipients)
            {
                ReliableMessage msg = c.ReliableChannel.CreateMessage();
                msg.InnerMessage.Write((byte)PacketTypes.Chatmessage);

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
                    AddChatMessage("Player \"" + command + "\" not found!", ChatMessageType.Error);
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

        public void SendChatMessage(ChatMessage chatMessage, Client recipient)
        {
            ReliableMessage msg = recipient.ReliableChannel.CreateMessage();
            msg.InnerMessage.Write((byte)PacketTypes.Chatmessage);

            chatMessage.WriteNetworkMessage(msg.InnerMessage);

            recipient.ReliableChannel.SendMessage(msg, recipient.Connection);
        }

        public void SendChatMessage(ChatMessage chatMessage, List<Client> recipients)
        {
            foreach (Client recipient in recipients)
            {
                SendChatMessage(chatMessage, recipient);
            }  
        }

        private void ReadCharacterData(NetIncomingMessage message)
        {
            Client sender = connectedClients.Find(c => c.Connection == message.SenderConnection);
            if (sender == null) return;

            string name = "";
            Gender gender = Gender.Male;
            int headSpriteId = 0;

            name = sender.name;
            try
            {
                //name            = message.ReadString();
                gender          = message.ReadBoolean() ? Gender.Male : Gender.Female;
                headSpriteId    = message.ReadByte();
            }
            catch
            {
                //name = "";
                gender = Gender.Male;
                headSpriteId = 0;
            }

            /*if (sender.characterInfo != null)
            {
                //clients can't change their character's name once it's been set
                name = sender.characterInfo.Name;
            }*/

            List<JobPrefab> jobPreferences = new List<JobPrefab>();
            int count = message.ReadByte();
            for (int i = 0; i < Math.Min(count, 3); i++)
            {
                string jobName = message.ReadString();
                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);
                if (jobPrefab != null) jobPreferences.Add(jobPrefab);
            }

            sender.characterInfo = new CharacterInfo(Character.HumanConfigFile, name, gender);
            sender.characterInfo.HeadSpriteId = headSpriteId;
            sender.jobPreferences = jobPreferences;
        }

        public void WriteCharacterData(NetOutgoingMessage msg, string name, Character c)
        {
            msg.Write(c.Info == null);
            msg.Write(c.ID);
            msg.Write(c.TeamID);
            msg.Write(c.ConfigPath);

            msg.Write(c.WorldPosition.X);
            msg.Write(c.WorldPosition.Y);

            msg.Write(c.Enabled);

            if (c.Info != null)
            {
                Client client = connectedClients.Find(cl => cl.Character == c);
                if (client != null)
                {
                    msg.Write(true);
                    msg.Write(client.ID);
                }
                else if (myCharacter == c)
                {
                    msg.Write(true);
                    msg.Write((byte)0);
                }
                else
                {
                    msg.Write(false);
                }

                msg.Write(name);

                msg.Write(c is AICharacter);
                msg.Write(c.Info.Gender == Gender.Female);
                msg.Write((byte)c.Info.HeadSpriteId);                            
                msg.Write(c.Info.Job == null ? "" : c.Info.Job.Name);
            
                Item.Spawner.FillNetworkData(msg, c.SpawnItems);
            }
        }

        public void SendCharacterSpawnMessage(Character character, List<NetConnection> recipients = null)
        {
            if (recipients != null && !recipients.Any()) return;

            NetOutgoingMessage message = server.CreateMessage();
            message.Write((byte)PacketTypes.NewCharacter);

            WriteCharacterData(message, character.Name, character);
                        
            SendMessage(message, NetDeliveryMethod.ReliableUnordered, recipients);
        }

        public void SendItemSpawnMessage(List<Item> items, List<NetConnection> recipients = null)
        {
            if (items == null || !items.Any()) return;

            NetOutgoingMessage message = server.CreateMessage();
            message.Write((byte)PacketTypes.NewItem);

            Item.Spawner.FillNetworkData(message, items);            

            SendMessage(message, NetDeliveryMethod.ReliableOrdered, recipients);
        }

        public void SendItemRemoveMessage(List<Item> items, List<NetConnection> recipients = null)
        {
            if (items == null || !items.Any()) return;

            NetOutgoingMessage message = server.CreateMessage();
            message.Write((byte)PacketTypes.RemoveItem);
            Item.Remover.FillNetworkData(message, items);

            SendMessage(message, NetDeliveryMethod.ReliableOrdered, recipients);
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
            SendMessage(msg, (Rand.Int(2) == 0) ? NetDeliveryMethod.ReliableOrdered : NetDeliveryMethod.Unreliable);

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
}
