using Barotrauma.Steam;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Barotrauma.Networking
{
    class GameClient : NetworkMember
    {
        private NetClient client;

        private GUIMessageBox reconnectBox, waitInServerQueueBox;

        private GUIButton endRoundButton;
        private GUITickBox endVoteTickBox;

        private ClientPermissions permissions = ClientPermissions.None;
        private List<string> permittedConsoleCommands = new List<string>();

        private bool connected;

        private byte myID;

        private List<Client> otherClients;

        private string serverIP;

        private bool needAuth;
        private bool requiresPw;
        private int nonce;
        private string saltedPw;
        private Facepunch.Steamworks.Auth.Ticket steamAuthTicket;

        private UInt16 lastSentChatMsgID = 0; //last message this client has successfully sent
        private UInt16 lastQueueChatMsgID = 0; //last message added to the queue
        private List<ChatMessage> chatMsgQueue = new List<ChatMessage>();

        public UInt16 LastSentEntityEventID;

        private ClientEntityEventManager entityEventManager;

        private FileReceiver fileReceiver;

        //has the client been given a character to control this round
        public bool HasSpawned;

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

        public FileReceiver FileReceiver
        {
            get { return fileReceiver; }
        }
        
        public bool MidRoundSyncing
        {
            get { return entityEventManager.MidRoundSyncing; }
        }

        public bool AllowDisguises
        {
            get;
            private set;
        }
        
        public GameClient(string newName)
        {
            var buttonContainer = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, inGameHUD.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                CanBeFocused = false
            };

            endRoundButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("EndRound"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (!permissions.HasFlag(ClientPermissions.EndRound)) return false;
                    RequestRoundEnd();
                    return true;
                },
                Visible = false
            };

            endVoteTickBox = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("EndRound"))
            {
                OnSelected = ToggleEndRoundVote,
                Visible = false
            };
            
            showLogButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("ServerLog"))
            {
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (ServerLog.LogFrame == null)
                    {
                        ServerLog.CreateLogFrame();
                    }
                    else
                    {
                        ServerLog.LogFrame = null;
                        GUI.KeyboardDispatcher.Subscriber = null;
                    }
                    return true;
                }
            };

            GameMain.DebugDraw = false;
            Hull.EditFire = false;
            Hull.EditWater = false;

            newName = newName.Replace(":", "").Replace(";", "");
            name = newName;

            entityEventManager = new ClientEntityEventManager(this);

            fileReceiver = new FileReceiver();
            fileReceiver.OnFinished += OnFileReceived;
            fileReceiver.OnTransferFailed += OnTransferFailed;

            characterInfo = new CharacterInfo(Character.HumanConfigFile, name, Gender.None, null)
            {
                Job = null
            };

            otherClients = new List<Client>();

            ServerLog = new ServerLog("");

            ChatMessage.LastID = 0;
            GameMain.NetLobbyScreen = new NetLobbyScreen();
        }

        public void ConnectToServer(string hostIP)
        {
            string[] address = hostIP.Split(':');
            if (address.Length == 1)
            {
                serverIP = hostIP;
                Port = NetConfig.DefaultPort;
            }
            else
            {
                serverIP = string.Join(":", address.Take(address.Length - 1));
                if (!int.TryParse(address[address.Length - 1], out int port))
                {
                    DebugConsole.ThrowError("Invalid port: " + address[address.Length - 1] + "!");
                    Port = NetConfig.DefaultPort;
                }
                else
                {
                    Port = port;
                }
            }

            myCharacter = Character.Controlled;
            ChatMessage.LastID = 0;

            // Create new instance of configs. Parameter is "application Id". It has to be same on client and server.
            NetPeerConfiguration = new NetPeerConfiguration("barotrauma");
            
            NetPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage | NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error);

            client = new NetClient(NetPeerConfiguration);
            NetPeer = client;
            client.Start();

            System.Net.IPEndPoint IPEndPoint = null;
            try
            {
                IPEndPoint = new System.Net.IPEndPoint(NetUtility.Resolve(serverIP), Port);
            }
            catch
            {
                new GUIMessageBox(TextManager.Get("CouldNotConnectToServer"),
                    TextManager.Get("InvalidIPAddress").Replace("[serverip]", serverIP).Replace("[port]", Port.ToString()));
                return;
            }

            NetOutgoingMessage outmsg = client.CreateMessage();
            WriteAuthRequest(outmsg);

            // Connect client, to ip previously requested from user 
            try
            {
                client.Connect(IPEndPoint, outmsg);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't connect to " + hostIP + ". Error message: " + e.Message);
                Disconnect();

                GameMain.ServerListScreen.Select();
                return;
            }

            updateInterval = new TimeSpan(0, 0, 0, 0, 150);

            CoroutineManager.StartCoroutine(WaitForStartingInfo(), "WaitForStartingInfo");
        }

        private bool RetryConnection(GUIButton button, object obj)
        {
            if (client != null) client.Shutdown("Disconnecting");
            ConnectToServer(serverIP);
            return true;
        }

        private bool ReturnToServerList(GUIButton button, object obj)
        {
            Disconnect();

            Submarine.Unload();
            GameMain.NetworkMember = null;
            GameMain.GameSession = null;
            GameMain.ServerListScreen.Select();

            return true;
        }

        private bool connectCancelled;
        private bool CancelConnect(GUIButton button, object obj)
        {
            connectCancelled = true;
            return true;
        }

        // Before main looping starts, we loop here and wait for approval message
        private IEnumerable<object> WaitForStartingInfo()
        {
            requiresPw = false;
            needAuth = true;
            saltedPw = "";

            connectCancelled = false;
            // When this is set to true, we are approved and ready to go
            bool CanStart = false;

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 20);
            DateTime reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, 200);

            // Loop until we are approved
            //TODO: show the name of the server instead of IP when connecting through the server list (more streamer-friendly)
            string connectingText = TextManager.Get("ConnectingTo").Replace("[serverip]", serverIP);
            while (!CanStart && !connectCancelled)
            {
                if (reconnectBox == null)
                {
                    reconnectBox = new GUIMessageBox(TextManager.Get("Connecting"), connectingText, new string[] { TextManager.Get("Cancel") });

                    reconnectBox.Buttons[0].OnClicked += CancelConnect;
                    reconnectBox.Buttons[0].OnClicked += reconnectBox.Close;
                }

                int seconds = DateTime.Now.Second;
                reconnectBox.Text.Text = connectingText;
                for (int i = 0; i < 1 + (seconds % 3); i++)
                {
                    reconnectBox.Text.Text += ".";
                }

                if (DateTime.Now > reqAuthTime)
                {
                    if (needAuth)
                    {
                        //request auth again
                        NetOutgoingMessage reqAuthMsg = client.CreateMessage();
                        WriteAuthRequest(reqAuthMsg);
                        client.SendMessage(reqAuthMsg, NetDeliveryMethod.Unreliable);
                    }
                    else
                    {
                        //request init again
                        NetOutgoingMessage outmsg = client.CreateMessage();
                        outmsg.Write((byte)ClientPacketHeader.REQUEST_INIT);
                        if (requiresPw)
                        {
                            outmsg.Write(saltedPw);
                        }
                        outmsg.Write(GameMain.Version.ToString());

                        var mpContentPackages = GameMain.SelectedPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);
                        outmsg.Write((UInt16)mpContentPackages.Count());
                        foreach (ContentPackage contentPackage in mpContentPackages)
                        {
                            outmsg.Write(contentPackage.Name);
                            outmsg.Write(contentPackage.MD5hash.Hash);
                        }
                        outmsg.Write(name);
                        client.SendMessage(outmsg, NetDeliveryMethod.Unreliable);
                    }
                    reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 1);
                }

                yield return CoroutineStatus.Running;

                if (DateTime.Now > timeOut) break;

                NetIncomingMessage inc;
                // If new messages arrived
                if ((inc = client.ReadMessage()) == null) continue;

                string pwMsg = "Password required";

                try
                {
                    switch (inc.MessageType)
                    {
                        case NetIncomingMessageType.Data:
                            DecompressIncomingMessage(inc);

                            ServerPacketHeader header = (ServerPacketHeader)inc.ReadByte();
                            switch (header)
                            {
                                case ServerPacketHeader.AUTH_RESPONSE:
                                    if (needAuth)
                                    {
                                        if (inc.ReadBoolean())
                                        {
                                            //requires password
                                            nonce = inc.ReadInt32();
                                            requiresPw = true;
                                        }
                                        else
                                        {
                                            requiresPw = false;
                                            reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, 200);
                                        }
                                        needAuth = false; //got auth!
                                    }
                                    break;
                                case ServerPacketHeader.AUTH_FAILURE:
                                    //failed to authenticate, can still use same nonce
                                    pwMsg = inc.ReadString();
                                    requiresPw = true;
                                    break;
                                case ServerPacketHeader.UPDATE_LOBBY:
                                    //server accepted client
                                    ReadLobbyUpdate(inc);
                                    CanStart = true;
                                    break;
                            }
                            break;
                        case NetIncomingMessageType.StatusChanged:
                            NetConnectionStatus connectionStatus = (NetConnectionStatus)inc.ReadByte();
                            if (connectionStatus == NetConnectionStatus.Disconnected)
                            {
                                ReadDisconnectMessage(inc, false);
                                connectCancelled = true;
                            }
                            break;
                    }
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error while connecting to the server", e);
                    break;
                }

                if (requiresPw && !CanStart && !connectCancelled)
                {
                    if (reconnectBox != null)
                    {
                        reconnectBox.Close(null, null);
                        reconnectBox = null;
                    }

                    var msgBox = new GUIMessageBox(pwMsg, "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") });
                    var passwordBox = new GUITextBox(new RectTransform(new Vector2(0.5f, 0.1f), msgBox.InnerFrame.RectTransform, Anchor.Center))
                    {
                        IgnoreLayoutGroups = true,
                        UserData = "password"
                    };

                    var okButton = msgBox.Buttons[0];
                    var cancelButton = msgBox.Buttons[1];

                    while (GUIMessageBox.MessageBoxes.Contains(msgBox))
                    {
                        while (client.ReadMessage() != null)
                        {
                            switch (inc.MessageType)
                            {
                                case NetIncomingMessageType.StatusChanged:
                                    NetConnectionStatus connectionStatus = (NetConnectionStatus)inc.ReadByte();
                                    if (connectionStatus == NetConnectionStatus.Disconnected)
                                    {
                                        ReadDisconnectMessage(inc, false);
                                        msgBox.Close(null, null);
                                        connectCancelled = true;
                                    }
                                    break;
                            }
                        }

                        if (DateTime.Now > reqAuthTime)
                        {
                            //request auth again to prevent timeout
                            NetOutgoingMessage reqAuthMsg = client.CreateMessage();
                            WriteAuthRequest(reqAuthMsg);
                            client.SendMessage(reqAuthMsg, NetDeliveryMethod.Unreliable);
                            reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 3);
                        }

                        okButton.Enabled = !string.IsNullOrWhiteSpace(passwordBox.Text);

                        if (okButton.Selected)
                        {
                            saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(passwordBox.Text)));
                            saltedPw = saltedPw + Convert.ToString(nonce);
                            saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(saltedPw)));

                            timeOut = DateTime.Now + new TimeSpan(0, 0, 20);
                            reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 1);

                            msgBox.Close(null, null);
                            break;
                        }
                        else if (cancelButton.Selected)
                        {
                            msgBox.Close(null, null);
                            connectCancelled = true;
                        }
                        else
                        {
                            yield return CoroutineStatus.Running;
                        }
                    }
                }
            }

            if (reconnectBox != null)
            {
                reconnectBox.Close(null, null);
                reconnectBox = null;
            }

            if (connectCancelled) yield return CoroutineStatus.Success;

            if (client.ConnectionStatus != NetConnectionStatus.Connected)
            {
                var reconnect = new GUIMessageBox(TextManager.Get("ConnectionFailed"), TextManager.Get("CouldNotConnectToServer"), new string[] { TextManager.Get("Retry"), TextManager.Get("Cancel") });

                DebugConsole.NewMessage("Failed to connect to the server - connection status: " + client.ConnectionStatus.ToString(), Color.Orange);

                reconnect.Buttons[0].OnClicked += RetryConnection;
                reconnect.Buttons[0].OnClicked += reconnect.Close;
                reconnect.Buttons[1].OnClicked += ReturnToServerList;
                reconnect.Buttons[1].OnClicked += reconnect.Close;
            }
            else
            {
                if (Screen.Selected != GameMain.GameScreen)
                {
                    GameMain.NetLobbyScreen.Select();
                }
                connected = true;
            }

            yield return CoroutineStatus.Success;
        }

        private void WriteAuthRequest(NetOutgoingMessage outmsg)
        {
            if (SteamManager.IsInitialized && SteamManager.USE_STEAM)
            {
                if (steamAuthTicket == null)
                {
                    steamAuthTicket = SteamManager.GetAuthSessionTicket();
                }

                DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 3);
                while ((steamAuthTicket.Data == null || steamAuthTicket.Data.Length == 0) &&
                    DateTime.Now < timeOut)
                {
                    System.Threading.Thread.Sleep(10);
                }

                outmsg.Write((byte)ClientPacketHeader.REQUEST_STEAMAUTH);
                outmsg.Write(SteamManager.GetSteamID());
                outmsg.Write(steamAuthTicket.Data.Length);
                outmsg.Write(steamAuthTicket.Data);

                DebugConsole.Log("Sending Steam auth message");
                DebugConsole.Log("   Steam ID: " + SteamManager.GetSteamID());
                DebugConsole.Log("   Ticket data: " + steamAuthTicket.Data.Length);
                DebugConsole.Log("   Msg length: " + outmsg.LengthBytes);
            }
            else
            {
                outmsg.Write((byte)ClientPacketHeader.REQUEST_AUTH);
            }
        }

        public override void Update(float deltaTime)
        {
#if DEBUG
            if (PlayerInput.GetKeyboardState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.P)) return;
#endif
            if (gameStarted) SetRadioButtonColor();

            base.Update(deltaTime);

            if (!connected) return;
            
            if (reconnectBox!=null)
            {
                reconnectBox.Close(null,null);
                reconnectBox = null;
            }

#if DEBUG
            try
            {
#endif
                CheckServerMessages();
#if DEBUG
            }
            catch (Exception e)
            {

                DebugConsole.ThrowError("Error while receiving message from server", e);         
            }
#endif


                if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                endVoteTickBox.Visible = Voting.AllowEndVoting && HasSpawned;

                if (respawnManager != null)
                {
                    respawnManager.Update(deltaTime);
                }

                if (updateTimer > DateTime.Now) return;
                SendIngameUpdate();
            }
            else
            {
                if (updateTimer > DateTime.Now) return;
                SendLobbyUpdate();
            }

            // Update current time
            updateTimer = DateTime.Now + updateInterval;  
        }

        private CoroutineHandle startGameCoroutine;

        private void DecompressIncomingMessage(NetIncomingMessage inc)
        {
            byte[] data = inc.Data;
            if (data[data.Length - 1] == 1)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    stream.Write(data, 0, inc.LengthBytes-1);
                    stream.Position = 0;
                    using (MemoryStream decompressed = new MemoryStream())
                    {
                        using (DeflateStream deflate = new DeflateStream(stream, CompressionMode.Decompress, false))
                        {
                            deflate.CopyTo(decompressed);
                        }
                        byte[] newData = decompressed.ToArray();

                        inc.Data = newData;
                        inc.LengthBytes = newData.Length;
                        inc.Position = 0;
                    }

                }
            }
        }

        /// <summary>
        /// Check for new incoming messages from server
        /// </summary>
        private void CheckServerMessages()
        {
            // Create new incoming message holder
            NetIncomingMessage inc;

            if (startGameCoroutine != null && CoroutineManager.IsCoroutineRunning(startGameCoroutine)) return;

            while ((inc = client.ReadMessage()) != null)
            {
                switch (inc.MessageType)
                {
                    case NetIncomingMessageType.Data:
                        DecompressIncomingMessage(inc);

                        ServerPacketHeader header = (ServerPacketHeader)inc.ReadByte();
                        switch (header)
                        {
                            case ServerPacketHeader.UPDATE_LOBBY:
                                ReadLobbyUpdate(inc);
                                break;
                            case ServerPacketHeader.UPDATE_INGAME:
                                ReadIngameUpdate(inc);
                                break;
                            case ServerPacketHeader.QUERY_STARTGAME:
                                string subName = inc.ReadString();
                                string subHash = inc.ReadString();

                                bool usingShuttle = inc.ReadBoolean();
                                string shuttleName = inc.ReadString();
                                string shuttleHash = inc.ReadString();

                                NetOutgoingMessage readyToStartMsg = client.CreateMessage();
                                readyToStartMsg.Write((byte)ClientPacketHeader.RESPONSE_STARTGAME);

                                GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;
                                readyToStartMsg.Write(
                                    GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList) &&
                                    GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox));

                                WriteCharacterInfo(readyToStartMsg);

                                client.SendMessage(readyToStartMsg, NetDeliveryMethod.ReliableUnordered);
                                
                                break;
                            case ServerPacketHeader.STARTGAME:
                                startGameCoroutine = GameMain.Instance.ShowLoading(StartGame(inc), false);
                                break;
                            case ServerPacketHeader.ENDGAME:
                                string endMessage = inc.ReadString();
                                bool missionSuccessful = inc.ReadBoolean();
                                if (missionSuccessful && GameMain.GameSession?.Mission != null)
                                {
                                    GameMain.GameSession.Mission.Completed = true;
                                }
                                CoroutineManager.StartCoroutine(EndGame(endMessage));
                                break;
                            case ServerPacketHeader.PERMISSIONS:
                                ReadPermissions(inc);
                                break;
                            case ServerPacketHeader.ACHIEVEMENT:
                                ReadAchievement(inc);
                                break;
                            case ServerPacketHeader.FILE_TRANSFER:
                                fileReceiver.ReadMessage(inc);
                                break;
                        }
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        NetConnectionStatus connectionStatus = (NetConnectionStatus)inc.ReadByte();
                        DebugConsole.NewMessage("Connection status changed: " + connectionStatus.ToString(), Color.Orange);

                        if (connectionStatus == NetConnectionStatus.Disconnected)
                        {
                            ReadDisconnectMessage(inc, true);
                        }
                        break;
                }
            }
        }

        private void ReadDisconnectMessage(NetIncomingMessage inc, bool allowReconnect)
        {
            string disconnectMsg = inc.ReadString();
            string[] splitMsg = disconnectMsg.Split(';');
            DisconnectReason disconnectReason = DisconnectReason.Unknown;
            if (splitMsg.Length > 0) Enum.TryParse(splitMsg[0], out disconnectReason);

            if (disconnectReason == DisconnectReason.ServerFull)
            {
                //already waiting for a slot to free up, do nothing
                if (CoroutineManager.IsCoroutineRunning("WaitInServerQueue")) return;

                reconnectBox?.Close(); reconnectBox = null;

                var queueBox = new GUIMessageBox(
                    TextManager.Get("DisconnectReason.ServerFull"),
                    TextManager.Get("ServerFullQuestionPrompt"), new string[] { TextManager.Get("Cancel"), TextManager.Get("ServerQueue") });

                queueBox.Buttons[0].OnClicked += queueBox.Close;
                queueBox.Buttons[1].OnClicked += queueBox.Close;
                queueBox.Buttons[1].OnClicked += (btn, userdata) =>
                {
                    reconnectBox?.Close(); reconnectBox = null;
                    CoroutineManager.StartCoroutine(WaitInServerQueue(), "WaitInServerQueue");
                    return true;
                };
                return;
            }
            else
            {
                //disconnected/denied for some other reason than the server being full
                // -> stop queuing and show a message box
                waitInServerQueueBox?.Close();
                waitInServerQueueBox = null;
                CoroutineManager.StopCoroutines("WaitInServerQueue");
            }

            if (allowReconnect && disconnectReason == DisconnectReason.Unknown)
            {
                reconnectBox = new GUIMessageBox(
                    TextManager.Get("ConnectionLost"),
                    TextManager.Get("ConnectionLostReconnecting"), new string[0]);
                connected = false;
                ConnectToServer(serverIP);
            }
            else
            {
                string msg = "";
                if (disconnectReason == DisconnectReason.Unknown)
                {
                    msg = disconnectMsg;
                }
                else
                {
                    msg = TextManager.Get("DisconnectReason." + disconnectReason.ToString());
                    for (int i = 1; i < splitMsg.Length; i++)
                    {
                        msg += splitMsg[i];
                    }
                }
                var msgBox = new GUIMessageBox(TextManager.Get(allowReconnect ? "ConnectionLost" : "CouldNotConnectToServer"), msg);
                msgBox.Buttons[0].OnClicked += ReturnToServerList;
            }
        }

        private IEnumerable<object> WaitInServerQueue()
        {
            waitInServerQueueBox = new GUIMessageBox(
                    TextManager.Get("ServerQueuePleaseWait"),
                    TextManager.Get("WaitingInServerQueue"), new string[] { TextManager.Get("Cancel") });
            waitInServerQueueBox.Buttons[0].OnClicked += (btn, userdata) =>
            {
                CoroutineManager.StopCoroutines("WaitInServerQueue");
                waitInServerQueueBox?.Close();
                waitInServerQueueBox = null;
                return true;
            };

            while (!connected)
            {
                if (!CoroutineManager.IsCoroutineRunning("WaitForStartingInfo"))
                {
                    ConnectToServer(serverIP);
                    yield return new WaitForSeconds(2.0f);
                }
                yield return new WaitForSeconds(0.5f);
            }

            waitInServerQueueBox?.Close();
            waitInServerQueueBox = null;

            yield return CoroutineStatus.Success;
        }


        private void ReadAchievement(NetIncomingMessage inc)
        {
            string achievementIdentifier = inc.ReadString();
            SteamAchievementManager.UnlockAchievement(achievementIdentifier);
        }

        private void ReadPermissions(NetIncomingMessage inc)
        {
            List<string> permittedConsoleCommands = new List<string>();
            ClientPermissions newPermissions = (ClientPermissions)inc.ReadByte();
            if (newPermissions.HasFlag(ClientPermissions.ConsoleCommands))
            {
                UInt16 consoleCommandCount = inc.ReadUInt16();
                for (int i = 0; i < consoleCommandCount; i++)
                {
                    permittedConsoleCommands.Add(inc.ReadString());
                }
            }

            SetPermissions(newPermissions, permittedConsoleCommands);
        }

        private void SetPermissions(ClientPermissions newPermissions, List<string> permittedConsoleCommands)
        {
            if (!(this.permittedConsoleCommands.Any(c => !permittedConsoleCommands.Contains(c)) ||
                permittedConsoleCommands.Any(c => !this.permittedConsoleCommands.Contains(c))))
            {
                if (newPermissions == permissions) return;
            }

            GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData as string == "permissions");            

            string msg = "";
            if (newPermissions == ClientPermissions.None)
            {
                msg = TextManager.Get("PermissionsRemoved");
            }
            else
            {
                msg = TextManager.Get("CurrentPermissions") + '\n';
                foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                {
                    if (!newPermissions.HasFlag(permission) || permission == ClientPermissions.None) continue;
                    msg += "   - " + TextManager.Get("ClientPermission." + permission) + "\n";
                }
            }

            permissions = newPermissions;
            this.permittedConsoleCommands = new List<string>(permittedConsoleCommands);
            GUIMessageBox msgBox = new GUIMessageBox(TextManager.Get("PermissionsChanged"), msg, GUIMessageBox.DefaultWidth, 0)
            {
                UserData = "permissions"
            };

            if (newPermissions.HasFlag(ClientPermissions.ConsoleCommands))
            {
                int listBoxWidth = (int)(msgBox.InnerFrame.Rect.Width) / 2 - 30;
                new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.1f), msgBox.InnerFrame.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.05f, 0.1f) },
                     TextManager.Get("PermittedConsoleCommands"), wrap: true, font: GUI.SmallFont);
                var commandList = new GUIListBox(new RectTransform(new Vector2(0.4f, 0.6f), msgBox.InnerFrame.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.05f, 0.2f) });
                foreach (string permittedCommand in permittedConsoleCommands)
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), commandList.Content.RectTransform, minSize: new Point(0, 15)),
                        permittedCommand, font: GUI.SmallFont)
                    {
                        CanBeFocused = false
                    };
                }
            }
            
            GameMain.NetLobbyScreen.SubList.Enabled = Voting.AllowSubVoting || HasPermission(ClientPermissions.SelectSub);
            GameMain.NetLobbyScreen.ModeList.Enabled = Voting.AllowModeVoting || HasPermission(ClientPermissions.SelectMode);
            GameMain.NetLobbyScreen.ShowLogButton.Visible = HasPermission(ClientPermissions.ServerLog);
            showLogButton.Visible = HasPermission(ClientPermissions.ServerLog);

            endRoundButton.Visible = HasPermission(ClientPermissions.EndRound);      
        }

        private IEnumerable<object> StartGame(NetIncomingMessage inc)
        {
            if (Character != null) Character.Remove();
            HasSpawned = false;
            
            GameMain.LightManager.LightingEnabled = true;

            //enable spectate button in case we fail to start the round now
            //(for example, due to a missing sub file or an error)
            GameMain.NetLobbyScreen.ShowSpectateButton();
            
            entityEventManager.Clear();
            LastSentEntityEventID = 0;

            endVoteTickBox.Selected = false;

            int seed                = inc.ReadInt32();
            string levelSeed        = inc.ReadString();
            float levelDifficulty   = inc.ReadFloat();

            byte losMode            = inc.ReadByte();

            int missionTypeIndex    = inc.ReadByte();

            string subName          = inc.ReadString();
            string subHash          = inc.ReadString();

            bool usingShuttle       = inc.ReadBoolean();
            string shuttleName      = inc.ReadString();
            string shuttleHash      = inc.ReadString();

            string modeName         = inc.ReadString();
            int missionIndex        = inc.ReadInt16();

            bool respawnAllowed     = inc.ReadBoolean();
            bool loadSecondSub      = inc.ReadBoolean();

            bool disguisesAllowed   = inc.ReadBoolean();
            bool isTraitor          = inc.ReadBoolean();
            string traitorTargetName = isTraitor ? inc.ReadString() : null;
            
            //monster spawn settings
            if (monsterEnabled == null)
            {
                List<string> monsterNames1 = GameMain.Instance.GetFilesOfType(ContentType.Character).ToList();
                for (int i = 0; i < monsterNames1.Count; i++)
                {
                    monsterNames1[i] = Path.GetFileName(Path.GetDirectoryName(monsterNames1[i]));
                }

                monsterEnabled = new Dictionary<string, bool>();
                foreach (string s in monsterNames1)
                {
                    if (!monsterEnabled.ContainsKey(s)) monsterEnabled.Add(s, true);
                }
            }

            List<string> monsterNames = monsterEnabled.Keys.ToList();
            foreach (string s in monsterNames)
            {
                monsterEnabled[s] = inc.ReadBoolean();
            }
            inc.ReadPadBits();

            GameModePreset gameMode = GameModePreset.list.Find(gm => gm.Name == modeName);
            MultiPlayerCampaign campaign = GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ?
                GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;

            if (gameMode == null)
            {
                DebugConsole.ThrowError("Game mode \"" + modeName + "\" not found!");
                yield return CoroutineStatus.Success;
            }

            GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;
            GameMain.LightManager.LosMode = (LosMode)losMode;

            AllowDisguises = disguisesAllowed;

            if (campaign == null)
            {
                if (!GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList))
                {
                    yield return CoroutineStatus.Success;
                }

                if (!GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox))
                {
                    yield return CoroutineStatus.Success;
                }
            }

            Rand.SetSyncedSeed(seed);

            if (campaign == null)
            {
                GameMain.GameSession = new GameSession(GameMain.NetLobbyScreen.SelectedSub, "", gameMode, missionIndex < 0 ? null : MissionPrefab.List[missionIndex]);
                GameMain.GameSession.StartRound(levelSeed, levelDifficulty, loadSecondSub);
            }
            else
            {
                if (GameMain.GameSession?.CrewManager != null) GameMain.GameSession.CrewManager.Reset();
                GameMain.GameSession.StartRound(campaign.Map.SelectedConnection.Level, 
                    reloadSub: true, 
                    loadSecondSub: false,
                    mirrorLevel: campaign.Map.CurrentLocation != campaign.Map.SelectedConnection.Locations[0]);
            }
            
            if (respawnAllowed) respawnManager = new RespawnManager(this, GameMain.NetLobbyScreen.UsingShuttle ? GameMain.NetLobbyScreen.SelectedShuttle : null);
                        
            gameStarted = true;

            GameMain.GameScreen.Select();
            
            yield return CoroutineStatus.Success;
        }

        public IEnumerable<object> EndGame(string endMessage)
        {
            if (!gameStarted) yield return CoroutineStatus.Success;

            if (GameMain.GameSession != null) GameMain.GameSession.GameMode.End(endMessage);

            gameStarted = false;
            Character.Controlled = null;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;
            respawnManager = null;

            float endPreviewLength = 10.0f;
            if (Screen.Selected == GameMain.GameScreen)
            {
                new TransitionCinematic(Submarine.MainSub, GameMain.GameScreen.Cam, endPreviewLength);
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
                c.InGame = false;
                c.Character = null;
            }
            yield return CoroutineStatus.Success;
        }

        private void ReadInitialUpdate(NetIncomingMessage inc)
        {
            myID = inc.ReadByte();

            UInt16 subListCount = inc.ReadUInt16();
            List<Submarine> submarines = new List<Submarine>();
            for (int i = 0; i < subListCount; i++)
            {
                string subName = inc.ReadString();
                string subHash = inc.ReadString();

                var matchingSub = Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.Hash == subHash);
                if (matchingSub != null)
                {
                    submarines.Add(matchingSub);
                }
                else
                {
                    submarines.Add(new Submarine(Path.Combine(Submarine.SavePath, subName) + ".sub", subHash, false));
                }
            }
            
            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.SubList, submarines);
            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.ShuttleList.ListBox, submarines);

            gameStarted = inc.ReadBoolean();
            bool allowSpectating = inc.ReadBoolean();

            ReadPermissions(inc);

            if (gameStarted)
            {
                new GUIMessageBox("Please wait", TextManager.Get(allowSpectating ? "RoundRunningSpectateEnabled" : "RoundRunningSpectateDisabled"));
                GameMain.NetLobbyScreen.Select();
            }
        }

        private void ReadLobbyUpdate(NetIncomingMessage inc)
        {
            ServerNetObject objHeader;
            while ((objHeader = (ServerNetObject)inc.ReadByte()) != ServerNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ServerNetObject.SYNC_IDS:
                        bool lobbyUpdated = inc.ReadBoolean();
                        inc.ReadPadBits();

                        if (lobbyUpdated)
                        {
                            UInt16 updateID     = inc.ReadUInt16();
                            string serverName   = inc.ReadString();
                            string serverText   = inc.ReadString();
 
                            if (inc.ReadBoolean())
                            {
                                if (GameSettings.VerboseLogging)
                                {
                                    DebugConsole.NewMessage("Received initial lobby update, ID: " + updateID + ", last ID: " + GameMain.NetLobbyScreen.LastUpdateID, Color.Gray);
                                }
                                ReadInitialUpdate(inc);
                            }

                            string selectSubName        = inc.ReadString();
                            string selectSubHash        = inc.ReadString();

                            bool usingShuttle           = inc.ReadBoolean();
                            string selectShuttleName    = inc.ReadString();
                            string selectShuttleHash    = inc.ReadString();

                            bool allowSubVoting         = inc.ReadBoolean();
                            bool allowModeVoting        = inc.ReadBoolean();

                            bool allowSpectating        = inc.ReadBoolean();

                            YesNoMaybe traitorsEnabled  = (YesNoMaybe)inc.ReadRangedInteger(0, 2);
                            int missionTypeIndex        = inc.ReadRangedInteger(0, Enum.GetValues(typeof(MissionType)).Length - 1);
                            int modeIndex               = inc.ReadByte();

                            string levelSeed            = inc.ReadString();
                            float levelDifficulty       = inc.ReadFloat();

                            byte botCount               = inc.ReadByte();
                            BotSpawnMode botSpawnMode   = inc.ReadBoolean() ? BotSpawnMode.Fill : BotSpawnMode.Normal;
                            
                            bool autoRestartEnabled     = inc.ReadBoolean();
                            float autoRestartTimer      = autoRestartEnabled ? inc.ReadFloat() : 0.0f;

                            int clientCount             = inc.ReadByte();
                            List<string> clientNames    = new List<string>();
                            List<byte> clientIDs        = new List<byte>();
                            List<ushort> characterIDs   = new List<ushort>();
                            for (int i = 0; i < clientCount; i++)
                            {
                                clientIDs.Add(inc.ReadByte());
                                clientNames.Add(inc.ReadString());
                                characterIDs.Add(inc.ReadUInt16());
                            }

                            //ignore the message if we already a more up-to-date one
                            if (NetIdUtils.IdMoreRecent(updateID, GameMain.NetLobbyScreen.LastUpdateID))
                            {
                                GameMain.NetLobbyScreen.LastUpdateID = updateID;

                                ServerLog.ServerName = serverName;

                                GameMain.NetLobbyScreen.ServerName = serverName;
                                GameMain.NetLobbyScreen.ServerMessage.Text = serverText;
                                GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;

                                if (!allowSubVoting) GameMain.NetLobbyScreen.TrySelectSub(selectSubName, selectSubHash, GameMain.NetLobbyScreen.SubList);
                                GameMain.NetLobbyScreen.TrySelectSub(selectShuttleName, selectShuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox);

                                GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);
                                GameMain.NetLobbyScreen.SetMissionType(missionTypeIndex);

                                if (!allowModeVoting) GameMain.NetLobbyScreen.SelectMode(modeIndex);                                
                                
                                GameMain.NetLobbyScreen.SetAllowSpectating(allowSpectating);
                                GameMain.NetLobbyScreen.LevelSeed = levelSeed;
                                GameMain.NetLobbyScreen.SetLevelDifficulty(levelDifficulty);
                                GameMain.NetLobbyScreen.SetBotCount(botCount);
                                GameMain.NetLobbyScreen.SetBotSpawnMode(botSpawnMode);                                
                                GameMain.NetLobbyScreen.SetAutoRestart(autoRestartEnabled, autoRestartTimer);

                                List<Client> currentClients = new List<Client>();
                                for (int i = 0; i < clientNames.Count; i++)
                                {
                                    //see if the client already exists
                                    var existingClient = ConnectedClients.Find(c => c.ID == clientIDs[i] && c.Name == clientNames[i]);
                                    if (existingClient == null) //if not, create it
                                    {
                                        existingClient = new Client(clientNames[i], clientIDs[i]);
                                        ConnectedClients.Add(existingClient);
                                        GameMain.NetLobbyScreen.AddPlayer(existingClient.Name);
                                    }
                                    if (characterIDs[i] > 0)
                                    {
                                        existingClient.Character = Entity.FindEntityByID(characterIDs[i]) as Character;
                                    }
                                    currentClients.Add(existingClient);
                                }
                                //remove clients that aren't present anymore
                                for (int i = ConnectedClients.Count - 1; i >= 0; i--)
                                {
                                    if (!currentClients.Contains(ConnectedClients[i]))
                                    {
                                        GameMain.NetLobbyScreen.RemovePlayer(ConnectedClients[i].Name);
                                        ConnectedClients.RemoveAt(i);
                                    }
                                }
                                
                                Voting.AllowSubVoting = allowSubVoting;
                                Voting.AllowModeVoting = allowModeVoting;
                            }
                        }

                        bool campaignUpdated = inc.ReadBoolean();
                        inc.ReadPadBits();
                        if (campaignUpdated)
                        {
                            MultiPlayerCampaign.ClientRead(inc);
                        }

                        lastSentChatMsgID = inc.ReadUInt16();

                        break;
                    case ServerNetObject.CHAT_MESSAGE:
                        ChatMessage.ClientRead(inc);
                        break;
                    case ServerNetObject.VOTE:
                        Voting.ClientRead(inc);
                        break;
                }
            }
        }

        private void ReadIngameUpdate(NetIncomingMessage inc)
        {
            List<IServerSerializable> entities = new List<IServerSerializable>();

            float sendingTime = inc.ReadFloat() - inc.SenderConnection.RemoteTimeOffset;

            ServerNetObject? prevObjHeader = null;
            long prevBitPos = 0;
            long prevBytePos = 0;

            long prevBitLength = 0;
            long prevByteLength = 0;

            ServerNetObject objHeader;
            while ((objHeader = (ServerNetObject)inc.ReadByte()) != ServerNetObject.END_OF_MESSAGE)
            {
                switch (objHeader)
                {
                    case ServerNetObject.SYNC_IDS:
                        lastSentChatMsgID = inc.ReadUInt16();
                        LastSentEntityEventID = inc.ReadUInt16();
                        break;
                    case ServerNetObject.ENTITY_POSITION:
                        UInt16 id = inc.ReadUInt16();
                        byte msgLength = inc.ReadByte();

                        long msgEndPos = inc.Position + msgLength * 8;

                        var entity = Entity.FindEntityByID(id) as IServerSerializable;
                        if (entity != null)
                        {
                            entity.ClientRead(objHeader, inc, sendingTime);
                        }

                        //force to the correct position in case the entity doesn't exist 
                        //or the message wasn't read correctly for whatever reason
                        inc.Position = msgEndPos;
                        inc.ReadPadBits();
                        break;
                    case ServerNetObject.ENTITY_EVENT:
                    case ServerNetObject.ENTITY_EVENT_INITIAL:
                        entityEventManager.Read(objHeader, inc, sendingTime, entities);
                        break;
                    case ServerNetObject.CHAT_MESSAGE:
                        ChatMessage.ClientRead(inc);
                        break;
                    default:
                        List<string> errorLines = new List<string>
                        {
                            "Error while reading update from server (unknown object header \"" + objHeader + "\"!)",
                            "Message length: " + inc.LengthBits + " (" + inc.LengthBytes + " bytes)",
                            prevObjHeader != null ? "Previous object type: " + prevObjHeader.ToString() : "Error occurred on the very first object!",
                            "Previous object was " + (prevBitLength) + " bits long (" + (prevByteLength) + " bytes)"
                        };
                        if (prevObjHeader == ServerNetObject.ENTITY_EVENT || prevObjHeader == ServerNetObject.ENTITY_EVENT_INITIAL)
                        {
                            foreach (IServerSerializable ent in entities)
                            {
                                if (ent == null)
                                {
                                    errorLines.Add(" - NULL");
                                    continue;
                                }
                                Entity e = ent as Entity;
                                errorLines.Add(" - " + e.ToString());
                            }
                        }
                        
                        foreach (string line in errorLines)
                        {
                            DebugConsole.ThrowError(line);
                        }
                        errorLines.Add("Last console messages:");
                        for (int i = DebugConsole.Messages.Count - 1; i > Math.Max(0, DebugConsole.Messages.Count - 20); i--)
                        {
                            errorLines.Add("[" + DebugConsole.Messages[i].Time + "] " + DebugConsole.Messages[i].Text);
                        }
                        GameAnalyticsManager.AddErrorEventOnce("GameClient.ReadInGameUpdate", GameAnalyticsSDK.Net.EGAErrorSeverity.Critical, string.Join("\n", errorLines));

                        DebugConsole.ThrowError("Writing object data to \"crashreport_object.bin\", please send this file to us at http://github.com/Regalis11/Barotrauma/issues");

                        FileStream fl = File.Open("crashreport_object.bin", FileMode.Create);
                        BinaryWriter sw = new BinaryWriter(fl);

                        sw.Write(inc.Data, (int)(prevBytePos - prevByteLength), (int)(prevByteLength));

                        sw.Close();
                        fl.Close();

                        throw new Exception("Error while reading update from server: please send us \"crashreport_object.bin\"!");
                }
                prevBitLength = inc.Position - prevBitPos;
                prevByteLength = inc.PositionInBytes - prevByteLength;

                prevObjHeader = objHeader;
                prevBitPos = inc.Position;
                prevBytePos = inc.PositionInBytes;
            }
        }

        private void SendLobbyUpdate()
        {
            NetOutgoingMessage outmsg = client.CreateMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_LOBBY);

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);

            var campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
            if (campaign == null || campaign.LastSaveID == 0)
            {
                outmsg.Write((UInt16)0);
            }
            else
            {
                outmsg.Write(campaign.LastSaveID);
                outmsg.Write(campaign.CampaignID);
                outmsg.Write(campaign.LastUpdateID);
                outmsg.Write(GameMain.NetLobbyScreen.CampaignCharacterDiscarded);
            }

            chatMsgQueue.RemoveAll(cMsg => !NetIdUtils.IdMoreRecent(cMsg.NetStateID, lastSentChatMsgID));
            for (int i = 0; i < chatMsgQueue.Count && i < ChatMessage.MaxMessagesPerPacket; i++)
            {
                if (outmsg.LengthBytes + chatMsgQueue[i].EstimateLengthBytesClient() > client.Configuration.MaximumTransmissionUnit - 5)
                {
                    //not enough room in this packet
                    return;
                }
                chatMsgQueue[i].ClientWrite(outmsg);
            }
            outmsg.Write((byte)ClientNetObject.END_OF_MESSAGE);
            
            if (outmsg.LengthBytes > client.Configuration.MaximumTransmissionUnit)
            {
                DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + client.Configuration.MaximumTransmissionUnit);
            }

            client.SendMessage(outmsg, NetDeliveryMethod.Unreliable);
        }

        private void SendIngameUpdate()
        {
            NetOutgoingMessage outmsg = client.CreateMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_INGAME);

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            //outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);
            outmsg.Write(entityEventManager.LastReceivedID);

            Character.Controlled?.ClientWrite(outmsg);

            entityEventManager.Write(outmsg, client.ServerConnection);

            chatMsgQueue.RemoveAll(cMsg => !NetIdUtils.IdMoreRecent(cMsg.NetStateID, lastSentChatMsgID));
            for (int i = 0; i < chatMsgQueue.Count && i < ChatMessage.MaxMessagesPerPacket; i++)
            {
                if (outmsg.LengthBytes + chatMsgQueue[i].EstimateLengthBytesClient() > client.Configuration.MaximumTransmissionUnit - 5)
                {
                    //not enough room in this packet
                    return;
                }
                chatMsgQueue[i].ClientWrite(outmsg);
            }            

            outmsg.Write((byte)ClientNetObject.END_OF_MESSAGE);

            if (outmsg.LengthBytes > client.Configuration.MaximumTransmissionUnit)
            {
                DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + client.Configuration.MaximumTransmissionUnit);
            }

            client.SendMessage(outmsg, NetDeliveryMethod.Unreliable);
        }

        public void SendChatMessage(ChatMessage msg)
        {
            if (client.ServerConnection == null) return;            
            lastQueueChatMsgID++;
            msg.NetStateID = lastQueueChatMsgID;
            chatMsgQueue.Add(msg);
        }

        public void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Default)
        {
            if (client.ServerConnection == null) return;

            ChatMessage chatMessage = ChatMessage.Create(
                gameStarted && myCharacter != null ? myCharacter.Name : name,
                message,
                type, 
                gameStarted && myCharacter != null ? myCharacter : null);

            lastQueueChatMsgID++;
            chatMessage.NetStateID = lastQueueChatMsgID;

            chatMsgQueue.Add(chatMessage);
        }

        public void RequestFile(FileTransferType fileType, string file, string fileHash)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Initiate);
            msg.Write((byte)fileType);
            if (file != null) msg.Write(file);
            if (fileHash != null) msg.Write(fileHash);
            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public void CancelFileTransfer(FileReceiver.FileTransferIn transfer)
        {
            CancelFileTransfer(transfer.SequenceChannel);
        }

        public void CancelFileTransfer(int sequenceChannel)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Cancel);
            msg.Write((byte)sequenceChannel);
            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        private void OnFileReceived(FileReceiver.FileTransferIn transfer)
        {
            switch (transfer.FileType)
            {
                case FileTransferType.Submarine:
                    new GUIMessageBox(TextManager.Get("ServerDownloadFinished"), TextManager.Get("FileDownloadedNotification").Replace("[filename]", transfer.FileName));
                    var newSub = new Submarine(transfer.FilePath);
                    var existingSubs = Submarine.SavedSubmarines.Where(s => s.Name == newSub.Name && s.MD5Hash.Hash == newSub.MD5Hash.Hash).ToList();
                    foreach (Submarine existingSub in existingSubs)
                    {
                        existingSub.Dispose();
                    }
                    Submarine.AddToSavedSubs(newSub);

                    for (int i = 0; i < 2; i++)
                    {
                        IEnumerable<GUIComponent> subListChildren = (i == 0) ? 
                            GameMain.NetLobbyScreen.ShuttleList.ListBox.Content.Children : 
                            GameMain.NetLobbyScreen.SubList.Content.Children;

                        var subElement = subListChildren.FirstOrDefault(c => 
                            ((Submarine)c.UserData).Name == newSub.Name && 
                            ((Submarine)c.UserData).MD5Hash.Hash == newSub.MD5Hash.Hash);
                        if (subElement == null) continue;

                        subElement.GetChild<GUITextBlock>().TextColor = new Color(subElement.GetChild<GUITextBlock>().TextColor, 1.0f);
                        subElement.UserData = newSub;
                        subElement.ToolTip = newSub.Description;

                        GUIButton infoButton = subElement.GetChild<GUIButton>();
                        if (infoButton == null)
                        {
                            int buttonSize = (int)(subElement.Rect.Height * 0.8f);
                            infoButton = new GUIButton(new RectTransform(new Point(buttonSize), subElement.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point((int)(buttonSize * 0.2f), 0) }, "?");
                        }
                        infoButton.UserData = newSub;
                        infoButton.OnClicked = (component, userdata) =>
                        {
                            ((Submarine)userdata).CreatePreviewWindow(new GUIMessageBox("", "", 550, 400));
                            return true;
                        };
                    }

                    if (GameMain.NetLobbyScreen.FailedSelectedSub != null && 
                        GameMain.NetLobbyScreen.FailedSelectedSub.First == newSub.Name &&
                        GameMain.NetLobbyScreen.FailedSelectedSub.Second == newSub.MD5Hash.Hash)
                    {
                        GameMain.NetLobbyScreen.TrySelectSub(newSub.Name, newSub.MD5Hash.Hash, GameMain.NetLobbyScreen.SubList);
                    }

                    if (GameMain.NetLobbyScreen.FailedSelectedShuttle != null &&
                        GameMain.NetLobbyScreen.FailedSelectedShuttle.First == newSub.Name &&
                        GameMain.NetLobbyScreen.FailedSelectedShuttle.Second == newSub.MD5Hash.Hash)
                    {
                        GameMain.NetLobbyScreen.TrySelectSub(newSub.Name, newSub.MD5Hash.Hash, GameMain.NetLobbyScreen.ShuttleList.ListBox);
                    }

                    break;
                case FileTransferType.CampaignSave:
                    var campaign = GameMain.GameSession?.GameMode as MultiPlayerCampaign;
                    if (campaign == null) return;

                    GameMain.GameSession.SavePath = transfer.FilePath;
                    if (GameMain.GameSession.Submarine == null)
                    {
                        var gameSessionDoc = SaveUtil.LoadGameSessionDoc(GameMain.GameSession.SavePath);
                        string subPath = Path.Combine(SaveUtil.TempPath, gameSessionDoc.Root.GetAttributeString("submarine", "")) + ".sub";  
                        GameMain.GameSession.Submarine = new Submarine(subPath, "");
                    }

                    SaveUtil.LoadGame(GameMain.GameSession.SavePath, GameMain.GameSession);
                    campaign.LastSaveID = campaign.PendingSaveID;
                    //decrement campaign update ID so the server will send us the latest data
                    //(as there may have been campaign updates after the save file was created)
                    campaign.LastUpdateID--;
                    break;
            }
        }

        private void OnTransferFailed(FileReceiver.FileTransferIn transfer)
        {
            if (transfer.FileType == FileTransferType.CampaignSave)
            {
                GameMain.Client.RequestFile(FileTransferType.CampaignSave, null, null);
            }
        }

        public void CreateEntityEvent(IClientSerializable entity, object[] extraData)
        {
            entityEventManager.CreateEvent(entity, extraData);
        }
        
        public bool HasPermission(ClientPermissions permission)
        {
            return permissions.HasFlag(permission);
        }

        public bool HasConsoleCommandPermission(string command)
        {
            if (!permissions.HasFlag(ClientPermissions.ConsoleCommands)) return false;

            command = command.ToLowerInvariant();
            return permittedConsoleCommands.Any(c => c.ToLowerInvariant() == command);
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            
            if (fileReceiver != null && fileReceiver.ActiveTransfers.Count > 0)
            {
                Vector2 pos = new Vector2(GameMain.NetLobbyScreen.InfoFrame.Rect.X, GameMain.GraphicsHeight - 35);

                GUI.DrawRectangle(spriteBatch, new Rectangle(
                    (int)pos.X,
                    (int)pos.Y, 
                    fileReceiver.ActiveTransfers.Count * 210 + 10, 
                    32), 
                    Color.Black * 0.8f, true);
                
                for (int i = 0; i < fileReceiver.ActiveTransfers.Count; i++)
                {
                    var transfer = fileReceiver.ActiveTransfers[i];
                    
                    GUI.DrawString(spriteBatch,
                        pos,
                        ToolBox.LimitString(TextManager.Get("DownloadingFile").Replace("[filename]", transfer.FileName), GUI.SmallFont, 200),
                        Color.White, null, 0, GUI.SmallFont);
                    GUI.DrawProgressBar(spriteBatch, new Vector2(pos.X, -pos.Y - 15), new Vector2(135, 15), transfer.Progress, Color.Green);
                    GUI.DrawString(spriteBatch, pos + new Vector2(5, 15),
                        MathUtils.GetBytesReadable((long)transfer.Received) + " / " + MathUtils.GetBytesReadable((long)transfer.FileSize),
                        Color.White, null, 0, GUI.SmallFont);

                    if (GUI.DrawButton(spriteBatch, new Rectangle((int)pos.X + 140, (int)pos.Y + 18, 60, 15), TextManager.Get("Cancel"), new Color(0.47f, 0.13f, 0.15f, 0.08f)))
                    {
                        CancelFileTransfer(transfer);
                        fileReceiver.StopTransfer(transfer);
                    }

                    pos.X += 210;
                }
            }

            if (!GameMain.DebugDraw) return;

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight * 0.3f);

            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black * 0.7f, true);
            GUI.Font.DrawString(spriteBatch, "Network statistics:", new Vector2(x + 10, y + 10), Color.White);

            if (client.ServerConnection != null)
            {
                GUI.Font.DrawString(spriteBatch, "Ping: " + (int)(client.ServerConnection.AverageRoundtripTime * 1000.0f) + " ms", new Vector2(x + 10, y + 25), Color.White);

                y += 15;

                GUI.SmallFont.DrawString(spriteBatch, "Received bytes: " + client.Statistics.ReceivedBytes, new Vector2(x + 10, y + 45), Color.White);
                GUI.SmallFont.DrawString(spriteBatch, "Received packets: " + client.Statistics.ReceivedPackets, new Vector2(x + 10, y + 60), Color.White);

                GUI.SmallFont.DrawString(spriteBatch, "Sent bytes: " + client.Statistics.SentBytes, new Vector2(x + 10, y + 75), Color.White);
                GUI.SmallFont.DrawString(spriteBatch, "Sent packets: " + client.Statistics.SentPackets, new Vector2(x + 10, y + 90), Color.White);
            }
            else
            {
                GUI.Font.DrawString(spriteBatch, "Disconnected", new Vector2(x + 10, y + 25), Color.White);
            }
        }


        public override void Disconnect()
        {
            client.Shutdown("");
            steamAuthTicket?.Cancel();

            foreach (var fileTransfer in FileReceiver.ActiveTransfers)
            {
                fileTransfer.Dispose();
            }

            if (HasPermission(ClientPermissions.ServerLog))
            {
                ServerLog?.Save();
            }

            GameMain.NetworkMember = null;
        }
        
        public void WriteCharacterInfo(NetOutgoingMessage msg)
        {
            msg.Write(characterInfo == null);
            if (characterInfo == null) return;

            msg.Write(characterInfo.Gender == Gender.Male);
            msg.Write((byte)characterInfo.HeadSpriteId);

            var jobPreferences = GameMain.NetLobbyScreen.JobPreferences;
            int count = Math.Min(jobPreferences.Count, 3);
            msg.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                msg.Write(jobPreferences[i].Identifier);
            }
        }

        public override bool SelectCrewCharacter(Character character, GUIComponent characterFrame)
        {
            if (character == null) return false;

            if (character != myCharacter)
            {
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.Character == character);
                if (client == null) return false;

                if (HasPermission(ClientPermissions.Ban))
                {
                    var banButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.15f), characterFrame.RectTransform, Anchor.BottomRight),
                        TextManager.Get("Ban"))
                    {
                        UserData = character.Name,
                        OnClicked = GameMain.NetLobbyScreen.BanPlayer
                    };
                }
                if (HasPermission(ClientPermissions.Kick))
                {
                    var kickButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.15f), characterFrame.RectTransform, Anchor.BottomLeft),
                        TextManager.Get("Kick"))
                    {
                        UserData = character.Name,
                        OnClicked = GameMain.NetLobbyScreen.KickPlayer
                    };
                }
                else if (Voting.AllowVoteKick)
                {
                    var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.15f), characterFrame.RectTransform, Anchor.BottomRight) { RelativeOffset = new Vector2(0.0f, 0.16f) },
                        TextManager.Get("VoteToKick"))
                    {
                        UserData = character,
                        OnClicked = VoteForKick
                    };
                    if (GameMain.NetworkMember.ConnectedClients != null)
                    {
                        kickVoteButton.Enabled = !client.HasKickVoteFromID(myID);
                    }
                }                
            }

            return true;
        }

        public void Vote(VoteType voteType, object data)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.UPDATE_LOBBY);
            msg.Write((byte)ClientNetObject.VOTE);
            Voting.ClientWrite(msg, voteType, data);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public bool VoteForKick(GUIButton button, object userdata)
        {
            var votedClient = userdata is Client ? (Client)userdata : otherClients.Find(c => c.Character == userdata);
            if (votedClient == null) return false;

            votedClient.AddKickVote(new Client(name, ID));
            Vote(VoteType.Kick, votedClient);

            button.Enabled = false;

            return true;
        }

        public override void KickPlayer(string kickedName, string reason)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((byte)ClientPermissions.Kick);            
            msg.Write(kickedName);
            msg.Write(reason);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public override void BanPlayer(string kickedName, string reason, bool range = false, TimeSpan? duration = null)
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((byte)ClientPermissions.Ban);
            msg.Write(kickedName);
            msg.Write(reason);
            msg.Write(range);
            msg.Write(duration.HasValue ? duration.Value.TotalSeconds : 0.0); //0 = permaban

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public void SendCampaignState()
        {
            MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
            if (campaign == null)
            {
                DebugConsole.ThrowError("Failed send campaign state to the server (no campaign active).\n" + Environment.StackTrace);
                return;
            }

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((byte)ClientPermissions.ManageCampaign);
            campaign.ClientWrite(msg);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public void SendConsoleCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                DebugConsole.ThrowError("Cannot send an empty console command to the server!\n" + Environment.StackTrace);
                return;
            }

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((byte)ClientPermissions.ConsoleCommands);
            msg.Write(command);
            Vector2 cursorWorldPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
            msg.Write(cursorWorldPos.X);
            msg.Write(cursorWorldPos.Y);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Tell the server to select a submarine (permission required)
        /// </summary>
        public void RequestSelectSub(int subIndex)
        {
            if (!HasPermission(ClientPermissions.SelectSub)) return;
            if (subIndex < 0 || subIndex >= GameMain.NetLobbyScreen.SubList.CountChildren)
            {
                DebugConsole.ThrowError("Submarine index out of bounds (" + subIndex + ")\n" + Environment.StackTrace);
                return;
            }

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((byte)ClientPermissions.SelectSub);
            msg.Write((UInt16)subIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Tell the server to select a submarine (permission required)
        /// </summary>
        public void RequestSelectMode(int modeIndex)
        {
            if (!HasPermission(ClientPermissions.SelectMode)) return;
            if (modeIndex < 0 || modeIndex >= GameMain.NetLobbyScreen.ModeList.CountChildren)
            {
                DebugConsole.ThrowError("Gamemode index out of bounds (" + modeIndex + ")\n" + Environment.StackTrace);
                return;
            }

            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((byte)ClientPermissions.SelectMode);
            msg.Write((UInt16)modeIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        /// <summary>
        /// Tell the server to end the round (permission required)
        /// </summary>
        public void RequestRoundEnd()
        {
            NetOutgoingMessage msg = client.CreateMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((byte)ClientPermissions.EndRound);

            client.SendMessage(msg, NetDeliveryMethod.ReliableUnordered);
        }

        public bool SpectateClicked(GUIButton button, object userData)
        {
            if (button != null) button.Enabled = false;
            
            NetOutgoingMessage readyToStartMsg = client.CreateMessage();
            readyToStartMsg.Write((byte)ClientPacketHeader.RESPONSE_STARTGAME);

            //assume we have the required sub files to start the round
            //(if not, we'll find out when the server sends the STARTGAME message and can initiate a file transfer)
            readyToStartMsg.Write(true); 

            WriteCharacterInfo(readyToStartMsg);

            client.SendMessage(readyToStartMsg, NetDeliveryMethod.ReliableUnordered);

            return false;
        }

        public bool SetReadyToStart(GUITickBox tickBox)
        {
            if (gameStarted)
            {
                tickBox.Visible = false;
                return false;
            }

            Vote(VoteType.StartRound, tickBox.Selected);

            return true;
        }

        public bool ToggleEndRoundVote(GUITickBox tickBox)
        {
            if (!gameStarted) return false;

            if (!Voting.AllowEndVoting || !HasSpawned)
            {
                tickBox.Visible = false;
                return false;
            }

            Vote(VoteType.EndRound, tickBox.Selected);
            return false;
        }

        public void ReportError(ClientNetError error, UInt16 expectedID = 0, UInt16 eventID = 0, UInt16 entityID = 0)
        {
            NetOutgoingMessage outMsg = client.CreateMessage();
            outMsg.Write((byte)ClientPacketHeader.ERROR);
            outMsg.Write((byte)error);
            outMsg.Write(Level.Loaded == null ? 0 : Level.Loaded.EqualityCheckVal);
            switch (error)
            {
                case ClientNetError.MISSING_EVENT:
                    outMsg.Write(expectedID);
                    outMsg.Write(eventID);
                    break;
                case ClientNetError.MISSING_ENTITY:
                    outMsg.Write(eventID);
                    outMsg.Write(entityID);
                    break;
            }
            client.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
        }
    }
}
