
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lidgren.Network;
using Microsoft.Xna.Framework;

namespace Subsurface.Networking
{
    class GameServer : NetworkMember
    {

        // Server object
        NetServer Server;
        // Configuration object
        NetPeerConfiguration Config;
        
        public List<Client> connectedClients = new List<Client>();

        //NetIncomingMessage inc;

        const int sparseUpdateInterval = 150;
        int sparseUpdateTimer;

        Client myClient;

        public GameServer()
        {
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

        public void Update()
        {
            // Server.ReadMessage() Returns new messages, that have not yet been read.
            // If "inc" is null -> ReadMessage returned null -> Its null, so dont do this :)

            NetIncomingMessage inc = Server.ReadMessage();
            if (inc != null) ReadMessage(inc);

            // if 30ms has passed
            if ((updateTimer) < DateTime.Now)
            {
                if (Server.ConnectionsCount > 0)
                {
                    if (sparseUpdateTimer <= 0) SparseUpdate();

                    SendNetworkEvents();
                }

                sparseUpdateTimer -= 1;
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

                    Client newClient = new Client();
                    newClient.version = inc.ReadString();
                    newClient.name = inc.ReadString();
                    newClient.Connection = inc.SenderConnection;

                    connectedClients.Add(newClient);
                    
                    inc.SenderConnection.Approve();
                    break;
                case NetIncomingMessageType.StatusChanged:
                    Debug.WriteLine(inc.SenderConnection + " status changed. " + (NetConnectionStatus)inc.SenderConnection.Status);
                    if (inc.SenderConnection.Status == NetConnectionStatus.Connected)
                    {


                        Client sender = connectedClients.Find(x => x.Connection == inc.SenderConnection);

                        if (sender == null) break;

                        if (sender.version != Game1.version.ToString())
                        {
                            DisconnectClient(sender, sender.name+" was unable to connect to the server (nonmatching game version)", 
                                "Subsurface version " + Game1.version + " required to connect to the server (Your version: " + sender.version + ")");

                        }
                        else
                        {

                            Game1.NetLobbyScreen.AddPlayer(sender.name);

                            // Notify the client that they have logged in
                            outmsg = Server.CreateMessage();

                            outmsg.Write((byte)PacketTypes.LoggedIn);

                            //notify the client about other clients already logged in
                            outmsg.Write((myClient == null) ? connectedClients.Count - 1 : connectedClients.Count);
                            foreach (Client c in connectedClients)
                            {
                                if (c.Connection == inc.SenderConnection) continue;
                                outmsg.Write(c.name);
                            }

                            if (myClient != null) outmsg.Write(myClient.name);

                            Server.SendMessage(outmsg, inc.SenderConnection, NetDeliveryMethod.ReliableUnordered, 0);


                            //notify other clients about the new client
                            outmsg = Server.CreateMessage();
                            outmsg.Write((byte)PacketTypes.PlayerJoined);

                            outmsg.Write(sender.name);
                        
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
            foreach (Character c in Character.characterList)
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
                    c.largeUpdateTimer = 0;
                    new NetworkEvent(c.ID, false);
                }
            }

            sparseUpdateTimer = sparseUpdateInterval;
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

                Server.SendMessage(message, Server.Connections, 
                    (networkEvent.IsImportant) ? NetDeliveryMethod.Unreliable : NetDeliveryMethod.ReliableUnordered, 0);                              
            }
            NetworkEvent.events.Clear();                       
        }


        public bool StartGame(GUIButton button, object obj)
        {
            int seed = DateTime.Now.Millisecond;
            Game1.random = new Random(seed);
            
            Map selectedMap = Game1.NetLobbyScreen.SelectedMap as Map;

            selectedMap.Load();

            Game1.GameSession = new GameSession("", false, Game1.NetLobbyScreen.GameDuration, Game1.NetLobbyScreen.SelectedMode);
            Game1.GameSession.StartShift(1);
            //EventManager.SelectEvent(Game1.netLobbyScreen.SelectedEvent);
            
            foreach (Client client in connectedClients)
            {
                client.inGame = true;

                WayPoint spawnPoint = WayPoint.GetRandom(WayPoint.SpawnType.Human);

                if (client.characterInfo==null)
                {
                    client.characterInfo = new CharacterInfo("Content/Characters/Human/human.xml", client.name);
                }

                client.character = new Character(client.characterInfo, (spawnPoint == null) ? Vector2.Zero : spawnPoint.SimPosition, true);
            }

            if (myClient != null)
            {
                WayPoint spawnPoint = WayPoint.GetRandom(WayPoint.SpawnType.Human);
                CharacterInfo ch = new CharacterInfo("Content/Characters/Human/human.xml", myClient.name);
                myClient.character = new Character(ch, (spawnPoint == null) ? Vector2.Zero : spawnPoint.SimPosition);
            }

            foreach (Client client in connectedClients)
            {
                NetOutgoingMessage msg = Server.CreateMessage();
                msg.Write((byte)PacketTypes.StartGame);

                msg.Write(seed);

                msg.Write(Game1.NetLobbyScreen.SelectedMap.Name);
                msg.Write(Game1.NetLobbyScreen.SelectedMap.MapHash.MD5Hash);
                
                msg.Write(Game1.NetLobbyScreen.GameDuration.TotalMinutes);

                WriteCharacterData(msg, client.name, client.character);

                msg.Write((myClient == null) ? connectedClients.Count - 1 : connectedClients.Count);
                foreach (Client otherClient in connectedClients)
                {
                    if (otherClient == client) continue;
                    WriteCharacterData(msg, otherClient.name, otherClient.character);
                }

                if (myClient!=null)
                {
                    WriteCharacterData(msg, myClient.name, myClient.character);
                }

                Server.SendMessage(msg, client.Connection, NetDeliveryMethod.ReliableUnordered, 0);
            }

            gameStarted = true;

            Game1.GameScreen.Cam.TargetPos = Vector2.Zero;

            Game1.GameScreen.Select();

            return true;
        }

        public void EndGame(string endMessage)
        {
            Map.Unload();
                      
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
            outmsg.Write(client.name);
            outmsg.Write(msg);

            Game1.NetLobbyScreen.RemovePlayer(client.name);

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



        public void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Server)
        {
            AddChatMessage(message, type);

            NetOutgoingMessage msg = Server.CreateMessage();
            msg.Write((byte)PacketTypes.Chatmessage);
            msg.Write((byte)type);
            msg.Write(message);

            if (Server.Connections.Count == 0) return;

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
            string name = message.ReadString();
            Gender gender = message.ReadBoolean() ? Gender.Male : Gender.Female;

            foreach (Client c in connectedClients)
            {
                if (c.Connection != message.SenderConnection) continue;
                c.characterInfo = new CharacterInfo("Content/Characters/Human/human.xml", name, gender);
            }
        }

        private void WriteCharacterData(NetOutgoingMessage message, string name, Character character)
        {
            message.Write(name);
            message.Write(character.ID);
            message.Write(character.info.gender==Gender.Female);
            message.Write(character.Inventory.ID);
            message.Write(character.SimPosition.X);
            message.Write(character.SimPosition.Y);
        }
    }

    class Client
    {
        public string name;

        public Character character;
        public CharacterInfo characterInfo;
        public NetConnection Connection { get; set; }
        public string version;
        public bool inGame;
    }
}
