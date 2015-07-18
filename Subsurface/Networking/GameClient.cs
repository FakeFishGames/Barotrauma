using System;
using System.Diagnostics;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Subsurface.Networking
{


    class GameClient : NetworkMember
    {
        NetClient Client;

        private Character myCharacter;
        private CharacterInfo characterInfo;

        GUIMessageBox reconnectBox;

        private bool connected;

        private int myID;

        List<Client> otherClients;

        private string serverIP;
                
        public Character Character
        {
            get { return myCharacter; }
            set { myCharacter = value; }
        }

        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
        }

        public int ID
        {
            get { return myID; }
        }

        public GameClient(string newName)
        {
            name = newName;

            characterInfo = new CharacterInfo("Content/Characters/Human/human.xml", name);

            otherClients = new List<Client>();
        }

        public void ConnectToServer(string hostIP)
        {
            serverIP = hostIP;

            myCharacter = Character.Controlled;

            // Create new instance of configs. Parameter is "application Id". It has to be same on client and server.
            NetPeerConfiguration Config = new NetPeerConfiguration("subsurface");
            
            //Config.SimulatedLoss = 0.2f;
            //Config.SimulatedMinimumLatency = 0.25f;

            // Create new client, with previously created configs
            Client = new NetClient(Config);
                      
            NetOutgoingMessage outmsg = Client.CreateMessage();                        
            Client.Start();

            outmsg.Write((byte)PacketTypes.Login);
            outmsg.Write(Game1.Version.ToString());
            outmsg.Write(name);

            // Connect client, to ip previously requested from user 
            try
            {
                Client.Connect(hostIP, 14242, outmsg);
            }
            catch (ArgumentNullException e)
            {
                DebugConsole.ThrowError("Couldn't connect to "+hostIP+". Error message: "+e.Message);
                return;
            }

            // Create timespan of 30ms
            updateInterval = new TimeSpan(0, 0, 0, 0, 200);

            // Set timer to tick every 50ms
            //update = new System.Timers.Timer(50);

            // When time has elapsed ( 50ms in this case ), call "update_Elapsed" funtion
            //update.Elapsed += new System.Timers.ElapsedEventHandler(Update);

            // Funtion that waits for connection approval info from server

            reconnectBox = new GUIMessageBox("CONNECTING", "Connecting to " + serverIP, new string[0]);
            CoroutineManager.StartCoroutine(WaitForStartingInfo());
            
            // Start the timer
            //update.Start();

        }

        private bool RetryConnection(GUIButton button, object obj)
        {
            ConnectToServer(serverIP);
            return true;
        }

        private bool SelectMainMenu(GUIButton button, object obj)
        {
            Disconnect();
            Game1.NetworkMember = null;
            Game1.MainMenuScreen.Select();
            return true;
        }

        // Before main looping starts, we loop here and wait for approval message
        private IEnumerable<Status> WaitForStartingInfo()
        {
            // When this is set to true, we are approved and ready to go
            bool CanStart = false;
            
            DateTime timeOut = DateTime.Now + new TimeSpan(0,0,5);

            // Loop untill we are approved
            while (!CanStart)
            {
                yield return Status.Running;

                if (DateTime.Now > timeOut) break;

                NetIncomingMessage inc;
                // If new messages arrived
                if ((inc = Client.ReadMessage()) == null) continue;

                // Switch based on the message types
                switch (inc.MessageType)
                {
                    // All manually sent messages are type of "Data"
                    case NetIncomingMessageType.Data:
                        byte packetType = inc.ReadByte();
                        if (packetType == (byte)PacketTypes.LoggedIn)
                        {
                            myID = inc.ReadInt32();

                            Game1.NetLobbyScreen.ClearPlayers();

                            //add the names of other connected clients to the lobby screen
                            int existingClients = inc.ReadInt32();
                            for (int i = 1; i <= existingClients; i++)
                            {
                                Client otherClient = new Client(inc.ReadString(), inc.ReadInt32());

                                Game1.NetLobbyScreen.AddPlayer(otherClient);
                            }

                            //add the name of own client to the lobby screen
                            Game1.NetLobbyScreen.AddPlayer(new Client(name, myID));

                            CanStart = true;
                        }
                        else if (packetType == (byte)PacketTypes.KickedOut)
                        {
                            string msg = inc.ReadString();
                            DebugConsole.ThrowError(msg);

                            Game1.MainMenuScreen.Select();
                        }
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        NetConnectionStatus connectionStatus = (NetConnectionStatus)inc.ReadByte();
                        Debug.WriteLine(connectionStatus);

                        if (connectionStatus != NetConnectionStatus.Connected)
                        {
                            string denyMessage = inc.ReadString();
                            DebugConsole.ThrowError(denyMessage);
                        }

                        break;
                    default:
                        Console.WriteLine(inc.ReadString() + " Strange message");
                        break;
                }                
            }

            if (reconnectBox != null)
            {
                reconnectBox.Close(null, null);
                reconnectBox = null;
            }

            if (Client.ConnectionStatus != NetConnectionStatus.Connected)
            {
                var reconnect = new GUIMessageBox("CONNECTION FAILED", "Failed to connect to server.", new string[] { "Retry", "Cancel" });
                reconnect.Buttons[0].OnClicked += RetryConnection;
                reconnect.Buttons[0].OnClicked += reconnect.Close;
                reconnect.Buttons[1].OnClicked += SelectMainMenu;
                reconnect.Buttons[1].OnClicked += reconnect.Close;
            }
            else
            {
                if (Screen.Selected == Game1.MainMenuScreen) Game1.NetLobbyScreen.Select();
                connected = true;
            }

            yield return Status.Success;
        }

        public override void Update()
        {
            //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
            //if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.K))
            //{
            //    SendRandomData();
            //}
            //if (PlayerInput.KeyDown(Microsoft.Xna.Framework.Input.Keys.L))
            //{
            //    ConnectToServer(serverIP);
            //}


            if (gameStarted) inGameHUD.Update((float)Physics.step);

            if (!connected || updateTimer > DateTime.Now) return;
            
            if (Client.ConnectionStatus == NetConnectionStatus.Disconnected && reconnectBox==null)
            {
                reconnectBox = new GUIMessageBox("CONNECTION LOST", "You have been disconnected from the server. Reconnecting...", new string[0]);
                connected = false;
                ConnectToServer(serverIP);
                return;
            }
            else if (reconnectBox!=null)
            {
                reconnectBox.Close(null,null);
                reconnectBox = null;
            }

            if (myCharacter != null)
            {
                if (myCharacter.IsDead)
                {
                    Character.Controlled = null;
                    Game1.GameScreen.Cam.TargetPos = Vector2.Zero;
                    Game1.GameScreen.Cam.Zoom = 1.0f;
                }
                else
                {
                    if (gameStarted) new NetworkEvent(myCharacter.ID, true);
                }
            }
                          
            foreach (NetworkEvent networkEvent in NetworkEvent.events)
            {
                NetOutgoingMessage message = Client.CreateMessage();
                message.Write((byte)PacketTypes.NetworkEvent);


                if (networkEvent.FillData(message))
                {
                    Client.SendMessage(message,
                        (networkEvent.IsImportant) ? NetDeliveryMethod.ReliableUnordered : NetDeliveryMethod.Unreliable);
                }
            }
                    
            NetworkEvent.events.Clear();
            
            CheckServerMessages();

            // Update current time
            updateTimer = DateTime.Now + updateInterval;
            
            
        }

        /// <summary>
        /// Check for new incoming messages from server
        /// </summary>
        private void CheckServerMessages()
        {
            // Create new incoming message holder
            NetIncomingMessage inc;
            
            while ((inc = Client.ReadMessage()) != null)
            {
                if (inc.MessageType != NetIncomingMessageType.Data) continue;
                
                switch (inc.ReadByte())
                {
                    case (byte)PacketTypes.StartGame:
                        if (gameStarted) continue;

                        if (this.Character != null) Character.Remove();

                        int seed = inc.ReadInt32();
                        Rand.SetSyncedSeed(seed);

                        string levelSeed = inc.ReadString();

                        string mapName = inc.ReadString();
                        string mapHash = inc.ReadString();

                        Game1.NetLobbyScreen.TrySelectMap(mapName, mapHash);
                        
                        double durationMinutes = inc.ReadDouble();

                        TimeSpan duration = new TimeSpan(0,(int)durationMinutes,0);

                        //int gameModeIndex = inc.ReadInt32();
                        Game1.GameSession = new GameSession(Submarine.Loaded);
                        Game1.GameSession.StartShift(duration, levelSeed);

                        //myCharacter = ReadCharacterData(inc);
                        //Character.Controlled = myCharacter;                       

                        int count = inc.ReadInt32();
                        for (int n = 0; n < count; n++)
                        {
                            int id = inc.ReadInt32();
                            Character newCharacter = ReadCharacterData(inc);

                            if (id == myID)
                            {
                                myCharacter = newCharacter;
                                Character.Controlled = myCharacter;   
                            }
                        }

                        gameStarted = true;

                        Game1.GameScreen.Select();

                        AddChatMessage("Press TAB to chat", ChatMessageType.Server);

                        break;
                    case (byte)PacketTypes.EndGame:
                        string endMessage = inc.ReadString();
                        EndGame(endMessage);
                        break;
                    case (byte)PacketTypes.PlayerJoined:

                        Client otherClient = new Client(inc.ReadString(), inc.ReadInt32());

                        Game1.NetLobbyScreen.AddPlayer(otherClient);

                        AddChatMessage(otherClient.name + " has joined the server", ChatMessageType.Server);

                        break;
                    case (byte)PacketTypes.PlayerLeft:
                        int leavingID = inc.ReadInt32();

                        AddChatMessage(inc.ReadString(), ChatMessageType.Server);
                        Game1.NetLobbyScreen.RemovePlayer(otherClients.Find(c => c.ID==leavingID));
                        break;

                    case (byte)PacketTypes.KickedOut:
                        string msg = inc.ReadString();

                        new GUIMessageBox("KICKED", msg);

                        Game1.MainMenuScreen.Select();

                        break;
                    case (byte)PacketTypes.Chatmessage:
                        ChatMessageType messageType = (ChatMessageType)inc.ReadByte();
                        AddChatMessage(inc.ReadString(), messageType);                        
                        break;
                    case (byte)PacketTypes.NetworkEvent:
                        //read the data from the message and update client state accordingly
                        if (!gameStarted) break;
                        NetworkEvent.ReadData(inc);
                        break;
                    case (byte)PacketTypes.UpdateNetLobby:
                        if (gameStarted) continue;
                        Game1.NetLobbyScreen.ReadData(inc);
                        break;
                    case (byte)PacketTypes.Traitor:
                        string targetName = inc.ReadString();

                        new GUIMessageBox("You are the Traitor!", "Your secret task is to assassinate " + targetName + "!");

                        break;
                }

                
            }
        }

        public void EndGame(string endMessage)
        {
            Submarine.Unload();

            Game1.NetLobbyScreen.Select();

            if (Game1.GameSession!=null) Game1.GameSession.EndShift("");

            new GUIMessageBox("The round has ended", endMessage);

            myCharacter = null;

            gameStarted = false;
        }

        public override void Disconnect()
        {
            NetOutgoingMessage msg = Client.CreateMessage();
            msg.Write((byte)PacketTypes.PlayerLeft);

            Client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
            Client.Shutdown("");
        }

        public void SendCharacterData()
        {
            if (characterInfo == null) return;

            NetOutgoingMessage msg = Client.CreateMessage();
            msg.Write((byte)PacketTypes.CharacterInfo);
            msg.Write(characterInfo.Name);
            msg.Write(characterInfo.Gender == Gender.Male);
            msg.Write(characterInfo.HeadSpriteId);

            var jobPreferences = Game1.NetLobbyScreen.JobPreferences;
            int count = Math.Min(jobPreferences.Count, 3);
            msg.Write(count);
            for (int i = 0; i < count; i++ )
            {
                msg.Write(jobPreferences[i].Name);
            }

            Client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        private Character ReadCharacterData(NetIncomingMessage inc)
        {
            string newName      = inc.ReadString();
            int ID              = inc.ReadInt32();
            bool isFemale       = inc.ReadBoolean();
            int inventoryID     = inc.ReadInt32();

            int headSpriteID    = inc.ReadInt32();
            
            Vector2 position    = new Vector2(inc.ReadFloat(), inc.ReadFloat());

            string jobName = inc.ReadString();
            JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);

            if (inc.Position > inc.LengthBits)
            {
                return null;
            }

            CharacterInfo ch = new CharacterInfo("Content/Characters/Human/human.xml", newName, isFemale ? Gender.Female : Gender.Male, jobPrefab);
            ch.HeadSpriteId = headSpriteID;

            WayPoint closestWaypoint = null;
            float closestDist = 0.0f;
            foreach (WayPoint wp in WayPoint.WayPointList)
            {
                float dist = Vector2.Distance(wp.SimPosition, position);
                if (closestWaypoint != null && dist > closestDist) continue;
                
                closestWaypoint = wp;
                closestDist = dist;
                continue;                
            }

            Character character = (closestWaypoint == null) ?
                new Character(ch, position) :
                new Character(ch, closestWaypoint);

            character.ID = ID;
            character.Inventory.ID = inventoryID;

            character.GiveJobItems(closestWaypoint);

            return character;
        }

        public override void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Default)
        {
            //AddChatMessage(message);

            type = (gameStarted && myCharacter != null && myCharacter.IsDead) ? ChatMessageType.Dead : ChatMessageType.Default;

            NetOutgoingMessage msg = Client.CreateMessage();
            msg.Write((byte)PacketTypes.Chatmessage);
            msg.Write((byte)type);
            msg.Write(message);

            Client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// sends some random data to the server (can be a networkevent or just something completely random)
        /// use for debugging purposes
        /// </summary>
        public void SendRandomData()
        {
            NetOutgoingMessage msg = Client.CreateMessage();
            switch (Rand.Int(5))
            {
                case 0:
                    msg.Write((byte)PacketTypes.NetworkEvent);
                    msg.Write((byte)NetworkEventType.UpdateEntity);
                    msg.Write(Rand.Int(MapEntity.mapEntityList.Count));
                    break;
                case 1:
                    msg.Write((byte)PacketTypes.NetworkEvent);
                    msg.Write((byte)Enum.GetNames(typeof(NetworkEventType)).Length);
                    msg.Write(Rand.Int(MapEntity.mapEntityList.Count));
                    break;
                case 2:
                    msg.Write((byte)PacketTypes.NetworkEvent);
                    msg.Write((byte)NetworkEventType.UpdateComponent);
                    msg.Write((int)Item.itemList[Rand.Int(Item.itemList.Count)].ID);
                    msg.Write(Rand.Int(8));
                    break;
                case 3:
                    msg.Write((byte)Enum.GetNames(typeof(PacketTypes)).Length);
                    break;
            }

            int byteCount = Rand.Int(100);
            for (int i = 0; i<byteCount; i++)
            {
                msg.Write((Rand.Int(2)==0) ? true : false);
            }


            Client.SendMessage(msg, (Rand.Int(2)==0) ? NetDeliveryMethod.ReliableOrdered : NetDeliveryMethod.Unreliable);
        }

    }
}
