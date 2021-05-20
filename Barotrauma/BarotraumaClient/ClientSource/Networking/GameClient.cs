using Barotrauma.Items.Components;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma.Networking
{
    class GameClient : NetworkMember
    {
        public override bool IsClient
        {
            get { return true; }
        }

        private string name;

        private UInt16 nameId = 0;

        public string Name
        {
            get { return name; }
        }

        public string PendingName = string.Empty;

        public void SetName(string value)
        {
            value = value.Replace(":", "").Replace(";", "");
            if (string.IsNullOrEmpty(value)) { return; }
            name = value;
            nameId++;
        }

        public void ForceNameAndJobUpdate()
        {
            nameId++;
        }

        private ClientPeer clientPeer;
        public ClientPeer ClientPeer { get { return clientPeer; } }

        private GUIMessageBox reconnectBox, waitInServerQueueBox;

        //TODO: move these to NetLobbyScreen
        public GUITickBox EndVoteTickBox;
        private GUIComponent buttonContainer;

        public readonly NetStats NetStats;

        protected GUITickBox cameraFollowsSub;
        public GUITickBox FollowSubTickBox => cameraFollowsSub;

        public bool IsFollowSubTickBoxVisible =>
            gameStarted && Screen.Selected == GameMain.GameScreen &&
            cameraFollowsSub != null && cameraFollowsSub.Visible;

        public CameraTransition EndCinematic;

        public bool LateCampaignJoin = false;

        private ClientPermissions permissions = ClientPermissions.None;
        private List<string> permittedConsoleCommands = new List<string>();

        private bool connected;

        private enum RoundInitStatus
        {
            NotStarted,
            Starting,
            WaitingForStartGameFinalize,
            Started,
            TimedOut,
            Error,
            Interrupted
        }

        private RoundInitStatus roundInitStatus = RoundInitStatus.NotStarted;

        private byte myID;

        private readonly List<Client> otherClients;

        public readonly List<SubmarineInfo> ServerSubmarines = new List<SubmarineInfo>();

        private string serverIP, serverName;

        private bool allowReconnect;
        private bool requiresPw;
        private int pwRetries;
        private bool canStart;

        private UInt16 lastSentChatMsgID = 0; //last message this client has successfully sent
        private UInt16 lastQueueChatMsgID = 0; //last message added to the queue
        private readonly List<ChatMessage> chatMsgQueue = new List<ChatMessage>();

        public UInt16 LastSentEntityEventID;

        private readonly ClientEntityEventManager entityEventManager;

        private readonly FileReceiver fileReceiver;

#if DEBUG
        public void PrintReceiverTransters()
        {
            foreach (var transfer in fileReceiver.ActiveTransfers)
            {
                DebugConsole.NewMessage(transfer.FileName + " " + transfer.Progress.ToString());
            }
        }
#endif

        //has the client been given a character to control this round
        public bool HasSpawned;

        public bool SpawnAsTraitor;
        public string TraitorFirstObjective;
        public TraitorMissionPrefab TraitorMission = null;

        public byte ID
        {
            get { return myID; }
        }

        public VoipClient VoipClient
        {
            get;
            private set;
        }

        public override List<Client> ConnectedClients
        {
            get
            {
                return otherClients;
            }
        }

        private readonly List<Client> previouslyConnectedClients = new List<Client>();
        public IEnumerable<Client> PreviouslyConnectedClients
        {
            get { return previouslyConnectedClients; }
        }

        public FileReceiver FileReceiver
        {
            get { return fileReceiver; }
        }

        public bool MidRoundSyncing
        {
            get { return entityEventManager.MidRoundSyncing; }
        }

        public ClientEntityEventManager EntityEventManager
        {
            get { return entityEventManager; }
        }

        public bool? WaitForNextRoundRespawn
        {
            get;
            set;
        }

        private readonly object serverEndpoint;
        private readonly int ownerKey;
        private readonly bool steamP2POwner;

        public bool IsServerOwner
        {
            get { return ownerKey > 0 || steamP2POwner; }
        }

        public GameClient(string newName, string ip, UInt64 steamId, string serverName = null, int ownerKey = 0, bool steamP2POwner = false)
        {
            //TODO: gui stuff should probably not be here?
            this.ownerKey = ownerKey;
            this.steamP2POwner = steamP2POwner;

            roundInitStatus = RoundInitStatus.NotStarted;

            allowReconnect = true;

            NetStats = new NetStats();

            inGameHUD = new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, GUI.Canvas), style: null)
            {
                CanBeFocused = false
            };

            cameraFollowsSub = new GUITickBox(new RectTransform(new Vector2(0.05f, 0.05f), inGameHUD.RectTransform, anchor: Anchor.TopCenter, pivot: Pivot.CenterLeft)
            {
                AbsoluteOffset = new Point(0, HUDLayoutSettings.ButtonAreaTop.Y + HUDLayoutSettings.ButtonAreaTop.Height / 2),
                MaxSize = new Point(GUI.IntScale(25))
            }, TextManager.Get("CamFollowSubmarine"))
            {
                Selected = Camera.FollowSub,
                OnSelected = (tbox) =>
                {
                    Camera.FollowSub = tbox.Selected;
                    return true;
                }
            };

            chatBox = new ChatBox(inGameHUD, isSinglePlayer: false);
            chatBox.OnEnterMessage += EnterChatMessage;
            chatBox.InputBox.OnTextChanged += TypingChatMessage;

            buttonContainer = new GUILayoutGroup(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.ButtonAreaTop, inGameHUD.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                AbsoluteSpacing = 5,
                CanBeFocused = false
            };

            EndVoteTickBox = new GUITickBox(new RectTransform(new Vector2(0.1f, 0.4f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("EndRound"))
            {
                UserData = TextManager.Get("EndRound"),
                OnSelected = ToggleEndRoundVote,
                Visible = false
            };

            ShowLogButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("ServerLog"))
            {
                OnClicked = (GUIButton button, object userData) =>
                {
                    if (serverSettings.ServerLog.LogFrame == null)
                    {
                        serverSettings.ServerLog.CreateLogFrame();
                    }
                    else
                    {
                        serverSettings.ServerLog.LogFrame = null;
                        GUI.KeyboardDispatcher.Subscriber = null;
                    }
                    return true;
                }
            };
            ShowLogButton.TextBlock.AutoScaleHorizontal = true;

            GameMain.DebugDraw = false;
            Hull.EditFire = false;
            Hull.EditWater = false;

            SetName(newName);

            entityEventManager = new ClientEntityEventManager(this);

            fileReceiver = new FileReceiver();
            fileReceiver.OnFinished += OnFileReceived;
            fileReceiver.OnTransferFailed += OnTransferFailed;

            characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, name, null)
            {
                Job = null
            };

            otherClients = new List<Client>();

            serverSettings = new ServerSettings(this, "Server", 0, 0, 0, false, false);

            if (steamId == 0)
            {
                serverEndpoint = ip;
            }
            else
            {
                serverEndpoint = steamId;
            }
            ConnectToServer(serverEndpoint, serverName);

            //ServerLog = new ServerLog("");

            ChatMessage.LastID = 0;
            GameMain.NetLobbyScreen?.Release();
            GameMain.NetLobbyScreen = new NetLobbyScreen();
        }

        private void ConnectToServer(object endpoint, string hostName)
        {
            LastClientListUpdateID = 0;

            foreach (var c in ConnectedClients)
            {
                GameMain.NetLobbyScreen.RemovePlayer(c);
                c.Dispose();
            }
            ConnectedClients.Clear();

            chatBox.InputBox.Enabled = false;
            if (GameMain.NetLobbyScreen?.ChatInput != null)
            {
                GameMain.NetLobbyScreen.ChatInput.Enabled = false;
            }

            serverName = hostName;

            myCharacter = Character.Controlled;
            ChatMessage.LastID = 0;

            clientPeer?.Close();
            clientPeer = null;
            object translatedEndpoint = null;
            if (endpoint is string hostIP)
            {
                int port;
                string[] address = hostIP.Split(':');
                if (address.Length == 1)
                {
                    serverIP = hostIP;
                    port = NetConfig.DefaultPort;
                }
                else
                {
                    serverIP = string.Join(":", address.Take(address.Length - 1));
                    if (!int.TryParse(address[address.Length - 1], out port))
                    {
                        DebugConsole.ThrowError("Invalid port: " + address[address.Length - 1] + "!");
                        port = NetConfig.DefaultPort;
                    }
                }

                clientPeer = new LidgrenClientPeer(Name);

                System.Net.IPEndPoint IPEndPoint = null;
                try
                {
                    IPEndPoint = new System.Net.IPEndPoint(Lidgren.Network.NetUtility.Resolve(serverIP), port);
                }
                catch
                {
                    new GUIMessageBox(TextManager.Get("CouldNotConnectToServer"),
                        TextManager.GetWithVariables("InvalidIPAddress", new string[2] { "[serverip]", "[port]" }, new string[2] { serverIP, port.ToString() }));
                    return;
                }

                translatedEndpoint = IPEndPoint;
            }
            else if (endpoint is UInt64)
            {
                if (steamP2POwner)
                {
                    clientPeer = new SteamP2POwnerPeer(Name);
                }
                else
                {
                    clientPeer = new SteamP2PClientPeer(Name);
                }

                translatedEndpoint = endpoint;
            }
            clientPeer.OnDisconnect = OnDisconnect;
            clientPeer.OnDisconnectMessageReceived = HandleDisconnectMessage;
            clientPeer.OnInitializationComplete = () =>
            {
                if (SteamManager.IsInitialized)
                {
                    Steamworks.SteamFriends.ClearRichPresence();
                    Steamworks.SteamFriends.SetRichPresence("status", "Playing on " + serverName);
                    Steamworks.SteamFriends.SetRichPresence("connect", "-connect \"" + serverName.Replace("\"", "\\\"") + "\" " + serverEndpoint);
                }

                canStart = true;
                connected = true;

                VoipClient = new VoipClient(this, clientPeer);

                if (Screen.Selected != GameMain.GameScreen)
                {
                    GameMain.NetLobbyScreen.Select();
                }
                else
                {
                    entityEventManager.ClearSelf();
                    foreach (Character c in Character.CharacterList)
                    {
                        c.ResetNetState();
                    }
                }

                chatBox.InputBox.Enabled = true;
                if (GameMain.NetLobbyScreen?.ChatInput != null)
                {
                    GameMain.NetLobbyScreen.ChatInput.Enabled = true;
                }
            };
            clientPeer.OnRequestPassword = (int salt, int retries) =>
            {
                if (pwRetries != retries)
                {
                    wrongPassword = retries > 0;
                    requiresPw = true; 
                }
                pwRetries = retries;
            };
            clientPeer.OnMessageReceived = ReadDataMessage;

            // Connect client, to endpoint previously requested from user
            try
            {
                clientPeer.Start(translatedEndpoint, ownerKey);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Couldn't connect to " + endpoint.ToString() + ". Error message: " + e.Message);
                Disconnect();
                chatBox.InputBox.Enabled = true;
                if (GameMain.NetLobbyScreen?.ChatInput != null)
                {
                    GameMain.NetLobbyScreen.ChatInput.Enabled = true;
                }
                GameMain.ServerListScreen.Select();
                return;
            }

            updateInterval = new TimeSpan(0, 0, 0, 0, 150);

            CoroutineManager.StartCoroutine(WaitForStartingInfo(), "WaitForStartingInfo");
        }

        private bool ReturnToPreviousMenu(GUIButton button, object obj)
        {
            Disconnect();

            Submarine.Unload();
            GameMain.Client = null;
            GameMain.GameSession = null;
            if (IsServerOwner)
            {
                GameMain.MainMenuScreen.Select();
            }
            else
            {
                GameMain.ServerListScreen.Select();
            }

            GUIMessageBox.MessageBoxes.RemoveAll(m => true);

            return true;
        }

        private bool connectCancelled;
        private void CancelConnect()
        {
            ChildServerRelay.ShutDown();
            connectCancelled = true;
            Disconnect();
        }

        private bool wrongPassword;

        // Before main looping starts, we loop here and wait for approval message
        private IEnumerable<object> WaitForStartingInfo()
        {
            GUI.SetCursorWaiting();
            requiresPw = false;
            pwRetries = -1;

            connectCancelled = wrongPassword = false;
            // When this is set to true, we are approved and ready to go
            canStart = false;

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 40);
            DateTime reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, 200);             

            // Loop until we are approved
            string connectingText = TextManager.Get("Connecting");
            while (!canStart && !connectCancelled)
            {
                if (reconnectBox == null && waitInServerQueueBox == null)
                {
                    string serverDisplayName = serverName;
                    if (string.IsNullOrEmpty(serverDisplayName)) { serverDisplayName = serverIP; }
                    if (string.IsNullOrEmpty(serverDisplayName) && clientPeer?.ServerConnection is SteamP2PConnection steamConnection)
                    {
                        serverDisplayName = steamConnection.SteamID.ToString();
                        if (SteamManager.IsInitialized)
                        {
                            string steamUserName = Steamworks.SteamFriends.GetFriendPersonaName(steamConnection.SteamID);
                            if (!string.IsNullOrEmpty(steamUserName) && steamUserName != "[unknown]")
                            {
                                serverDisplayName = steamUserName;
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(serverDisplayName)) { serverDisplayName = TextManager.Get("Unknown"); }

                    reconnectBox = new GUIMessageBox(
                        connectingText,
                        TextManager.GetWithVariable("ConnectingTo", "[serverip]", serverDisplayName),
                        new string[] { TextManager.Get("Cancel") });
                    reconnectBox.Buttons[0].OnClicked += (btn, userdata) => { CancelConnect(); return true; };
                    reconnectBox.Buttons[0].OnClicked += reconnectBox.Close;
                }

                if (reconnectBox != null)
                {
                    reconnectBox.Header.Text = connectingText + new string('.', ((int)Timing.TotalTime % 3 + 1));
                }

                yield return CoroutineStatus.Running;

                if (DateTime.Now > timeOut)
                {
                    clientPeer?.Close(Lidgren.Network.NetConnection.NoResponseMessage);
                    var msgBox = new GUIMessageBox(TextManager.Get("ConnectionFailed"), TextManager.Get("CouldNotConnectToServer"));
                    msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                    reconnectBox?.Close(); reconnectBox = null;
                    break;
                }

                if (requiresPw && !canStart && !connectCancelled)
                {
                    GUI.ClearCursorWait();
                    reconnectBox?.Close(); reconnectBox = null;

                    string pwMsg = TextManager.Get("PasswordRequired");

                    var msgBox = new GUIMessageBox(pwMsg, "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") },
                        relativeSize: new Vector2(0.25f, 0.1f), minSize: new Point(400, (int)(170 * Math.Max(1.0f, GUI.Scale))));
                    var passwordHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), msgBox.Content.RectTransform), childAnchor: Anchor.TopCenter);
                    var passwordBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1f), passwordHolder.RectTransform) { MinSize = new Point(0, 20) })
                    {
                        UserData = "password",
                        Censor = true
                    };

                    if (wrongPassword)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(1f, 0.33f), passwordHolder.RectTransform), TextManager.Language == "English" ? TextManager.Get("incorrectpassword") : "Incorrect password", GUI.Style.Red, GUI.Font, textAlignment: Alignment.Center);
                        passwordHolder.Recalculate();
                    }

                    msgBox.Content.Recalculate();
                    msgBox.Content.RectTransform.MinSize = new Point(0, msgBox.Content.RectTransform.Children.Sum(c => c.Rect.Height));
                    msgBox.Content.Parent.RectTransform.MinSize = new Point(0, (int)(msgBox.Content.RectTransform.MinSize.Y / msgBox.Content.RectTransform.RelativeSize.Y));

                    var okButton = msgBox.Buttons[0];
                    okButton.OnClicked += msgBox.Close;
                    var cancelButton = msgBox.Buttons[1];
                    cancelButton.OnClicked += msgBox.Close;

                    okButton.OnClicked += (GUIButton button, object obj) =>
                    {
                        clientPeer?.SendPassword(passwordBox.Text);
                        requiresPw = false;
                        return true;
                    };

                    cancelButton.OnClicked += (GUIButton button, object obj) =>
                    {
                        requiresPw = false;
                        connectCancelled = true;
                        GameMain.ServerListScreen.Select();
                        return true;
                    };

                    while (GUIMessageBox.MessageBoxes.Contains(msgBox))
                    {
                        if (!requiresPw)
                        {
                            msgBox.Close();
                            break;
                        }
                        yield return CoroutineStatus.Running;
                    }
                }
            }

            reconnectBox?.Close(); reconnectBox = null;

            GUI.ClearCursorWait();
            if (connectCancelled) { yield return CoroutineStatus.Success; }

            yield return CoroutineStatus.Success;
        }

        public override void Update(float deltaTime)
        {
#if DEBUG
            if (PlayerInput.GetKeyboardState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.P)) return;
#endif

            foreach (Client c in ConnectedClients)
            {
                if (c.Character != null && c.Character.Removed) { c.Character = null; }
                c.UpdateSoundPosition();
            }

            if (VoipCapture.Instance != null)
            {
                if (VoipCapture.Instance.LastEnqueueAudio > DateTime.Now - new TimeSpan(0, 0, 0, 0, milliseconds: 100))
                {
                    var myClient = ConnectedClients.Find(c => c.ID == ID);
                    if (Screen.Selected == GameMain.NetLobbyScreen)
                    {
                        GameMain.NetLobbyScreen.SetPlayerSpeaking(myClient);
                    }
                    else
                    {
                        GameMain.GameSession?.CrewManager?.SetClientSpeaking(myClient);
                    }
                }
            }

            NetStats.Update(deltaTime);

            UpdateHUD(deltaTime);

            base.Update(deltaTime);

            try
            {
                incomingMessagesToProcess.Clear();
                incomingMessagesToProcess.AddRange(pendingIncomingMessages);
                foreach (var inc in incomingMessagesToProcess)
                {
                    ReadDataMessage(inc);
                }
                pendingIncomingMessages.Clear();
                clientPeer?.Update(deltaTime);
            }
            catch (Exception e)
            {
                string errorMsg = "Error while reading a message from server. {" + e + "}. ";
                if (GameMain.Client == null) { errorMsg += "Client disposed."; }
                errorMsg += "\n" + e.StackTrace.CleanupStackTrace();
                if (e.InnerException != null)
                {
                    errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace.CleanupStackTrace();
                }
                GameAnalyticsManager.AddErrorEventOnce("GameClient.Update:CheckServerMessagesException" + e.TargetSite.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                DebugConsole.ThrowError("Error while reading a message from server.", e);
                new GUIMessageBox(TextManager.Get("Error"), TextManager.GetWithVariables("MessageReadError", new string[2] { "[message]", "[targetsite]" }, new string[2] { e.Message, e.TargetSite.ToString() }));
                Disconnect();
                GameMain.ServerListScreen.Select();
                return;
            }

            if (!connected) return;

            if (reconnectBox != null)
            {
                reconnectBox.Close();
                reconnectBox = null;
            }

            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                EndVoteTickBox.Visible = serverSettings.Voting.AllowEndVoting && HasSpawned && !(GameMain.GameSession?.GameMode is CampaignMode);

                respawnManager?.Update(deltaTime);

                if (updateTimer <= DateTime.Now)
                {
                    SendIngameUpdate();
                }
            }
            else
            {
                if (updateTimer <= DateTime.Now)
                {
                    SendLobbyUpdate();
                }
            }

            if (serverSettings.VoiceChatEnabled)
            {
                VoipClient?.SendToServer();
            }

            if (IsServerOwner && connected && !connectCancelled)
            {
                if (GameMain.WindowActive)
                {
                    if (ChildServerRelay.Process?.HasExited ?? true)
                    {
                        Disconnect();
                        var msgBox = new GUIMessageBox(TextManager.Get("ConnectionLost"), TextManager.Get("ServerProcessClosed"));
                        msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                    }
                }
            }

            if (updateTimer <= DateTime.Now)
            {
                // Update current time
                updateTimer = DateTime.Now + updateInterval;
            }
        }

        private readonly List<IReadMessage> pendingIncomingMessages = new List<IReadMessage>();
        private readonly List<IReadMessage> incomingMessagesToProcess = new List<IReadMessage>();

        private void ReadDataMessage(IReadMessage inc)
        {
            ServerPacketHeader header = (ServerPacketHeader)inc.ReadByte();

            if (roundInitStatus != RoundInitStatus.Started &&
                roundInitStatus != RoundInitStatus.NotStarted &&
                roundInitStatus != RoundInitStatus.Error &&
                roundInitStatus != RoundInitStatus.Interrupted &&
                header != ServerPacketHeader.STARTGAMEFINALIZE &&
                header != ServerPacketHeader.ENDGAME &&
                header != ServerPacketHeader.PING_REQUEST &&
                header != ServerPacketHeader.FILE_TRANSFER)
            {
                //rewind the header byte we just read
                inc.BitPosition -= 8;
                pendingIncomingMessages.Add(inc);
                return;
            }

            MultiPlayerCampaign campaign = GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ?
                                                GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;

            switch (header)
            {
                case ServerPacketHeader.PING_REQUEST:
                    IWriteMessage response = new WriteOnlyMessage();
                    response.Write((byte)ClientPacketHeader.PING_RESPONSE);
                    byte requestLen = inc.ReadByte();
                    response.Write(requestLen);
                    for (int i = 0; i < requestLen; i++)
                    {
                        byte b = inc.ReadByte();
                        response.Write(b);
                    }
                    clientPeer.Send(response, DeliveryMethod.Unreliable);
                    break;
                case ServerPacketHeader.CLIENT_PINGS:
                    byte clientCount = inc.ReadByte();
                    for (int i = 0; i < clientCount; i++)
                    {
                        byte clientId = inc.ReadByte();
                        UInt16 clientPing = inc.ReadUInt16();
                        Client client = ConnectedClients.Find(c => c.ID == clientId);
                        if (client != null)
                        {
                            client.Ping = clientPing;
                        }
                    }
                    break;
                case ServerPacketHeader.UPDATE_LOBBY:
                    ReadLobbyUpdate(inc);
                    break;
                case ServerPacketHeader.UPDATE_INGAME:
                    try
                    {
                        ReadIngameUpdate(inc);
                    }
                    catch (Exception e)
                    {
                        string errorMsg = "Error while reading an ingame update message from server. {" + e + "}\n" + e.StackTrace.CleanupStackTrace();
                        if (e.InnerException != null)
                        {
                            errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace.CleanupStackTrace();
                        }
#if DEBUG
                        DebugConsole.ThrowError("Error while reading an ingame update message from server.", e);
#endif
                        GameAnalyticsManager.AddErrorEventOnce("GameClient.ReadDataMessage:ReadIngameUpdate", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                        throw;
                    }
                    break;
                case ServerPacketHeader.VOICE:
                    if (VoipClient == null)
                    {
                        string errorMsg = "Failed to read a voice packet from the server (VoipClient == null). ";
                        if (GameMain.Client == null) { errorMsg += "Client disposed. "; }
                        errorMsg += "\n" + Environment.StackTrace.CleanupStackTrace();
                        GameAnalyticsManager.AddErrorEventOnce(
                            "GameClient.ReadDataMessage:VoipClientNull",
                            GameMain.Client == null ? GameAnalyticsSDK.Net.EGAErrorSeverity.Error : GameAnalyticsSDK.Net.EGAErrorSeverity.Warning,
                            errorMsg);
                        return;
                    }

                    VoipClient.Read(inc);
                    break;
                case ServerPacketHeader.QUERY_STARTGAME:
                    DebugConsole.Log("Received QUERY_STARTGAME packet.");
                    string subName = inc.ReadString();
                    string subHash = inc.ReadString();

                    bool usingShuttle = inc.ReadBoolean();
                    string shuttleName = inc.ReadString();
                    string shuttleHash = inc.ReadString();

                    byte campaignID = inc.ReadByte();
                    UInt16 campaignSaveID = inc.ReadUInt16();
                    UInt16 campaignUpdateID = inc.ReadUInt16();

                    IWriteMessage readyToStartMsg = new WriteOnlyMessage();
                    readyToStartMsg.Write((byte)ClientPacketHeader.RESPONSE_STARTGAME);

                    if (campaign != null) { campaign.PendingSubmarineSwitch = null; }
                    GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;
                    bool readyToStart;
                    if (campaign == null && campaignID == 0)
                    {
                        readyToStart = GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList) &&
                                       GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox);
                    }
                    else
                    {
                        readyToStart =
                            campaign != null &&
                            campaign.CampaignID == campaignID &&
                            campaign.LastSaveID == campaignSaveID &&
                            campaign.LastUpdateID == campaignUpdateID;
                    }
                    readyToStartMsg.Write(readyToStart);

                    DebugConsole.Log(readyToStart ? "Ready to start." : "Not ready to start.");

                    WriteCharacterInfo(readyToStartMsg);

                    clientPeer.Send(readyToStartMsg, DeliveryMethod.Reliable);

                    if (readyToStart && !CoroutineManager.IsCoroutineRunning("WaitForStartRound"))
                    {
                        CoroutineManager.StartCoroutine(GameMain.NetLobbyScreen.WaitForStartRound(startButton: null), "WaitForStartRound");
                    }
                    break;
                case ServerPacketHeader.STARTGAME:
                    DebugConsole.Log("Received STARTGAME packet.");
                    if (Screen.Selected == GameMain.GameScreen && GameMain.GameSession?.GameMode is CampaignMode)
                    {
                        //start without a loading screen if playing a campaign round
                        CoroutineManager.StartCoroutine(StartGame(inc));
                    }
                    else
                    {
                        GUIMessageBox.CloseAll();
                        GameMain.Instance.ShowLoading(StartGame(inc), false);
                    }
                    break;
                case ServerPacketHeader.STARTGAMEFINALIZE:
                    DebugConsole.Log("Received STARTGAMEFINALIZE packet.");
                    if (roundInitStatus == RoundInitStatus.WaitingForStartGameFinalize)
                    {
                        //waiting for a save file
                        if (campaign != null &&
                            campaign.PendingSaveID > campaign.LastSaveID &&
                            fileReceiver.ActiveTransfers.Any(t => t.FileType == FileTransferType.CampaignSave))
                        {
                            return;
                        }
                        ReadStartGameFinalize(inc);
                    }
                    break;
                case ServerPacketHeader.ENDGAME:
                    CampaignMode.TransitionType transitionType = (CampaignMode.TransitionType)inc.ReadByte();
                    string endMessage = string.Empty;

                    endMessage = inc.ReadString();
                    byte missionCount = inc.ReadByte();
                    for (int i = 0; i < missionCount; i++)
                    {
                        bool missionSuccessful = inc.ReadBoolean();
                        var mission = GameMain.GameSession?.GetMission(i);
                        if (mission != null)
                        {
                            mission.Completed = missionSuccessful;
                        }
                    }
                    CharacterTeamType winningTeam = (CharacterTeamType)inc.ReadByte();
                    if (winningTeam != CharacterTeamType.None)
                    {
                        GameMain.GameSession.WinningTeam = winningTeam;
                        var combatMission = GameMain.GameSession.Missions.FirstOrDefault(m => m is CombatMission);
                        if (combatMission != null)
                        {
                            combatMission.Completed = true;
                        }
                    }

                    byte traitorCount = inc.ReadByte();
                    List<TraitorMissionResult> traitorResults = new List<TraitorMissionResult>();
                    for (int i = 0; i<traitorCount; i++)
                    {
                        traitorResults.Add(new TraitorMissionResult(inc));
                    }

                    roundInitStatus = RoundInitStatus.Interrupted;
                    CoroutineManager.StartCoroutine(EndGame(endMessage, traitorResults, transitionType), "EndGame");
                    break;
                case ServerPacketHeader.CAMPAIGN_SETUP_INFO:
                    UInt16 saveCount = inc.ReadUInt16();
                    List<string> saveFiles = new List<string>();
                    for (int i = 0; i < saveCount; i++)
                    {
                        saveFiles.Add(inc.ReadString());
                    }
                    MultiPlayerCampaign.StartCampaignSetup(saveFiles);
                    break;
                case ServerPacketHeader.PERMISSIONS:
                    ReadPermissions(inc);
                    break;
                case ServerPacketHeader.ACHIEVEMENT:
                    ReadAchievement(inc);
                    break;
                case ServerPacketHeader.CHEATS_ENABLED:
                    bool cheatsEnabled = inc.ReadBoolean();
                    inc.ReadPadBits();
                    if (cheatsEnabled == DebugConsole.CheatsEnabled)
                    {
                        return;
                    }
                    else
                    {
                        DebugConsole.CheatsEnabled = cheatsEnabled;
                        SteamAchievementManager.CheatsEnabled = cheatsEnabled;
                        if (cheatsEnabled)
                        {
                            var cheatMessageBox = new GUIMessageBox(TextManager.Get("CheatsEnabledTitle"), TextManager.Get("CheatsEnabledDescription"));
                            cheatMessageBox.Buttons[0].OnClicked += (btn, userdata) =>
                            {
                                DebugConsole.TextBox.Select();
                                return true;
                            };
                        }
                    }
                    break;
                case ServerPacketHeader.CREW:
                    campaign?.ClientReadCrew(inc);
                    break;
                case ServerPacketHeader.READY_CHECK:
                    ReadyCheck.ClientRead(inc);
                    break;
                case ServerPacketHeader.FILE_TRANSFER:
                    fileReceiver.ReadMessage(inc);
                    break;
                case ServerPacketHeader.TRAITOR_MESSAGE:
                    ReadTraitorMessage(inc);
                    break;
                case ServerPacketHeader.MISSION:
                    {
                        int missionIndex = inc.ReadByte();
                        Mission mission = GameMain.GameSession?.GetMission(missionIndex);
                        mission?.ClientRead(inc);
                    }
                    break;
                case ServerPacketHeader.EVENTACTION:
                    GameMain.GameSession?.EventManager.ClientRead(inc);
                    break;
            }
        }

        private void ReadStartGameFinalize(IReadMessage inc)
        {
            TaskPool.ListTasks();
            ushort contentToPreloadCount = inc.ReadUInt16();
            List<ContentFile> contentToPreload = new List<ContentFile>();
            for (int i = 0; i < contentToPreloadCount; i++)
            {
                ContentType contentType = (ContentType)inc.ReadByte();
                string filePath = inc.ReadString();
                contentToPreload.Add(new ContentFile(filePath, contentType));
            }

            GameMain.GameSession.EventManager.PreloadContent(contentToPreload);

            int subEqualityCheckValue = inc.ReadInt32();
            if (subEqualityCheckValue != (Submarine.MainSub?.Info?.EqualityCheckVal ?? 0))
            {
                string errorMsg = "Submarine equality check failed. The submarine loaded at your end doesn't match the one loaded by the server." +
                    " There may have been an error in receiving the up-to-date submarine file from the server.";
                GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:SubsDontMatch" + Level.Loaded.Seed, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                throw new Exception(errorMsg);
            }

            byte missionCount = inc.ReadByte();
            if (missionCount != GameMain.GameSession.Missions.Count())
            {
                string errorMsg = $"Mission equality check failed. Mission count doesn't match the server (server: {missionCount}, client: {GameMain.GameSession.Missions.Count()})";
                throw new Exception(errorMsg);
            }
            foreach (Mission mission in GameMain.GameSession.Missions)
            {
                string missionIdentifier = inc.ReadString() ?? "";
                if (missionIdentifier != mission.Prefab.Identifier)
                {
                    string errorMsg = $"Mission equality check failed. The mission selected at your end doesn't match the one loaded by the server (server: {missionIdentifier ?? "null"}, client: {mission.Prefab.Identifier})";
                    GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:MissionsDontMatch" + Level.Loaded.Seed, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    throw new Exception(errorMsg);
                }
            }

            byte equalityCheckValueCount = inc.ReadByte();
            List<int> levelEqualityCheckValues = new List<int>();
            for (int i = 0; i < equalityCheckValueCount; i++)
            {
                levelEqualityCheckValues.Add(inc.ReadInt32());
            }

            if (Level.Loaded.EqualityCheckValues.Count != levelEqualityCheckValues.Count)
            {
                string errorMsg = "Level equality check failed. The level generated at your end doesn't match the level generated by the server" +
                    " (client value count: " + Level.Loaded.EqualityCheckValues.Count +
                    ", level value count: " + levelEqualityCheckValues.Count +
                    ", seed: " + Level.Loaded.Seed +
                    ", sub: " + Submarine.MainSub.Info.Name + " (" + Submarine.MainSub.Info.MD5Hash.ShortHash + ")" +
                    ", mirrored: " + Level.Loaded.Mirrored + ").";
                GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:LevelsDontMatch" + Level.Loaded.Seed, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                throw new Exception(errorMsg);
            }
            else
            {
                for (int i = 0; i < equalityCheckValueCount; i++)
                {
                    if (Level.Loaded.EqualityCheckValues[i] != levelEqualityCheckValues[i])
                    {
                        string errorMsg = "Level equality check failed. The level generated at your end doesn't match the level generated by the server" +
                            " (client value #" + i + ": " + Level.Loaded.EqualityCheckValues[i] +
                            ", server value #" + i + ": " + levelEqualityCheckValues[i].ToString("X") +
                            ", level value count: " + levelEqualityCheckValues.Count +
                            ", seed: " + Level.Loaded.Seed +
                            ", sub: " + Submarine.MainSub.Info.Name + " (" + Submarine.MainSub.Info.MD5Hash.ShortHash + ")" +
                            ", mirrored: " + Level.Loaded.Mirrored + ").";
                        GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:LevelsDontMatch" + Level.Loaded.Seed, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                        throw new Exception(errorMsg);
                    }
                }
            }

            foreach (Mission mission in GameMain.GameSession.Missions)
            {
                mission.ClientReadInitial(inc);
            }

            if (inc.ReadBoolean())
            {
                CrewManager.ClientReadActiveOrders(inc);
            }

            roundInitStatus = RoundInitStatus.Started;
        }


        private void OnDisconnect(bool disableReconnect)
        {
            CoroutineManager.StopCoroutines("WaitForStartingInfo");
            reconnectBox?.Close();
            reconnectBox = null;

            GameMain.Config.RestoreBackupPackages();

            GUI.ClearCursorWait();

            if (disableReconnect) { allowReconnect = false; }
            if (!this.allowReconnect) { CancelConnect(); }

            if (SteamManager.IsInitialized)
            {
                Steamworks.SteamFriends.ClearRichPresence();
            }
        }

        private void HandleDisconnectMessage(string disconnectMsg)
        {
            disconnectMsg = disconnectMsg ?? "";

            string[] splitMsg = disconnectMsg.Split('/');
            DisconnectReason disconnectReason = DisconnectReason.Unknown;
            bool disconnectReasonIncluded = false;
            if (splitMsg.Length > 0)
            {
                if (Enum.TryParse(splitMsg[0], out disconnectReason)) { disconnectReasonIncluded = true; }
            }

            if (disconnectMsg == Lidgren.Network.NetConnection.NoResponseMessage ||
                disconnectReason == DisconnectReason.Banned ||
                disconnectReason == DisconnectReason.Kicked ||
                disconnectReason == DisconnectReason.TooManyFailedLogins)
            {
                allowReconnect = false;
            }

            DebugConsole.NewMessage("Received a disconnect message (" + disconnectMsg + ")");

            if (disconnectReason != DisconnectReason.Banned &&
                disconnectReason != DisconnectReason.ServerShutdown &&
                disconnectReason != DisconnectReason.TooManyFailedLogins &&
                disconnectReason != DisconnectReason.NotOnWhitelist &&
                disconnectReason != DisconnectReason.MissingContentPackage &&
                disconnectReason != DisconnectReason.InvalidVersion)
            {
                GameAnalyticsManager.AddErrorEventOnce(
                "GameClient.HandleDisconnectMessage",
                GameAnalyticsSDK.Net.EGAErrorSeverity.Debug,
                "Client received a disconnect message. Reason: " + disconnectReason.ToString() + ", message: " + disconnectMsg);
            }

            if (disconnectReason == DisconnectReason.ServerFull)
            {
                CoroutineManager.StopCoroutines("WaitForStartingInfo");
                //already waiting for a slot to free up, stop waiting for starting info and 
                //let WaitInServerQueue reattempt connecting later
                if (CoroutineManager.IsCoroutineRunning("WaitInServerQueue"))
                {
                    return;
                }

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

            bool eventSyncError =
                disconnectReason == DisconnectReason.ExcessiveDesyncOldEvent ||
                disconnectReason == DisconnectReason.ExcessiveDesyncRemovedEvent ||
                disconnectReason == DisconnectReason.SyncTimeout;

            if (allowReconnect &&
                (disconnectReason == DisconnectReason.Unknown || eventSyncError))
            {
                if (eventSyncError)
                {
                    GameMain.NetLobbyScreen.Select();
                    GameMain.GameSession?.EndRound("", null);
                    gameStarted = false;
                    myCharacter = null;
                }

                DebugConsole.NewMessage("Attempting to reconnect...");

                //if the first part of the message is the disconnect reason Enum, don't include it in the popup message
                string msg = TextManager.GetServerMessage(disconnectReasonIncluded ? string.Join('/', splitMsg.Skip(1)) : disconnectMsg);
                msg = string.IsNullOrWhiteSpace(msg) ?
                    TextManager.Get("ConnectionLostReconnecting") :
                    msg + '\n' + TextManager.Get("ConnectionLostReconnecting");

                reconnectBox?.Close();
                reconnectBox = new GUIMessageBox(
                    TextManager.Get("ConnectionLost"), msg, 
                    new string[] { TextManager.Get("Cancel") });
                reconnectBox.Buttons[0].OnClicked += (btn, userdata) => { CancelConnect(); return true; };
                connected = false;
                ConnectToServer(serverEndpoint, serverName);
            }
            else
            {
                connected = false;
                connectCancelled = true;

                string msg = "";
                if (disconnectReason == DisconnectReason.Unknown)
                {
                    DebugConsole.NewMessage("Not attempting to reconnect (unknown disconnect reason).");
                    msg = disconnectMsg;
                }
                else
                {
                    DebugConsole.NewMessage("Not attempting to reconnect (DisconnectReason doesn't allow reconnection).");
                    msg = TextManager.Get("DisconnectReason." + disconnectReason.ToString()) + " ";

                    for (int i = 1; i < splitMsg.Length; i++)
                    {
                        msg += TextManager.GetServerMessage(splitMsg[i]);
                    }

                    if (disconnectReason == DisconnectReason.ServerCrashed && IsServerOwner)
                    {
                        msg = TextManager.Get("ServerProcessCrashed");
                    }
                }

                reconnectBox?.Close();

                if (msg == Lidgren.Network.NetConnection.NoResponseMessage)
                {
                    //display a generic "could not connect" popup if the message is Lidgren's "failed to establish connection"
                    var msgBox = new GUIMessageBox(TextManager.Get("ConnectionFailed"), TextManager.Get(allowReconnect ? "ConnectionLost" : "CouldNotConnectToServer"));
                    msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                }
                else
                {
                    var msgBox = new GUIMessageBox(TextManager.Get(allowReconnect ? "ConnectionLost" : "CouldNotConnectToServer"), msg);
                    msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                }

                if (disconnectReason == DisconnectReason.InvalidName)
                {
                    GameMain.ServerListScreen.ClientNameBox.Text = "";
                    GameMain.ServerListScreen.ClientNameBox.Flash(flashDuration: 5.0f);
                    GameMain.ServerListScreen.ClientNameBox.Select();
                }
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
                    ConnectToServer(serverEndpoint, serverName);
                    yield return new WaitForSeconds(5.0f);
                }
                yield return new WaitForSeconds(0.5f);
            }

            waitInServerQueueBox?.Close();
            waitInServerQueueBox = null;

            yield return CoroutineStatus.Success;
        }


        private void ReadAchievement(IReadMessage inc)
        {
            string achievementIdentifier = inc.ReadString();
            SteamAchievementManager.UnlockAchievement(achievementIdentifier);
        }

        private void ReadTraitorMessage(IReadMessage inc)
        {
            TraitorMessageType messageType = (TraitorMessageType)inc.ReadByte();
            string missionIdentifier = inc.ReadString();
            string message = inc.ReadString();
            message = TextManager.GetServerMessage(message);

            var missionPrefab = TraitorMissionPrefab.List.Find(t => t.Identifier == missionIdentifier);
            Sprite icon = missionPrefab?.Icon;

            switch (messageType)
            {
                case TraitorMessageType.Objective:
                    var isTraitor = !string.IsNullOrEmpty(message);
                    SpawnAsTraitor = isTraitor;
                    TraitorFirstObjective = message;
                    TraitorMission = missionPrefab;
                    if (Character != null)
                    {
                        Character.IsTraitor = isTraitor;
                        Character.TraitorCurrentObjective = message;
                    }
                    break;
                case TraitorMessageType.Console:
                    GameMain.Client.AddChatMessage(ChatMessage.Create("", message, ChatMessageType.Console, null));
                    DebugConsole.NewMessage(message);
                    break;
                case TraitorMessageType.ServerMessageBox:
                    var msgBox = new GUIMessageBox("", message, new string[0], type: GUIMessageBox.Type.InGame, icon: icon);
                    if (msgBox.Icon != null)
                    {
                        msgBox.IconColor = missionPrefab.IconColor;
                    }
                    break;
                case TraitorMessageType.Server:
                default:
                    GameMain.Client.AddChatMessage(message, ChatMessageType.Server);
                    break;
            }
        }

        private void ReadPermissions(IReadMessage inc)
        {
            List<string> permittedConsoleCommands = new List<string>();
            byte clientID = inc.ReadByte();

            ClientPermissions permissions = ClientPermissions.None;
            List<DebugConsole.Command> permittedCommands = new List<DebugConsole.Command>();
            Client.ReadPermissions(inc, out permissions, out permittedCommands);

            Client targetClient = ConnectedClients.Find(c => c.ID == clientID);
            targetClient?.SetPermissions(permissions, permittedCommands);
            if (clientID == myID)
            {
                SetMyPermissions(permissions, permittedCommands.Select(command => command.names[0]));
            }
        }

        private void SetMyPermissions(ClientPermissions newPermissions, IEnumerable<string> permittedConsoleCommands)
        {
            if (!(this.permittedConsoleCommands.Any(c => !permittedConsoleCommands.Contains(c)) ||
                permittedConsoleCommands.Any(c => !this.permittedConsoleCommands.Contains(c))))
            {
                if (newPermissions == permissions) return;
            }

            bool refreshCampaignUI = false;

            if (permissions.HasFlag(ClientPermissions.ManageCampaign) != newPermissions.HasFlag(ClientPermissions.ManageCampaign) ||
                permissions.HasFlag(ClientPermissions.ManageRound) != newPermissions.HasFlag(ClientPermissions.ManageRound))
            {
                refreshCampaignUI = true;
            }

            permissions = newPermissions;
            this.permittedConsoleCommands = new List<string>(permittedConsoleCommands);
            //don't show the "permissions changed" popup if the client owns the server
            if (!IsServerOwner)
            {
                GUIMessageBox.MessageBoxes.RemoveAll(mb => mb.UserData as string == "permissions");
                GUIMessageBox msgBox = new GUIMessageBox("", "") { UserData = "permissions" };
                msgBox.Content.ClearChildren();
                msgBox.Content.RectTransform.RelativeSize = new Vector2(0.95f, 0.9f);

                var header = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBox.Content.RectTransform), TextManager.Get("PermissionsChanged"), textAlignment: Alignment.Center, font: GUI.LargeFont);
                header.RectTransform.IsFixedSize = true;

                var permissionArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), msgBox.Content.RectTransform), isHorizontal: true) { Stretch = true, RelativeSpacing = 0.05f };
                var leftColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), permissionArea.RectTransform)) { Stretch = true, RelativeSpacing = 0.05f };
                var rightColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), permissionArea.RectTransform)) { Stretch = true, RelativeSpacing = 0.05f };

                var permissionsLabel = new GUITextBlock(new RectTransform(new Vector2(newPermissions == ClientPermissions.None ? 2.0f : 1.0f, 0.0f), leftColumn.RectTransform),
                    TextManager.Get(newPermissions == ClientPermissions.None ? "PermissionsRemoved" : "CurrentPermissions"),
                    wrap: true, font: (newPermissions == ClientPermissions.None ? GUI.Font : GUI.SubHeadingFont));
                permissionsLabel.RectTransform.NonScaledSize = new Point(permissionsLabel.Rect.Width, permissionsLabel.Rect.Height);
                permissionsLabel.RectTransform.IsFixedSize = true;
                if (newPermissions != ClientPermissions.None)
                {
                    string permissionList = "";
                    foreach (ClientPermissions permission in Enum.GetValues(typeof(ClientPermissions)))
                    {
                        if (!newPermissions.HasFlag(permission) || permission == ClientPermissions.None) { continue; }
                        permissionList += "   - " + TextManager.Get("ClientPermission." + permission) + "\n";
                    }
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), leftColumn.RectTransform),
                        permissionList);
                }

                if (newPermissions.HasFlag(ClientPermissions.ConsoleCommands))
                {
                    var commandsLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), rightColumn.RectTransform),
                         TextManager.Get("PermittedConsoleCommands"), wrap: true, font: GUI.SubHeadingFont);
                    var commandList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), rightColumn.RectTransform));
                    foreach (string permittedCommand in permittedConsoleCommands)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), commandList.Content.RectTransform, minSize: new Point(0, 15)),
                            permittedCommand, font: GUI.SmallFont)
                        {
                            CanBeFocused = false
                        };
                    }
                    permissionsLabel.RectTransform.NonScaledSize = commandsLabel.RectTransform.NonScaledSize =
                        new Point(permissionsLabel.Rect.Width, Math.Max(permissionsLabel.Rect.Height, commandsLabel.Rect.Height));
                    commandsLabel.RectTransform.IsFixedSize = true;
                }

                new GUIButton(new RectTransform(new Vector2(0.5f, 0.05f), msgBox.Content.RectTransform), TextManager.Get("ok"))
                {
                    OnClicked = msgBox.Close
                };

                permissionArea.RectTransform.MinSize = new Point(0, Math.Max(leftColumn.RectTransform.Children.Sum(c => c.Rect.Height), rightColumn.RectTransform.Children.Sum(c => c.Rect.Height)));
                permissionArea.RectTransform.IsFixedSize = true;
                int contentHeight = (int)(msgBox.Content.RectTransform.Children.Sum(c => c.Rect.Height + msgBox.Content.AbsoluteSpacing) * 1.05f);
                msgBox.Content.ChildAnchor = Anchor.TopCenter;
                msgBox.Content.Stretch = true;
                msgBox.Content.RectTransform.MinSize = new Point(0, contentHeight);
                msgBox.InnerFrame.RectTransform.MinSize = new Point(0, (int)(contentHeight / permissionArea.RectTransform.RelativeSize.Y / msgBox.Content.RectTransform.RelativeSize.Y));
            }

            if (refreshCampaignUI)
            {
                if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    campaign.CampaignUI?.UpgradeStore?.RefreshAll();
                    campaign.CampaignUI?.CrewManagement?.RefreshPermissions();
                }
            }

            GameMain.NetLobbyScreen.RefreshEnabledElements();
        }

        private IEnumerable<object> StartGame(IReadMessage inc)
        {
            Character?.Remove();
            Character = null;
            HasSpawned = false;
            eventErrorWritten = false;
            GameMain.NetLobbyScreen.StopWaitingForStartRound();

            while (CoroutineManager.IsCoroutineRunning("EndGame"))
            {
                EndCinematic?.Stop();
                yield return CoroutineStatus.Running;
            }

            //enable spectate button in case we fail to start the round now
            //(for example, due to a missing sub file or an error)
            GameMain.NetLobbyScreen.ShowSpectateButton();

            entityEventManager.Clear();
            LastSentEntityEventID = 0;

            EndVoteTickBox.Selected = false;

            WaitForNextRoundRespawn = null;

            roundInitStatus = RoundInitStatus.Starting;

            int seed = inc.ReadInt32();
            string modeIdentifier = inc.ReadString();

            GameModePreset gameMode = GameModePreset.List.Find(gm => gm.Identifier == modeIdentifier);
            if (gameMode == null)
            {
                DebugConsole.ThrowError("Game mode \"" + modeIdentifier + "\" not found!");
                roundInitStatus = RoundInitStatus.Interrupted;
                yield return CoroutineStatus.Failure;
            }

            bool respawnAllowed = inc.ReadBoolean();
            serverSettings.AllowDisguises = inc.ReadBoolean();
            serverSettings.AllowRewiring = inc.ReadBoolean();
            serverSettings.LockAllDefaultWires = inc.ReadBoolean();
            serverSettings.AllowRagdollButton = inc.ReadBoolean();
            GameMain.NetLobbyScreen.UsingShuttle = inc.ReadBoolean();
            GameMain.LightManager.LosMode = (LosMode)inc.ReadByte();
            bool includesFinalize = inc.ReadBoolean(); inc.ReadPadBits();
            GameMain.LightManager.LightingEnabled = true;

            serverSettings.ReadMonsterEnabled(inc);

            Rand.SetSyncedSeed(seed);

            Task loadTask = null;
            var roundSummary = (GUIMessageBox.MessageBoxes.Find(c => c?.UserData is RoundSummary)?.UserData) as RoundSummary;

            if (gameMode != GameModePreset.MultiPlayerCampaign)
            {
                string levelSeed = inc.ReadString();
                float levelDifficulty = inc.ReadSingle();
                string subName = inc.ReadString();
                string subHash = inc.ReadString();
                string shuttleName = inc.ReadString();
                string shuttleHash = inc.ReadString();
                List<int> missionIndices = new List<int>();
                int missionCount = inc.ReadByte();
                for (int i = 0; i < missionCount; i++)
                {
                    missionIndices.Add(inc.ReadInt16());
                }
                if (!GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList))
                {
                    roundInitStatus = RoundInitStatus.Interrupted;
                    yield return CoroutineStatus.Success;
                }

                if (!GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox))
                {
                    roundInitStatus = RoundInitStatus.Interrupted;
                    yield return CoroutineStatus.Success;
                }

                //this shouldn't happen, TrySelectSub should stop the coroutine if the correct sub/shuttle cannot be found
                if (GameMain.NetLobbyScreen.SelectedSub == null ||
                    GameMain.NetLobbyScreen.SelectedSub.Name != subName ||
                    GameMain.NetLobbyScreen.SelectedSub.MD5Hash?.Hash != subHash)
                {
                    string errorMsg = "Failed to select submarine \"" + subName + "\" (hash: " + subHash + ").";
                    if (GameMain.NetLobbyScreen.SelectedSub == null)
                    {
                        errorMsg += "\n" + "SelectedSub is null";
                    }
                    else
                    {
                        if (GameMain.NetLobbyScreen.SelectedSub.Name != subName)
                        {
                            errorMsg += "\n" + "Name mismatch: " + GameMain.NetLobbyScreen.SelectedSub.Name + " != " + subName;
                        }
                        if (GameMain.NetLobbyScreen.SelectedSub.MD5Hash?.Hash != subHash)
                        {
                            errorMsg += "\n" + "Hash mismatch: " + GameMain.NetLobbyScreen.SelectedSub.MD5Hash?.Hash + " != " + subHash;
                        }
                    }
                    gameStarted = true;
                    GameMain.NetLobbyScreen.Select();
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:FailedToSelectSub" + subName, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    roundInitStatus = RoundInitStatus.Interrupted;
                    yield return CoroutineStatus.Failure;
                }
                if (GameMain.NetLobbyScreen.SelectedShuttle == null ||
                    GameMain.NetLobbyScreen.SelectedShuttle.Name != shuttleName ||
                    GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash?.Hash != shuttleHash)
                {
                    gameStarted = true;
                    GameMain.NetLobbyScreen.Select();
                    string errorMsg = "Failed to select shuttle \"" + shuttleName + "\" (hash: " + shuttleHash + ").";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:FailedToSelectShuttle" + shuttleName, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    roundInitStatus = RoundInitStatus.Interrupted;
                    yield return CoroutineStatus.Failure;
                }

                var selectedMissions = missionIndices.Select(i => MissionPrefab.List[i]);

                GameMain.GameSession = new GameSession(GameMain.NetLobbyScreen.SelectedSub, gameMode, missionPrefabs: selectedMissions);
                GameMain.GameSession.StartRound(levelSeed, levelDifficulty);
            }
            else
            {
                if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign))
                {
                    throw new InvalidOperationException("Attempted to start a campaign round when a campaign was not active.");
                }

                if (GameMain.GameSession?.CrewManager != null) { GameMain.GameSession.CrewManager.Reset(); }

                byte campaignID = inc.ReadByte();
                int nextLocationIndex = inc.ReadInt32();
                int nextConnectionIndex = inc.ReadInt32();
                int selectedLocationIndex = inc.ReadInt32();
                bool mirrorLevel = inc.ReadBoolean();


                if (campaign.CampaignID != campaignID)
                {
                    string errorMsg = "Failed to start campaign round (campaign ID does not match).";
                    gameStarted = true;
                    DebugConsole.ThrowError(errorMsg);
                    GameMain.NetLobbyScreen.Select();
                    roundInitStatus = RoundInitStatus.Interrupted;
                    yield return CoroutineStatus.Failure;
                }
                else if (campaign.Map == null)
                {
                    string errorMsg = "Failed to start campaign round (campaign map not loaded yet).";
                    gameStarted = true;
                    DebugConsole.ThrowError(errorMsg);
                    GameMain.NetLobbyScreen.Select();
                    roundInitStatus = RoundInitStatus.Interrupted;
                    yield return CoroutineStatus.Failure;
                }

                campaign.Map.SelectLocation(selectedLocationIndex);

                LevelData levelData = nextLocationIndex > -1 ?
                    campaign.Map.Locations[nextLocationIndex].LevelData :
                    campaign.Map.Connections[nextConnectionIndex].LevelData;

                if (roundSummary != null)
                {
                    loadTask = campaign.SelectSummaryScreen(roundSummary, levelData, mirrorLevel, null);
                    roundSummary.ContinueButton.Visible = false;
                }
                else
                {
                    GameMain.GameSession.StartRound(levelData, mirrorLevel);
                }
            }

            if (GameMain.Client?.ServerSettings?.Voting != null)
            {
                GameMain.Client.ServerSettings.Voting.ResetVotes(GameMain.Client.ConnectedClients);
            }

            if (loadTask != null)
            {
                while (!loadTask.IsCompleted && !loadTask.IsFaulted && !loadTask.IsCanceled)
                {
                    yield return CoroutineStatus.Running;
                }
            }

            roundInitStatus = RoundInitStatus.WaitingForStartGameFinalize;

            DateTime? timeOut = null;
            DateTime requestFinalizeTime = DateTime.Now;
            TimeSpan requestFinalizeInterval = new TimeSpan(0, 0, 2);
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.REQUEST_STARTGAMEFINALIZE);
            clientPeer.Send(msg, DeliveryMethod.Unreliable);

            while (true)
            {
                try
                {
                    if (timeOut.HasValue)
                    {
                        if (DateTime.Now > requestFinalizeTime)
                        {
                            msg = new WriteOnlyMessage();
                            msg.Write((byte)ClientPacketHeader.REQUEST_STARTGAMEFINALIZE);
                            clientPeer.Send(msg, DeliveryMethod.Unreliable);
                            requestFinalizeTime = DateTime.Now + requestFinalizeInterval;
                        }
                        if (DateTime.Now > timeOut)
                        {
                            DebugConsole.ThrowError("Error while starting the round (did not receive STARTGAMEFINALIZE message from the server). Stopping the round...");
                            roundInitStatus = RoundInitStatus.TimedOut;
                            break;
                        }
                    }
                    else
                    {
                        if (includesFinalize)
                        {
                            ReadStartGameFinalize(inc);
                            break;
                        }

                        //wait for up to 30 seconds for the server to send the STARTGAMEFINALIZE message
                        timeOut = DateTime.Now + new TimeSpan(0, 0, seconds: 30);
                    }

                    if (!connected)
                    {
                        roundInitStatus = RoundInitStatus.Interrupted;
                        break;
                    }

                    if (roundInitStatus != RoundInitStatus.WaitingForStartGameFinalize) { break; }

                    clientPeer.Update((float)Timing.Step);

                    if (roundInitStatus != RoundInitStatus.WaitingForStartGameFinalize) { break; }
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("There was an error initializing the round.", e, true);
                    roundInitStatus = RoundInitStatus.Error;
                    break;
                }

                //waiting for a STARTGAMEFINALIZE message
                yield return CoroutineStatus.Running;
            }

            if (roundInitStatus != RoundInitStatus.Started)
            {
                if (roundInitStatus != RoundInitStatus.Interrupted)
                {
                    DebugConsole.ThrowError(roundInitStatus.ToString());
                    CoroutineManager.StartCoroutine(EndGame(""));
                    yield return CoroutineStatus.Failure;
                }
                else
                {
                    yield return CoroutineStatus.Success;
                }
            }

            if (GameMain.GameSession.Submarine.Info.IsFileCorrupted)
            {
                DebugConsole.ThrowError($"Failed to start a round. Could not load the submarine \"{GameMain.GameSession.Submarine.Info.Name}\".");
                yield return CoroutineStatus.Failure;
            }

            for (int i = 0; i < Submarine.MainSubs.Length; i++)
            {
                if (Submarine.MainSubs[i] == null) { break; }

                var teamID = i == 0 ? CharacterTeamType.Team1 : CharacterTeamType.Team2;
                Submarine.MainSubs[i].TeamID = teamID;
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == null) { continue; }
                    if (item.Submarine != Submarine.MainSubs[i] && !Submarine.MainSubs[i].DockedTo.Contains(item.Submarine)) { continue; }
                    foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                    {
                        wifiComponent.TeamID = Submarine.MainSubs[i].TeamID;
                    }
                }
                foreach (Submarine sub in Submarine.MainSubs[i].DockedTo)
                {
                    if (sub.Info.Type == SubmarineType.Outpost) { continue; }
                    sub.TeamID = teamID;
                }
            }

            if (respawnAllowed)
            {
                bool isOutpost = GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign && Level.Loaded?.Type == LevelData.LevelType.Outpost;
                respawnManager = new RespawnManager(this, GameMain.NetLobbyScreen.UsingShuttle && !isOutpost ? GameMain.NetLobbyScreen.SelectedShuttle : null);
            }

            gameStarted = true;
            ServerSettings.ServerDetailsChanged = true;

            if (roundSummary != null)
            {
                roundSummary.ContinueButton.Visible = true;
            }

            GameMain.GameScreen.Select();

            AddChatMessage($"ServerMessage.HowToCommunicate~[chatbutton]={GameMain.Config.KeyBindText(InputType.Chat)}~[radiobutton]={GameMain.Config.KeyBindText(InputType.RadioChat)}", ChatMessageType.Server);

            yield return CoroutineStatus.Success;
        }

        public IEnumerable<object> EndGame(string endMessage, List<TraitorMissionResult> traitorResults = null, CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            //round starting up, wait for it to finish
            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 60);
            while (TaskPool.IsTaskRunning("AsyncCampaignStartRound"))
            {
                if (DateTime.Now > timeOut)
                {
                    throw new Exception("Failed to end a round (async campaign round start timed out).");
                }
                yield return new WaitForSeconds(1.0f);
            }

            if (!gameStarted)
            {
                GameMain.NetLobbyScreen.Select();
                yield return CoroutineStatus.Success;
            }

            if (GameMain.GameSession != null) { GameMain.GameSession.EndRound(endMessage, traitorResults, transitionType); }
            
            ServerSettings.ServerDetailsChanged = true;

            gameStarted = false;
            Character.Controlled = null;
            WaitForNextRoundRespawn = null;
            SpawnAsTraitor = false;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;
            respawnManager = null;

            if (Screen.Selected == GameMain.GameScreen)
            {
                // Enable characters near the main sub for the endCinematic
                foreach (Character c in Character.CharacterList)
                {
                    if (Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, c.WorldPosition) < MathUtils.Pow2(c.Params.DisableDistance))
                    {
                        c.Enabled = true;
                    }
                }

                EndCinematic = new CameraTransition(Submarine.MainSub, GameMain.GameScreen.Cam, Alignment.CenterLeft, Alignment.CenterRight);
                while (EndCinematic.Running && Screen.Selected == GameMain.GameScreen)
                {
                    yield return CoroutineStatus.Running;
                }
                EndCinematic = null;
            }
            
            Submarine.Unload();
            if (transitionType == CampaignMode.TransitionType.None)
            {
                GameMain.NetLobbyScreen.Select();
            }
            myCharacter = null;
            foreach (Client c in otherClients)
            {
                c.InGame = false;
                c.Character = null;
            }

            yield return CoroutineStatus.Success;
        }

        private void ReadInitialUpdate(IReadMessage inc)
        {
            myID = inc.ReadByte();

            UInt16 subListCount = inc.ReadUInt16();
            ServerSubmarines.Clear();
            for (int i = 0; i < subListCount; i++)
            {
                string subName = inc.ReadString();
                string subHash = inc.ReadString();
                byte subClass  = inc.ReadByte();
                bool requiredContentPackagesInstalled = inc.ReadBoolean();

                var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.Hash == subHash);
                if (matchingSub == null)
                {
                    matchingSub = new SubmarineInfo(Path.Combine(SubmarineInfo.SavePath, subName) + ".sub", subHash, tryLoad: false)
                    {
                        SubmarineClass = (SubmarineClass)subClass
                    };
                }
                matchingSub.RequiredContentPackagesInstalled = requiredContentPackagesInstalled;
                ServerSubmarines.Add(matchingSub);
            }

            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.SubList, ServerSubmarines);
            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.ShuttleList.ListBox, ServerSubmarines);

            gameStarted = inc.ReadBoolean();
            bool allowSpectating = inc.ReadBoolean();

            ReadPermissions(inc);
            
            if (gameStarted)
            {
                string ownedSubmarineIndexes = inc.ReadString();
                if (ownedSubmarineIndexes != string.Empty)
                {
                    string[] ownedIndexes = ownedSubmarineIndexes.Split(';');

                    if (GameMain.GameSession != null)
                    {
                        GameMain.GameSession.OwnedSubmarines = new List<SubmarineInfo>();
                        for (int i = 0; i < ownedIndexes.Length; i++)
                        {
                            int index;
                            if (int.TryParse(ownedIndexes[i], out index))
                            {
                                SubmarineInfo sub = GameMain.Client.ServerSubmarines[index];
                                if (GameMain.NetLobbyScreen.CheckIfCampaignSubMatches(sub, "owned"))
                                {
                                    GameMain.GameSession.OwnedSubmarines.Add(sub);
                                }
                            }
                        }
                    }
                    else
                    {
                        GameMain.NetLobbyScreen.ServerOwnedSubmarines = new List<SubmarineInfo>();
                        for (int i = 0; i < ownedIndexes.Length; i++)
                        {
                            int index;
                            if (int.TryParse(ownedIndexes[i], out index))
                            {
                                SubmarineInfo sub = GameMain.Client.ServerSubmarines[index];
                                if (GameMain.NetLobbyScreen.CheckIfCampaignSubMatches(sub, "owned"))
                                {
                                    GameMain.NetLobbyScreen.ServerOwnedSubmarines.Add(sub);
                                }
                            }
                        }
                    }
                }

                if (Screen.Selected != GameMain.GameScreen)
                {
                    new GUIMessageBox(TextManager.Get("PleaseWait"), TextManager.Get(allowSpectating ? "RoundRunningSpectateEnabled" : "RoundRunningSpectateDisabled"));
                    GameMain.NetLobbyScreen.Select();
                }
            }
        }

        private void ReadClientList(IReadMessage inc)
        {
            bool refreshCampaignUI = false;
            UInt16 listId = inc.ReadUInt16();
            List<TempClient> tempClients = new List<TempClient>();
            int clientCount = inc.ReadByte();
            for (int i = 0; i < clientCount; i++)
            {
                byte id             = inc.ReadByte();
                UInt64 steamId      = inc.ReadUInt64();
                UInt16 nameId       = inc.ReadUInt16();
                string name         = inc.ReadString();
                string preferredJob = inc.ReadString();
                byte preferredTeam  = inc.ReadByte();
                UInt16 characterID  = inc.ReadUInt16();
                float karma         = inc.ReadSingle();
                bool muted          = inc.ReadBoolean();
                bool inGame         = inc.ReadBoolean();
                bool hasPermissions = inc.ReadBoolean();
                bool isOwner        = inc.ReadBoolean();
                bool allowKicking   = inc.ReadBoolean() || IsServerOwner;
                inc.ReadPadBits();

                tempClients.Add(new TempClient
                {
                    ID = id,
                    NameID = nameId,
                    SteamID = steamId,
                    Name = name,
                    PreferredJob = preferredJob,
                    PreferredTeam = (CharacterTeamType)preferredTeam,
                    CharacterID = characterID,
                    Karma = karma,
                    Muted = muted,
                    InGame = inGame,
                    HasPermissions = hasPermissions,
                    IsOwner = isOwner,
                    AllowKicking = allowKicking
                });
            }

            if (NetIdUtils.IdMoreRecent(listId, LastClientListUpdateID))
            {
                bool updateClientListId = true;
                List<Client> currentClients = new List<Client>();
                foreach (TempClient tc in tempClients)
                {
                    //see if the client already exists
                    var existingClient = ConnectedClients.Find(c => c.ID == tc.ID && c.Name == tc.Name);
                    if (existingClient == null) //if not, create it
                    {
                        existingClient = new Client(tc.Name, tc.ID)
                        {
                            SteamID = tc.SteamID,
                            Muted = tc.Muted,
                            InGame = tc.InGame,
                            AllowKicking = tc.AllowKicking,
                            IsOwner = tc.IsOwner
                        };
                        ConnectedClients.Add(existingClient);
                        refreshCampaignUI = true;
                        GameMain.NetLobbyScreen.AddPlayer(existingClient);
                    }
                    existingClient.NameID = tc.NameID;
                    existingClient.PreferredJob = tc.PreferredJob;
                    existingClient.PreferredTeam = tc.PreferredTeam;
                    existingClient.Character = null;
                    existingClient.Karma = tc.Karma;
                    existingClient.Muted = tc.Muted;
                    existingClient.HasPermissions = tc.HasPermissions;
                    existingClient.InGame = tc.InGame;
                    existingClient.IsOwner = tc.IsOwner;
                    existingClient.AllowKicking = tc.AllowKicking;
                    GameMain.NetLobbyScreen.SetPlayerNameAndJobPreference(existingClient);
                    if (Screen.Selected != GameMain.NetLobbyScreen && tc.CharacterID > 0)
                    {
                        existingClient.CharacterID = tc.CharacterID;
                    }
                    if (existingClient.ID == myID)
                    {
                        existingClient.SetPermissions(permissions, permittedConsoleCommands);
                        if (!NetIdUtils.IdMoreRecent(nameId, tc.NameID))
                        {
                            name = tc.Name;
                            nameId = tc.NameID;
                        }
                        if (GameMain.NetLobbyScreen.CharacterNameBox != null &&
                            !GameMain.NetLobbyScreen.CharacterNameBox.Selected)
                        {
                            GameMain.NetLobbyScreen.CharacterNameBox.Text = name;
                        }
                    }
                    currentClients.Add(existingClient);
                }
                //remove clients that aren't present anymore
                for (int i = ConnectedClients.Count - 1; i >= 0; i--)
                {
                    if (!currentClients.Contains(ConnectedClients[i]))
                    {
                        GameMain.NetLobbyScreen.RemovePlayer(ConnectedClients[i]);
                        ConnectedClients[i].Dispose();
                        ConnectedClients.RemoveAt(i);
                        refreshCampaignUI = true;
                    }
                }
                foreach (Client client in ConnectedClients)
                {
                    int index = previouslyConnectedClients.FindIndex(c => c.ID == client.ID);
                    if (index < 0)
                    {
                        if (previouslyConnectedClients.Count > 100)
                        {
                            previouslyConnectedClients.RemoveRange(0, previouslyConnectedClients.Count - 100);
                        }
                    }
                    else
                    {
                        previouslyConnectedClients.RemoveAt(index);
                    }
                    previouslyConnectedClients.Add(client);
                }
                if (updateClientListId) { LastClientListUpdateID = listId; }

                if (clientPeer is SteamP2POwnerPeer)
                {
                    TaskPool.Add("WaitForPingDataAsync (owner)",
                        Steamworks.SteamNetworkingUtils.WaitForPingDataAsync(), (task) =>
                    {
                        Steam.SteamManager.UpdateLobby(serverSettings);
                    });

                    Steam.SteamManager.UpdateLobby(serverSettings);
                }
            }

            if (refreshCampaignUI)
            {
                if (GameMain.GameSession?.GameMode is CampaignMode campaign)
                {
                    campaign.CampaignUI?.UpgradeStore?.RefreshAll();
                    campaign.CampaignUI?.CrewManagement?.RefreshPermissions();
                }
            }
        }

        private bool initialUpdateReceived;

        private void ReadLobbyUpdate(IReadMessage inc)
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
                            var prevDispatcher = GUI.KeyboardDispatcher.Subscriber;

                            UInt16 updateID = inc.ReadUInt16();

                            UInt16 settingsLen = inc.ReadUInt16();
                            byte[] settingsData = inc.ReadBytes(settingsLen);

                            bool isInitialUpdate = inc.ReadBoolean();
                            if (isInitialUpdate)
                            {
                                if (GameSettings.VerboseLogging)
                                {
                                    DebugConsole.NewMessage("Received initial lobby update, ID: " + updateID + ", last ID: " + GameMain.NetLobbyScreen.LastUpdateID, Color.Gray);
                                }
                                ReadInitialUpdate(inc);
                                initialUpdateReceived = true;
                            }

                            string selectSubName = inc.ReadString();
                            string selectSubHash = inc.ReadString();

                            bool usingShuttle = inc.ReadBoolean();
                            string selectShuttleName = inc.ReadString();
                            string selectShuttleHash = inc.ReadString();

                            UInt16 campaignSubmarineIndexCount = inc.ReadUInt16();
                            List<int> campaignSubIndices = new List<int>();
                            for (int i = 0; i< campaignSubmarineIndexCount; i++)
                            {
                                campaignSubIndices.Add(inc.ReadUInt16());
                            }

                            bool allowSubVoting = inc.ReadBoolean();
                            bool allowModeVoting = inc.ReadBoolean();

                            bool voiceChatEnabled = inc.ReadBoolean();

                            bool allowSpectating = inc.ReadBoolean();

                            YesNoMaybe traitorsEnabled = (YesNoMaybe)inc.ReadRangedInteger(0, 2);
                            MissionType missionType = (MissionType)inc.ReadRangedInteger(0, (int)MissionType.All);
                            int modeIndex = inc.ReadByte();

                            string levelSeed = inc.ReadString();
                            float levelDifficulty = inc.ReadSingle();

                            byte botCount = inc.ReadByte();
                            BotSpawnMode botSpawnMode = inc.ReadBoolean() ? BotSpawnMode.Fill : BotSpawnMode.Normal;

                            bool autoRestartEnabled = inc.ReadBoolean();
                            float autoRestartTimer = autoRestartEnabled ? inc.ReadSingle() : 0.0f;

                            bool radiationEnabled = inc.ReadBoolean();

                            //ignore the message if we already a more up-to-date one
                            //or if we're still waiting for the initial update
                            if (NetIdUtils.IdMoreRecent(updateID, GameMain.NetLobbyScreen.LastUpdateID) &&
                                (isInitialUpdate || initialUpdateReceived))
                            {
                                ReadWriteMessage settingsBuf = new ReadWriteMessage();
                                settingsBuf.Write(settingsData, 0, settingsLen); settingsBuf.BitPosition = 0;
                                serverSettings.ClientRead(settingsBuf);
                                if (!IsServerOwner)
                                {
                                    ServerInfo info = serverSettings.GetServerListInfo();
                                    GameMain.ServerListScreen.AddToRecentServers(info);
                                    GameMain.NetLobbyScreen.Favorite.Visible = true;
                                    GameMain.NetLobbyScreen.Favorite.Selected = GameMain.ServerListScreen.IsFavorite(info);
                                }
                                else
                                {
                                    GameMain.NetLobbyScreen.Favorite.Visible = false;
                                }

                                GameMain.NetLobbyScreen.LastUpdateID = updateID;

                                serverSettings.ServerLog.ServerName = serverSettings.ServerName;

                                if (!GameMain.NetLobbyScreen.ServerName.Selected) GameMain.NetLobbyScreen.ServerName.Text = serverSettings.ServerName;
                                if (!GameMain.NetLobbyScreen.ServerMessage.Selected) GameMain.NetLobbyScreen.ServerMessage.Text = serverSettings.ServerMessageText;
                                GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;

                                if (!allowSubVoting) GameMain.NetLobbyScreen.TrySelectSub(selectSubName, selectSubHash, GameMain.NetLobbyScreen.SubList);
                                GameMain.NetLobbyScreen.TrySelectSub(selectShuttleName, selectShuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox);

                                GameMain.NetLobbyScreen.SetTraitorsEnabled(traitorsEnabled);
                                GameMain.NetLobbyScreen.SetMissionType(missionType);

                                if (!allowModeVoting) GameMain.NetLobbyScreen.SelectMode(modeIndex);
                                if (isInitialUpdate && GameMain.NetLobbyScreen.SelectedMode == GameModePreset.MultiPlayerCampaign)
                                {
                                    if (GameMain.Client.IsServerOwner) RequestSelectMode(modeIndex);
                                }

                                if (campaignSubIndices != null)
                                {
                                    GameMain.NetLobbyScreen.CampaignSubmarines = new List<SubmarineInfo>();
                                    foreach (UInt16 campaignSubIndex in campaignSubIndices)
                                    {
                                        SubmarineInfo sub = GameMain.Client.ServerSubmarines[campaignSubIndex];
                                        if (GameMain.NetLobbyScreen.CheckIfCampaignSubMatches(sub, "campaign"))
                                        {
                                            GameMain.NetLobbyScreen.CampaignSubmarines.Add(sub);
                                        }                                        
                                    }

                                    if (HasPermission(ClientPermissions.ManageCampaign) && !gameStarted && GameMain.NetLobbyScreen?.CampaignSetupUI != null)
                                    {
                                        GameMain.NetLobbyScreen.CampaignSetupUI.RefreshMultiplayerCampaignSubUI(GameMain.NetLobbyScreen.CampaignSubmarines);
                                    }
                                }

                                GameMain.NetLobbyScreen.SetAllowSpectating(allowSpectating);
                                GameMain.NetLobbyScreen.LevelSeed = levelSeed;
                                GameMain.NetLobbyScreen.SetLevelDifficulty(levelDifficulty);
                                GameMain.NetLobbyScreen.SetRadiationEnabled(radiationEnabled);
                                GameMain.NetLobbyScreen.SetBotSpawnMode(botSpawnMode);
                                GameMain.NetLobbyScreen.SetBotCount(botCount);
                                GameMain.NetLobbyScreen.SetAutoRestart(autoRestartEnabled, autoRestartTimer);

                                serverSettings.VoiceChatEnabled = voiceChatEnabled;
                                serverSettings.Voting.AllowSubVoting = allowSubVoting;
                                serverSettings.Voting.AllowModeVoting = allowModeVoting;

                                if (clientPeer is SteamP2POwnerPeer)
                                {
                                    Steam.SteamManager.UpdateLobby(serverSettings);
                                }

                                GUI.KeyboardDispatcher.Subscriber = prevDispatcher;
                            }
                        }

                        bool campaignUpdated = inc.ReadBoolean();
                        inc.ReadPadBits();
                        if (campaignUpdated)
                        {
                            MultiPlayerCampaign.ClientRead(inc);
                        }
                        else if (GameMain.NetLobbyScreen.SelectedMode != GameModePreset.MultiPlayerCampaign)
                        {
                            GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
                        }

                        lastSentChatMsgID = inc.ReadUInt16();
                        break;
                    case ServerNetObject.CLIENT_LIST:
                        ReadClientList(inc);
                        break;
                    case ServerNetObject.CHAT_MESSAGE:
                        ChatMessage.ClientRead(inc);
                        break;
                    case ServerNetObject.VOTE:
                        serverSettings.Voting.ClientRead(inc);
                        break;
                }
            }
        }

        private void ReadIngameUpdate(IReadMessage inc)
        {
            List<IServerSerializable> entities = new List<IServerSerializable>();

            float sendingTime = inc.ReadSingle() - 0.0f;//TODO: reimplement inc.SenderConnection.RemoteTimeOffset;

            ServerNetObject? prevObjHeader = null;
            long prevBitPos = 0;
            long prevBytePos = 0;

            long prevBitLength = 0;
            long prevByteLength = 0;

            ServerNetObject? objHeader = null;
            try
            {
                while ((objHeader = (ServerNetObject)inc.ReadByte()) != ServerNetObject.END_OF_MESSAGE)
                {
                    switch (objHeader)
                    {
                        case ServerNetObject.SYNC_IDS:
                            lastSentChatMsgID = inc.ReadUInt16();
                            LastSentEntityEventID = inc.ReadUInt16();

                            bool campaignUpdated = inc.ReadBoolean();
                            inc.ReadPadBits();
                            if (campaignUpdated)
                            {
                                MultiPlayerCampaign.ClientRead(inc);
                            }
                            else if (GameMain.NetLobbyScreen.SelectedMode != GameModePreset.MultiPlayerCampaign)
                            {
                                GameMain.NetLobbyScreen.SetCampaignCharacterInfo(null);
                            }
                            break;
                        case ServerNetObject.ENTITY_POSITION:
                            bool isItem = inc.ReadBoolean();
                            UInt16 id = inc.ReadUInt16();
                            uint msgLength = inc.ReadVariableUInt32();
                            int msgEndPos = (int)(inc.BitPosition + msgLength * 8);

                            var entity = Entity.FindEntityByID(id) as IServerSerializable;
                            if (msgEndPos > inc.LengthBits)
                            {
                                DebugConsole.ThrowError($"Error while reading a position update for the entity \"({entity?.ToString() ?? "null"})\". Message length exceeds the size of the buffer.");
                                return;
                            }

                            entities.Add(entity);
                            if (entity != null && (entity is Item || entity is Character || entity is Submarine))
                            {
                                if (entity is Item != isItem)
                                {
                                    DebugConsole.AddWarning($"Received a potentially invalid ENTITY_POSITION message. Entity type does not match (server entity is {(isItem ? "an item" : "not an item")}, client entity is {(entity?.GetType().ToString() ?? "null")}). Ignoring the message...");
                                }
                                else
                                {
                                    entity.ClientRead(objHeader.Value, inc, sendingTime);
                                }
                            }                            

                            //force to the correct position in case the entity doesn't exist
                            //or the message wasn't read correctly for whatever reason
                            inc.BitPosition = msgEndPos;
                            inc.ReadPadBits();
                            break;
                        case ServerNetObject.CLIENT_LIST:
                            ReadClientList(inc);
                            break;
                        case ServerNetObject.ENTITY_EVENT:
                        case ServerNetObject.ENTITY_EVENT_INITIAL:
                            if (!entityEventManager.Read(objHeader.Value, inc, sendingTime, entities))
                            {
                                return;
                            }
                            break;
                        case ServerNetObject.CHAT_MESSAGE:
                            ChatMessage.ClientRead(inc);
                            break;
                        default:
                            throw new Exception($"Unknown object header \"{objHeader}\"!)");
                    }
                    prevBitLength = inc.BitPosition - prevBitPos;
                    prevByteLength = inc.BytePosition - prevBytePos;

                    prevObjHeader = objHeader;
                    prevBitPos = inc.BitPosition;
                    prevBytePos = inc.BytePosition;
                }
            }

            catch (Exception ex)
            {
                List<string> errorLines = new List<string>
                {
                    ex.Message,
                    "Message length: " + inc.LengthBits + " (" + inc.LengthBytes + " bytes)",
                    "Read position: " + inc.BitPosition,
                    "Header: " + (objHeader != null ? objHeader.Value.ToString() : "Error occurred on the very first header!"),
                    prevObjHeader != null ? "Previous header: " + prevObjHeader : "Error occurred on the very first header!",
                    "Previous object was " + (prevBitLength) + " bits long (" + (prevByteLength) + " bytes)",
                    " "
                };
                errorLines.Add(ex.StackTrace.CleanupStackTrace());
                errorLines.Add(" ");
                if (prevObjHeader == ServerNetObject.ENTITY_EVENT || prevObjHeader == ServerNetObject.ENTITY_EVENT_INITIAL || 
                    objHeader == ServerNetObject.ENTITY_EVENT || objHeader == ServerNetObject.ENTITY_EVENT_INITIAL ||
                    objHeader == ServerNetObject.ENTITY_POSITION || prevObjHeader == ServerNetObject.ENTITY_POSITION)
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

                DebugConsole.ThrowError("Writing object data to \"networkerror_data.log\", please send this file to us at http://github.com/Regalis11/Barotrauma/issues");

                using (FileStream fl = File.Open("networkerror_data.log", System.IO.FileMode.Create))
                {
                    using (System.IO.BinaryWriter bw = new System.IO.BinaryWriter(fl))
                    using (System.IO.StreamWriter sw = new System.IO.StreamWriter(fl))
                    {
                        bw.Write(inc.Buffer, (int)(prevBytePos - prevByteLength), (int)(prevByteLength));
                        sw.WriteLine("");
                        foreach (string line in errorLines)
                        {
                            sw.WriteLine(line);
                        }
                    }
                }
                throw new Exception("Read error: please send us \"networkerror_data.log\"!");
            }            
        }

        private void SendLobbyUpdate()
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_LOBBY);

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);
            outmsg.Write(LastClientListUpdateID);
            outmsg.Write(nameId);
            outmsg.Write(name);
            var jobPreferences = GameMain.NetLobbyScreen.JobPreferences;
            if (jobPreferences.Count > 0)
            {
                outmsg.Write(jobPreferences[0].First.Identifier);
            }
            else
            {
                outmsg.Write("");
            }
            outmsg.Write((byte)GameMain.Config.TeamPreference);

            if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign) || campaign.LastSaveID == 0)
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
                if (outmsg.LengthBytes + chatMsgQueue[i].EstimateLengthBytesClient() > MsgConstants.MTU - 5)
                {
                    //no more room in this packet
                    break;
                }
                chatMsgQueue[i].ClientWrite(outmsg);
            }
            outmsg.Write((byte)ClientNetObject.END_OF_MESSAGE);

            if (outmsg.LengthBytes > MsgConstants.MTU)
            {
                DebugConsole.ThrowError($"Maximum packet size exceeded ({outmsg.LengthBytes} > {MsgConstants.MTU})");
            }

            clientPeer.Send(outmsg, DeliveryMethod.Unreliable);
        }

        private void SendIngameUpdate()
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_INGAME);
            outmsg.Write(entityEventManager.MidRoundSyncingDone);
            outmsg.WritePadBits();

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            //outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);
            outmsg.Write(entityEventManager.LastReceivedID);
            outmsg.Write(LastClientListUpdateID);

            if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign) || campaign.LastSaveID == 0)
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

            Character.Controlled?.ClientWrite(outmsg);
            GameMain.GameScreen.Cam?.ClientWrite(outmsg);

            entityEventManager.Write(outmsg, clientPeer?.ServerConnection);

            chatMsgQueue.RemoveAll(cMsg => !NetIdUtils.IdMoreRecent(cMsg.NetStateID, lastSentChatMsgID));
            for (int i = 0; i < chatMsgQueue.Count && i < ChatMessage.MaxMessagesPerPacket; i++)
            {
                if (outmsg.LengthBytes + chatMsgQueue[i].EstimateLengthBytesClient() > MsgConstants.MTU - 5)
                {
                    //not enough room in this packet
                    break;
                }
                chatMsgQueue[i].ClientWrite(outmsg);
            }

            outmsg.Write((byte)ClientNetObject.END_OF_MESSAGE);

            if (outmsg.LengthBytes > MsgConstants.MTU)
            {
                DebugConsole.ThrowError($"Maximum packet size exceeded ({outmsg.LengthBytes} > {MsgConstants.MTU})");
            }

            clientPeer.Send(outmsg, DeliveryMethod.Unreliable);
        }

        public void SendChatMessage(ChatMessage msg)
        {
            if (clientPeer?.ServerConnection == null) { return; }
            lastQueueChatMsgID++;
            msg.NetStateID = lastQueueChatMsgID;
            chatMsgQueue.Add(msg);
        }

        public void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Default)
        {
            if (clientPeer?.ServerConnection == null) { return; }

            ChatMessage chatMessage = ChatMessage.Create(
                gameStarted && myCharacter != null ? myCharacter.Name : name,
                message,
                type,
                gameStarted && myCharacter != null ? myCharacter : null);

            lastQueueChatMsgID++;
            chatMessage.NetStateID = lastQueueChatMsgID;

            chatMsgQueue.Add(chatMessage);
        }

        public void SendRespawnPromptResponse(bool waitForNextRoundRespawn)
        {
            WaitForNextRoundRespawn = waitForNextRoundRespawn;
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.READY_TO_SPAWN);
            msg.Write((bool)waitForNextRoundRespawn);
            clientPeer?.Send(msg, DeliveryMethod.Reliable);
        }

        public void RequestFile(FileTransferType fileType, string file, string fileHash)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Initiate);
            msg.Write((byte)fileType);
            if (file != null) msg.Write(file);
            if (fileHash != null) msg.Write(fileHash);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void CancelFileTransfer(FileReceiver.FileTransferIn transfer)
        {
            CancelFileTransfer(transfer.ID);
        }

        public void UpdateFileTransfer(int id, int offset, bool reliable = false)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Data);
            msg.Write((byte)id);
            msg.Write(offset);
            clientPeer.Send(msg, reliable ? DeliveryMethod.Reliable : DeliveryMethod.Unreliable);
        }

        public void CancelFileTransfer(int id)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.FILE_REQUEST);
            msg.Write((byte)FileTransferMessageType.Cancel);
            msg.Write((byte)id);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        private void OnFileReceived(FileReceiver.FileTransferIn transfer)
        {
            switch (transfer.FileType)
            {
                case FileTransferType.Submarine:
                    new GUIMessageBox(TextManager.Get("ServerDownloadFinished"), TextManager.GetWithVariable("FileDownloadedNotification", "[filename]", transfer.FileName));
                    var newSub = new SubmarineInfo(transfer.FilePath);
                    if (newSub.IsFileCorrupted) { return; }

                    var existingSubs = SubmarineInfo.SavedSubmarines.Where(s => s.Name == newSub.Name && s.MD5Hash.Hash == newSub.MD5Hash.Hash).ToList();
                    foreach (SubmarineInfo existingSub in existingSubs)
                    {
                        existingSub.Dispose();
                    }
                    SubmarineInfo.AddToSavedSubs(newSub);

                    for (int i = 0; i < 2; i++)
                    {
                        IEnumerable<GUIComponent> subListChildren = (i == 0) ?
                            GameMain.NetLobbyScreen.ShuttleList.ListBox.Content.Children :
                            GameMain.NetLobbyScreen.SubList.Content.Children;

                        var subElement = subListChildren.FirstOrDefault(c =>
                            ((SubmarineInfo)c.UserData).Name == newSub.Name &&
                            ((SubmarineInfo)c.UserData).MD5Hash.Hash == newSub.MD5Hash.Hash);
                        if (subElement == null) continue;

                        Color newSubTextColor = new Color(subElement.GetChild<GUITextBlock>().TextColor, 1.0f);
                        subElement.GetChild<GUITextBlock>().TextColor = newSubTextColor;

                        GUITextBlock classTextBlock = subElement.GetChildByUserData("classtext") as GUITextBlock;
                        if (classTextBlock != null)
                        {
                            Color newSubClassTextColor = new Color(classTextBlock.TextColor, 0.8f);
                            classTextBlock.Text = TextManager.Get($"submarineclass.{newSub.SubmarineClass}");
                            classTextBlock.TextColor = newSubClassTextColor;
                        }

                        subElement.UserData = newSub;
                        subElement.ToolTip = newSub.Description;
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

                    Pair<string, string> failedCampaignSub = GameMain.NetLobbyScreen.FailedCampaignSubs.Find(s => s.First == newSub.Name && s.Second == newSub.MD5Hash.Hash);
                    if (failedCampaignSub != null)
                    {
                        GameMain.NetLobbyScreen.CampaignSubmarines.Add(newSub);
                        GameMain.NetLobbyScreen.FailedCampaignSubs.Remove(failedCampaignSub);
                    }

                    Pair<string, string> failedOwnedSub = GameMain.NetLobbyScreen.FailedOwnedSubs.Find(s => s.First == newSub.Name && s.Second == newSub.MD5Hash.Hash);
                    if (failedOwnedSub != null)
                    {
                        GameMain.NetLobbyScreen.ServerOwnedSubmarines.Add(newSub);
                        GameMain.NetLobbyScreen.FailedOwnedSubs.Remove(failedOwnedSub);
                    }

                    // Replace a submarine dud with the downloaded version
                    SubmarineInfo existingServerSub = ServerSubmarines.Find(s => s.Name == newSub.Name && s.MD5Hash?.Hash == newSub.MD5Hash?.Hash);
                    if (existingServerSub != null)
                    {
                        int existingIndex = ServerSubmarines.IndexOf(existingServerSub);
                        ServerSubmarines.RemoveAt(existingIndex);
                        ServerSubmarines.Insert(existingIndex, newSub);
                        existingServerSub.Dispose();
                    }

                    break;
                case FileTransferType.CampaignSave:
                    XDocument gameSessionDoc = SaveUtil.LoadGameSessionDoc(transfer.FilePath);
                    byte campaignID = (byte)MathHelper.Clamp(gameSessionDoc.Root.GetAttributeInt("campaignid", 0), 0, 255);
                    if (!(GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign) || campaign.CampaignID != campaignID)
                    {
                        string savePath = transfer.FilePath;
                        GameMain.GameSession = new GameSession(null, savePath, GameModePreset.MultiPlayerCampaign, CampaignSettings.Unsure);
                        campaign = (MultiPlayerCampaign)GameMain.GameSession.GameMode;
                        campaign.CampaignID = campaignID;
                        GameMain.NetLobbyScreen.ToggleCampaignMode(true);
                    }

                    GameMain.GameSession.SavePath = transfer.FilePath;
                    if (GameMain.GameSession.SubmarineInfo == null || campaign.Map == null)
                    {
                        string subPath = Path.Combine(SaveUtil.TempPath, gameSessionDoc.Root.GetAttributeString("submarine", "")) + ".sub";
                        GameMain.GameSession.SubmarineInfo = new SubmarineInfo(subPath, "");
                    }

                    campaign.LoadState(GameMain.GameSession.SavePath);
                    GameMain.GameSession?.SubmarineInfo?.Reload();
                    GameMain.GameSession?.SubmarineInfo?.CheckSubsLeftBehind();

                    if (GameMain.GameSession?.SubmarineInfo?.Name != null)
                    {
                        GameMain.NetLobbyScreen.TryDisplayCampaignSubmarine(GameMain.GameSession.SubmarineInfo);
                    }
                    campaign.LastSaveID = campaign.PendingSaveID;

                    if (Screen.Selected == GameMain.NetLobbyScreen)
                    {
                        //reselect to refrest the state of the lobby screen (enable spectate button, etc)
                        GameMain.NetLobbyScreen.Select();
                    }

                    DebugConsole.Log("Campaign save received (" + GameMain.GameSession.SavePath + "), save ID " + campaign.LastSaveID);
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

        public override void CreateEntityEvent(INetSerializable entity, object[] extraData)
        {
            if (!(entity is IClientSerializable)) throw new InvalidCastException("Entity is not IClientSerializable");
            entityEventManager.CreateEvent(entity as IClientSerializable, extraData);
        }

        public bool HasPermission(ClientPermissions permission)
        {
            return permissions.HasFlag(permission);
        }

        public bool HasConsoleCommandPermission(string commandName)
        {
            if (!permissions.HasFlag(ClientPermissions.ConsoleCommands)) { return false; }

            if (permittedConsoleCommands.Any(c => c.Equals(commandName, StringComparison.OrdinalIgnoreCase))) { return true; }

            //check aliases
            foreach (DebugConsole.Command command in DebugConsole.Commands)
            {
                if (command.names.Contains(commandName))
                {
                    if (command.names.Intersect(permittedConsoleCommands).Any()) { return true; }
                    break;
                }
            }

            return false;
        }

        public override void Disconnect()
        {
            allowReconnect = false;

            if (clientPeer is SteamP2PClientPeer || clientPeer is SteamP2POwnerPeer)
            {
                SteamManager.LeaveLobby();
            }

            clientPeer?.Close();
            clientPeer = null;

            List<FileReceiver.FileTransferIn> activeTransfers = new List<FileReceiver.FileTransferIn>(FileReceiver.ActiveTransfers);
            foreach (var fileTransfer in activeTransfers)
            {
                FileReceiver.StopTransfer(fileTransfer, deleteFile: true);
            }

            if (HasPermission(ClientPermissions.ServerLog))
            {
                serverSettings.ServerLog?.Save();
            }

            if (ChildServerRelay.Process != null)
            {
                int checks = 0;
                while (ChildServerRelay.Process != null && !ChildServerRelay.Process.HasExited)
                {
                    if (checks > 10)
                    {
                        ChildServerRelay.ShutDown();
                    }
                    Thread.Sleep(100);
                    checks++;
                }
            }
            ChildServerRelay.ShutDown();

            characterInfo?.Remove();

            VoipClient?.Dispose();
            VoipClient = null;
            GameMain.Client = null;
            GameMain.GameSession = null;
        }

        public void WriteCharacterInfo(IWriteMessage msg)
        {
            msg.Write(characterInfo == null);
            if (characterInfo == null) return;

            msg.Write((byte)characterInfo.Gender);
            msg.Write((byte)characterInfo.Race);
            msg.Write((byte)characterInfo.HeadSpriteId);
            msg.Write((byte)characterInfo.HairIndex);
            msg.Write((byte)characterInfo.BeardIndex);
            msg.Write((byte)characterInfo.MoustacheIndex);
            msg.Write((byte)characterInfo.FaceAttachmentIndex);

            var jobPreferences = GameMain.NetLobbyScreen.JobPreferences;
            int count = Math.Min(jobPreferences.Count, 3);
            msg.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                msg.Write(jobPreferences[i].First.Identifier);
                msg.Write((byte)jobPreferences[i].Second);
            }
        }

        public void Vote(VoteType voteType, object data)
        {
            if (clientPeer == null) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.UPDATE_LOBBY);
            msg.Write((byte)ClientNetObject.VOTE);
            serverSettings.Voting.ClientWrite(msg, voteType, data);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void VoteForKick(Client votedClient)
        {
            if (votedClient == null) { return; }
            votedClient.AddKickVote(ConnectedClients.FirstOrDefault(c => c.ID == myID));
            Vote(VoteType.Kick, votedClient);
        }

        #region Submarine Change Voting
        public void InitiateSubmarineChange(SubmarineInfo sub, VoteType voteType)
        {
            if (sub == null) return;
            if (serverSettings.Voting.VoteRunning)
            {
                new GUIMessageBox(TextManager.Get("unabletoinitiateavoteheader"), TextManager.Get("votealreadyactivetext"));
                return;
            }
            Vote(voteType, sub);
        }

        public void ShowSubmarineChangeVoteInterface(Client starter, SubmarineInfo info, VoteType type, float timeOut)
        {
            if (info == null || votingInterface != null) return;
            votingInterface = new VotingInterface(starter, info, type, timeOut);
        }
        #endregion

        public override void AddChatMessage(ChatMessage message)
        {
            base.AddChatMessage(message);

            if (string.IsNullOrEmpty(message.Text)) { return; }
            GameMain.NetLobbyScreen.NewChatMessage(message);
            chatBox.AddMessage(message);
        }

        public override void KickPlayer(string kickedName, string reason)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.Kick);
            msg.Write(kickedName);
            msg.Write(reason);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public override void BanPlayer(string kickedName, string reason, bool range = false, TimeSpan? duration = null)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.Ban);
            msg.Write(kickedName);
            msg.Write(reason);
            msg.Write(range);
            msg.Write(duration.HasValue ? duration.Value.TotalSeconds : 0.0); //0 = permaban

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public override void UnbanPlayer(string playerName, string playerIP)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.Unban);
            msg.Write(string.IsNullOrEmpty(playerName) ? "" : playerName);
            msg.Write(string.IsNullOrEmpty(playerIP) ? "" : playerIP);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void UpdateClientPermissions(Client targetClient)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManagePermissions);
            targetClient.WritePermissions(msg);
            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SendCampaignState()
        {
            MultiPlayerCampaign campaign = GameMain.GameSession.GameMode as MultiPlayerCampaign;
            if (campaign == null)
            {
                DebugConsole.ThrowError("Failed send campaign state to the server (no campaign active).\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManageCampaign);
            campaign.ClientWrite(msg);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SendConsoleCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                DebugConsole.ThrowError("Cannot send an empty console command to the server!\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ConsoleCommands);
            msg.Write(command);
            Vector2 cursorWorldPos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
            msg.Write(cursorWorldPos.X);
            msg.Write(cursorWorldPos.Y);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to start the round (permission required)
        /// </summary>
        public void RequestStartRound(bool continueCampaign = false)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManageRound);
            msg.Write(false); //indicates round start
            msg.Write(continueCampaign);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to select a submarine (permission required)
        /// </summary>
        public void RequestSelectSub(int subIndex, bool isShuttle)
        {
            if (!HasPermission(ClientPermissions.SelectSub)) return;

            var subList = isShuttle ? GameMain.NetLobbyScreen.ShuttleList.ListBox : GameMain.NetLobbyScreen.SubList;

            if (subIndex < 0 || subIndex >= subList.Content.CountChildren)
            {
                DebugConsole.ThrowError("Submarine index out of bounds (" + subIndex + ")\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.SelectSub);
            msg.Write(false);
            msg.Write(isShuttle); msg.WritePadBits();
            msg.Write((UInt16)subIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to add / remove a purchasable submarine (permission required)
        /// </summary>
        public void RequestCampaignSub(SubmarineInfo sub, bool add)
        {
            if (!HasPermission(ClientPermissions.SelectSub) || sub == null) return;
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.SelectSub);
            msg.Write(true);
            msg.Write(sub.EqualityCheckVal);
            msg.Write(add);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to select a mode (permission required)
        /// </summary>
        public void RequestSelectMode(int modeIndex)
        {
            if (!HasPermission(ClientPermissions.SelectMode)) return;
            if (modeIndex < 0 || modeIndex >= GameMain.NetLobbyScreen.ModeList.Content.CountChildren)
            {
                DebugConsole.ThrowError("Gamemode index out of bounds (" + modeIndex + ")\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.SelectMode);
            msg.Write((UInt16)modeIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SetupNewCampaign(SubmarineInfo sub, string saveName, string mapSeed, CampaignSettings settings)
        {
            GameMain.NetLobbyScreen.CampaignSetupFrame.Visible = false;
            GameMain.NetLobbyScreen.CampaignFrame.Visible = false;

            saveName = Path.GetFileNameWithoutExtension(saveName);

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.CAMPAIGN_SETUP_INFO);

            msg.Write(true); msg.WritePadBits();
            msg.Write(saveName);
            msg.Write(mapSeed);
            msg.Write(sub.Name);
            msg.Write(sub.MD5Hash.Hash);
            settings.Serialize(msg);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SetupLoadCampaign(string saveName)
        {
            GameMain.NetLobbyScreen.CampaignSetupFrame.Visible = false;
            GameMain.NetLobbyScreen.CampaignFrame.Visible = false;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.CAMPAIGN_SETUP_INFO);

            msg.Write(false); msg.WritePadBits();
            msg.Write(saveName);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to end the round (permission required)
        /// </summary>
        public void RequestRoundEnd()
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManageRound);
            msg.Write(true); //indicates round end

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public bool SpectateClicked(GUIButton button, object userData)
        {
            MultiPlayerCampaign campaign = 
                GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ?
                GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;
            if (campaign != null && campaign.LastSaveID < campaign.PendingSaveID)
            {
                new GUIMessageBox("", TextManager.Get("campaignfiletransferinprogress"));
                return false;
            }
            if (button != null) { button.Enabled = false; }
            if (campaign != null) { LateCampaignJoin = true; }

            if (clientPeer == null) { return false; }

            IWriteMessage readyToStartMsg = new WriteOnlyMessage();
            readyToStartMsg.Write((byte)ClientPacketHeader.RESPONSE_STARTGAME);

            //assume we have the required sub files to start the round
            //(if not, we'll find out when the server sends the STARTGAME message and can initiate a file transfer)
            readyToStartMsg.Write(true);

            WriteCharacterInfo(readyToStartMsg);

            clientPeer.Send(readyToStartMsg, DeliveryMethod.Reliable);

            return false;
        }

        public bool SetReadyToStart(GUITickBox tickBox)
        {
            if (gameStarted)
            {
                tickBox.Parent.Visible = false;
                return false;
            }
            Vote(VoteType.StartRound, tickBox.Selected);
            return true;
        }

        public bool ToggleEndRoundVote(GUITickBox tickBox)
        {
            if (!gameStarted) return false;

            if (!serverSettings.Voting.AllowEndVoting || !HasSpawned)
            {
                tickBox.Visible = false;
                return false;
            }

            Vote(VoteType.EndRound, tickBox.Selected);
            return false;
        }

        protected CharacterInfo characterInfo;
        protected Character myCharacter;

        public CharacterInfo CharacterInfo
        {
            get { return characterInfo; }
            set { characterInfo = value; }
        }

        public Character Character
        {
            get { return myCharacter; }
            set { myCharacter = value; }
        }

        protected GUIFrame inGameHUD;
        protected ChatBox chatBox;
        public GUIButton ShowLogButton; //TODO: move to NetLobbyScreen
        
        public GUIFrame InGameHUD
        {
            get { return inGameHUD; }
        }

        public ChatBox ChatBox
        {
            get { return chatBox; }
        }

        public VotingInterface VotingInterface
        {
            get { return votingInterface; }
        }
        private VotingInterface votingInterface;

        public bool TypingChatMessage(GUITextBox textBox, string text)
        {
            return chatBox.TypingChatMessage(textBox, text);
        }

        public bool EnterChatMessage(GUITextBox textBox, string message)
        {
            textBox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

            if (string.IsNullOrWhiteSpace(message))
            {
                if (textBox == chatBox.InputBox) textBox.Deselect();
                return false;
            }
            chatBox.ChatManager.Store(message);
            SendChatMessage(message);

            if (textBox.DeselectAfterMessage)
            {
                textBox.Deselect();
            }
            textBox.Text = "";

            if (ChatBox.CloseAfterMessageSent)
            {
                ChatBox.ToggleOpen = false;
                ChatBox.CloseAfterMessageSent = false;
            }

            return true;
        }

        public virtual void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;

            if (gameStarted &&
                Screen.Selected == GameMain.GameScreen)
            {
                inGameHUD.AddToGUIUpdateList();
                GameMain.NetLobbyScreen.FileTransferFrame?.AddToGUIUpdateList();
            }

            serverSettings.AddToGUIUpdateList();
            if (serverSettings.ServerLog.LogFrame != null) serverSettings.ServerLog.LogFrame.AddToGUIUpdateList();

            GameMain.NetLobbyScreen?.PlayerFrame?.AddToGUIUpdateList();
        }

        public void UpdateHUD(float deltaTime)
        {
            GUITextBox msgBox = null;

            if (Screen.Selected == GameMain.GameScreen)
            {
                msgBox = chatBox.InputBox;
            }
            else if (Screen.Selected == GameMain.NetLobbyScreen)
            {
                msgBox = GameMain.NetLobbyScreen.ChatInput;
            }

            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                var controller = Character.Controlled?.SelectedConstruction?.GetComponent<Controller>();
                bool disableButtons = Character.Controlled != null && (controller != null && controller.HideHUD);
                buttonContainer.Visible = !disableButtons;
                
                if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
                {
                    inGameHUD.UpdateManually(deltaTime);
                    chatBox.Update(deltaTime);

                    if (votingInterface != null)
                    {
                        votingInterface.Update(deltaTime);
                        if (!votingInterface.VoteRunning)
                        {
                            votingInterface.Remove();
                            votingInterface = null;
                        }
                    }

                    cameraFollowsSub.Visible = Character.Controlled == null;
                }
                /*if (Character.Controlled == null || Character.Controlled.IsDead)
                {
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    GameMain.LightManager.LosEnabled = false;
                }*/
            }

            //tab doesn't autoselect the chatbox when debug console is open,
            //because tab is used for autocompleting console commands
            if (msgBox != null)
            {
                if (GUI.KeyboardDispatcher.Subscriber == null)                
                {
                    bool chatKeyHit = PlayerInput.KeyHit(InputType.Chat);
                    bool radioKeyHit = PlayerInput.KeyHit(InputType.RadioChat) && (Character.Controlled == null || Character.Controlled.SpeechImpediment < 100);

                    if (chatKeyHit || radioKeyHit)
                    {
                        if (msgBox.Selected)
                        {
                            msgBox.Text = "";
                            msgBox.Deselect();
                        }
                        else
                        {
                            if (Screen.Selected == GameMain.GameScreen)
                            {
                                if (chatKeyHit)
                                {
                                    msgBox.AddToGUIUpdateList();
                                    ChatBox.GUIFrame.Flash(Color.DarkGreen, 0.5f);
                                    if (!chatBox.ToggleOpen)
                                    {
                                        ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                                        ChatBox.ToggleOpen = true;
                                    }
                                }

                                if (radioKeyHit)
                                {
                                    msgBox.AddToGUIUpdateList();
                                    ChatBox.GUIFrame.Flash(Color.YellowGreen, 0.5f);
                                    if (!chatBox.ToggleOpen)
                                    {
                                        ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                                        ChatBox.ToggleOpen = true;
                                    }
                                    
                                    if (!msgBox.Text.StartsWith(ChatBox.RadioChatString))
                                    {
                                        msgBox.Text = ChatBox.RadioChatString;
                                    }
                                } 
                            }

                            msgBox.Select(msgBox.Text.Length);
                        }
                    }
                }
            }
        }

        public virtual void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;

            if (fileReceiver != null && fileReceiver.ActiveTransfers.Count > 0)
            {
                var transfer = fileReceiver.ActiveTransfers.First();
                GameMain.NetLobbyScreen.FileTransferFrame.Visible = true;
                GameMain.NetLobbyScreen.FileTransferFrame.UserData = transfer;
                GameMain.NetLobbyScreen.FileTransferTitle.Text =
                    ToolBox.LimitString(
                        TextManager.GetWithVariable("DownloadingFile", "[filename]", transfer.FileName),
                        GameMain.NetLobbyScreen.FileTransferTitle.Font,
                        GameMain.NetLobbyScreen.FileTransferTitle.Rect.Width);
                GameMain.NetLobbyScreen.FileTransferProgressBar.BarSize = transfer.Progress;
                GameMain.NetLobbyScreen.FileTransferProgressText.Text =
                    MathUtils.GetBytesReadable((long)transfer.Received) + " / " + MathUtils.GetBytesReadable((long)transfer.FileSize);
            }
            else
            {
                GameMain.NetLobbyScreen.FileTransferFrame.Visible = false;
            }

            if (!gameStarted || Screen.Selected != GameMain.GameScreen) { return; }

            inGameHUD.DrawManually(spriteBatch);

            if (EndVoteCount > 0)
            {
                if (EndVoteTickBox.Visible)
                {
                    EndVoteTickBox.Text =
                        (EndVoteTickBox.UserData as string) + " " + EndVoteCount + "/" + EndVoteMax;
                }
                else
                {
                    string endVoteText = TextManager.GetWithVariables("EndRoundVotes", new string[2] { "[votes]", "[max]" }, new string[2] { EndVoteCount.ToString(), EndVoteMax.ToString() });
                    GUI.DrawString(spriteBatch, EndVoteTickBox.Rect.Center.ToVector2() - GUI.SmallFont.MeasureString(endVoteText) / 2,
                        endVoteText,
                        Color.White,
                        font: GUI.SmallFont);
                }
            }
            else
            {
                EndVoteTickBox.Text = EndVoteTickBox.UserData as string;
            }

            if (respawnManager != null)
            {
                string respawnText = string.Empty;
                Color textColor = Color.White;
                bool canChooseRespawn =
                    GameMain.GameSession.GameMode is CampaignMode &&
                    Character.Controlled == null &&
                    Level.Loaded?.Type != LevelData.LevelType.Outpost &&
                    (characterInfo == null || HasSpawned);
                if (respawnManager.CurrentState == RespawnManager.State.Waiting)
                {
                    if (respawnManager.RespawnCountdownStarted)
                    {
                        float timeLeft = (float)(respawnManager.RespawnTime - DateTime.Now).TotalSeconds;
                        respawnText = TextManager.GetWithVariable(respawnManager.UsingShuttle ? "RespawnShuttleDispatching" : "RespawningIn", "[time]", ToolBox.SecondsToReadableTime(timeLeft));
                    }
                    else if (respawnManager.PendingRespawnCount > 0)
                    {
                        respawnText = TextManager.GetWithVariables("RespawnWaitingForMoreDeadPlayers", 
                            new string[] { "[deadplayers]", "[requireddeadplayers]" },
                            new string[] { respawnManager.PendingRespawnCount.ToString(), respawnManager.RequiredRespawnCount.ToString() });
                    }
                }
                else if (respawnManager.CurrentState == RespawnManager.State.Transporting && 
                    respawnManager.ReturnCountdownStarted)
                {
                    float timeLeft = (float)(respawnManager.ReturnTime - DateTime.Now).TotalSeconds;
                    respawnText = timeLeft <= 0.0f ?
                        "" :
                        TextManager.GetWithVariable("RespawnShuttleLeavingIn", "[time]", ToolBox.SecondsToReadableTime(timeLeft));
                    if (timeLeft < 20.0f)
                    {
                        //oscillate between 0-1
                        float phase = (float)(Math.Sin(timeLeft * MathHelper.Pi) + 1.0f) * 0.5f;
                        //textScale = 1.0f + phase * 0.5f;
                        textColor = Color.Lerp(GUI.Style.Red, Color.White, 1.0f - phase);
                    }
                    canChooseRespawn = false;
                }

                GameMain.GameSession?.SetRespawnInfo(
                    visible: !string.IsNullOrEmpty(respawnText) || canChooseRespawn, text: respawnText, textColor: textColor, 
                    buttonsVisible: canChooseRespawn, waitForNextRoundRespawn: (WaitForNextRoundRespawn ?? true));                
            }

            if (!ShowNetStats) { return; }

            NetStats.Draw(spriteBatch, new Rectangle(300, 10, 300, 150));

            /* TODO: reimplement
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
            }*/
        }

        public virtual bool SelectCrewCharacter(Character character, GUIComponent frame)
        {
            if (character == null) { return false; }

            if (character != myCharacter)
            {
                var client = previouslyConnectedClients.Find(c => c.Character == character);
                if (client == null) { return false; }

                CreateSelectionRelatedButtons(client, frame);
            }

            return true;
        }

        public virtual bool SelectCrewClient(Client client, GUIComponent frame)
        {
            if (client == null || client.ID == ID) { return false; }
            CreateSelectionRelatedButtons(client, frame);
            return true;
        }

        private void CreateSelectionRelatedButtons(Client client, GUIComponent frame)
        {
            var content = new GUIFrame(new RectTransform(new Vector2(1f, 1.0f - frame.RectTransform.RelativeSize.Y), frame.RectTransform, Anchor.BottomCenter, Pivot.TopCenter),
                    style: null);

            var mute = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform, Anchor.TopCenter),
                TextManager.Get("Mute"))
            {
                Selected = client.MutedLocally,
                OnSelected = (tickBox) => { client.MutedLocally = tickBox.Selected; return true; }
            };

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.35f), content.RectTransform, Anchor.BottomCenter), isHorizontal: true, childAnchor: Anchor.BottomLeft)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            if (!GameMain.Client.GameStarted || (GameMain.Client.Character == null || GameMain.Client.Character.IsDead) && (client.Character == null || client.Character.IsDead))
            {
                var messageButton = new GUIButton(new RectTransform(new Vector2(1f, 0.2f), content.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0f, buttonContainer.RectTransform.RelativeSize.Y) },
                    TextManager.Get("message"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) =>
                    {
                        chatBox.InputBox.Text = $"{client.Name}; ";
                        CoroutineManager.StartCoroutine(selectCoroutine());
                        return false;
                    }
                };
            }

            // Need a delayed selection due to the inputbox being deselected when a left click occurs outside of it
            IEnumerable<object> selectCoroutine()
            {
                yield return new WaitForSeconds(0.01f, true);
                chatBox.InputBox.Select(chatBox.InputBox.Text.Length);
            }

            if (HasPermission(ClientPermissions.Ban) && client.AllowKicking)
            {
                var banButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                    TextManager.Get("Ban"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) => { GameMain.NetLobbyScreen.BanPlayer(client); return false; }
                };
            }
            if (HasPermission(ClientPermissions.Kick) && client.AllowKicking)
            {
                var kickButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                    TextManager.Get("Kick"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) => { GameMain.NetLobbyScreen.KickPlayer(client); return false; }
                };
            }
            else if (serverSettings.Voting.AllowVoteKick && client.AllowKicking)
            {
                var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                    TextManager.Get("VoteToKick"), style: "GUIButtonSmall")
                {
                    UserData = client,
                    OnClicked = (btn, userdata) => { VoteForKick(client); btn.Enabled = false; return true; }
                };
                if (GameMain.NetworkMember.ConnectedClients != null)
                {
                    kickVoteButton.Enabled = !client.HasKickVoteFromID(myID);
                }
            }
        }

        public void CreateKickReasonPrompt(string clientName, bool ban, bool rangeBan = false)
        {
            var banReasonPrompt = new GUIMessageBox(
                TextManager.Get(ban ? "BanReasonPrompt" : "KickReasonPrompt"),
                "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") }, new Vector2(0.25f, 0.25f), new Point(400, 260));

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.6f), banReasonPrompt.InnerFrame.RectTransform, Anchor.Center))
            {
                AbsoluteSpacing = GUI.IntScale(5)
            };
            var banReasonBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform))
            {
                Wrap = true,
                MaxTextLength = 100
            };

            GUINumberInput durationInputDays = null, durationInputHours = null;
            GUITickBox permaBanTickBox = null;

            if (ban)
            {                
                var labelContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), content.RectTransform), isHorizontal: false);
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.0f), labelContainer.RectTransform), TextManager.Get("BanDuration"), font: GUI.SubHeadingFont) { Padding = Vector4.Zero };
                var buttonContent = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), labelContainer.RectTransform), isHorizontal: true);
                permaBanTickBox = new GUITickBox(new RectTransform(new Vector2(0.4f, 0.15f), buttonContent.RectTransform), TextManager.Get("BanPermanent"))
                {
                    Selected = true
                };

                var durationContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 1f), buttonContent.RectTransform), isHorizontal: true)
                {
                    Visible = false
                };

                permaBanTickBox.OnSelected += (tickBox) =>
                {
                    durationContainer.Visible = !tickBox.Selected;
                    return true;
                };

                durationInputDays = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 1000
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), TextManager.Get("Days"));
                durationInputHours = new GUINumberInput(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), GUINumberInput.NumberType.Int)
                {
                    MinValueInt = 0,
                    MaxValueFloat = 24
                };
                new GUITextBlock(new RectTransform(new Vector2(0.2f, 1.0f), durationContainer.RectTransform), TextManager.Get("Hours"));
            }

            banReasonPrompt.Buttons[0].OnClicked += (btn, userData) =>
            {
                if (ban)
                {
                    if (!permaBanTickBox.Selected)
                    {
                        TimeSpan banDuration = new TimeSpan(durationInputDays.IntValue, durationInputHours.IntValue, 0, 0);
                        BanPlayer(clientName, banReasonBox.Text, ban, banDuration);
                    }
                    else
                    {
                        BanPlayer(clientName, banReasonBox.Text, range: rangeBan);
                    }
                }
                else
                {
                    KickPlayer(clientName, banReasonBox.Text);
                }
                return true;
            };
            banReasonPrompt.Buttons[0].OnClicked += banReasonPrompt.Close;
            banReasonPrompt.Buttons[1].OnClicked += banReasonPrompt.Close;
        }

        public void ReportError(ClientNetError error, UInt16 expectedID = 0, UInt16 eventID = 0, UInt16 entityID = 0)
        {
            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)ClientPacketHeader.ERROR);
            outMsg.Write((byte)error);
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
            clientPeer.Send(outMsg, DeliveryMethod.Reliable);

            if (!eventErrorWritten)
            {
                WriteEventErrorData(error, expectedID, eventID, entityID);
                eventErrorWritten = true;
            }
        }

        private bool eventErrorWritten;
        private void WriteEventErrorData(ClientNetError error, UInt16 expectedID, UInt16 eventID, UInt16 entityID)
        {
            List<string> errorLines = new List<string>
            {
                error.ToString(), ""
            };

            if (IsServerOwner)
            {
                errorLines.Add("SERVER OWNER");
            }

            if (error == ClientNetError.MISSING_EVENT)
            {
                errorLines.Add("Expected ID: " + expectedID + ", received " + eventID);
            }
            else if (error == ClientNetError.MISSING_ENTITY)
            {
                errorLines.Add("Event ID: " + eventID + ", entity ID " + entityID);
            }

            if (GameMain.GameSession?.GameMode != null)
            {
                errorLines.Add("Game mode: " + GameMain.GameSession.GameMode.Name);
                if (GameMain.GameSession?.GameMode is MultiPlayerCampaign campaign)
                {
                    errorLines.Add("Campaign ID: " + campaign.CampaignID);
                    errorLines.Add("Campaign save ID: " + campaign.LastSaveID + "(pending: " + campaign.PendingSaveID + ")");
                }
                foreach (Mission mission in GameMain.GameSession.Missions)
                {
                    errorLines.Add("Mission: " + mission.Prefab.Identifier);
                }
            }
            if (GameMain.GameSession?.Submarine != null)
            {
                errorLines.Add("Submarine: " + GameMain.GameSession.Submarine.Info.Name);
            }
            if (GameMain.NetworkMember?.RespawnManager?.RespawnShuttle != null)
            {
                errorLines.Add("Respawn shuttle: " + GameMain.NetworkMember.RespawnManager.RespawnShuttle.Info.Name);
            }
            if (Level.Loaded != null)
            {
                errorLines.Add("Level: " + Level.Loaded.Seed + ", " + string.Join(", ", Level.Loaded.EqualityCheckValues.Select(cv => cv.ToString("X"))));
                errorLines.Add("Entity count before generating level: " + Level.Loaded.EntityCountBeforeGenerate);
                errorLines.Add("Entities:");
                foreach (Entity e in Level.Loaded.EntitiesBeforeGenerate)
                {
                    errorLines.Add("    " + e.ID + ": " + e.ToString());
                }
                errorLines.Add("Entity count after generating level: " + Level.Loaded.EntityCountAfterGenerate);
            }

            errorLines.Add("Entity IDs:");
            List<Entity> sortedEntities = Entity.GetEntities().ToList();
            sortedEntities.Sort((e1, e2) => e1.ID.CompareTo(e2.ID));
            foreach (Entity e in sortedEntities)
            {
                errorLines.Add(e.ID + ": " + e.ToString());
            }

            errorLines.Add("");
            errorLines.Add("Last debug messages:");
            for (int i = DebugConsole.Messages.Count - 1; i > 0 && i > DebugConsole.Messages.Count - 15; i--)
            {
                errorLines.Add("   " + DebugConsole.Messages[i].Time + " - " + DebugConsole.Messages[i].Text);
            }

            string filePath = "event_error_log_client_" + Name + "_" + DateTime.UtcNow.ToShortTimeString() + ".log";
            filePath = Path.Combine(ServerLog.SavePath, ToolBox.RemoveInvalidFileNameChars(filePath));

            if (!Directory.Exists(ServerLog.SavePath))
            {
                Directory.CreateDirectory(ServerLog.SavePath);
            }
            File.WriteAllLines(filePath, errorLines);
        }

#if DEBUG
        public void ForceTimeOut()
        {
            clientPeer?.ForceTimeOut();
        }
#endif
    }
}
