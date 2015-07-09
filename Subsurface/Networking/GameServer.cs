
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lidgren.Network;
using Microsoft.Xna.Framework;

namespace Subsurface.Networking
{
    class GameServer : NetworkMember
    {
        NetServer Server;
        NetPeerConfiguration Config;
        
        public List<Client> connectedClients = new List<Client>();

        TimeSpan SparseUpdateInterval = new TimeSpan(0, 0, 0, 1);
        DateTime sparseUpdateTimer;

        Client myClient;

        public GameServer()
        {
            name = "Server";

            Config = new NetPeerConfiguration("subsurface");

            Config.Port = 14242;
            
            //Config.SimulatedLoss = 0.2f;
            //Config.SimulatedMinimumLatency = 0.25f;

            Config.MaximumConnections = 10;
            
            Config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
                        
            try
            {
                Server = new NetServer(Config);    
                Server.Start();
            }

            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't start the server", e);
            }

            
            updateInterval = new TimeSpan(0, 0, 0, 0, 30);

            DebugConsole.NewMessage("Server started", Color.Green);

        }

        public override void Update()
        {
            // Server.ReadMessage() Returns new messages, that have not yet been read.
            // If "inc" is null -> ReadMessage returned null -> Its null, so dont do this :)

            NetIncomingMessage inc = Server.ReadMessage();
            if (inc != null) ReadMessage(inc);

            // if 30ms has passed
            if (updateTimer < DateTime.Now)
            {
                if (Server.ConnectionsCount > 0)
                {
                    if (sparseUpdateTimer < DateTime.Now) SparseUpdate();

                    SendNetworkEvents();
                }

                updateTimer = DateTime.Now + updateInterval;
            }
        }

        private void ReadMessage(NetIncomingMessage inc)
        {
            NetOutgoingMessage outmsg;

            switch (inc.MessageType)
            {
                case NetIncomingMessageType.ConnectionApproval:
                    if (inc.ReadByte() != (byte)PacketTypes.Login) break;
                    
                    DebugConsole.NewMessage("New player has joined the server", Color.White);

                    //Character ch = new Character("Content/Characters/Human/human.xml");

                    string version = inc.ReadString();
                    string name = inc.ReadString();

                    int id = 1;
                    while (connectedClients.Find(c=>c.ID==id)!=null)
                    {
                        id++;
                    }

                    Client newClient = new Client(name, id);
                    newClient.Connection = inc.SenderConnection;
                    newClient.version = version;

                    connectedClients.Add(newClient);
                    
                    inc.SenderConnection.Approve();
                    break;
                case NetIncomingMessageType.StatusChanged:
                    Debug.WriteLine(inc.SenderConnection + " status changed. " + (NetConnectionStatus)inc.SenderConnection.Status);
                    if (inc.SenderConnection.Status == NetConnectionStatus.Connected)
                    {
                        Client sender = connectedClients.Find(x => x.Connection == inc.SenderConnection);

                        if (sender == null) break;

                        if (sender.version != Game1.Version.ToString())
                        {
                            DisconnectClient(sender, sender.name+" was unable to connect to the server (nonmatching game version)", 
                                "Subsurface version " + Game1.Version + " required to connect to the server (Your version: " + sender.version + ")");

                        }
                        else
                        {
                            AssignJobs();

                            Game1.NetLobbyScreen.AddPlayer(sender);

                            // Notify the client that they have logged in
                            outmsg = Server.CreateMessage();

                            outmsg.Write((byte)PacketTypes.LoggedIn);

                            outmsg.Write(sender.ID);

                            //notify the client about other clients already logged in
                            outmsg.Write((myClient == null) ? connectedClients.Count - 1 : connectedClients.Count);
                            foreach (Client c in connectedClients)
                            {
                                if (c.Connection == inc.SenderConnection) continue;
                                outmsg.Write(c.name);
                                outmsg.Write(c.ID);
                            }

                            if (myClient != null) outmsg.Write(myClient.name);

                            Server.SendMessage(outmsg, inc.SenderConnection, NetDeliveryMethod.ReliableUnordered, 0);


                            //notify other clients about the new client
                            outmsg = Server.CreateMessage();
                            outmsg.Write((byte)PacketTypes.PlayerJoined);

                            outmsg.Write(sender.name);
                            outmsg.Write(sender.ID);
                        
                            //send the message to everyone except the client who just logged in
                            SendMessage(outmsg, NetDeliveryMethod.ReliableUnordered, inc.SenderConnection);

                            AddChatMessage(sender.name + " has joined the server", ChatMessageType.Server);

                            UpdateNetLobby(null);
                        }
                    }
                    else if (inc.SenderConnection.Status == NetConnectionStatus.Disconnected)
                    {
                        DisconnectClient(inc.SenderConnection);
                    }
                    


                    break;
                case NetIncomingMessageType.Data:

                    switch (inc.ReadByte())
                    {
                        case (byte)PacketTypes.NetworkEvent:
                            if (!gameStarted) break;
                            if (!NetworkEvent.ReadData(inc)) break;

                            outmsg = Server.CreateMessage();
                            outmsg.Write(inc);

                            List<NetConnection> recipients = new List<NetConnection>();

                            foreach (Client client in connectedClients)
                            {
                                if (client.Connection == inc.SenderConnection) continue;
                                if (!client.inGame) continue;

                                recipients.Add(client.Connection);  
                            }

                            if (recipients.Count == 0) break;
                            Server.SendMessage(outmsg, recipients, inc.DeliveryMethod, 0);  
                            
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
                    }
                    break;
                case NetIncomingMessageType.WarningMessage:
                    Debug.WriteLine(inc.ReadString());
                    break;
            }
        }

        private void SparseUpdate()
        {
            foreach (Character c in Character.CharacterList)
            {
                bool isClient = false;
                foreach (Client client in connectedClients)
                {
                    if (client.character != c) continue;
                    isClient = true;
                    break;
                }

                if (!isClient)
                {
                    c.LargeUpdateTimer = 0;
                    new NetworkEvent(c.ID, false);
                }
            }

            new NetworkEvent(Submarine.Loaded.ID, false);

            sparseUpdateTimer = DateTime.Now + SparseUpdateInterval;
        }

        private void SendMessage(NetOutgoingMessage msg, NetDeliveryMethod deliveryMethod, NetConnection excludedConnection)
        {
            List<NetConnection> recipients = new List<NetConnection>();

            foreach (Client client in connectedClients)
            {
                if (client.Connection != excludedConnection) recipients.Add(client.Connection);                
            }

            if (recipients.Count == 0) return;

            Server.SendMessage(msg, recipients, deliveryMethod, 0);  
            
        }

        private void SendNetworkEvents()
        {
            if (NetworkEvent.events.Count == 0) return;
                    
            //System.Diagnostics.Debug.WriteLine("*************************");
            foreach (NetworkEvent networkEvent in NetworkEvent.events)  
            {
                //System.Diagnostics.Debug.WriteLine("networkevent "+networkEvent.ID);

                NetOutgoingMessage message = Server.CreateMessage();
                message.Write((byte)PacketTypes.NetworkEvent);
                //if (!networkEvent.IsClient) continue;
                            
                networkEvent.FillData(message);

                if (Server.ConnectionsCount>0)
                {
                    Server.SendMessage(message, Server.Connections, 
                        (networkEvent.IsImportant) ? NetDeliveryMethod.Unreliable : NetDeliveryMethod.ReliableUnordered, 0);  
                }
                            
            }
            NetworkEvent.events.Clear();                       
        }


        public bool StartGame(GUIButton button, object obj)
        {
            int seed = DateTime.Now.Millisecond;
            Rand.SetSyncedSeed(seed);
            
            Submarine selectedMap = Game1.NetLobbyScreen.SelectedMap as Submarine;
            

            //selectedMap.Load();

            Game1.GameSession = new GameSession(selectedMap, Game1.NetLobbyScreen.SelectedMode);
            Game1.GameSession.StartShift(Game1.NetLobbyScreen.GameDuration, Game1.NetLobbyScreen.LevelSeed);
            //EventManager.SelectEvent(Game1.netLobbyScreen.SelectedEvent);

            List<CharacterInfo> characterInfos = new List<CharacterInfo>();

            foreach (Client client in connectedClients)
            {
                client.inGame = true;

                WayPoint spawnPoint = WayPoint.GetRandom(SpawnType.Human);

                if (client.characterInfo==null)
                {
                    client.characterInfo = new CharacterInfo("Content/Characters/Human/human.xml", client.name);
                }
                characterInfos.Add(client.characterInfo);

                //client.character = new Character(client.characterInfo, (spawnPoint == null) ? Vector2.Zero : spawnPoint.SimPosition, true);
            }

            WayPoint[] assignedWayPoints = WayPoint.SelectCrewSpawnPoints(characterInfos);

            for (int i = 0; i < connectedClients.Count; i++ )
            {
                connectedClients[i].character = new Character(
                    connectedClients[i].characterInfo, assignedWayPoints[i], true);
                connectedClients[i].character.GiveJobItems(assignedWayPoints[i]);
            }

            //todo: fix
            if (myClient != null)
            {
                WayPoint spawnPoint = WayPoint.GetRandom(SpawnType.Human);
                CharacterInfo ch = new CharacterInfo("Content/Characters/Human/human.xml", myClient.name);
                myClient.character = new Character(ch, (spawnPoint == null) ? Vector2.Zero : spawnPoint.SimPosition);
            }

            //foreach (Client client in connectedClients)
            //{
            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((byte)PacketTypes.StartGame);

            msg.Write(seed);

            msg.Write(Game1.NetLobbyScreen.LevelSeed);

            msg.Write(Game1.NetLobbyScreen.SelectedMap.Name);
            msg.Write(Game1.NetLobbyScreen.SelectedMap.Hash.MD5Hash);
                
            msg.Write(Game1.NetLobbyScreen.GameDuration.TotalMinutes);

            //WriteCharacterData(msg, client.name, client.character);

            msg.Write((myClient == null) ? connectedClients.Count : connectedClients.Count+1);
            foreach (Client client in connectedClients)
            {
                //if (otherClient == client) continue;
                msg.Write(client.ID);
                WriteCharacterData(msg, client.name, client.character);
            }

            if (myClient!=null)
            {
                WriteCharacterData(msg, myClient.name, myClient.character);
            }

                SendMessage(msg, NetDeliveryMethod.ReliableUnordered, null);
            //}

            gameStarted = true;

            Game1.GameScreen.Cam.TargetPos = Vector2.Zero;

            Game1.GameScreen.Select();

            return true;
        }

        public void EndGame(string endMessage)
        {
            Submarine.Unload();
                      
            gameStarted = false;

            if (connectedClients.Count>0)
            {
                NetOutgoingMessage msg = Server.CreateMessage();
                msg.Write((byte)PacketTypes.EndGame);
                msg.Write(endMessage);

                if (Server.ConnectionsCount > 0)
                {
                    Server.SendMessage(msg, Server.Connections, NetDeliveryMethod.ReliableOrdered, 0);
                }

                foreach (Client client in connectedClients)
                {
                    client.character = null;
                    client.inGame = false;
                }
            }

            Game1.NetLobbyScreen.Select();

            DebugConsole.ThrowError(endMessage);
        }

        private void DisconnectClient(NetConnection senderConnection)
        {
            Client client = connectedClients.Find(x => x.Connection == senderConnection);
            if (client != null) DisconnectClient(client);
        }

        private void DisconnectClient(Client client, string msg = "", string targetmsg = "")
        {
            if (client == null) return;

            if (gameStarted && client.character != null) client.character.Kill(true);

            if (msg == "") msg = client.name + " has left the server";
            if (targetmsg == "") targetmsg = "You have left the server";
            
            NetOutgoingMessage outmsg = Server.CreateMessage();
            outmsg.Write((byte)PacketTypes.KickedOut);
            outmsg.Write(targetmsg);
            Server.SendMessage(outmsg, client.Connection, NetDeliveryMethod.ReliableUnordered, 0);

            connectedClients.Remove(client);

            outmsg = Server.CreateMessage();
            outmsg.Write((byte)PacketTypes.PlayerLeft);
            outmsg.Write(client.ID);
            outmsg.Write(msg);

            Game1.NetLobbyScreen.RemovePlayer(client);

            if (Server.Connections.Count > 0)
            {
                Server.SendMessage(outmsg, Server.Connections, NetDeliveryMethod.ReliableUnordered, 0);
            }

            AddChatMessage(msg, ChatMessageType.Server);
        }

        public void KickPlayer(string playerName)
        {
            playerName = playerName.ToLower();
            Client client = null;
            foreach (Client c in connectedClients)
            {
                if (c.name.ToLower() != playerName) continue;
                client = c;
                break;               
            }

            if (client == null) return;

            DisconnectClient(client, client.name + " has been kicked from the server", "You have been kicked from the server");
        }

        public void NewTraitor(Client traitor, Client target)
        {
            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((byte)PacketTypes.Traitor);
            msg.Write(target.name);
            if (Server.Connections.Count > 0)
            {
                Server.SendMessage(msg, traitor.Connection, NetDeliveryMethod.ReliableUnordered, 0);
            }
        }

        public bool UpdateNetLobby(object obj)
        {
            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((byte)PacketTypes.UpdateNetLobby);
            Game1.NetLobbyScreen.WriteData(msg);

            if (Server.Connections.Count > 0)
            {
                Server.SendMessage(msg, Server.Connections, NetDeliveryMethod.ReliableUnordered, 0);
            }

            return true;
        }



        public override void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Server)
        {
            AddChatMessage(message, type);

            if (Server.Connections.Count == 0) return;

            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((byte)PacketTypes.Chatmessage);
            msg.Write((byte)type);
            msg.Write(message);

            if (type==ChatMessageType.Dead)
            {
                List<NetConnection> recipients = new List<NetConnection>();
                foreach (Client c in connectedClients)
                {
                    if (c.character != null && c.character.IsDead) recipients.Add(c.Connection);                    
                }
                if (recipients.Count>0)
                {
                    Server.SendMessage(msg, recipients, NetDeliveryMethod.ReliableUnordered, 0);
                }                
            }
            else
            {
                Server.SendMessage(msg, Server.Connections, NetDeliveryMethod.ReliableUnordered, 0);
            }
            
        }

        private void ReadCharacterData(NetIncomingMessage message)
        {
            string name         = message.ReadString();
            Gender gender       = message.ReadBoolean() ? Gender.Male : Gender.Female;
            int headSpriteId    = message.ReadInt32();


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

                c.characterInfo = new CharacterInfo("Content/Characters/Human/human.xml", name, gender);
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
            message.Write(character.Inventory.ID);

            message.Write(character.Info.HeadSpriteId);

            message.Write(character.SimPosition.X);
            message.Write(character.SimPosition.Y);

            message.Write(character.Info.Job.Name);
        }

        private void AssignJobs()
        {
            List<Client> unassigned = new List<Client>(connectedClients);

            int[] assignedClientCount = new int[JobPrefab.List.Count];

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

            for (int preferenceIndex = 0; preferenceIndex < 3; preferenceIndex++)
            {
                for (int i = unassigned.Count - 1; i >= 0; i--)
                {
                    int jobIndex = JobPrefab.List.FindIndex(jp => jp == unassigned[i].jobPreferences[preferenceIndex]);

                    //if there's enough crew members assigned to the job already, continue
                    if (assignedClientCount[jobIndex] >= JobPrefab.List[jobIndex].MaxNumber) continue;

                    unassigned[i].assignedJob = JobPrefab.List[jobIndex];

                    assignedClientCount[jobIndex]++;
                    unassigned.RemoveAt(i);
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
                if (preferredClient == null || index < bestPreference)
                {
                    bestPreference = index;
                    preferredClient = c;
                }
            }

            //none of the clients wants the job
            if (forceAssign && preferredClient == null)
            {
                preferredClient = clients[Rand.Int(clients.Count)];
            }

            return preferredClient;
        }

        public override void Disconnect()
        {
            Server.Shutdown("");
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

        public Client(string name, int ID)
        {
            this.name = name;
            this.ID = ID;

            jobPreferences = new List<JobPrefab>(JobPrefab.List.GetRange(0,3));
        }
    }
}
