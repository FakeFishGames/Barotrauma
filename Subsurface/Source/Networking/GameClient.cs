using System;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Barotrauma.Networking.ReliableMessages;
using FarseerPhysics;
using System.IO;
using System.Linq;
using System.Text;
using Barotrauma.Items.Components;
using System.ComponentModel;

namespace Barotrauma.Networking
{
    class GameClient : NetworkMember
    {
        private NetClient client;

        private GUIMessageBox reconnectBox;

        private ReliableChannel reliableChannel;

        private FileStreamReceiver fileStreamReceiver;
        private Queue<Pair<string, FileTransferMessageType>> requestFileQueue;

        private GUIButton endRoundButton;
        private GUITickBox endVoteTickBox;

        private ClientPermissions permissions;

        private bool connected;

        private byte myID;

        private List<Client> otherClients;

        private string serverIP;
                
        public byte ID
        {
            get { return myID; }
        }

        public override List<Client> ConnectedClients
        {
            get
            {
                return otherClients;
            }
        }

        public string ActiveFileTransferName
        {
            get { return (fileStreamReceiver == null || fileStreamReceiver.Status == FileTransferStatus.Finished) ? "" : fileStreamReceiver.FileName; }
        }

        public GameClient(string newName)
        {
            endVoteTickBox = new GUITickBox(new Rectangle(GameMain.GraphicsWidth - 170, 20, 20, 20), "End round", Alignment.TopLeft, inGameHUD);
            endVoteTickBox.OnSelected = ToggleEndRoundVote;
            endVoteTickBox.Visible = false;

            endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170, 20, 150, 20), "End round", Alignment.TopLeft, GUI.Style, inGameHUD);
            endRoundButton.OnClicked = (btn, userdata) => 
            {
                if (!permissions.HasFlag(ClientPermissions.EndRound)) return false;

                var msg = client.CreateMessage();

                msg.Write((byte)PacketTypes.EndGame);
                client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);

                return true; 
            };
            endRoundButton.Visible = false;

            newName = newName.Replace(":", "");
            newName = newName.Replace(";", "");

            GameMain.DebugDraw = false;
            Hull.EditFire = false;
            Hull.EditWater = false;

            name = newName;

            requestFileQueue = new Queue<Pair<string, FileTransferMessageType>>();

            characterInfo = new CharacterInfo(Character.HumanConfigFile, name);
            characterInfo.Job = null;

            otherClients = new List<Client>();

            GameMain.NetLobbyScreen = new NetLobbyScreen();
        }

        public void ConnectToServer(string hostIP, string password = "")
        {
            string[] address = hostIP.Split(':');
            if (address.Length==1)
            {
                serverIP = hostIP;
                Port = NetConfig.DefaultPort;
            }
            else
            {
                serverIP = address[0];

                if (!int.TryParse(address[1], out Port))
                {
                    DebugConsole.ThrowError("Invalid port: "+address[1]+"!");
                    Port = NetConfig.DefaultPort;
                }                
            }

            myCharacter = Character.Controlled;

            // Create new instance of configs. Parameter is "application Id". It has to be same on client and server.
            NetPeerConfiguration config = new NetPeerConfiguration("barotrauma");

#if DEBUG
            config.SimulatedLoss = 0.05f;
            config.SimulatedDuplicatesChance = 0.05f;
            config.SimulatedMinimumLatency = 0.1f;
            config.SimulatedRandomLatency = 0.2f;
#endif 

            config.DisableMessageType(NetIncomingMessageType.DebugMessage | NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error);

            client = new NetClient(config);
            netPeer = client;
            reliableChannel = new ReliableChannel(client);
            client.Start();

            NetOutgoingMessage outmsg = client.CreateMessage();            
            outmsg.Write((byte)PacketTypes.Login);


            System.Net.IPEndPoint IPEndPoint = null;
            try
            {
                IPEndPoint = new System.Net.IPEndPoint(NetUtility.Resolve(serverIP), Port);
            }
            catch
            {
                new GUIMessageBox("Could not connect to server", "Failed to resolve address ''"+serverIP+":"+Port+"''. Please make sure you have entered a valid IP address.");
                return;
            }


            // Connect client, to ip previously requested from user 
            try
            {
                client.Connect(IPEndPoint, outmsg);

            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't connect to "+hostIP+". Error message: "+e.Message);
                Disconnect();

                GameMain.ServerListScreen.Select();
                return;
            }
            
            updateInterval = new TimeSpan(0, 0, 0, 0, 150);

            if (reconnectBox==null)
            {
                reconnectBox = new GUIMessageBox("CONNECTING", "Connecting to " + serverIP, new string[] { "Cancel" });

                reconnectBox.Buttons[0].OnClicked += CancelConnect;
                reconnectBox.Buttons[0].OnClicked += reconnectBox.Close;
            }

            String sendPw = "";
            if (password.Length>0)
            {
                sendPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password)));
            }
            CoroutineManager.StartCoroutine(WaitForStartingInfo(sendPw));
            
            // Start the timer
            //update.Start();

        }

        private bool RetryConnection(GUIButton button, object obj)
        {
            if (client != null) client.Shutdown("Disconnecting");
            ConnectToServer(serverIP);
            return true;
        }

        private bool SelectMainMenu(GUIButton button, object obj)
        {
            Disconnect();
            GameMain.NetworkMember = null;
            GameMain.MainMenuScreen.Select();

            GameMain.MainMenuScreen.SelectTab(MainMenuScreen.Tab.LoadGame);

            return true;
        }

        private bool connectCanceled;

        private bool CancelConnect(GUIButton button, object obj)
        {
            connectCanceled = true;
            return true;
        }

        // Before main looping starts, we loop here and wait for approval message
        private IEnumerable<object> WaitForStartingInfo(string password)
        {
            connectCanceled = false;
            // When this is set to true, we are approved and ready to go
            bool CanStart = false;
            
            DateTime timeOut = DateTime.Now + new TimeSpan(0,0,20);

            // Loop until we are approved
            while (!CanStart && !connectCanceled)
            {
                int seconds = DateTime.Now.Second;

                string connectingText = "Connecting to " + serverIP;
                for (int i = 0; i < 1 + (seconds % 3); i++ )
                {
                    connectingText += ".";
                }
                reconnectBox.Text = connectingText;

                yield return CoroutineStatus.Running;

                if (DateTime.Now > timeOut) break;

                NetIncomingMessage inc;
                // If new messages arrived
                if ((inc = client.ReadMessage()) == null) continue;

                try
                {
                    // Switch based on the message types
                    switch (inc.MessageType)
                    {
                        // All manually sent messages are type of "Data"
                        case NetIncomingMessageType.Data:
                            byte packetType = inc.ReadByte();
                            if (packetType == (byte)PacketTypes.LoggedIn)
                            {
                                myID = inc.ReadByte();
                                permissions = (ClientPermissions)inc.ReadInt32();
                                gameStarted = inc.ReadBoolean();
                                bool hasCharacter = inc.ReadBoolean();
                                bool allowSpectating = inc.ReadBoolean();
                                
                                endRoundButton.Visible = permissions.HasFlag(ClientPermissions.EndRound);

                                if (gameStarted && Screen.Selected != GameMain.GameScreen)
                                {                           
                                    new GUIMessageBox("Please wait",
                                        (allowSpectating) ?
                                        "A round is already running, but you can spectate the game while waiting for a respawn shuttle or a new round." :
                                        "A round is already running and the admin has disabled spectating. You will have to wait for a new round to start.");
                                }

                                if (gameStarted && !hasCharacter && myCharacter != null)
                                {
                                    GameMain.NetLobbyScreen.Select();

                                    new GUIMessageBox("Connection timed out", "You were disconnected for too long and your character was deleted. Please wait for another round to start.");
                                }

                                GameMain.NetLobbyScreen.ClearPlayers();

                                //add the names of other connected clients to the lobby screen
                                int clientCount = inc.ReadByte();
                                for (int i = 0; i < clientCount; i++)
                                {
                                    Client otherClient = new Client(inc.ReadString(), inc.ReadByte());

                                    GameMain.NetLobbyScreen.AddPlayer(otherClient.name);
                                    otherClients.Add(otherClient);
                                }

                                List<Submarine> submarines = new List<Submarine>();
                                int subCount = inc.ReadByte();
                                for (int i = 0; i < subCount; i++ )
                                {
                                    string subName = inc.ReadString();
                                    string subHash = inc.ReadString();

                                    var matchingSub = Submarine.SavedSubmarines.Find(s => s.Name == subName);
                                    if (matchingSub != null)
                                    {
                                        submarines.Add(matchingSub);
                                    }
                                    else
                                    {
                                        submarines.Add(new Submarine(Path.Combine(Submarine.SavePath, subName), subHash, false));
                                    }
                                    
                                }
                                GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.SubList, submarines);
                                GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.ShuttleList.ListBox, submarines);

                                //add the name of own client to the lobby screen
                                GameMain.NetLobbyScreen.AddPlayer(name);

                                CanStart = true;

                                NetOutgoingMessage lobbyUpdateRequest = client.CreateMessage();
                                lobbyUpdateRequest.Write((byte)PacketTypes.RequestNetLobbyUpdate);
                                client.SendMessage(lobbyUpdateRequest, NetDeliveryMethod.ReliableUnordered);
                            }
                            break;
                        case NetIncomingMessageType.StatusChanged:

                            NetConnectionStatus connectionStatus = (NetConnectionStatus)inc.ReadByte();
                            DebugConsole.NewMessage("Connection status changed: " + connectionStatus.ToString(), Color.Orange);
                            
                            if (connectionStatus == NetConnectionStatus.Disconnected)
                            {
                                string denyMessage = inc.ReadString();

                                if (denyMessage == "Password required!" || denyMessage == "Wrong password!")
                                {
                                    GameMain.ServerListScreen.JoinServer(serverIP, true, denyMessage);
                                }
                                else
                                {
                                    new GUIMessageBox("Couldn't connect to server", denyMessage);
                                }

                                connectCanceled = true;
                            }
                            else if (connectionStatus == NetConnectionStatus.Connected)
                            {
                                int nonce = inc.SenderConnection.RemoteHailMessage.ReadInt32();

                                var outmsg = client.CreateMessage();

                                string saltedPw = password;
                                saltedPw = saltedPw + Convert.ToString(nonce);
                                saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(saltedPw)));
                                NetEncryption algo = new NetXtea(client, saltedPw);
                                outmsg.Write((byte)PacketTypes.Login);
                                outmsg.Write(saltedPw);
                                outmsg.Write(myID);
                                outmsg.Write(GameMain.Version.ToString());
                                outmsg.Write(GameMain.SelectedPackage.Name);
                                outmsg.Write(GameMain.SelectedPackage.MD5hash.Hash);
                                outmsg.Write(name);

                                outmsg.Encrypt(algo);

                                client.SendMessage(outmsg, NetDeliveryMethod.ReliableUnordered);
                            }

                            break;
                        default:
                            Console.WriteLine(inc.ReadString() + " Strange message");
                            connectCanceled = true;
                            break;
                    }                
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error while connecting to server", e);
                    break;
                }
            }


            if (reconnectBox != null)
            {
                reconnectBox.Close(null, null);
                reconnectBox = null;
            }

            if (connectCanceled) yield return CoroutineStatus.Success;

            if (client.ConnectionStatus != NetConnectionStatus.Connected)
            {
                var reconnect = new GUIMessageBox("CONNECTION FAILED", "Failed to connect to server.", new string[] { "Retry", "Cancel" });

                DebugConsole.NewMessage("Failed to connect to server - connection status: "+client.ConnectionStatus.ToString(), Color.Orange);

                reconnect.Buttons[0].OnClicked += RetryConnection;
                reconnect.Buttons[0].OnClicked += reconnect.Close;
                reconnect.Buttons[1].OnClicked += SelectMainMenu;
                reconnect.Buttons[1].OnClicked += reconnect.Close;
            }
            else
            {
                if (Screen.Selected != GameMain.GameScreen)
                {
                    List<Submarine> subList = GameMain.NetLobbyScreen.GetSubList();

                    GameMain.NetLobbyScreen = new NetLobbyScreen();
                    GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.SubList, subList);
                    GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.ShuttleList.ListBox, subList);
                    
                    GameMain.NetLobbyScreen.Select();
                }
                connected = true;
            }

            yield return CoroutineStatus.Success;
        }

        public override void Update(float deltaTime)
        {
#if DEBUG
            if (PlayerInput.GetKeyboardState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.P)) return;
#endif

            base.Update(deltaTime);

            if (!connected) return;
            
            if (reconnectBox!=null)
            {
                reconnectBox.Close(null,null);
                reconnectBox = null;
            }

            try
            {
                CheckServerMessages();
            }
            catch (Exception e)
            {
#if DEBUG
                DebugConsole.ThrowError("Error while receiving message from server", e);
#endif            
            }
            
            reliableChannel.Update(deltaTime);

            if (gameStarted && respawnManager != null)
            {
                respawnManager.Update(deltaTime);
            }

            if (updateTimer > DateTime.Now) return;

            if (myCharacter != null)
            {
                if (myCharacter.IsDead)
                {
                    //Character.Controlled = null;
                    //GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                }
                else if (gameStarted)
                {
                    new NetworkEvent(NetworkEventType.EntityUpdate, myCharacter.ID, true);
                }
            }

            var message = ComposeNetworkEventMessage(NetworkEventDeliveryMethod.ReliableChannel);
            if (message != null)
            {
                ReliableMessage reliableMessage = reliableChannel.CreateMessage();
                message.Position = 0;
                reliableMessage.InnerMessage.Write(message.ReadBytes(message.LengthBytes));

                reliableChannel.SendMessage(reliableMessage, client.ServerConnection);
            }

            message = ComposeNetworkEventMessage(NetworkEventDeliveryMethod.Unreliable);
            if (message != null) client.SendMessage(message, NetDeliveryMethod.Unreliable);

            message = ComposeNetworkEventMessage(NetworkEventDeliveryMethod.ReliableLidgren);
            if (message != null) client.SendMessage(message, NetDeliveryMethod.ReliableUnordered);
  
            NetworkEvent.Events.Clear();
            
            // Update current time
            updateTimer = DateTime.Now + updateInterval;  
        }

        private CoroutineHandle startGameCoroutine;

        /// <summary>
        /// Check for new incoming messages from server
        /// </summary>
        private void CheckServerMessages()
        {
            // Create new incoming message holder
            NetIncomingMessage inc;

            if (startGameCoroutine != null && CoroutineManager.IsCoroutineRunning(startGameCoroutine)) return;

            if (fileStreamReceiver == null && requestFileQueue.Count > 0)
            {
                var newRequest = requestFileQueue.Dequeue();
                RequestFile(newRequest.First, newRequest.Second);
            }
            
            while ((inc = client.ReadMessage()) != null)
            {
                if (inc.MessageType == NetIncomingMessageType.StatusChanged)
                {
                    NetConnectionStatus connectionStatus = (NetConnectionStatus)inc.ReadByte();
                    DebugConsole.NewMessage("Connection status changed: " + connectionStatus.ToString(), Color.Orange);

                    if (connectionStatus == NetConnectionStatus.Disconnected)
                    {
                        string disconnectMessage = inc.ReadString();
                        if (string.IsNullOrEmpty(disconnectMessage) || disconnectMessage == "Connection timed out")
                        {
                            if (reconnectBox == null)
                            {
                                reconnectBox = new GUIMessageBox("CONNECTION LOST", "You have been disconnected from the server. Reconnecting...", new string[0]);
                                connected = false;
                                ConnectToServer(serverIP);
                            }
                        }
                        else
                        {
                            new GUIMessageBox("Disconnected", disconnectMessage);

                            Disconnect();
                            GameMain.ServerListScreen.Select();
                        }
                    }
                }

                if (inc.MessageType != NetIncomingMessageType.Data) continue;
                
                byte packetType = inc.ReadByte();

                if (packetType == (byte)PacketTypes.ReliableMessage)
                {
                    if (!reliableChannel.CheckMessage(inc)) continue;
                    packetType = inc.ReadByte();
                }

                switch (packetType)
                {
                    case (byte)PacketTypes.CanStartGame:
                        string subName = inc.ReadString();
                        string subHash = inc.ReadString();

                        string shuttleName = inc.ReadString();
                        string shuttleHash = inc.ReadString();

                        if (GameMain.NetLobbyScreen.TrySelectSub(subName,subHash,GameMain.NetLobbyScreen.SubList) &&
                            GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox))
                        {
                            NetOutgoingMessage readyToStartMsg = client.CreateMessage();
                            readyToStartMsg.Write((byte)PacketTypes.StartGame);
                            client.SendMessage(readyToStartMsg, NetDeliveryMethod.ReliableUnordered);
                        }
                        break;
                    case (byte)PacketTypes.StartGame:
                        if (Screen.Selected == GameMain.GameScreen) continue;

                        startGameCoroutine = GameMain.ShowLoading(StartGame(inc), false);

                        break;
                    case (byte)PacketTypes.EndGame:
                        string endMessage = inc.ReadString();
                        CoroutineManager.StartCoroutine(EndGame(endMessage));
                        break;
                    case (byte)PacketTypes.Respawn:
                        if (gameStarted && respawnManager != null) respawnManager.ReadNetworkEvent(inc);
                        break;
                    case (byte)PacketTypes.PlayerJoined:

                        Client otherClient = new Client(inc.ReadString(), inc.ReadByte());

                        GameMain.NetLobbyScreen.AddPlayer(otherClient.name);
                        otherClients.Add(otherClient);

                        AddChatMessage(otherClient.name + " has joined the server", ChatMessageType.Server);

                        break;
                    case (byte)PacketTypes.Permissions:
                        ClientPermissions newPermissions = (ClientPermissions)inc.ReadInt32();

                        if (newPermissions != permissions)
                        {
                            if (GUIMessageBox.MessageBoxes.Count > 0)
                            {
                                var existingMsgBox = GUIMessageBox.MessageBoxes.Peek();
                                if (existingMsgBox.UserData as string == "permissions")
                                {
                                    GUIMessageBox.MessageBoxes.Dequeue();
                                }
                            }

                            //new GUIMessageBox("The host has changed you permissions. New permissions")
                            string msg = "";
                            if (newPermissions == ClientPermissions.None)
                            {
                                msg = "The host has removed all your special permissions.";
                            }
                            else
                            {
                                msg = "Your current permissions:\n";
                                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                                {
                                    if (!newPermissions.HasFlag(permissions) || permission == ClientPermissions.None) continue;

                                    System.Reflection.FieldInfo fi = typeof(ClientPermissions).GetField(permission.ToString());
                                    DescriptionAttribute[] attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                                    msg += "   - " + attributes[0].Description+"\n";
                                }
                            }

                            new GUIMessageBox("Permissions changed", msg).UserData = "permissions";
                        }

                        endRoundButton.Visible = permissions.HasFlag(ClientPermissions.EndRound);

                        break;
                    case (byte)PacketTypes.RequestFile:
                        bool accepted = inc.ReadBoolean();

                        if (!accepted)
                        {
                            new GUIMessageBox("File transfer canceled", inc.ReadString());

                            if (fileStreamReceiver!=null)
                            {
                                fileStreamReceiver.DeleteFile();
                                fileStreamReceiver.Dispose();
                                fileStreamReceiver = null;
                            }
                        }

                        break;
                    case (byte)PacketTypes.FileStream:
                        if (fileStreamReceiver == null)
                        {
                            //todo: unexpected file
                        }
                        else
                        {    
                            fileStreamReceiver.ReadMessage(inc);
                        }
                        
                        break;
                    case (byte)PacketTypes.PlayerLeft:
                        byte leavingID = inc.ReadByte();

                        AddChatMessage(inc.ReadString(), ChatMessageType.Server);
                        Client disconnectedClient = otherClients.Find(c => c.ID == leavingID);

                        if (disconnectedClient != null)
                        {
                            otherClients.Remove(disconnectedClient);
                            GameMain.NetLobbyScreen.RemovePlayer(disconnectedClient.name);
                        }
                        
                        if (!gameStarted) return;

                        List<Character> crew = new List<Character>();
                        foreach (Character c in Character.CharacterList)
                        {
                            if (!c.IsNetworkPlayer || !c.IsHumanoid || c.Info==null) continue;
                            crew.Add(c);
                        }

                        //GameMain.GameSession.CrewManager.CreateCrewFrame(crew);

                        break;
                    case (byte)PacketTypes.Chatmessage:
                        //ChatMessageType messageType = (ChatMessageType)inc.ReadByte();
                        var chatMsg = ChatMessage.ReadNetworkMessage(inc);
                        AddChatMessage(chatMsg);
                        break;
                    case (byte)PacketTypes.NetworkEvent:
                        //read the data from the message and update client state accordingly
                        if (!gameStarted) break;

                        NetworkEvent.ReadMessage(inc);
                        
                        break;
                    case (byte)PacketTypes.UpdateNetLobby:
                        //if (gameStarted) continue;
                        GameMain.NetLobbyScreen.ReadData(inc);
                        break;
                    case (byte)PacketTypes.Traitor:
                        string targetName = inc.ReadString();

                        TraitorManager.CreateStartPopUp(targetName);

                        break;
                    case (byte)PacketTypes.ResendRequest:
                        reliableChannel.HandleResendRequest(inc);
                        break;
                    case (byte)PacketTypes.LatestMessageID:
                        reliableChannel.HandleLatestMessageID(inc);
                        break;
                    case  (byte)PacketTypes.VoteStatus:
                        Voting.ReadData(inc);
                        break;
                    case (byte)PacketTypes.NewItem:
                        Item.Spawner.ReadNetworkData(inc);
                        break;
                    case (byte)PacketTypes.NewCharacter:
                        ReadCharacterData(inc);
                        break;
                    case (byte)PacketTypes.RemoveItem:
                        Item.Remover.ReadNetworkData(inc);
                        break;
                }

                if (packetType == (byte)PacketTypes.StartGame) break;
            }
        }

        private IEnumerable<object> StartGame(NetIncomingMessage inc)
        {
            if (Character != null) Character.Remove();

            endVoteTickBox.Selected = false;

            int seed = inc.ReadInt32();
            string levelSeed = inc.ReadString();

            int missionTypeIndex = inc.ReadByte();

            string subName = inc.ReadString();
            string subHash = inc.ReadString();
            
            string shuttleName = inc.ReadString();
            string shuttleHash = inc.ReadString();

            string modeName = inc.ReadString();

            bool respawnAllowed = inc.ReadBoolean();

            GameModePreset gameMode = GameModePreset.list.Find(gm => gm.Name == modeName);

            if (gameMode == null)
            {
                DebugConsole.ThrowError("Game mode ''" + modeName + "'' not found!");
                yield return CoroutineStatus.Success;
            }

            if (!GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList))
            {
                yield return CoroutineStatus.Success;
            }

            if (!GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox))
            {
                yield return CoroutineStatus.Success;
            }

            Rand.SetSyncedSeed(seed);
            //int gameModeIndex = inc.ReadInt32();

            GameMain.GameSession = new GameSession(GameMain.NetLobbyScreen.SelectedSub, "", gameMode, Mission.MissionTypes[missionTypeIndex]);
            GameMain.GameSession.StartShift(levelSeed);

            if (respawnAllowed) respawnManager = new RespawnManager(this, GameMain.NetLobbyScreen.SelectedShuttle);


            //myCharacter = ReadCharacterData(inc);
            //Character.Controlled = myCharacter;                       

            List<Character> crew = new List<Character>();

            byte characterCount = inc.ReadByte();
            for (int i = 0; i < characterCount; i++)
            {
                ReadCharacterData(inc);
            }
            
            gameStarted = true;

            endVoteTickBox.Visible = Voting.AllowEndVoting && myCharacter != null;

            GameMain.GameScreen.Select();

            AddChatMessage("Press TAB to chat. Use ''r;'' to talk through the radio.", ChatMessageType.Server);

            //GameMain.GameSession.CrewManager.CreateCrewFrame(crew);

            yield return CoroutineStatus.Success;
        }
        

        public IEnumerable<object> EndGame(string endMessage)
        {
            if (!gameStarted) yield return CoroutineStatus.Success;

            if (GameMain.GameSession != null) GameMain.GameSession.gameMode.End(endMessage);

            //var messageBox = new GUIMessageBox("The round has ended", endMessage, 400, 300);

            gameStarted = false;

            Character.Controlled = null;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;

            respawnManager = null;

            float endPreviewLength = 10.0f;

            if (Screen.Selected == GameMain.GameScreen)
            {
                var cinematic = new TransitionCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, endPreviewLength);

                float secondsLeft = endPreviewLength;

                do
                {
                    secondsLeft -= CoroutineManager.UnscaledDeltaTime;

                    yield return CoroutineStatus.Running;
                } while (secondsLeft > 0.0f);
            }

            Submarine.Unload();

            GameMain.NetLobbyScreen.Select();

            myCharacter = null;
            foreach (Client c in otherClients)
            {
                c.Character = null;
            }

            yield return CoroutineStatus.Success;

        }

        public bool HasPermission(ClientPermissions permission)
        {
            return false;// permissions.HasFlag(permission);
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            
            if (fileStreamReceiver != null && 
                (fileStreamReceiver.Status == FileTransferStatus.Receiving || fileStreamReceiver.Status == FileTransferStatus.NotStarted))
            {
                Vector2 pos = new Vector2(GameMain.GraphicsWidth / 2 - 130, GameMain.NetLobbyScreen.InfoFrame.Rect.Y / 2 - 15);
                
                GUI.DrawString(spriteBatch, 
                    pos, 
                    "Downloading " + fileStreamReceiver.FileName, 
                    Color.White, null, 0, GUI.SmallFont);


                GUI.DrawProgressBar(spriteBatch, new Vector2(pos.X, -pos.Y - 12), new Vector2(200, 15), fileStreamReceiver.Progress, Color.Green);

                GUI.DrawString(spriteBatch, pos + new Vector2(5,12),
                    MathUtils.GetBytesReadable((long)fileStreamReceiver.Received) + " / " + MathUtils.GetBytesReadable((long)fileStreamReceiver.FileSize), 
                    Color.White, null, 0, GUI.SmallFont);

                if (GUI.DrawButton(spriteBatch, new Rectangle((int)pos.X + 210, (int)pos.Y+12, 65, 15), "Cancel", new Color(0.47f, 0.13f, 0.15f, 0.08f)))
                {
                    CancelFileTransfer();
                }
            }

            if (!GameMain.DebugDraw) return;

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight * 0.3f);

            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black * 0.7f, true);
            spriteBatch.DrawString(GUI.Font, "Network statistics:", new Vector2(x + 10, y + 10), Color.White);

            if (client.ServerConnection != null)
            {
                spriteBatch.DrawString(GUI.Font, "Ping: " + (int)(client.ServerConnection.AverageRoundtripTime * 1000.0f) + " ms", new Vector2(x + 10, y + 25), Color.White);

                y += 15;

                spriteBatch.DrawString(GUI.SmallFont, "Received bytes: " + client.Statistics.ReceivedBytes, new Vector2(x + 10, y + 45), Color.White);
                spriteBatch.DrawString(GUI.SmallFont, "Received packets: " + client.Statistics.ReceivedPackets, new Vector2(x + 10, y + 60), Color.White);

                spriteBatch.DrawString(GUI.SmallFont, "Sent bytes: " + client.Statistics.SentBytes, new Vector2(x + 10, y + 75), Color.White);
                spriteBatch.DrawString(GUI.SmallFont, "Sent packets: " + client.Statistics.SentPackets, new Vector2(x + 10, y + 90), Color.White);
            }
            else
            {
                spriteBatch.DrawString(GUI.Font, "Disconnected", new Vector2(x + 10, y + 25), Color.White);
            }


        }


        public override void Disconnect()
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)PacketTypes.PlayerLeft);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
            client.Shutdown("");
            GameMain.NetworkMember = null;
        }


        public override bool SelectCrewCharacter(GUIComponent component, object obj)
        {
            var characterFrame = component.Parent.Parent.FindChild("selectedcharacter");

            Character character = obj as Character;
            if (character == null) return false;

            if (character != myCharacter)
            {
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.Character == character);
                if (client == null) return false;
                
                if (HasPermission(ClientPermissions.Ban))
                {
                    var banButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Ban", Alignment.BottomRight, GUI.Style, characterFrame);
                    banButton.UserData = character.Name;
                    banButton.OnClicked += GameMain.NetLobbyScreen.BanPlayer;                    
                }
                if (HasPermission(ClientPermissions.Kick))
                {
                    var kickButton = new GUIButton(new Rectangle(0, 0, 100, 20), "Kick", Alignment.BottomLeft, GUI.Style, characterFrame);
                    kickButton.UserData = character.Name;
                    kickButton.OnClicked += GameMain.NetLobbyScreen.KickPlayer;
                }
                else if (Voting.AllowVoteKick)
                {
                    var kickVoteButton = new GUIButton(new Rectangle(0, 0, 120, 20), "Vote to Kick", Alignment.BottomLeft, GUI.Style, characterFrame);
                
                    if (GameMain.NetworkMember.ConnectedClients != null)
                    {
                        kickVoteButton.Enabled = !client.HasKickVoteFromID(myID);                        
                    }

                    kickVoteButton.UserData = character;
                    kickVoteButton.OnClicked += VoteForKick;
                }                
            }

            return true;
        }

        public void RequestFile(string file, FileTransferMessageType fileType)
        {
            if (fileStreamReceiver!=null)
            {
                var request = new Pair<string, FileTransferMessageType>()
                {
                    First = file,
                    Second = fileType
                };

                requestFileQueue.Enqueue(request);
                return;
            }

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)PacketTypes.RequestFile);
            msg.Write((byte)fileType);

            msg.Write(file);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);

            fileStreamReceiver = new FileStreamReceiver(client, Path.Combine(Submarine.SavePath, "Downloaded"), fileType, OnFileReceived);
        }

        private void OnFileReceived(FileStreamReceiver receiver)
        {
            if (receiver.Status == FileTransferStatus.Error)
            {
                new GUIMessageBox("Error while receiving file from server", receiver.ErrorMessage, 400, 350);
                receiver.DeleteFile();

            }
            else if (receiver.Status == FileTransferStatus.Finished)
            {
                new GUIMessageBox("Download finished", "File ''" + receiver.FileName + "'' was downloaded succesfully.");

                switch (receiver.FileType)
                {
                    case FileTransferMessageType.Submarine:
                            Submarine.SavedSubmarines.RemoveAll(s => s.Name + ".sub" == receiver.FileName);

                        for (int i = 0; i<2; i++)
                        {

                            var textBlock = (i == 0) ?
                                GameMain.NetLobbyScreen.ShuttleList.ListBox.children.Find(c => (c.UserData as Submarine).Name+".sub" == receiver.FileName) :
                                GameMain.NetLobbyScreen.SubList.children.Find(c => (c.UserData as Submarine).Name+".sub" == receiver.FileName);
                            if (textBlock == null) continue;                            

                            (textBlock as GUITextBlock).TextColor = Color.White;

                            var newSub = new Submarine(receiver.FilePath);
                            Submarine.SavedSubmarines.Add(newSub);
                            textBlock.UserData = newSub;
                        }

                        break;
                }
            }

            fileStreamReceiver = null;
        }

        private void CancelFileTransfer()
        {
            fileStreamReceiver.DeleteFile();
            fileStreamReceiver.Dispose();
            fileStreamReceiver = null;

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)PacketTypes.RequestFile);
            msg.Write((byte)FileTransferMessageType.Cancel);
            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public override void KickPlayer(string kickedName, bool ban)
        {
            if (!permissions.HasFlag(ClientPermissions.Kick) && !ban) return;
            if (!permissions.HasFlag(ClientPermissions.Ban) && ban) return;

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)PacketTypes.KickPlayer);
            msg.Write(ban);
            msg.Write(kickedName);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);            
        }

        public bool VoteForKick(GUIButton button, object userdata)
        {
            var votedClient = otherClients.Find(c => c.Character == userdata);
            if (votedClient == null) return false;

            votedClient.AddKickVote(new Client(name, ID));

            if (votedClient == null) return false;

            Vote(VoteType.Kick, votedClient);

            button.Enabled = false;

            return true;
        }

        public void Vote(VoteType voteType, object userData)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)PacketTypes.Vote);
            msg.Write((byte)voteType);

            switch (voteType)
            {
                case VoteType.Sub:
                    msg.Write(((Submarine)userData).Name);
                    break;
                case VoteType.Mode:
                    msg.Write(((GameModePreset)userData).Name);
                    break;
                case VoteType.EndRound:
                    msg.Write((bool)userData);
                    break;
                case VoteType.Kick:
                    Client votedClient = userData as Client;
                    if (votedClient == null) return;

                    msg.Write(votedClient.ID);
                    break;
            }

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public bool SpectateClicked(GUIButton button, object userData)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)PacketTypes.SpectateRequest);
            
            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);

            if (button != null) button.Enabled = false;

            return false;
        }

        public bool ToggleEndRoundVote(GUITickBox tickBox)
        {
            if (!gameStarted) return false;

            if (!Voting.AllowEndVoting || myCharacter==null)
            {
                tickBox.Visible = false;
                return false;
            }

            Vote(VoteType.EndRound, tickBox.Selected);

            return false;
        }



        public void SendCharacterData()
        {
            if (characterInfo == null) return;

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)PacketTypes.CharacterInfo);

            msg.Write(characterInfo.Name);
            msg.Write(characterInfo.Gender == Gender.Male);
            msg.Write((byte)characterInfo.HeadSpriteId);

            var jobPreferences = GameMain.NetLobbyScreen.JobPreferences;
            int count = Math.Min(jobPreferences.Count, 3);
            msg.Write((byte)count);
            for (int i = 0; i < count; i++ )
            {
                msg.Write(jobPreferences[i].Name);
            }

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public Character ReadCharacterData(NetIncomingMessage inc)
        {
            bool noInfo         = inc.ReadBoolean();
            ushort id           = inc.ReadUInt16();
            string configPath   = inc.ReadString();

            Vector2 position    = new Vector2(inc.ReadFloat(), inc.ReadFloat());
                
            bool enabled        = inc.ReadBoolean();

            Character character = null;

            if (noInfo)
            {
                var existingEntity = Entity.FindEntityByID(id);
                if (existingEntity is AICharacter && existingEntity.ID == id)
                {
                    return (Character)existingEntity;
                }

                character = Character.Create(configPath, position, null, true);
                character.ID = id;
            }
            else
            {
                bool hasOwner = inc.ReadBoolean();
                int ownerId = -1;
                if (hasOwner)
                {
                    ownerId = inc.ReadByte();
                }

                string newName      = inc.ReadString();

                bool hasAi          = inc.ReadBoolean();
                bool isFemale       = inc.ReadBoolean();
                int headSpriteID    = inc.ReadByte();
                string jobName      = inc.ReadString();

                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name == jobName);

                CharacterInfo ch = new CharacterInfo(configPath, newName, isFemale ? Gender.Female : Gender.Male, jobPrefab);
                ch.HeadSpriteId = headSpriteID;

                character = Character.Create(configPath, position, ch, ownerId != myID, hasAi);
                character.ID = id;

                if (configPath == Character.HumanConfigFile)
                {
                    GameMain.GameSession.CrewManager.characters.Add(character);
                }

                Item.Spawner.ReadNetworkData(inc);

                if (ownerId == myID)
                {
                    myCharacter = character;
                    Character.Controlled = character;
                    GameMain.LightManager.LosEnabled = true;

                    if (endVoteTickBox != null) endVoteTickBox.Visible = Voting.AllowEndVoting;
                }
                else
                {
                    var characterOwner = otherClients.Find(c => c.ID == ownerId);
                    if (characterOwner != null) characterOwner.Character = character;

                }
            }

            character.Enabled = enabled;

            return character;
        }

        public override void SendChatMessage(string message, ChatMessageType? type = null)
        {
            if (client.ServerConnection == null) return;

            type = ChatMessageType.Default;
                        
            if (Screen.Selected == GameMain.GameScreen && (myCharacter == null || myCharacter.IsDead)) 
            {
                type = ChatMessageType.Dead;
            }
            else
            {
                string command = ChatMessage.GetChatMessageCommand(message, out message).ToLowerInvariant();
                
                if (command=="r" || command=="radio" && CanUseRadio(Character.Controlled)) type = ChatMessageType.Radio;              
            }
            
            var chatMessage = ChatMessage.Create(
                gameStarted && myCharacter != null ? myCharacter.Name : name,
                message, (ChatMessageType)type, gameStarted ? myCharacter : null);

            ReliableMessage msg = reliableChannel.CreateMessage();
            msg.InnerMessage.Write((byte)PacketTypes.Chatmessage);
            chatMessage.WriteNetworkMessage(msg.InnerMessage);

            reliableChannel.SendMessage(msg, client.ServerConnection);
        }

        /// <summary>
        /// sends some random data to the server (can be a networkevent or just something completely random)
        /// use for debugging purposes
        /// </summary>
        //public void SendRandomData()
        //{
        //    NetOutgoingMessage msg = client.CreateMessage();
        //    switch (Rand.Int(5))
        //    {
        //        case 0:
        //            msg.WriteEnum(PacketTypes.NetworkEvent);
        //            msg.WriteEnum(NetworkEventType.EntityUpdate);
        //            msg.Write(Rand.Int(MapEntity.mapEntityList.Count));
        //            break;
        //        case 1:
        //            msg.WriteEnum(PacketTypes.NetworkEvent);
        //            msg.Write((byte)Enum.GetNames(typeof(NetworkEventType)).Length);
        //            msg.Write(Rand.Int(MapEntity.mapEntityList.Count));
        //            break;
        //        case 2:
        //            msg.WriteEnum(PacketTypes.NetworkEvent);
        //            msg.WriteEnum(NetworkEventType.ComponentUpdate);
        //            msg.Write((int)Item.ItemList[Rand.Int(Item.ItemList.Count)].ID);
        //            msg.Write(Rand.Int(8));
        //            break;
        //        case 3:
        //            msg.Write((byte)Enum.GetNames(typeof(PacketTypes)).Length);
        //            break;
        //    }

        //    int bitCount = Rand.Int(100);
        //    for (int i = 0; i<bitCount; i++)
        //    {
        //        msg.Write(Rand.Int(2)==0);
        //    }


        //    client.SendMessage(msg, (Rand.Int(2)==0) ? NetDeliveryMethod.ReliableOrdered : NetDeliveryMethod.Unreliable);
        //}

    }
}
