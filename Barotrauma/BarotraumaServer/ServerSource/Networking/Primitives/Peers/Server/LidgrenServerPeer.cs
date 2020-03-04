using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    class LidgrenServerPeer : ServerPeer
    {
        private readonly ServerSettings serverSettings;

        private NetPeerConfiguration netPeerConfiguration;
        private NetServer netServer;

        private class PendingClient
        {
            public string Name;
            public int OwnerKey;
            public NetConnection Connection;
            public ConnectionInitialization InitializationStep;
            public double UpdateTime;
            public double TimeOut;
            public int Retries;
            public UInt64? SteamID;
            public Int32? PasswordSalt;
            public bool AuthSessionStarted;

            public PendingClient(NetConnection conn)
            {
                OwnerKey = 0;
                Connection = conn;
                InitializationStep = ConnectionInitialization.SteamTicketAndVersion;
                Retries = 0;
                SteamID = null;
                PasswordSalt = null;
                UpdateTime = Timing.TotalTime + Timing.Step * 3.0;
                TimeOut = NetworkConnection.TimeoutThreshold;
                AuthSessionStarted = false;
            }
        }

        private readonly List<LidgrenConnection> connectedClients;
        private readonly List<PendingClient> pendingClients;

        private readonly List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenServerPeer(int? ownKey, ServerSettings settings)
        {
            serverSettings = settings;

            netServer = null;

            connectedClients = new List<LidgrenConnection>();
            pendingClients = new List<PendingClient>();

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            ownerKey = ownKey;
        }

        public override void Start()
        {
            if (netServer != null) { return; }

            netPeerConfiguration = new NetPeerConfiguration("barotrauma")
            {
                AcceptIncomingConnections = true,
                AutoExpandMTU = false,
                MaximumConnections = NetConfig.MaxPlayers * 2,
                EnableUPnP = serverSettings.EnableUPnP,
                Port = serverSettings.Port
            };

            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage |
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);

            netPeerConfiguration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            netServer = new NetServer(netPeerConfiguration);

            netServer.Start();

            if (serverSettings.EnableUPnP)
            {
                InitUPnP();

                while (DiscoveringUPnP()) { }

                FinishUPnP();
            }
        }

        public override void Close(string msg = null)
        {
            if (netServer == null) { return; }

            for (int i = pendingClients.Count - 1; i >= 0; i--)
            {
                RemovePendingClient(pendingClients[i], DisconnectReason.ServerShutdown, msg);
            }

            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Disconnect(connectedClients[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            netServer.Shutdown(msg ?? DisconnectReason.ServerShutdown.ToString());

            pendingClients.Clear();
            connectedClients.Clear();

            netServer = null;

            Steamworks.SteamServer.OnValidateAuthTicketResponse -= OnAuthChange;

            OnShutdown?.Invoke();
        }

        public override void Update(float deltaTime)
        {
            if (netServer == null) { return; }

            if (OnOwnerDetermined != null && OwnerConnection != null)
            {
                OnOwnerDetermined?.Invoke(OwnerConnection);
                OnOwnerDetermined = null;
            }

            netServer.ReadMessages(incomingLidgrenMessages);
            
            //process incoming connections first
            foreach (NetIncomingMessage inc in incomingLidgrenMessages.Where(m => m.MessageType == NetIncomingMessageType.ConnectionApproval))
            {
                HandleConnection(inc);
            }

            try
            {
                //after processing connections, go ahead with the rest of the messages
                foreach (NetIncomingMessage inc in incomingLidgrenMessages.Where(m => m.MessageType != NetIncomingMessageType.ConnectionApproval))
                {
                    switch (inc.MessageType)
                    {
                        case NetIncomingMessageType.Data:
                            HandleDataMessage(inc);
                            break;
                        case NetIncomingMessageType.StatusChanged:
                            HandleStatusChanged(inc);
                            break;
                    }
                }
            }

            catch (Exception e)
            {
                string errorMsg = "Server failed to read an incoming message. {" + e + "}\n" + e.StackTrace;
                GameAnalyticsManager.AddErrorEventOnce("LidgrenServerPeer.Update:ClientReadException" + e.TargetSite.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
            }

            for (int i = 0; i < pendingClients.Count; i++)
            {
                PendingClient pendingClient = pendingClients[i];
                UpdatePendingClient(pendingClient, deltaTime);
                if (i >= pendingClients.Count || pendingClients[i] != pendingClient) { i--; }
            }

            incomingLidgrenMessages.Clear();
        }

        private void InitUPnP()
        {
            if (netServer == null) { return; }

            netServer.UPnP.ForwardPort(netPeerConfiguration.Port, "barotrauma");
#if USE_STEAM
            netServer.UPnP.ForwardPort(serverSettings.QueryPort, "barotrauma");
#endif
        }

        private bool DiscoveringUPnP()
        {
            if (netServer == null) { return false; }

            return netServer.UPnP.Status == UPnPStatus.Discovering;
        }

        private void FinishUPnP()
        {
            //do nothing
        }

        private void HandleConnection(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }
            
            if (connectedClients.Count >= serverSettings.MaxPlayers)
            {
                inc.SenderConnection.Deny(DisconnectReason.ServerFull.ToString());
                return;
            }

            if (serverSettings.BanList.IsBanned(inc.SenderConnection.RemoteEndPoint.Address, 0, out string banReason))
            {
                //IP banned: deny immediately
                inc.SenderConnection.Deny(DisconnectReason.Banned.ToString() + "/ " + banReason);
                return;
            }

            PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);

            if (pendingClient == null)
            {
                pendingClient = new PendingClient(inc.SenderConnection);
                pendingClients.Add(pendingClient);
            }

            inc.SenderConnection.Approve();
        }

        private void HandleDataMessage(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);

            byte incByte = inc.ReadByte();
            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;

            if (isConnectionInitializationStep && pendingClient != null)
            {
                ReadConnectionInitializationStep(pendingClient, inc);
            }
            else if (!isConnectionInitializationStep)
            {
                LidgrenConnection conn = connectedClients.Find(c => c.NetConnection == inc.SenderConnection);
                if (conn == null)
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.AuthenticationRequired, "Received data message from unauthenticated client");
                    }
                    else if (inc.SenderConnection.Status != NetConnectionStatus.Disconnected &&
                             inc.SenderConnection.Status != NetConnectionStatus.Disconnecting)
                    {
                        inc.SenderConnection.Disconnect(DisconnectReason.AuthenticationRequired.ToString() + "/ Received data message from unauthenticated client");
                    }
                    return;
                }
                if (pendingClient != null) { pendingClients.Remove(pendingClient); }
                if (serverSettings.BanList.IsBanned(conn.IPEndPoint.Address, conn.SteamID, out string banReason))
                {
                    Disconnect(conn, DisconnectReason.Banned.ToString() + "/ " + banReason);
                    return;
                }
                UInt16 length = inc.ReadUInt16();

                //DebugConsole.NewMessage(isCompressed + " " + isConnectionInitializationStep + " " + (int)incByte + " " + length);

                IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressed, inc.PositionInBytes, length, conn);
                OnMessageReceived?.Invoke(conn, msg);
            }
        }
        
        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            switch (inc.SenderConnection.Status)
            {
                case NetConnectionStatus.Disconnected:
                    string disconnectMsg;
                    LidgrenConnection conn = connectedClients.Find(c => c.NetConnection == inc.SenderConnection);
                    if (conn != null)
                    {
                        if (conn == OwnerConnection)
                        {
                            DebugConsole.NewMessage("Owner disconnected: closing the server...");
                            GameServer.Log("Owner disconnected: closing the server...", ServerLog.MessageType.ServerMessage);
                            Close(DisconnectReason.ServerShutdown.ToString() + "/ Owner disconnected");
                        }
                        else
                        {
                            disconnectMsg = $"ServerMessage.HasDisconnected~[client]={conn.Name}";
                            Disconnect(conn, disconnectMsg);
                        }
                    }
                    else
                    {
                        PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);
                        if (pendingClient != null)
                        {
                            RemovePendingClient(pendingClient, DisconnectReason.Unknown, $"ServerMessage.HasDisconnected~[client]={pendingClient.Name}");
                        }
                    }
                    break;
            }
        }

        private void ReadConnectionInitializationStep(PendingClient pendingClient, NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            pendingClient.TimeOut = NetworkConnection.TimeoutThreshold;

            ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();

            //DebugConsole.NewMessage(initializationStep+" "+pendingClient.InitializationStep);

            if (pendingClient.InitializationStep != initializationStep) return;

            pendingClient.UpdateTime = Timing.TotalTime + Timing.Step;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    string name = Client.SanitizeName(inc.ReadString());
                    int ownKey = inc.ReadInt32();
                    UInt64 steamId = inc.ReadUInt64();
                    UInt16 ticketLength = inc.ReadUInt16();
                    byte[] ticket = inc.ReadBytes(ticketLength);

                    if (!Client.IsValidName(name, serverSettings))
                    {
                        if (OwnerConnection != null ||
                            !IPAddress.IsLoopback(pendingClient.Connection.RemoteEndPoint.Address.MapToIPv4NoThrow()) &&
                            ownerKey == null || ownKey == 0 && ownKey != ownerKey)
                        {
                            RemovePendingClient(pendingClient, DisconnectReason.InvalidName, "The name \"" + name + "\" is invalid");
                            return;
                        }
                    }

                    string version = inc.ReadString();
                    bool isCompatibleVersion = NetworkMember.IsCompatible(version, GameMain.Version.ToString()) ?? false;
                    if (!isCompatibleVersion)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.InvalidVersion,
                                    $"DisconnectMessage.InvalidVersion~[version]={GameMain.Version.ToString()}~[clientversion]={version}");

                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible game version)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(name + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible game version)", Microsoft.Xna.Framework.Color.Red);
                        return;
                    }

                    int contentPackageCount = inc.ReadVariableInt32();
                    List<ClientContentPackage> clientContentPackages = new List<ClientContentPackage>();
                    for (int i = 0; i < contentPackageCount; i++)
                    {
                        string packageName = inc.ReadString();
                        string packageHash = inc.ReadString();
                        clientContentPackages.Add(new ClientContentPackage(packageName, packageHash));
                    }

                    //check if the client is missing any of our packages
                    List<ContentPackage> missingPackages = new List<ContentPackage>();
                    foreach (ContentPackage serverContentPackage in GameMain.SelectedPackages)
                    {
                        if (!serverContentPackage.HasMultiplayerIncompatibleContent) continue;
                        bool packageFound = clientContentPackages.Any(cp => cp.Name == serverContentPackage.Name && cp.Hash == serverContentPackage.MD5hash.Hash);
                        if (!packageFound) { missingPackages.Add(serverContentPackage); }
                    }

                    //check if the client is using packages we don't have
                    List<ClientContentPackage> redundantPackages = new List<ClientContentPackage>();
                    foreach (ClientContentPackage clientContentPackage in clientContentPackages)
                    {
                        bool packageFound = GameMain.SelectedPackages.Any(cp => cp.Name == clientContentPackage.Name && cp.MD5hash.Hash == clientContentPackage.Hash);
                        if (!packageFound) { redundantPackages.Add(clientContentPackage); }
                    }

                    if (missingPackages.Count == 1)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.MissingContentPackage,
                            $"DisconnectMessage.MissingContentPackage~[missingcontentpackage]={GetPackageStr(missingPackages[0])}");
                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address + ") couldn't join the server (missing content package " + GetPackageStr(missingPackages[0]) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    else if (missingPackages.Count > 1)
                    {
                        List<string> packageStrs = new List<string>();
                        missingPackages.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                        RemovePendingClient(pendingClient, DisconnectReason.MissingContentPackage,
                            $"DisconnectMessage.MissingContentPackages~[missingcontentpackages]={string.Join(", ", packageStrs)}");
                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address + ") couldn't join the server (missing content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    if (redundantPackages.Count == 1)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.IncompatibleContentPackage,
                            $"DisconnectMessage.IncompatibleContentPackage~[incompatiblecontentpackage]={GetPackageStr(redundantPackages[0])}");
                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address + ") couldn't join the server (using an incompatible content package " + GetPackageStr(redundantPackages[0]) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    if (redundantPackages.Count > 1)
                    {
                        List<string> packageStrs = new List<string>();
                        redundantPackages.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                        RemovePendingClient(pendingClient, DisconnectReason.IncompatibleContentPackage,
                            $"DisconnectMessage.IncompatibleContentPackages~[incompatiblecontentpackages]={string.Join(", ", packageStrs)}");
                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address + ") couldn't join the server (using incompatible content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                        return;
                    }

                    if (pendingClient.SteamID == null)
                    {
                        bool requireSteamAuth = GameMain.Config.RequireSteamAuthentication;
#if DEBUG
                        requireSteamAuth = false;
#endif

                        //steam auth cannot be done (SteamManager not initialized or no ticket given),
                        //but it's not required either -> let the client join without auth
                        if ((!Steam.SteamManager.IsInitialized || (ticket?.Length??0) == 0) &&
                            !requireSteamAuth)
                        {
                            pendingClient.Name = name;
                            pendingClient.OwnerKey = ownKey;
                            pendingClient.InitializationStep = ConnectionInitialization.ContentPackageOrder;
                        }
                        else
                        {
                            Steamworks.BeginAuthResult authSessionStartState = Steam.SteamManager.StartAuthSession(ticket, steamId);
                            if (authSessionStartState != Steamworks.BeginAuthResult.OK)
                            {
                                RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed, "Steam auth session failed to start: " + authSessionStartState.ToString());
                                return;
                            }
                            pendingClient.SteamID = steamId;
                            pendingClient.Name = name;
                            pendingClient.OwnerKey = ownKey;
                            pendingClient.AuthSessionStarted = true;
                        }
                    }
                    else //TODO: could remove since this seems impossible
                    {
                        if (pendingClient.SteamID != steamId)
                        {
                            RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed, "SteamID mismatch");
                            return;
                        }
                    }
                    break;
                case ConnectionInitialization.Password:
                    int pwLength = inc.ReadByte();
                    byte[] incPassword = new byte[pwLength];
                    inc.ReadBytes(incPassword, 0, pwLength);
                    if (pendingClient.PasswordSalt == null)
                    {
                        DebugConsole.ThrowError("Received password message from client without salt");
                        return;
                    }
                    if (serverSettings.IsPasswordCorrect(incPassword, pendingClient.PasswordSalt.Value))
                    {
                        pendingClient.InitializationStep = ConnectionInitialization.ContentPackageOrder;
                    }
                    else
                    {
                        pendingClient.Retries++;
                        if (serverSettings.BanAfterWrongPassword && pendingClient.Retries > serverSettings.MaxPasswordRetriesBeforeBan)
                        {
                            string banMsg = "Failed to enter correct password too many times";
                            if (pendingClient.SteamID != null)
                            {
                                serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.SteamID.Value, banMsg, null);
                            }
                            serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.Connection.RemoteEndPoint.Address, banMsg, null);
                            RemovePendingClient(pendingClient, DisconnectReason.Banned, banMsg);
                            return;
                        }
                    }
                    pendingClient.UpdateTime = Timing.TotalTime;
                    break;
                case ConnectionInitialization.ContentPackageOrder:
                    pendingClient.InitializationStep = ConnectionInitialization.Success;
                    pendingClient.UpdateTime = Timing.TotalTime;
                    break;
            }
        }


        private void UpdatePendingClient(PendingClient pendingClient, float deltaTime)
        {
            if (netServer == null) { return; }

            if (serverSettings.BanList.IsBanned(pendingClient.Connection.RemoteEndPoint.Address, pendingClient.SteamID ?? 0, out string banReason))
            {
                RemovePendingClient(pendingClient, DisconnectReason.Banned, banReason);
                return;
            }

            //DebugConsole.NewMessage("pending client status: " + pendingClient.InitializationStep);

            if (connectedClients.Count >= serverSettings.MaxPlayers)
            {
                RemovePendingClient(pendingClient, DisconnectReason.ServerFull, "");
            }

            if (pendingClient.InitializationStep == ConnectionInitialization.Success)
            {
                LidgrenConnection newConnection = new LidgrenConnection(pendingClient.Name, pendingClient.Connection, pendingClient.SteamID ?? 0)
                {
                    Status = NetworkConnectionStatus.Connected
                };
                connectedClients.Add(newConnection);
                pendingClients.Remove(pendingClient);

                if (OwnerConnection == null &&
                    IPAddress.IsLoopback(pendingClient.Connection.RemoteEndPoint.Address.MapToIPv4NoThrow()) &&
                    ownerKey != null && pendingClient.OwnerKey != 0 && pendingClient.OwnerKey == ownerKey)
                {
                    ownerKey = null;
                    OwnerConnection = newConnection;
                }

                OnInitializationComplete?.Invoke(newConnection);
                return;
            }


            pendingClient.TimeOut -= deltaTime;
            if (pendingClient.TimeOut < 0.0)
            {
                RemovePendingClient(pendingClient, DisconnectReason.Unknown, Lidgren.Network.NetConnection.NoResponseMessage);
            }

            if (Timing.TotalTime < pendingClient.UpdateTime) { return; }
            pendingClient.UpdateTime = Timing.TotalTime + 1.0;

            NetOutgoingMessage outMsg = netServer.CreateMessage();
            outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
            outMsg.Write((byte)pendingClient.InitializationStep);
            switch (pendingClient.InitializationStep)
            {
                case ConnectionInitialization.ContentPackageOrder:
                    var mpContentPackages = GameMain.SelectedPackages.Where(cp => cp.HasMultiplayerIncompatibleContent).ToList();
                    outMsg.WriteVariableInt32(mpContentPackages.Count);
                    for (int i = 0; i < mpContentPackages.Count; i++)
                    {
                        outMsg.Write(mpContentPackages[i].MD5hash.Hash);
                    }
                    break;
                case ConnectionInitialization.Password:
                    outMsg.Write(pendingClient.PasswordSalt == null); outMsg.WritePadBits();
                    if (pendingClient.PasswordSalt == null)
                    {
                        pendingClient.PasswordSalt = CryptoRandom.Instance.Next();
                        outMsg.Write(pendingClient.PasswordSalt.Value);
                    }
                    else
                    {
                        outMsg.Write(pendingClient.Retries);
                    }
                    break;
            }
#if DEBUG
            netPeerConfiguration.SimulatedDuplicatesChance = GameMain.Server.SimulatedDuplicatesChance;
            netPeerConfiguration.SimulatedMinimumLatency = GameMain.Server.SimulatedMinimumLatency;
            netPeerConfiguration.SimulatedRandomLatency = GameMain.Server.SimulatedRandomLatency;
            netPeerConfiguration.SimulatedLoss = GameMain.Server.SimulatedLoss;
#endif
            NetSendResult result = netServer.SendMessage(outMsg, pendingClient.Connection, NetDeliveryMethod.ReliableUnordered);
            if (result != NetSendResult.Sent && result != NetSendResult.Queued)
            {
                DebugConsole.NewMessage("Failed to send initialization step " + pendingClient.InitializationStep.ToString() + " to pending client: " + result.ToString(), Microsoft.Xna.Framework.Color.Yellow);
            }
            //DebugConsole.NewMessage("sent update to pending client: " + pendingClient.InitializationStep);
        }

        private void RemovePendingClient(PendingClient pendingClient, DisconnectReason reason, string msg)
        {
            if (netServer == null) { return; }

            if (pendingClients.Contains(pendingClient))
            {
                pendingClients.Remove(pendingClient);

                if (pendingClient.AuthSessionStarted)
                {
                    Steam.SteamManager.StopAuthSession(pendingClient.SteamID.Value);
                    pendingClient.SteamID = null;
                    pendingClient.AuthSessionStarted = false;
                }

                pendingClient.Connection.Disconnect(reason + "/" + msg);
            }
        }

        public override void InitializeSteamServerCallbacks()
        {
            Steamworks.SteamServer.OnValidateAuthTicketResponse += OnAuthChange;
        }

        private void OnAuthChange(Steamworks.SteamId steamID, Steamworks.SteamId ownerID, Steamworks.AuthResponse status)
        {
            if (netServer == null) { return; }

            PendingClient pendingClient = pendingClients.Find(c => c.SteamID == steamID);
            DebugConsole.Log(steamID + " validation: " + status+", "+(pendingClient!=null));
            
            if (pendingClient == null)
            {
                if (status != Steamworks.AuthResponse.OK)
                {
                    LidgrenConnection connection = connectedClients.Find(c => c.SteamID == steamID);
                    if (connection != null)
                    {
                        Disconnect(connection, DisconnectReason.SteamAuthenticationFailed.ToString() + "/ Steam authentication status changed: " + status.ToString());
                    }
                }
                return;
            }

            if (serverSettings.BanList.IsBanned(pendingClient.Connection.RemoteEndPoint.Address, steamID, out string banReason))
            {
                RemovePendingClient(pendingClient, DisconnectReason.Banned, banReason);
                return;
            }

            if (status == Steamworks.AuthResponse.OK)
            {
                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;
                pendingClient.UpdateTime = Timing.TotalTime;
            }
            else
            {
                RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed, "Steam authentication failed: " + status.ToString());
                return;
            }
        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod)
        {
            if (netServer == null) { return; }

            if (!(conn is LidgrenConnection lidgrenConn)) return;
            if (!connectedClients.Contains(lidgrenConn))
            {
                DebugConsole.ThrowError("Tried to send message to unauthenticated connection: " + lidgrenConn.IPString);
                return;
            }

            NetDeliveryMethod lidgrenDeliveryMethod = NetDeliveryMethod.Unreliable;
            switch (deliveryMethod)
            {
                case DeliveryMethod.Unreliable:
                    lidgrenDeliveryMethod = NetDeliveryMethod.Unreliable;
                    break;
                case DeliveryMethod.Reliable:
                    lidgrenDeliveryMethod = NetDeliveryMethod.ReliableUnordered;
                    break;
                case DeliveryMethod.ReliableOrdered:
                    lidgrenDeliveryMethod = NetDeliveryMethod.ReliableOrdered;
                    break;
            }

            NetOutgoingMessage lidgrenMsg = netServer.CreateMessage();
            byte[] msgData = new byte[msg.LengthBytes];
            msg.PrepareForSending(ref msgData, out bool isCompressed, out int length);
            lidgrenMsg.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            NetSendResult result = netServer.SendMessage(lidgrenMsg, lidgrenConn.NetConnection, lidgrenDeliveryMethod);
            if (result != NetSendResult.Sent && result != NetSendResult.Queued)
            {
                DebugConsole.NewMessage("Failed to send message to "+conn.Name+": " + result.ToString(), Microsoft.Xna.Framework.Color.Yellow);
            }
        }
        
        public override void Disconnect(NetworkConnection conn,string msg=null)
        {
            if (netServer == null) { return; }

            if (!(conn is LidgrenConnection lidgrenConn)) { return; }
            if (connectedClients.Contains(lidgrenConn))
            {
                lidgrenConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.Remove(lidgrenConn);
                OnDisconnect?.Invoke(conn, msg);
                Steam.SteamManager.StopAuthSession(conn.SteamID);
            }
            lidgrenConn.NetConnection.Disconnect(msg ?? "Disconnected");
        }
    }
}
