using System;
using System.Diagnostics;
using Lidgren.Network;
using Microsoft.Xna.Framework;

namespace Subsurface.Networking
{


    class GameClient : NetworkMember
    {
        NetClient Client;

        private Character myCharacter;
        private CharacterInfo characterInfo;
                
        string name;

        // Create timer that tells client, when to send update
       // System.Timers.Timer update;

        public Character Character
        {
            get { return myCharacter; }
            set { myCharacter = value; }
        }

        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
        }

        public string Name
        {
            get { return name; }
            set 
            {
                if (string.IsNullOrEmpty(name)) return;
                name = value; 
            }
        }

        public GameClient(string newName)
        {
            name = newName;

            characterInfo = new CharacterInfo("Content/Characters/Human/human.xml", name);
        }

        public bool ConnectToServer(string hostIP)
        {
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
            outmsg.Write(Game1.version.ToString());
            outmsg.Write(name);

            // Connect client, to ip previously requested from user 
            try
            {
                Client.Connect(hostIP, 14242, outmsg);
            }
            catch (ArgumentNullException e)
            {
                DebugConsole.ThrowError("Couldn't connect to "+hostIP+". Error message: "+e.Message);
                return false;
            }

            // Create timespan of 30ms
            updateInterval = new TimeSpan(0, 0, 0, 0, 200);

            // Set timer to tick every 50ms
            //update = new System.Timers.Timer(50);

            // When time has elapsed ( 50ms in this case ), call "update_Elapsed" funtion
            //update.Elapsed += new System.Timers.ElapsedEventHandler(Update);

            // Funtion that waits for connection approval info from server
            WaitForStartingInfo();

            if (Client.ConnectionStatus!=NetConnectionStatus.Connected)
            {
                DebugConsole.ThrowError("Couldn't connect to server");
                return false;
            }
            else
            {
                return true;
            }

            // Start the timer
            //update.Start();


        }

        // Before main looping starts, we loop here and wait for approval message
        private void WaitForStartingInfo()
        {
            // When this is set to true, we are approved and ready to go
            bool CanStart = false;
            
            DateTime timeOut = DateTime.Now + new TimeSpan(0,0,5);

            // Loop untill we are approved
            while (!CanStart)
            {
                if (DateTime.Now>timeOut) return;

                NetIncomingMessage inc;
                // If new messages arrived
                if ((inc = Client.ReadMessage()) == null) continue;

                // Switch based on the message types
                switch (inc.MessageType)
                {
                    // All manually sent messages are type of "Data"
                    case NetIncomingMessageType.Data:
                        if (inc.ReadByte() == (byte)PacketTypes.LoggedIn)
                        {
                            //add the names of other connected clients to the lobby screen
                            int existingClients = inc.ReadInt32();
                            for (int i = 1; i <= existingClients; i++)
                            {
                                Game1.NetLobbyScreen.AddPlayer(inc.ReadString());
                            }

                            //add the name of own client to the lobby screen
                            Game1.NetLobbyScreen.AddPlayer(name);

                            CanStart = true;                        
                        }
                        else if (inc.ReadByte() == (byte)PacketTypes.KickedOut)
                        {
                            string msg = inc.ReadString();
                            DebugConsole.ThrowError(msg);

                            Game1.MainMenuScreen.Select();
                        }
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        Debug.WriteLine((NetConnectionStatus)inc.ReadByte());

                        break;
                    default:
                        // Should not happen and if happens, don't care
                        Console.WriteLine(inc.ReadString() + " Strange message");
                        break;
                }
                
            }
        }

        public void Update()
        {
            if (updateTimer > DateTime.Now) return;
            
            if (myCharacter!=null)
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

                        int seed = inc.ReadInt32();
                        Game1.random = new Random(seed);

                        string mapName = inc.ReadString();
                        string mapHash = inc.ReadString();

                        Game1.NetLobbyScreen.TrySelectMap(mapName, mapHash);


                        //Map.Load(mapFile);



                        double durationMinutes = inc.ReadDouble();

                        TimeSpan duration = new TimeSpan(0,(int)durationMinutes,0);

                        //int gameModeIndex = inc.ReadInt32();
                        Game1.GameSession = new GameSession(Map.Loaded, duration);
                        Game1.GameSession.StartShift(1);

                        myCharacter = ReadCharacterData(inc);
                        Character.Controlled = myCharacter;                       

                        int count = inc.ReadInt32();
                        for (int n = 0; n < count; n++)
                        {
                            ReadCharacterData(inc);
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

                        Client otherClient = new Client();
                        otherClient.name = inc.ReadString();

                        Game1.NetLobbyScreen.AddPlayer(otherClient.name);

                        //string newPlayerName = inc.ReadString();
                        //int newPlayerID = inc.ReadInt32();

                        //CharacterInfo ch = new CharacterInfo("Content/Characters/Human/human.xml", newPlayerName);
                        //ch.ID = newPlayerID;

                        //Character.newCharacterQueue.Enqueue(ch);

                        AddChatMessage(otherClient.name + " has joined the server", ChatMessageType.Server);

                        break;
                    case (byte)PacketTypes.PlayerLeft:
                        string leavingName = inc.ReadString();

                        AddChatMessage(inc.ReadString(), ChatMessageType.Server);
                        Game1.NetLobbyScreen.RemovePlayer(leavingName);
                        break;

                    case (byte)PacketTypes.KickedOut:
                        string msg= inc.ReadString();

                        DebugConsole.ThrowError(msg);

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

                        Game1.GameSession.NewChatMessage("You are an agent of Ordo Europae", messageColor[(int)ChatMessageType.Server]);
                        Game1.GameSession.NewChatMessage("Your secret task is to assassinate " + targetName + "!", messageColor[(int)ChatMessageType.Server]);
                        break;
                }

                
            }
        }

        public void EndGame(string endMessage)
        {
            Map.Unload();

            Game1.NetLobbyScreen.Select();

            if (Game1.GameSession!=null) Game1.GameSession.EndShift(null, null);
            
            DebugConsole.ThrowError(endMessage);

            myCharacter = null;

            gameStarted = false;
        }

        public void Disconnect()
        {
            NetOutgoingMessage msg = Client.CreateMessage();
            msg.Write((byte)PacketTypes.PlayerLeft);

            Client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public void SendCharacterData()
        {
            if (characterInfo == null) return;

            NetOutgoingMessage msg = Client.CreateMessage();
            msg.Write((byte)PacketTypes.CharacterInfo);
            msg.Write(characterInfo.name);
            msg.Write(characterInfo.gender == Gender.Male);

            Client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);

        }

        private Character ReadCharacterData(NetIncomingMessage inc)
        {
            string newName = inc.ReadString();
            int ID = inc.ReadInt32();
            bool isFemale = inc.ReadBoolean();
            int inventoryID = inc.ReadInt32();
            Vector2 position = new Vector2(inc.ReadFloat(), inc.ReadFloat());

            CharacterInfo ch = new CharacterInfo("Content/Characters/Human/human.xml", newName, isFemale ? Gender.Female : Gender.Male);
            Character character = new Character(ch, position);
            character.ID = ID;
            character.Inventory.ID = inventoryID;

            return character;
        }

        public void SendChatMessage(string message)
        {
            //AddChatMessage(message);

            ChatMessageType type = (gameStarted && myCharacter != null && myCharacter.IsDead) ? ChatMessageType.Dead : ChatMessageType.Default;

            NetOutgoingMessage msg = Client.CreateMessage();
            msg.Write((byte)PacketTypes.Chatmessage);
            msg.Write((byte)type);
            msg.Write(message);

            Client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

    }
}
