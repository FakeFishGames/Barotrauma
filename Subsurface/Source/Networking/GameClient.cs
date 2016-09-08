using System;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
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
        
        private GUIButton endRoundButton;
        private GUITickBox endVoteTickBox;

        private ClientPermissions permissions;

        private bool connected;

        private byte myID;

        private List<Client> otherClients;

        private string serverIP;

        private bool needAuth;
        private bool requiresPw;
        private int nonce;
        private string saltedPw;

        private UInt32 lastSentChatMsgID = 0; //last message this client has successfully sent
        private UInt32 lastQueueChatMsgID = 0; //last message added to the queue
        private List<ChatMessage> chatMsgQueue = new List<ChatMessage>();

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
        
        public GameClient(string newName)
        {
            endVoteTickBox = new GUITickBox(new Rectangle(GameMain.GraphicsWidth - 170, 20, 20, 20), "End round", Alignment.TopLeft, inGameHUD);
            endVoteTickBox.OnSelected = ToggleEndRoundVote;
            endVoteTickBox.Visible = false;

            endRoundButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 170 - 170, 20, 150, 20), "End round", Alignment.TopLeft, GUI.Style, inGameHUD);
            endRoundButton.OnClicked = (btn, userdata) => 
            {
                if (!permissions.HasFlag(ClientPermissions.EndRound)) return false;

                //TODO: tell server that client requested round end

                return true; 
            };
            endRoundButton.Visible = false;

            newName = newName.Replace(":", "");
            newName = newName.Replace(";", "");

            GameMain.DebugDraw = false;
            Hull.EditFire = false;
            Hull.EditWater = false;

            name = newName;
            
            characterInfo = new CharacterInfo(Character.HumanConfigFile, name);
            characterInfo.Job = null;

            otherClients = new List<Client>();

            GameMain.NetLobbyScreen = new NetLobbyScreen();
        }

        public void ConnectToServer(string hostIP)
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
            client.Start();
            
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

            NetOutgoingMessage outmsg = client.CreateMessage();
            outmsg.Write((byte)ClientPacketHeader.REQUEST_AUTH);

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

            CoroutineManager.StartCoroutine(WaitForStartingInfo());
            
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
            
            DateTime timeOut = DateTime.Now + new TimeSpan(0,0,20);
            DateTime reqAuthTime = DateTime.Now + new TimeSpan(0, 0, 0, 0, 200);

            // Loop until we are approved
            while (!CanStart && !connectCancelled)
            {
                if (reconnectBox == null)
                {
                    reconnectBox = new GUIMessageBox("CONNECTING", "Connecting to " + serverIP, new string[] { "Cancel" });

                    reconnectBox.Buttons[0].OnClicked += CancelConnect;
                    reconnectBox.Buttons[0].OnClicked += reconnectBox.Close;
                }

                int seconds = DateTime.Now.Second;

                string connectingText = "Connecting to " + serverIP;
                for (int i = 0; i < 1 + (seconds % 3); i++ )
                {
                    connectingText += ".";
                }
                reconnectBox.Text = connectingText;

                if (DateTime.Now > reqAuthTime)
                {
                    if (needAuth)
                    {
                        //request auth again
                        NetOutgoingMessage reqAuthMsg = client.CreateMessage();
                        reqAuthMsg.Write((byte)ClientPacketHeader.REQUEST_AUTH);
                        client.SendMessage(reqAuthMsg, NetDeliveryMethod.Unreliable);
                    }
                    else
                    {
                        //request init again
                        if (!requiresPw)
                        {
                            NetOutgoingMessage outmsg = client.CreateMessage();
                            outmsg.Write((byte)ClientPacketHeader.REQUEST_INIT);
                            outmsg.Write(GameMain.Version.ToString());
                            outmsg.Write(GameMain.SelectedPackage.Name);
                            outmsg.Write(GameMain.SelectedPackage.MD5hash.Hash);
                            outmsg.Write(name);
                            client.SendMessage(outmsg, NetDeliveryMethod.Unreliable);
                        }
                        else
                        {
                            NetOutgoingMessage outmsg = client.CreateMessage();
                            outmsg.Write((byte)ClientPacketHeader.REQUEST_INIT);
                            outmsg.Write(saltedPw);
                            outmsg.Write(GameMain.Version.ToString());
                            outmsg.Write(GameMain.SelectedPackage.Name);
                            outmsg.Write(GameMain.SelectedPackage.MD5hash.Hash);
                            outmsg.Write(name);
                            client.SendMessage(outmsg, NetDeliveryMethod.Unreliable);
                        }
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
                                string denyMessage = inc.ReadString();
                                
                                new GUIMessageBox("Couldn't connect to server", denyMessage);

                                connectCancelled = true;
                            }
                            break;
                    }
                }

                catch (Exception e)
                {
                    DebugConsole.ThrowError("Error while connecting to server", e);
                    break;
                }

                if (requiresPw && !CanStart && !connectCancelled)
                {
                    if (reconnectBox != null)
                    {
                        reconnectBox.Close(null, null);
                        reconnectBox = null;
                    }

                    var msgBox = new GUIMessageBox(pwMsg, "", new string[] { "OK", "Cancel" });
                    var passwordBox = new GUITextBox(new Rectangle(0, 40, 150, 25), Alignment.TopLeft, GUI.Style, msgBox.children[0]);
                    passwordBox.UserData = "password";

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
                                        string denyMessage = inc.ReadString();

                                        new GUIMessageBox("Couldn't connect to server", denyMessage);

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
                            reqAuthMsg.Write((byte)ClientPacketHeader.REQUEST_AUTH);
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
                    
                }
            }
            
            if (!gameStarted)
            {
                SendLobbyUpdate();
            }

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

            while ((inc = client.ReadMessage()) != null)
            {
                switch (inc.MessageType)
                {
                    case NetIncomingMessageType.Data:
                        ServerPacketHeader header = (ServerPacketHeader)inc.ReadByte();
                        switch (header)
                        {
                            case ServerPacketHeader.UPDATE_LOBBY:
                                ReadLobbyUpdate(inc);
                                break;
                        }
                        break;
                }
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
                            GameMain.NetLobbyScreen.LastUpdateID = inc.ReadUInt32();
                            GameMain.NetLobbyScreen.ServerName = inc.ReadString();
                            GameMain.NetLobbyScreen.ServerMessage = inc.ReadString();

                            UInt16 subListCount = inc.ReadUInt16();
                            if (subListCount > 0)
                            {
                                List<Submarine> submarines = new List<Submarine>();
                                for (int i = 0; i < subListCount; i++)
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
                            }
                            string selectSubName = inc.ReadString();
                            string selectSubHash = inc.ReadString();
                            GameMain.NetLobbyScreen.TrySelectSub(selectSubName, selectSubHash, GameMain.NetLobbyScreen.SubList);
                            string selectShuttleName = inc.ReadString();
                            string selectShuttleHash = inc.ReadString();
                            GameMain.NetLobbyScreen.TrySelectSub(selectShuttleName, selectShuttleHash, GameMain.NetLobbyScreen.ShuttleList.ListBox);

                            GameMain.NetLobbyScreen.SetTraitorsEnabled((YesNoMaybe)inc.ReadRangedInteger(0, 2));

                            GameMain.NetLobbyScreen.SetMissionType(inc.ReadRangedInteger(0, Mission.MissionTypes.Count - 1));

                            GameMain.NetLobbyScreen.SelectMode(inc.ReadByte());

                            GameMain.NetLobbyScreen.LevelSeed = inc.ReadString();

                            bool autoRestartEnabled = inc.ReadBoolean();
                            float autoRestartTimer = autoRestartEnabled ? inc.ReadFloat() : 0.0f; 

                            GameMain.NetLobbyScreen.SetAutoRestart(autoRestartEnabled, autoRestartTimer);
                        }
                        lastSentChatMsgID = inc.ReadUInt32();
                        break;
                    case ServerNetObject.CHAT_MESSAGE:
                        ChatMessage.ClientRead(inc);
                        break;
                }
            }
        }

        private void SendLobbyUpdate()
        {
            NetOutgoingMessage outmsg = client.CreateMessage();
            outmsg.Write((byte)ClientPacketHeader.UPDATE_LOBBY);

            outmsg.Write((byte)ClientNetObject.SYNC_IDS);
            outmsg.Write(GameMain.NetLobbyScreen.LastUpdateID);
            outmsg.Write(ChatMessage.LastID);
            ChatMessage removeMsg;
            while ((removeMsg=chatMsgQueue.Find(cMsg => cMsg.NetStateID <= lastSentChatMsgID)) != null)
            {
                chatMsgQueue.Remove(removeMsg);
            }

            foreach (ChatMessage cMsg in chatMsgQueue)
            {
                cMsg.ClientWrite(outmsg);
            }
            outmsg.Write((byte)ClientNetObject.END_OF_MESSAGE);
            client.SendMessage(outmsg, NetDeliveryMethod.Unreliable);
        }

        public override void SendChatMessage(string message, ChatMessageType? type = null)
        {
            if (client.ServerConnection == null) return;

            type = ChatMessageType.Default;

            ChatMessage chatMessage = ChatMessage.Create(
                gameStarted && myCharacter != null ? myCharacter.Name : name,
                message, (ChatMessageType)type, gameStarted ? myCharacter : null);

            lastQueueChatMsgID++;
            chatMessage.netStateID = lastQueueChatMsgID;

            chatMsgQueue.Add(chatMessage);
        }
        
        public bool HasPermission(ClientPermissions permission)
        {
            return false;// permissions.HasFlag(permission);
        }

        public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
        {
            base.Draw(spriteBatch);
            
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
            //TODO: tell server
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
        
        public bool VoteForKick(GUIButton button, object userdata)
        {
            var votedClient = otherClients.Find(c => c.Character == userdata);
            if (votedClient == null) return false;

            votedClient.AddKickVote(new Client(name, ID));

            if (votedClient == null) return false;

            //Vote(VoteType.Kick, votedClient);

            button.Enabled = false;

            return true;
        }
        
        public bool SpectateClicked(GUIButton button, object userData)
        {
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

            //Vote(VoteType.EndRound, tickBox.Selected);

            return false;
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
