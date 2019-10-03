using Barotrauma.Items.Components;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace Barotrauma.Networking
{
    class GameClient : NetworkMember
    {
        public override bool IsClient
        {
            get { return true; }
        }

        private ClientPeer clientPeer;
        public ClientPeer ClientPeer { get { return clientPeer; } }

        private GUIMessageBox reconnectBox, waitInServerQueueBox;

        //TODO: move these to NetLobbyScreen
        public GUIButton EndRoundButton;
        public GUITickBox EndVoteTickBox;
        private GUIComponent buttonContainer;

        private NetStats netStats;

        protected GUITickBox cameraFollowsSub;

        public RoundEndCinematic EndCinematic;

        private ClientPermissions permissions = ClientPermissions.None;
        private List<string> permittedConsoleCommands = new List<string>();

        private bool connected;

        private byte myID;

        private List<Client> otherClients;

        private readonly List<Submarine> serverSubmarines = new List<Submarine>();

        private string serverIP, serverName;

        private bool allowReconnect;
        private bool requiresPw;
        private int pwRetries;
        private bool canStart;

        private UInt16 lastSentChatMsgID = 0; //last message this client has successfully sent
        private UInt16 lastQueueChatMsgID = 0; //last message added to the queue
        private List<ChatMessage> chatMsgQueue = new List<ChatMessage>();

        public UInt16 LastSentEntityEventID;

        private ClientEntityEventManager entityEventManager;

        private FileReceiver fileReceiver;

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

        public FileReceiver FileReceiver
        {
            get { return fileReceiver; }
        }

        public bool MidRoundSyncing
        {
            get { return entityEventManager.MidRoundSyncing; }
        }

        private object serverEndpoint;
        private int ownerKey;
        private bool steamP2POwner;

        public bool IsServerOwner
        {
            get { return ownerKey > 0 || steamP2POwner; }
        }
        
        public GameClient(string newName, string ip, UInt64 steamId, string serverName = null, int ownerKey = 0, bool steamP2POwner = false)
        {
            //TODO: gui stuff should probably not be here?
            this.ownerKey = ownerKey;
            this.steamP2POwner = steamP2POwner;

            allowReconnect = true;

            netStats = new NetStats();

            inGameHUD = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
            {
                CanBeFocused = false
            };

            cameraFollowsSub = new GUITickBox(new RectTransform(new Vector2(0.05f, 0.05f), inGameHUD.RectTransform, anchor: Anchor.TopCenter)
            {
                AbsoluteOffset = new Point(0, 5),
                MaxSize = new Point(25, 25)
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

            EndRoundButton = new GUIButton(new RectTransform(new Vector2(0.1f, 0.6f), buttonContainer.RectTransform) { MinSize = new Point(150, 0) },
                TextManager.Get("EndRound"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (!permissions.HasFlag(ClientPermissions.ManageRound)) { return false; }
                    if (!Submarine.MainSub.AtStartPosition && !Submarine.MainSub.AtEndPosition)
                    {
                        var msgBox = new GUIMessageBox("", TextManager.Get("EndRoundSubNotAtLevelEnd"),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });
                        msgBox.Buttons[0].OnClicked = (_, __) =>
                        {
                            GameMain.Client.RequestRoundEnd();
                            return true;
                        };
                        msgBox.Buttons[0].OnClicked += msgBox.Close;
                        msgBox.Buttons[1].OnClicked += msgBox.Close;
                    }
                    else
                    {
                        RequestRoundEnd();
                    }
                    return true;
                },
                Visible = false
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

            GameMain.DebugDraw = false;
            Hull.EditFire = false;
            Hull.EditWater = false;

            Name = newName;

            entityEventManager = new ClientEntityEventManager(this);

            fileReceiver = new FileReceiver();
            fileReceiver.OnFinished += OnFileReceived;
            fileReceiver.OnTransferFailed += OnTransferFailed;

            characterInfo = new CharacterInfo(Character.HumanSpeciesName, name, null)
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
            GameMain.NetLobbyScreen = new NetLobbyScreen();
        }

        private void ConnectToServer(object endpoint, string hostName)
        {
            chatBox.InputBox.Enabled = false;
            if (GameMain.NetLobbyScreen?.TextBox != null)
            {
                GameMain.NetLobbyScreen.TextBox.Enabled = false;
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
                    SteamManager.Instance.User.ClearRichPresence();
                    SteamManager.Instance.User.SetRichPresence("status", "Playing on " + serverName);
                    SteamManager.Instance.User.SetRichPresence("connect", "-connect \"" + serverName.Replace("\"","\\\"") + "\" " + serverEndpoint);
                }

                canStart = true;
                connected = true;

                VoipClient = new VoipClient(this, clientPeer);

                if (Screen.Selected != GameMain.GameScreen)
                {
                    GameMain.NetLobbyScreen.Select();
                }

                chatBox.InputBox.Enabled = true;
                if (GameMain.NetLobbyScreen?.TextBox != null)
                {
                    GameMain.NetLobbyScreen.TextBox.Enabled = true;
                }
            };
            clientPeer.OnRequestPassword = (int salt, int retries) =>
            {
                if (pwRetries != retries) { requiresPw = true; }
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
                if (GameMain.NetLobbyScreen?.TextBox != null)
                {
                    GameMain.NetLobbyScreen.TextBox.Enabled = true;
                }
                GameMain.ServerListScreen.Select();
                return;
            }

            updateInterval = new TimeSpan(0, 0, 0, 0, 150);

            CoroutineManager.StartCoroutine(WaitForStartingInfo(), "WaitForStartingInfo");
        }
        
        private bool RetryConnection(GUIButton button, object obj)
        {
            if (clientPeer != null) { clientPeer.Close(); }
            clientPeer = null;
            ConnectToServer(serverEndpoint, serverName);
            return true;
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
            if (!(GameMain.ServerChildProcess?.HasExited ?? true))
            {
                GameMain.ServerChildProcess.Kill();
                GameMain.ServerChildProcess = null;
            }
            connectCancelled = true;
            Disconnect();
        }

        // Before main looping starts, we loop here and wait for approval message
        private IEnumerable<object> WaitForStartingInfo()
        {
            requiresPw = false;
            pwRetries = -1;

            connectCancelled = false;
            // When this is set to true, we are approved and ready to go
            canStart = false;

            DateTime timeOut = DateTime.Now + new TimeSpan(0, 0, 20);
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
                            string steamUserName = SteamManager.Instance.Friends.GetName(steamConnection.SteamID);
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
                    reconnectBox?.Close(); reconnectBox = null;
                    break;
                }
                
                if (requiresPw && !canStart && !connectCancelled)
                {
                    reconnectBox?.Close(); reconnectBox = null;

                    string pwMsg = TextManager.Get("PasswordRequired");

                    var msgBox = new GUIMessageBox(pwMsg, "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") },
                        relativeSize: new Vector2(0.25f, 0.2f), minSize: new Point(400, 150));
                    var passwordBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 0.1f), msgBox.InnerFrame.RectTransform, Anchor.Center) { MinSize = new Point(0, 20) })
                    {
                        IgnoreLayoutGroups = true,
                        UserData = "password",
                        Censor = true
                    };

                    var okButton = msgBox.Buttons[0];
                    var cancelButton = msgBox.Buttons[1];

                    okButton.OnClicked += (GUIButton button, object obj) =>
                    {
                        clientPeer.SendPassword(passwordBox.Text);
                        requiresPw = false;
                        return true;
                    };

                    cancelButton.OnClicked += (GUIButton button, object obj) =>
                    {
                        requiresPw = false;
                        connectCancelled = true;
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

            if (connectCancelled) yield return CoroutineStatus.Success;
            
            yield return CoroutineStatus.Success;
        }

        public override void Update(float deltaTime)
        {
#if DEBUG
            if (PlayerInput.GetKeyboardState.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.P)) return;
#endif

            foreach (Client c in ConnectedClients)
            {
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

            /*TODO: reimplement
            if (ShowNetStats && client?.ServerConnection != null)
            {
                netStats.AddValue(NetStats.NetStatType.ReceivedBytes, client.ServerConnection.Statistics.ReceivedBytes);
                netStats.AddValue(NetStats.NetStatType.SentBytes, client.ServerConnection.Statistics.SentBytes);
                netStats.AddValue(NetStats.NetStatType.ResentMessages, client.ServerConnection.Statistics.ResentMessages);
                netStats.Update(deltaTime);
            }*/

            UpdateHUD(deltaTime);

            base.Update(deltaTime);

            try
            {
                clientPeer?.Update(deltaTime);
            }
            catch (Exception e)
            {
                string errorMsg = "Error while reading a message from server. {" + e + "}. ";
                if (GameMain.Client == null) { errorMsg += "Client disposed."; }
                errorMsg += "\n" + e.StackTrace;
                if (e.InnerException != null)
                {
                    errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace;
                }
                GameAnalyticsManager.AddErrorEventOnce("GameClient.Update:CheckServerMessagesException" + e.TargetSite.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                DebugConsole.ThrowError("Error while reading a message from server.", e);
                new GUIMessageBox(TextManager.Get("Error"), TextManager.GetWithVariables("MessageReadError", new string[2] { "[message]", "[targetsite]" }, new string[2] { e.Message, e.TargetSite.ToString() }));
                Disconnect();
                GameMain.MainMenuScreen.Select();
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
                EndVoteTickBox.Visible = serverSettings.Voting.AllowEndVoting && HasSpawned;

                if (respawnManager != null)
                {
                    respawnManager.Update(deltaTime);
                }

                if (updateTimer > DateTime.Now) { return; }
                SendIngameUpdate();
            }
            else
            {
                if (updateTimer > DateTime.Now) { return; }
                SendLobbyUpdate();
            }

            if (serverSettings.VoiceChatEnabled)
            {
                VoipClient?.SendToServer();
            }

            if (IsServerOwner && connected && !connectCancelled)
            {
                if (GameMain.ServerChildProcess?.HasExited ?? true)
                {
                    Disconnect();
                    var msgBox = new GUIMessageBox(TextManager.Get("ConnectionLost"), TextManager.Get("ServerProcessClosed"));
                    msgBox.Buttons[0].OnClicked += ReturnToPreviousMenu;
                }
            }

            // Update current time
            updateTimer = DateTime.Now + updateInterval;
        }

        private CoroutineHandle startGameCoroutine;

        private void ReadDataMessage(IReadMessage inc)
        {
            ServerPacketHeader header = (ServerPacketHeader)inc.ReadByte();
            switch (header)
            {
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
                        string errorMsg = "Error while reading an ingame update message from server. {" + e + "}\n" + e.StackTrace;
                        if (e.InnerException != null)
                        {
                            errorMsg += "\nInner exception: " + e.InnerException.Message + "\n" + e.InnerException.StackTrace;
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
                        errorMsg += "\n" + Environment.StackTrace;
                        GameAnalyticsManager.AddErrorEventOnce(
                            "GameClient.ReadDataMessage:VoipClientNull", 
                            GameMain.Client == null ? GameAnalyticsSDK.Net.EGAErrorSeverity.Error : GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, 
                            errorMsg);
                        return;
                    }

                    VoipClient.Read(inc);
                    break;
                case ServerPacketHeader.QUERY_STARTGAME:
                    string subName = inc.ReadString();
                    string subHash = inc.ReadString();

                    bool usingShuttle = inc.ReadBoolean();
                    string shuttleName = inc.ReadString();
                    string shuttleHash = inc.ReadString();

                    IWriteMessage readyToStartMsg = new WriteOnlyMessage();
                    readyToStartMsg.Write((byte)ClientPacketHeader.RESPONSE_STARTGAME);

                    MultiPlayerCampaign campaign = GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ?
                                                        GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;

                    GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;
                    bool readyToStart;
                    if (campaign == null)
                    {
                        readyToStart = GameMain.NetLobbyScreen.TrySelectSub(subName, subHash, GameMain.NetLobbyScreen.SubList) &&
                                       GameMain.NetLobbyScreen.TrySelectSub(shuttleName, shuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox);
                    }
                    else
                    {
                        readyToStart = !fileReceiver.ActiveTransfers.Any(c => c.FileType == FileTransferType.CampaignSave) &&
                                            (campaign.LastSaveID == campaign.PendingSaveID);
                    }
                    readyToStartMsg.Write(readyToStart);

                    WriteCharacterInfo(readyToStartMsg);
                    
                    clientPeer.Send(readyToStartMsg, DeliveryMethod.Reliable);

                    if (readyToStart && !CoroutineManager.IsCoroutineRunning("WaitForStartRound"))
                    {
                        CoroutineManager.StartCoroutine(GameMain.NetLobbyScreen.WaitForStartRound(startButton: null, allowCancel: false), "WaitForStartRound");
                    }
                    break;
                case ServerPacketHeader.STARTGAME:
                    startGameCoroutine = GameMain.Instance.ShowLoading(StartGame(inc), false);
                    break;
                case ServerPacketHeader.ENDGAME:
                    string endMessage = inc.ReadString();
                    bool missionSuccessful = inc.ReadBoolean();
                    Character.TeamType winningTeam = (Character.TeamType)inc.ReadByte();
                    if (missionSuccessful && GameMain.GameSession?.Mission != null)
                    {
                        GameMain.GameSession.WinningTeam = winningTeam;
                        GameMain.GameSession.Mission.Completed = true;
                    }
                    CoroutineManager.StartCoroutine(EndGame(endMessage), "EndGame");
                    break;
                case ServerPacketHeader.CAMPAIGN_SETUP_INFO:
                    UInt16 saveCount = inc.ReadUInt16();
                    List<string> saveFiles = new List<string>();
                    for (int i = 0; i < saveCount; i++)
                    {
                        saveFiles.Add(inc.ReadString());
                    }
                    GameMain.NetLobbyScreen.CampaignSetupUI = MultiPlayerCampaign.StartCampaignSetup(serverSubmarines, saveFiles);
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
                            new GUIMessageBox(TextManager.Get("CheatsEnabledTitle"), TextManager.Get("CheatsEnabledDescription"));
                        }
                    }
                    break;
                case ServerPacketHeader.FILE_TRANSFER:
                    fileReceiver.ReadMessage(inc);
                    break;
                case ServerPacketHeader.TRAITOR_MESSAGE:
                    ReadTraitorMessage(inc);
                    break;
            }
        }
        
        private void OnDisconnect()
        {
            if (SteamManager.IsInitialized)
            {
                SteamManager.Instance.User.ClearRichPresence();
            }
        }

        private void HandleDisconnectMessage(string disconnectMsg)
        {
            disconnectMsg = disconnectMsg ?? "";

            string[] splitMsg = disconnectMsg.Split('/');
            DisconnectReason disconnectReason = DisconnectReason.Unknown;
            if (splitMsg.Length > 0) { Enum.TryParse(splitMsg[0], out disconnectReason); }

            if (disconnectMsg == Lidgren.Network.NetConnection.NoResponseMessage)
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
            
            if (allowReconnect && disconnectReason == DisconnectReason.Unknown)
            {
                DebugConsole.NewMessage("Attempting to reconnect...");

                string msg = TextManager.GetServerMessage(disconnectMsg);
                msg = string.IsNullOrWhiteSpace(msg) ?
                    TextManager.Get("ConnectionLostReconnecting") :
                    msg + '\n' + TextManager.Get("ConnectionLostReconnecting");

                reconnectBox = new GUIMessageBox(
                    TextManager.Get("ConnectionLost"),
                    msg, new string[0]);
                connected = false;
                ConnectToServer(serverEndpoint, serverName);
            }
            else
            {
                connectCancelled = true;

                string msg = "";
                if (disconnectReason == DisconnectReason.Unknown)
                {
                    DebugConsole.NewMessage("Do not attempt reconnect (not allowed).");
                    msg = disconnectMsg;
                }
                else
                {
                    DebugConsole.NewMessage("Do not attempt to reconnect (DisconnectReason doesn't allow reconnection).");
                    msg = TextManager.Get("DisconnectReason." + disconnectReason.ToString());
                    
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

            switch(messageType) {
                case TraitorMessageType.Objective:
                    var isTraitor = !string.IsNullOrEmpty(message); 
                    if (Character != null)
                    {
                        Character.IsTraitor = isTraitor;
                        Character.TraitorCurrentObjective = message;
                    }
                    else
                    {
                        SpawnAsTraitor = isTraitor;
                        TraitorFirstObjective = message;
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
            if (targetClient != null)
            {
                targetClient.SetPermissions(permissions, permittedCommands);
            }
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

            permissions = newPermissions;
            this.permittedConsoleCommands = new List<string>(permittedConsoleCommands);
            //don't show the "permissions changed" popup if the client owns the server
            if (!IsServerOwner)
            {
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

                GUIMessageBox msgBox = new GUIMessageBox(TextManager.Get("PermissionsChanged"), msg)
                {
                    UserData = "permissions"
                };

                if (newPermissions.HasFlag(ClientPermissions.ConsoleCommands))
                {
                    int listBoxWidth = (int)(msgBox.InnerFrame.Rect.Width) / 2 - 30;
                    new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.1f), msgBox.InnerFrame.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.05f, 0.15f) },
                         TextManager.Get("PermittedConsoleCommands"), wrap: true, font: GUI.SmallFont);
                    var commandList = new GUIListBox(new RectTransform(new Vector2(0.4f, 0.55f), msgBox.InnerFrame.RectTransform, Anchor.TopRight) { RelativeOffset = new Vector2(0.05f, 0.25f) });
                    foreach (string permittedCommand in permittedConsoleCommands)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), commandList.Content.RectTransform, minSize: new Point(0, 15)),
                            permittedCommand, font: GUI.SmallFont)
                        {
                            CanBeFocused = false
                        };
                    }
                }
            }

            GameMain.NetLobbyScreen.UpdatePermissions();
        }

        private IEnumerable<object> StartGame(IReadMessage inc)
        {
            if (Character != null) Character.Remove();
            HasSpawned = false;

            while (CoroutineManager.IsCoroutineRunning("EndGame"))
            {
                if (EndCinematic != null) { EndCinematic.Stop(); }
                yield return CoroutineStatus.Running;
            }

            GameMain.LightManager.LightingEnabled = true;

            //enable spectate button in case we fail to start the round now
            //(for example, due to a missing sub file or an error)
            GameMain.NetLobbyScreen.ShowSpectateButton();

            entityEventManager.Clear();
            LastSentEntityEventID = 0;

            EndVoteTickBox.Selected = false;

            int seed                    = inc.ReadInt32();
            string levelSeed            = inc.ReadString();
            int levelEqualityCheckVal   = inc.ReadInt32();
            float levelDifficulty       = inc.ReadSingle();

            byte losMode            = inc.ReadByte();

            int missionTypeIndex    = inc.ReadByte();

            string subName          = inc.ReadString();
            string subHash          = inc.ReadString();

            bool usingShuttle       = inc.ReadBoolean();
            string shuttleName      = inc.ReadString();
            string shuttleHash      = inc.ReadString();

            string modeIdentifier   = inc.ReadString();
            int missionIndex        = inc.ReadInt16();

            bool respawnAllowed     = inc.ReadBoolean();
            bool loadSecondSub      = inc.ReadBoolean();

            bool disguisesAllowed   = inc.ReadBoolean();
            bool rewiringAllowed    = inc.ReadBoolean();

            bool allowRagdollButton = inc.ReadBoolean();

            serverSettings.ReadMonsterEnabled(inc);

            GameModePreset gameMode = GameModePreset.List.Find(gm => gm.Identifier == modeIdentifier);
            MultiPlayerCampaign campaign = GameMain.NetLobbyScreen.SelectedMode == GameMain.GameSession?.GameMode.Preset ?
                GameMain.GameSession?.GameMode as MultiPlayerCampaign : null;

            if (gameMode == null)
            {
                DebugConsole.ThrowError("Game mode \"" + modeIdentifier + "\" not found!");
                yield return CoroutineStatus.Success;
            }

            GameMain.NetLobbyScreen.UsingShuttle = usingShuttle;
            GameMain.LightManager.LosMode = (LosMode)losMode;

            serverSettings.AllowDisguises = disguisesAllowed;
            serverSettings.AllowRewiring = rewiringAllowed;
            serverSettings.AllowRagdollButton = allowRagdollButton;

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
                //this shouldn't happen, TrySelectSub should stop the coroutine if the correct sub/shuttle cannot be found
                if (GameMain.NetLobbyScreen.SelectedSub == null ||
                    GameMain.NetLobbyScreen.SelectedSub.Name != subName ||
                    GameMain.NetLobbyScreen.SelectedSub.MD5Hash?.Hash != subHash)
                {
                    string errorMsg = "Failed to select submarine \"" + subName + "\" (hash: " + subHash + ").";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:FailedToSelectSub" + subName, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    CoroutineManager.StartCoroutine(EndGame(""));
                    yield return CoroutineStatus.Failure;
                }
                if (GameMain.NetLobbyScreen.SelectedShuttle == null ||
                    GameMain.NetLobbyScreen.SelectedShuttle.Name != shuttleName ||
                    GameMain.NetLobbyScreen.SelectedShuttle.MD5Hash?.Hash != shuttleHash)
                {
                    string errorMsg = "Failed to select shuttle \"" + shuttleName + "\" (hash: " + shuttleHash + ").";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:FailedToSelectShuttle" + shuttleName, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    CoroutineManager.StartCoroutine(EndGame(""));
                    yield return CoroutineStatus.Failure;
                }

                GameMain.GameSession = missionIndex < 0 ?
                    new GameSession(GameMain.NetLobbyScreen.SelectedSub, "", gameMode, MissionType.None) :
                    new GameSession(GameMain.NetLobbyScreen.SelectedSub, "", gameMode, MissionPrefab.List[missionIndex]);
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

            for (int i = 0; i < Submarine.MainSubs.Length; i++)
            {
                if (!loadSecondSub && i > 0) { break; }

                var teamID = i == 0 ? Character.TeamType.Team1 : Character.TeamType.Team2;
                Submarine.MainSubs[i].TeamID = teamID;
                foreach (Submarine sub in Submarine.MainSubs[i].DockedTo)
                {
                    sub.TeamID = teamID;
                }
            }

            if (Level.Loaded.EqualityCheckVal != levelEqualityCheckVal)
            {
                string errorMsg = "Level equality check failed. The level generated at your end doesn't match the level generated by the server (seed " + Level.Loaded.Seed + ").";
                DebugConsole.ThrowError(errorMsg, createMessageBox: true);
                GameAnalyticsManager.AddErrorEventOnce("GameClient.StartGame:LevelsDontMatch" + levelSeed, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                CoroutineManager.StartCoroutine(EndGame(""));
                yield return CoroutineStatus.Failure;
            }

            if (respawnAllowed) respawnManager = new RespawnManager(this, GameMain.NetLobbyScreen.UsingShuttle ? GameMain.NetLobbyScreen.SelectedShuttle : null);

            ServerSettings.ServerDetailsChanged = true;
            gameStarted = true;

            GameMain.GameScreen.Select();

            AddChatMessage($"ServerMessage.HowToCommunicate~[chatbutton]={GameMain.Config.KeyBind(InputType.Chat).ToString()}~[radiobutton]={GameMain.Config.KeyBind(InputType.RadioChat).ToString()}", ChatMessageType.Server);

            yield return CoroutineStatus.Success;
        }

        public IEnumerable<object> EndGame(string endMessage)
        {
            if (!gameStarted)
            {
                GameMain.NetLobbyScreen.Select();
                yield return CoroutineStatus.Success;
            }

            if (GameMain.GameSession != null) { GameMain.GameSession.GameMode.End(endMessage); }

            // Enable characters near the main sub for the endCinematic
            foreach (Character c in Character.CharacterList)
            {
                if (Vector2.DistanceSquared(Submarine.MainSub.WorldPosition, c.WorldPosition) < NetConfig.EnableCharacterDistSqr)
                {
                    c.Enabled = true;
                }
            }

            ServerSettings.ServerDetailsChanged = true;

            gameStarted = false;
            Character.Controlled = null;
            GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
            GameMain.LightManager.LosEnabled = false;
            respawnManager = null;
            
            if (Screen.Selected == GameMain.GameScreen)
            {
                EndCinematic = new RoundEndCinematic(Submarine.MainSub, GameMain.GameScreen.Cam);
                while (EndCinematic.Running && Screen.Selected == GameMain.GameScreen)
                {
                    yield return CoroutineStatus.Running;
                }
                EndCinematic = null;
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

        private void ReadInitialUpdate(IReadMessage inc)
        {
            myID = inc.ReadByte();

            UInt16 subListCount = inc.ReadUInt16();
            serverSubmarines.Clear();
            for (int i = 0; i < subListCount; i++)
            {
                string subName = inc.ReadString();
                string subHash = inc.ReadString();
                bool requiredContentPackagesInstalled = inc.ReadBoolean();

                var matchingSub =
                    Submarine.SavedSubmarines.FirstOrDefault(s => s.Name == subName && s.MD5Hash.Hash == subHash) ??
                    new Submarine(Path.Combine(Submarine.SavePath, subName) + ".sub", subHash, false);

                matchingSub.RequiredContentPackagesInstalled = requiredContentPackagesInstalled;
                serverSubmarines.Add(matchingSub);
            }

            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.SubList, serverSubmarines);
            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.ShuttleList.ListBox, serverSubmarines);

            gameStarted = inc.ReadBoolean();
            bool allowSpectating = inc.ReadBoolean();

            ReadPermissions(inc);

            if (gameStarted)
            {
                new GUIMessageBox(TextManager.Get("PleaseWait"), TextManager.Get(allowSpectating ? "RoundRunningSpectateEnabled" : "RoundRunningSpectateDisabled"));
                GameMain.NetLobbyScreen.Select();
            }
        }

        private void ReadClientList(IReadMessage inc)
        {
            UInt16 listId = inc.ReadUInt16();
            List<TempClient> tempClients = new List<TempClient>();
            int clientCount = inc.ReadByte();
            for (int i = 0; i < clientCount; i++)
            {
                byte id             = inc.ReadByte();
                UInt64 steamId      = inc.ReadUInt64();
                string name         = inc.ReadString();
                UInt16 characterID  = inc.ReadUInt16();
                bool muted          = inc.ReadBoolean();
                bool allowKicking   = inc.ReadBoolean();
                inc.ReadPadBits();

                tempClients.Add(new TempClient
                {
                    ID = id,
                    SteamID = steamId,
                    Name = name,
                    CharacterID = characterID,
                    Muted = muted,
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
                            AllowKicking = tc.AllowKicking
                        };
                        ConnectedClients.Add(existingClient);
                        GameMain.NetLobbyScreen.AddPlayer(existingClient);
                    }
                    existingClient.Character = null;
                    existingClient.Muted = tc.Muted;
                    existingClient.AllowKicking = tc.AllowKicking;
                    if (tc.CharacterID > 0)
                    {
                        existingClient.Character = Entity.FindEntityByID(tc.CharacterID) as Character;
                        if (existingClient.Character == null)
                        {
                            updateClientListId = false;
                        }
                    }
                    if (existingClient.ID == myID)
                    {
                        existingClient.SetPermissions(permissions, permittedConsoleCommands);
                        name = tc.Name;
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
                    }
                }
                if (updateClientListId) { LastClientListUpdateID = listId; }

                if (clientPeer is SteamP2POwnerPeer)
                {
                    Steam.SteamManager.UpdateLobby(serverSettings);
                }
            }
        }

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

                            UInt16 updateID     = inc.ReadUInt16();

                            UInt16 settingsLen = inc.ReadUInt16();
                            byte[] settingsData = inc.ReadBytes(settingsLen);

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

                            bool voiceChatEnabled       = inc.ReadBoolean();

                            bool allowSpectating        = inc.ReadBoolean();

                            YesNoMaybe traitorsEnabled  = (YesNoMaybe)inc.ReadRangedInteger(0, 2);
                            int missionTypeIndex        = inc.ReadRangedInteger(0, Enum.GetValues(typeof(MissionType)).Length - 1);
                            int modeIndex               = inc.ReadByte();

                            string levelSeed            = inc.ReadString();
                            float levelDifficulty       = inc.ReadSingle();

                            byte botCount               = inc.ReadByte();
                            BotSpawnMode botSpawnMode   = inc.ReadBoolean() ? BotSpawnMode.Fill : BotSpawnMode.Normal;

                            bool autoRestartEnabled     = inc.ReadBoolean();
                            float autoRestartTimer      = autoRestartEnabled ? inc.ReadSingle() : 0.0f;

                            //ignore the message if we already a more up-to-date one
                            if (NetIdUtils.IdMoreRecent(updateID, GameMain.NetLobbyScreen.LastUpdateID))
                            {
                                ReadWriteMessage settingsBuf = new ReadWriteMessage();
                                settingsBuf.Write(settingsData, 0, settingsLen); settingsBuf.BitPosition = 0;
                                serverSettings.ClientRead(settingsBuf);


                                GameMain.NetLobbyScreen.LastUpdateID = updateID;

                                serverSettings.ServerLog.ServerName = serverSettings.ServerName;

                                if (!GameMain.NetLobbyScreen.ServerName.Selected) GameMain.NetLobbyScreen.ServerName.Text = serverSettings.ServerName;
                                if (!GameMain.NetLobbyScreen.ServerMessage.Selected) GameMain.NetLobbyScreen.ServerMessage.Text = serverSettings.ServerMessageText;
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
                        else if (GameMain.NetLobbyScreen.SelectedMode?.Identifier != "multiplayercampaign")
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

            ServerNetObject objHeader;
            while ((objHeader = (ServerNetObject)inc.ReadByte()) != ServerNetObject.END_OF_MESSAGE)
            {
                bool eventReadFailed = false;
                switch (objHeader)
                {
                    case ServerNetObject.SYNC_IDS:
                        lastSentChatMsgID = inc.ReadUInt16();
                        LastSentEntityEventID = inc.ReadUInt16();
                        break;
                    case ServerNetObject.ENTITY_POSITION:
                        UInt16 id = inc.ReadUInt16();
                        byte msgLength = inc.ReadByte();

                        int msgEndPos = inc.BitPosition + msgLength * 8;

                        var entity = Entity.FindEntityByID(id) as IServerSerializable;
                        if (entity != null)
                        {
                            entity.ClientRead(objHeader, inc, sendingTime);
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
                        if (!entityEventManager.Read(objHeader, inc, sendingTime, entities))
                        {
                            eventReadFailed = true;
                            break;
                        }
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

                        sw.Write(inc.Buffer, (int)(prevBytePos - prevByteLength), (int)(prevByteLength));

                        sw.Close();
                        fl.Close();

                        throw new Exception("Error while reading update from server: please send us \"crashreport_object.bin\"!");
                }
                prevBitLength = inc.BitPosition - prevBitPos;
                prevByteLength = inc.BytePosition - prevByteLength;

                prevObjHeader = objHeader;
                prevBitPos = inc.BitPosition;
                prevBytePos = inc.BytePosition;

                if (eventReadFailed)
                {
                    break;
                }
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
            outmsg.Write(name);

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
                DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU);
            }

            clientPeer.Send(outmsg, DeliveryMethod.Unreliable);
        }

        private void SendIngameUpdate()
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_INGAME);

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            //outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);
            outmsg.Write(entityEventManager.LastReceivedID);
            outmsg.Write(LastClientListUpdateID);

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
                DebugConsole.ThrowError("Maximum packet size exceeded (" + outmsg.LengthBytes + " > " + MsgConstants.MTU);
            }

            clientPeer.Send(outmsg, DeliveryMethod.Unreliable);
        }

        public void SendChatMessage(ChatMessage msg)
        {
            if (clientPeer.ServerConnection == null) return;
            lastQueueChatMsgID++;
            msg.NetStateID = lastQueueChatMsgID;
            chatMsgQueue.Add(msg);
        }

        public void SendChatMessage(string message, ChatMessageType type = ChatMessageType.Default)
        {
            if (clientPeer.ServerConnection == null) return;

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

        public void UpdateFileTransfer(int id, int offset, bool reliable=false)
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
                    var newSub = new Submarine(transfer.FilePath);
                    if (newSub.IsFileCorrupted) { return; }

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
                            ((Submarine)userdata).CreatePreviewWindow(new GUIMessageBox("", "", new Vector2(0.25f, 0.25f), new Point(500, 400)));
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
                    if (campaign == null) { return; }

                    GameMain.GameSession.SavePath = transfer.FilePath;
                    if (GameMain.GameSession.Submarine == null)
                    {
                        var gameSessionDoc = SaveUtil.LoadGameSessionDoc(GameMain.GameSession.SavePath);
                        string subPath = Path.Combine(SaveUtil.TempPath, gameSessionDoc.Root.GetAttributeString("submarine", "")) + ".sub";
                        GameMain.GameSession.Submarine = new Submarine(subPath, "");
                    }

                    SaveUtil.LoadGame(GameMain.GameSession.SavePath, GameMain.GameSession);
                    campaign.LastSaveID = campaign.PendingSaveID;

                    DebugConsole.Log("Campaign save received, save ID " + campaign.LastSaveID);
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

            commandName = commandName.ToLowerInvariant();
            if (permittedConsoleCommands.Any(c => c.ToLowerInvariant() == commandName)) { return true; }

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

            if (GameMain.ServerChildProcess != null)
            {
                int checks = 0;
                while (!GameMain.ServerChildProcess.HasExited)
                {
                    if (checks > 10)
                    {
                        GameMain.ServerChildProcess.Kill();
                    }
                    Thread.Sleep(100);
                    checks++;
                }
                GameMain.ServerChildProcess = null;
            }

            VoipClient?.Dispose();
            VoipClient = null;
            GameMain.Client = null;
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
                msg.Write(jobPreferences[i].Identifier);
            }
        }

        public void Vote(VoteType voteType, object data)
        {
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
            votedClient.AddKickVote(ConnectedClients.First(c => c.ID == ID));
            Vote(VoteType.Kick, votedClient);
        }

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
                DebugConsole.ThrowError("Failed send campaign state to the server (no campaign active).\n" + Environment.StackTrace);
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
                DebugConsole.ThrowError("Cannot send an empty console command to the server!\n" + Environment.StackTrace);
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
        public void RequestStartRound()
        {
            if (!HasPermission(ClientPermissions.ManageRound)) return;

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.ManageRound);
            msg.Write(false); //indicates round start

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
                DebugConsole.ThrowError("Submarine index out of bounds (" + subIndex + ")\n" + Environment.StackTrace);
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.SelectSub);
            msg.Write(isShuttle); msg.WritePadBits();
            msg.Write((UInt16)subIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        /// <summary>
        /// Tell the server to select a submarine (permission required)
        /// </summary>
        public void RequestSelectMode(int modeIndex)
        {
            if (!HasPermission(ClientPermissions.SelectMode)) return;
            if (modeIndex < 0 || modeIndex >= GameMain.NetLobbyScreen.ModeList.Content.CountChildren)
            {
                DebugConsole.ThrowError("Gamemode index out of bounds (" + modeIndex + ")\n" + Environment.StackTrace);
                return;
            }

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.SERVER_COMMAND);
            msg.Write((UInt16)ClientPermissions.SelectMode);
            msg.Write((UInt16)modeIndex);
            msg.Write((byte)ServerNetObject.END_OF_MESSAGE);

            clientPeer.Send(msg, DeliveryMethod.Reliable);
        }

        public void SetupNewCampaign(Submarine sub, string saveName, string mapSeed)
        {
            saveName = Path.GetFileNameWithoutExtension(saveName);

            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.CAMPAIGN_SETUP_INFO);

            msg.Write(true); msg.WritePadBits();
            msg.Write(saveName);
            msg.Write(mapSeed);
            msg.Write(sub.Name);
            msg.Write(sub.MD5Hash.Hash);

            clientPeer.Send(msg, DeliveryMethod.Reliable);

            GameMain.NetLobbyScreen.CampaignSetupUI = null;
        }

        public void SetupLoadCampaign(string saveName)
        {
            IWriteMessage msg = new WriteOnlyMessage();
            msg.Write((byte)ClientPacketHeader.CAMPAIGN_SETUP_INFO);

            msg.Write(false); msg.WritePadBits();
            msg.Write(saveName);

            clientPeer.Send(msg, DeliveryMethod.Reliable);

            GameMain.NetLobbyScreen.CampaignSetupUI = null;
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
                tickBox.Visible = false;
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

            SendChatMessage(message);

            textBox.Deselect();
            textBox.Text = "";

            return true;
        }

        public virtual void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;

            if (gameStarted &&
                Screen.Selected == GameMain.GameScreen)
            {
                inGameHUD.AddToGUIUpdateList();
            }
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
                msgBox = GameMain.NetLobbyScreen.TextBox;
            }

            if (gameStarted && Screen.Selected == GameMain.GameScreen)
            {
                bool disableButtons =
                    Character.Controlled != null &&
                    Character.Controlled.SelectedConstruction?.GetComponent<Controller>() != null;
                buttonContainer.Visible = !disableButtons;
                
                if (!GUI.DisableHUD && !GUI.DisableUpperHUD)
                {
                    inGameHUD.UpdateManually(deltaTime);
                    chatBox.Update(deltaTime);

                    cameraFollowsSub.Visible = Character.Controlled == null;
                }
                if (Character.Controlled == null || Character.Controlled.IsDead)
                {
                    GameMain.GameScreen.Cam.TargetPos = Vector2.Zero;
                    GameMain.LightManager.LosEnabled = false;
                }
            }

            //tab doesn't autoselect the chatbox when debug console is open,
            //because tab is used for autocompleting console commands
            if (msgBox != null)
            {
                if ((PlayerInput.KeyHit(InputType.Chat) || PlayerInput.KeyHit(InputType.RadioChat)) &&
                    GUI.KeyboardDispatcher.Subscriber == null)
                {
                    if (msgBox.Selected)
                    {
                        msgBox.Text = "";
                        msgBox.Deselect();
                    }
                    else
                    {
                        msgBox.Select();
                        if (Screen.Selected == GameMain.GameScreen && PlayerInput.KeyHit(InputType.RadioChat))
                        {
                            msgBox.Text = "r; ";
                        }
                    }
                }
            }
            serverSettings.AddToGUIUpdateList();
            if (serverSettings.ServerLog.LogFrame != null) serverSettings.ServerLog.LogFrame.AddToGUIUpdateList();
        }

        public virtual void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            if (GUI.DisableHUD || GUI.DisableUpperHUD) return;
            
            if (fileReceiver != null && fileReceiver.ActiveTransfers.Count > 0)
            {
                Vector2 downloadBarSize = new Vector2(250, 35) * GUI.Scale;
                Vector2 pos = new Vector2(GameMain.NetLobbyScreen.InfoFrame.Rect.X, GameMain.GraphicsHeight - downloadBarSize.Y - 5);

                GUI.DrawRectangle(spriteBatch, new Rectangle(
                    (int)pos.X,
                    (int)pos.Y,
                    (int)(fileReceiver.ActiveTransfers.Count * (downloadBarSize.X + 10)),
                    (int)downloadBarSize.Y),
                    Color.Black * 0.8f, true);

                for (int i = 0; i < fileReceiver.ActiveTransfers.Count; i++)
                {
                    var transfer = fileReceiver.ActiveTransfers[i];

                    GUI.DrawString(spriteBatch,
                        pos,
                        ToolBox.LimitString(TextManager.GetWithVariable("DownloadingFile", "[filename]", transfer.FileName), GUI.SmallFont, (int)downloadBarSize.X),
                        Color.White, null, 0, GUI.SmallFont);
                    GUI.DrawProgressBar(spriteBatch, new Vector2(pos.X, -pos.Y - downloadBarSize.Y / 2), new Vector2(downloadBarSize.X * 0.7f, downloadBarSize.Y / 2), transfer.Progress, Color.Green);
                    GUI.DrawString(spriteBatch, pos + new Vector2(5, downloadBarSize.Y / 2),
                        MathUtils.GetBytesReadable((long)transfer.Received) + " / " + MathUtils.GetBytesReadable((long)transfer.FileSize),
                        Color.White, null, 0, GUI.SmallFont);

                    if (GUI.DrawButton(spriteBatch, new Rectangle(
                            (int)(pos.X + downloadBarSize.X * 0.7f), (int)(pos.Y + downloadBarSize.Y / 2),
                            (int)(downloadBarSize.X * 0.3f), (int)(downloadBarSize.Y / 2)), 
                        TextManager.Get("Cancel"), new Color(0.47f, 0.13f, 0.15f, 0.08f)))
                    {
                        CancelFileTransfer(transfer);
                        fileReceiver.StopTransfer(transfer);
                    }

                    pos.X += (downloadBarSize.X + 10);
                }
            }
            
            if (!gameStarted || Screen.Selected != GameMain.GameScreen) return;

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
                string respawnText = "";
                float textScale = 1.0f;
                Color textColor = Color.White;
                if (respawnManager.CurrentState == RespawnManager.State.Waiting &&
                    respawnManager.RespawnCountdownStarted)
                {
                    float timeLeft = (float)(respawnManager.RespawnTime - DateTime.Now).TotalSeconds;
                    respawnText = TextManager.GetWithVariable(respawnManager.UsingShuttle ? "RespawnShuttleDispatching" : "RespawningIn", "[time]", ToolBox.SecondsToReadableTime(timeLeft));
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
                        textScale = 1.0f + phase * 0.5f;
                        textColor = Color.Lerp(Color.Red, Color.White, 1.0f - phase);
                    }
                }
                
                if (!string.IsNullOrEmpty(respawnText))
                {
                    GUI.SmallFont.DrawString(spriteBatch, respawnText, new Vector2(120.0f, 10), textColor, 0.0f, Vector2.Zero, textScale, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0.0f);
                }
            }

            if (!ShowNetStats) return;

            netStats.Draw(spriteBatch, new Rectangle(300, 10, 300, 150));

            int width = 200, height = 300;
            int x = GameMain.GraphicsWidth - width, y = (int)(GameMain.GraphicsHeight * 0.3f);

            GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black * 0.7f, true);
            GUI.Font.DrawString(spriteBatch, "Network statistics:", new Vector2(x + 10, y + 10), Color.White);

            /* TODO: reimplement
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

        public virtual bool SelectCrewCharacter(Character character, GUIComponent characterFrame)
        {
            if (character == null) { return false; }

            if (character != myCharacter)
            {
                var client = GameMain.NetworkMember.ConnectedClients.Find(c => c.Character == character);
                if (client == null) { return false; }

                var mute = new GUITickBox(new RectTransform(new Vector2(0.95f, 0.1f), characterFrame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0.0f, 0.1f) },
                    TextManager.Get("Mute"))
                {
                    Selected = client.MutedLocally,
                    OnSelected = (tickBox) => { client.MutedLocally = tickBox.Selected; return true; }
                };

                var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.1f), characterFrame.RectTransform, Anchor.BottomCenter), isHorizontal: true)
                {
                    RelativeSpacing = 0.05f,
                    ChildAnchor = Anchor.CenterLeft,
                    Stretch = true
                };

                if (HasPermission(ClientPermissions.Ban))
                {
                    var banButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                        TextManager.Get("Ban"))
                    {
                        UserData = client,
                        OnClicked = (btn, userdata) => { GameMain.NetLobbyScreen.BanPlayer(client); return false; }
                    };
                }
                if (HasPermission(ClientPermissions.Kick))
                {
                    var kickButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                        TextManager.Get("Kick"))
                    {
                        UserData = client,
                        OnClicked = (btn, userdata) => { GameMain.NetLobbyScreen.KickPlayer(client); return false; }
                    };
                }
                else if (serverSettings.Voting.AllowVoteKick)
                {
                    var kickVoteButton = new GUIButton(new RectTransform(new Vector2(0.45f, 0.9f), buttonContainer.RectTransform),
                        TextManager.Get("VoteToKick"))
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

            return true;
        }

        public void CreateKickReasonPrompt(string clientName, bool ban, bool rangeBan = false)
        {
            var banReasonPrompt = new GUIMessageBox(
                TextManager.Get(ban ? "BanReasonPrompt" : "KickReasonPrompt"),
                "", new string[] { TextManager.Get("OK"), TextManager.Get("Cancel") }, new Vector2(0.25f, 0.2f), new Point(400, 200));

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.6f), banReasonPrompt.InnerFrame.RectTransform, Anchor.Center));
            var banReasonBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform))
            {
                Wrap = true,
                MaxTextLength = 100
            };

            GUINumberInput durationInputDays = null, durationInputHours = null;
            GUITickBox permaBanTickBox = null;

            if (ban)
            {
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform), TextManager.Get("BanDuration"));
                permaBanTickBox = new GUITickBox(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform) { RelativeOffset = new Vector2(0.05f, 0.0f) },
                    TextManager.Get("BanPermanent"))
                {
                    Selected = true
                };

                var durationContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.15f), content.RectTransform), isHorizontal: true)
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
            clientPeer.Send(outMsg, DeliveryMethod.Reliable);
        }
    }
}
