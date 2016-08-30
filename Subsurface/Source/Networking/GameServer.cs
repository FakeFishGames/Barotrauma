
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

            config.MaximumConnections = maxPlayers;

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
                inGameHUD.Update((float)Physics.step);

                if (respawnManager != null) respawnManager.Update(deltaTime);

                bool isCrewDead =  
                    connectedClients.Find(c => c.Character != null && !c.Character.IsDead)==null &&
                   (myCharacter == null || myCharacter.IsDead);

                //restart if all characters are dead or submarine is at the end of the level
                if ((autoRestart && isCrewDead) 
                    || 
                    (EndRoundAtLevelEnd && Submarine.MainSub != null && Submarine.MainSub.AtEndPosition))
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
                //c.ReliableChannel.Update(deltaTime);

                //slowly reset spam timers
                c.ChatSpamTimer = Math.Max(0.0f, c.ChatSpamTimer - deltaTime);
                c.ChatSpamSpeed = Math.Max(0.0f, c.ChatSpamSpeed - deltaTime);
            }

            NetIncomingMessage inc = null; 
            while ((inc = server.ReadMessage()) != null)
            {
                try
                {
                    
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
                }

                if (server.ConnectionsCount > 0)
                {
                    if (sparseUpdateTimer < DateTime.Now) SparseUpdate();
                    
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
                log.LogFrame.Update(0.016f);
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
                        var radio = message.Sender.Inventory.Items.First(i => i != null && i.GetComponent<WifiComponent>() != null);
                        if (radio == null) message.Type = ChatMessageType.Default;
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

        public void SendChatMessage(ChatMessage chatMessage, Client recipient)
        {
            
        }

        public void SendChatMessage(ChatMessage chatMessage, List<Client> recipients)
        {
            
        }

        private void ReadCharacterData(NetIncomingMessage message)
        {
            Client sender = connectedClients.Find(c => c.Connection == message.SenderConnection);
            if (sender == null) return;

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

            if (sender.characterInfo != null)
            {
                //clients can't change their character's name once it's been set
                name = sender.characterInfo.Name;
            }

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
        

            SendMessage(message, NetDeliveryMethod.ReliableOrdered, recipients);
        }

        public void SendItemRemoveMessage(List<Item> items, List<NetConnection> recipients = null)
        {
            if (items == null || !items.Any()) return;

            NetOutgoingMessage message = server.CreateMessage();
            message.Write((byte)PacketTypes.RemoveItem);
            
            SendMessage(message, NetDeliveryMethod.ReliableOrdered, recipients);
        }

        public void AssignJobs(List<Client> unassigned)
        {
            unassigned = new List<Client>(unassigned);
            
            int[] assignedClientCount = new int[JobPrefab.List.Count];

            if (characterInfo!=null)
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

            foreach (Client client in connectedClients)
            {
                if (client.FileStreamSender != null) client.FileStreamSender.Dispose();
            }

            server.Shutdown("The server has been shut down");
        }
    }
}
