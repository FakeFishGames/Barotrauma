using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Threading;
using Lidgren.Network;
using Facepunch.Steamworks;

namespace Barotrauma.Networking
{
    class SteamP2PServerPeer : ServerPeer
    {
        private ServerSettings serverSettings;

        private NetPeerConfiguration netPeerConfiguration;
        private NetServer netServer;

        private NetConnection netConnection;

        private Facepunch.Steamworks.Server steamServer;
        
        public UInt64 OwnerSteamID
        {
            get;
            private set;
        }
        
        private class PendingClient
        {
            public string Name;
            public ConnectionInitialization InitializationStep;
            public double UpdateTime;
            public double TimeOut;
            public int Retries;
            public UInt64 SteamID;
            public Int32? PasswordSalt;
            public bool AuthSessionStarted;

            public PendingClient(UInt64 steamId)
            {
                InitializationStep = ConnectionInitialization.SteamTicketAndVersion;
                Retries = 0;
                SteamID = steamId;
                PasswordSalt = null;
                UpdateTime = Timing.TotalTime;
                TimeOut = 20.0;
                AuthSessionStarted = false;
            }

            public void Heartbeat()
            {
                TimeOut = 5.0;
            }
        }

        private List<SteamP2PConnection> connectedClients;
        private List<PendingClient> pendingClients;

        private List<NetIncomingMessage> incomingLidgrenMessages;

        public SteamP2PServerPeer(UInt64 steamId, ServerSettings settings)
        {
            serverSettings = settings;

            netServer = null;

            connectedClients = new List<SteamP2PConnection>();
            pendingClients = new List<PendingClient>();

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            steamServer = null;

            ownerKey = null;

            OwnerSteamID = steamId;
        }

        public override void Start()
        {
            if (netServer != null) { return; }

            netPeerConfiguration = new NetPeerConfiguration("barotrauma");
            netPeerConfiguration.AcceptIncomingConnections = true;
            netPeerConfiguration.AutoExpandMTU = false;
            netPeerConfiguration.MaximumConnections = 1; //only allow owner to connect
            netPeerConfiguration.EnableUPnP = false;
            netPeerConfiguration.Port = Steam.SteamManager.STEAMP2P_OWNER_PORT;

            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage |
                NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt |
                NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error |
                NetIncomingMessageType.UnconnectedData);

            netPeerConfiguration.EnableMessageType(NetIncomingMessageType.ConnectionApproval);

            netServer = new NetServer(netPeerConfiguration);

            netServer.Start();
        }

        public override void Close(string msg = null)
        {
            if (netServer == null) { return; }

            if (OwnerConnection != null) OwnerConnection.Status = NetworkConnectionStatus.Disconnected;

            for (int i = pendingClients.Count - 1; i >= 0; i--)
            {
                RemovePendingClient(pendingClients[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Disconnect(connectedClients[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            netServer.Shutdown(msg ?? DisconnectReason.ServerShutdown.ToString());

            pendingClients.Clear();
            connectedClients.Clear();

            netServer = null;

            if (steamServer != null)
            {
                steamServer.Auth.OnAuthChange = null;
            }
            steamServer = null;

            OnShutdown?.Invoke();
        }

        public override void Update()
        {
            if (netServer == null) { return; }

            netServer.ReadMessages(incomingLidgrenMessages);

            //process incoming connections first
            foreach (NetIncomingMessage inc in incomingLidgrenMessages.Where(m => m.MessageType == NetIncomingMessageType.ConnectionApproval))
            {
                HandleConnection(inc);
            }

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

            for (int i = 0; i < pendingClients.Count; i++)
            {
                PendingClient pendingClient = pendingClients[i];
                UpdatePendingClient(pendingClient);
                if (i >= pendingClients.Count || pendingClients[i] != pendingClient) { i--; }
            }

            incomingLidgrenMessages.Clear();
        }
        
        private void HandleConnection(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }
            
            if (netConnection != null && inc.SenderConnection != netConnection)
            {
                inc.SenderConnection.Deny(DisconnectReason.SessionTaken.ToString()+"/ Owner is already connected");
                return;
            }

            if (IPAddress.IsLoopback(inc.SenderConnection.RemoteEndPoint.Address.MapToIPv4()))
            {
                inc.SenderConnection.Approve();
                netConnection = inc.SenderConnection;
                
                return;
            }

            inc.SenderConnection.Deny(DisconnectReason.Kicked.ToString()+"/ Incoming connection is not loopback");
        }

        private void HandleDataMessage(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            if (inc.SenderConnection != netConnection) { return; }

            UInt64 senderSteamId = inc.ReadUInt64();

            byte incByte = inc.ReadByte();
            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;
            bool isDisconnectMessage = (incByte & (byte)PacketHeader.IsDisconnectMessage) != 0;
            bool isServerMessage = (incByte & (byte)PacketHeader.IsServerMessage) != 0;
            bool isHeartbeatMessage = (incByte & (byte)PacketHeader.IsHeartbeatMessage) != 0;
            
            if (isServerMessage)
            {
                DebugConsole.ThrowError("got server message from" + senderSteamId.ToString());
                return;
            }

            if (senderSteamId != OwnerSteamID) //sender is remote, handle disconnects and heartbeats
            {
                PendingClient pendingClient = pendingClients.Find(c => c.SteamID == senderSteamId);
                SteamP2PConnection connectedClient = connectedClients.Find(c => c.SteamID == senderSteamId);
                
                pendingClient?.Heartbeat();
                connectedClient?.Heartbeat();

                if (serverSettings.BanList.IsBanned(senderSteamId))
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.Banned.ToString()+"/ Banned");
                    }
                    else if (connectedClient != null)
                    {
                        Disconnect(connectedClient, DisconnectReason.Banned.ToString() + "/ Banned");
                    }
                    return;
                }
                else if (isDisconnectMessage)
                {
                    if (pendingClient != null)
                    {
                        string disconnectMsg = $"ServerMessage.HasDisconnected~[client]={pendingClient.Name}";
                        RemovePendingClient(pendingClient, disconnectMsg);
                    }
                    else if (connectedClient != null)
                    {
                        string disconnectMsg = $"ServerMessage.HasDisconnected~[client]={connectedClient.Name}";
                        Disconnect(connectedClient, disconnectMsg, false);
                    }
                    return;
                }
                else if (isHeartbeatMessage)
                {
                    //message exists solely as a heartbeat, ignore its contents
                    return;
                }
                else if (isConnectionInitializationStep)
                {
                    if (pendingClient != null)
                    {
                        ReadConnectionInitializationStep(pendingClient, new ReadOnlyMessage(inc.Data, false, inc.PositionInBytes, inc.LengthBytes - inc.PositionInBytes, null));
                    }
                    else
                    {
                        ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();
                        if (initializationStep == ConnectionInitialization.ConnectionStarted)
                        {
                            pendingClients.Add(new PendingClient(senderSteamId));
                        }
                    }
                }
                else if (connectedClient != null)
                {
                    UInt16 length = inc.ReadUInt16();
                    
                    IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressed, inc.PositionInBytes, length, connectedClient);
                    OnMessageReceived?.Invoke(connectedClient, msg);
                }
            }
            else //sender is owner
            {
                if (OwnerConnection != null) { (OwnerConnection as SteamP2PConnection).Heartbeat(); }

                if (isDisconnectMessage)
                {
                    DebugConsole.ThrowError("Received disconnect message from owner");
                    return;
                }
                if (isServerMessage)
                {
                    DebugConsole.ThrowError("Received server message from owner");
                    return;
                }
                if (isConnectionInitializationStep)
                {
                    if (OwnerConnection == null)
                    {
                        string ownerName = inc.ReadString();
                        OwnerConnection = new SteamP2PConnection(ownerName, OwnerSteamID);
                        OwnerConnection.Status = NetworkConnectionStatus.Connected;
                        
                        OnInitializationComplete?.Invoke(OwnerConnection);
                        OnOwnerDetermined?.Invoke(OwnerConnection);
                    }
                    return;
                }
                if (isHeartbeatMessage)
                {
                    return;
                }
                else
                {
                    UInt16 length = inc.ReadUInt16();

                    IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressed, inc.PositionInBytes, length, OwnerConnection);
                    OnMessageReceived?.Invoke(OwnerConnection, msg);
                }
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            DebugConsole.NewMessage(inc.SenderConnection.Status.ToString());

            switch (inc.SenderConnection.Status)
            {
                case NetConnectionStatus.Connected:
                    NetOutgoingMessage outMsg = netServer.CreateMessage();
                    outMsg.Write(OwnerSteamID);
                    outMsg.Write((byte)(PacketHeader.IsConnectionInitializationStep | PacketHeader.IsServerMessage));
                    netServer.SendMessage(outMsg, netConnection, NetDeliveryMethod.ReliableUnordered);
                    break;
                case NetConnectionStatus.Disconnected:
                    Close(DisconnectReason.ServerShutdown.ToString() + "/ Owner disconnected");
                    break;
            }
        }

        private void ReadConnectionInitializationStep(PendingClient pendingClient, IReadMessage inc)
        {
            if (netServer == null) { return; }

            pendingClient.TimeOut = 20.0;

            ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();

            //DebugConsole.NewMessage(initializationStep+" "+pendingClient.InitializationStep);

            if (pendingClient.InitializationStep != initializationStep) return;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    string name = Client.SanitizeName(inc.ReadString());
                    UInt64 steamId = inc.ReadUInt64();
                    UInt16 ticketLength = inc.ReadUInt16();
                    byte[] ticket = new byte[ticketLength];
                    inc.ReadBytes(ticket, 0, ticketLength);

                    if (!Client.IsValidName(name, serverSettings))
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.InvalidName.ToString() + "/ The name \"" + name + "\" is invalid");
                        return;
                    }

                    string version = inc.ReadString();
                    bool isCompatibleVersion = NetworkMember.IsCompatible(version, GameMain.Version.ToString()) ?? false;
                    if (!isCompatibleVersion)
                    {
                        RemovePendingClient(pendingClient,
                                    $"DisconnectMessage.InvalidVersion~[version]={GameMain.Version.ToString()}~[clientversion]={version}");

                        GameServer.Log(name + " (" + pendingClient.SteamID.ToString() + ") couldn't join the server (incompatible game version)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(name + " (" + pendingClient.SteamID.ToString() + ") couldn't join the server (incompatible game version)", Microsoft.Xna.Framework.Color.Red);
                        return;
                    }

                    int contentPackageCount = (int)inc.Read7BitEncoded();
                    List<ClientContentPackage> contentPackages = new List<ClientContentPackage>();
                    for (int i = 0; i < contentPackageCount; i++)
                    {
                        string packageName = inc.ReadString();
                        string packageHash = inc.ReadString();
                        contentPackages.Add(new ClientContentPackage(packageName, packageHash));
                    }

                    List<ContentPackage> missingPackages = new List<ContentPackage>();
                    foreach (ContentPackage contentPackage in GameMain.SelectedPackages)
                    {
                        if (!contentPackage.HasMultiplayerIncompatibleContent) continue;
                        bool packageFound = false;
                        for (int i = 0; i < (int)contentPackageCount; i++)
                        {
                            if (contentPackages[i].Name == contentPackage.Name && contentPackages[i].Hash == contentPackage.MD5hash.Hash)
                            {
                                packageFound = true;
                                break;
                            }
                        }
                        if (!packageFound) missingPackages.Add(contentPackage);
                    }

                    if (missingPackages.Count == 1)
                    {
                        RemovePendingClient(pendingClient,
                            $"DisconnectMessage.MissingContentPackage~[missingcontentpackage]={GetPackageStr(missingPackages[0])}");
                        GameServer.Log(name + " (" + pendingClient.SteamID.ToString() + ") couldn't join the server (missing content package " + GetPackageStr(missingPackages[0]) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    else if (missingPackages.Count > 1)
                    {
                        List<string> packageStrs = new List<string>();
                        missingPackages.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                        RemovePendingClient(pendingClient,
                            $"DisconnectMessage.MissingContentPackages~[missingcontentpackages]={string.Join(", ", packageStrs)}");
                        GameServer.Log(name + " (" + pendingClient.SteamID.ToString() + ") couldn't join the server (missing content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                        return;
                    }

                    if (!pendingClient.AuthSessionStarted)
                    {
                        ServerAuth.StartAuthSessionResult authSessionStartState = Steam.SteamManager.StartAuthSession(ticket, steamId);
                        if (authSessionStartState != ServerAuth.StartAuthSessionResult.OK)
                        {
                            RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed.ToString()+"/ Steam auth session failed to start: " + authSessionStartState.ToString());
                            return;
                        }
                        pendingClient.Name = name;
                        pendingClient.AuthSessionStarted = true;
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
                        pendingClient.InitializationStep = ConnectionInitialization.Success;
                    }
                    else
                    {
                        pendingClient.Retries++;

                        if (pendingClient.Retries >= 3)
                        {
                            string banMsg = "Failed to enter correct password too many times";
                            serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.SteamID, banMsg, null);

                            RemovePendingClient(pendingClient, DisconnectReason.Banned.ToString()+"/ "+banMsg);
                            return;
                        }
                    }
                    pendingClient.UpdateTime = Timing.TotalTime;
                    break;
            }
        }

        protected struct ClientContentPackage
        {
            public string Name;
            public string Hash;

            public ClientContentPackage(string name, string hash)
            {
                Name = name; Hash = hash;
            }
        }

        private string GetPackageStr(ContentPackage contentPackage)
        {
            return "\"" + contentPackage.Name + "\" (hash " + contentPackage.MD5hash.ShortHash + ")";
        }

        private void UpdatePendingClient(PendingClient pendingClient)
        {
            if (netServer == null) { return; }

            if (serverSettings.BanList.IsBanned(pendingClient.SteamID))
            {
                RemovePendingClient(pendingClient, DisconnectReason.Banned.ToString()+"/ Initialization interrupted by ban");
                return;
            }

            //DebugConsole.NewMessage("pending client status: " + pendingClient.InitializationStep);

            if (connectedClients.Count >= serverSettings.MaxPlayers-1)
            {
                RemovePendingClient(pendingClient, DisconnectReason.ServerFull.ToString());
            }
            
            if (pendingClient.InitializationStep == ConnectionInitialization.Success)
            {
                SteamP2PConnection newConnection = new SteamP2PConnection(pendingClient.Name, pendingClient.SteamID);
                newConnection.Status = NetworkConnectionStatus.Connected;
                connectedClients.Add(newConnection);
                pendingClients.Remove(pendingClient);
                OnInitializationComplete?.Invoke(newConnection);
            }

            pendingClient.TimeOut -= Timing.Step;
            if (pendingClient.TimeOut < 0.0)
            {
                RemovePendingClient(pendingClient,  Lidgren.Network.NetConnection.NoResponseMessage);
            }

            if (Timing.TotalTime < pendingClient.UpdateTime) { return; }
            pendingClient.UpdateTime = Timing.TotalTime + 1.0;
            
            NetOutgoingMessage outMsg = netServer.CreateMessage();
            outMsg.Write(pendingClient.SteamID);
            outMsg.Write((byte)(PacketHeader.IsConnectionInitializationStep |
                                PacketHeader.IsServerMessage));
            outMsg.Write((byte)pendingClient.InitializationStep);
            switch (pendingClient.InitializationStep)
            {
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

            if (netConnection != null)
            {
                NetSendResult result = netServer.SendMessage(outMsg, netConnection, NetDeliveryMethod.ReliableUnordered);
            }
        }

        private void RemovePendingClient(PendingClient pendingClient, string reason)
        {
            if (netServer == null) { return; }

            if (pendingClients.Contains(pendingClient))
            {
                SendDisconnectMessage(pendingClient.SteamID, reason);

                pendingClients.Remove(pendingClient);

                if (pendingClient.AuthSessionStarted)
                {
                    Steam.SteamManager.StopAuthSession(pendingClient.SteamID);
                    pendingClient.SteamID = 0;
                    pendingClient.AuthSessionStarted = false;
                }
            }
        }

        public override void InitializeSteamServerCallbacks(Server steamSrvr)
        {
            steamServer = steamSrvr;

            steamServer.Auth.OnAuthChange = OnAuthChange;
        }

        private void OnAuthChange(ulong steamID, ulong ownerID, ServerAuth.Status status)
        {
            if (netServer == null) { return; }

            PendingClient pendingClient = pendingClients.Find(c => c.SteamID == steamID);
            DebugConsole.NewMessage(steamID + " validation: " + status + ", " + (pendingClient != null));

            if (pendingClient == null)
            {
                if (status != ServerAuth.Status.OK)
                {
                    SteamP2PConnection connection = connectedClients.Find(c => c.SteamID == steamID);
                    if (connection != null)
                    {
                        Disconnect(connection, DisconnectReason.SteamAuthenticationFailed.ToString() + "/ Steam authentication status changed: " + status.ToString());
                    }
                }
                return;
            }

            if (serverSettings.BanList.IsBanned(steamID))
            {
                RemovePendingClient(pendingClient, DisconnectReason.Banned.ToString()+"/ SteamID banned");
                return;
            }

            if (status == ServerAuth.Status.OK)
            {
                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.Success;
                pendingClient.UpdateTime = Timing.TotalTime;
            }
            else
            {
                RemovePendingClient(pendingClient, DisconnectReason.SteamAuthenticationFailed.ToString()+"/ Steam authentication failed: " + status.ToString());
                return;
            }
        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod)
        {
            if (netServer == null) { return; }

            if (!(conn is SteamP2PConnection steamp2pConn)) return;
            if (!connectedClients.Contains(steamp2pConn) && conn != OwnerConnection)
            {
                DebugConsole.ThrowError("Tried to send message to unauthenticated connection: " + steamp2pConn.SteamID.ToString());
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
            byte[] msgData = new byte[1500];
            bool isCompressed; int length;
            msg.PrepareForSending(msgData, out isCompressed, out length);
            lidgrenMsg.Write(conn.SteamID);
            lidgrenMsg.Write((byte)((isCompressed ? PacketHeader.IsCompressed : PacketHeader.None) | PacketHeader.IsServerMessage));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            netServer.SendMessage(lidgrenMsg, netConnection, lidgrenDeliveryMethod);
        }

        private void SendDisconnectMessage(UInt64 steamId, string msg)
        {
            if (netServer == null) { return; }
            if (string.IsNullOrWhiteSpace(msg)) { return; }

            NetOutgoingMessage lidgrenMsg = netServer.CreateMessage();
            lidgrenMsg.Write(steamId);
            lidgrenMsg.Write((byte)(PacketHeader.IsDisconnectMessage | PacketHeader.IsServerMessage));
            lidgrenMsg.Write(msg);

            netServer.SendMessage(lidgrenMsg, netConnection, NetDeliveryMethod.ReliableUnordered);
        }

        private void Disconnect(NetworkConnection conn, string msg, bool sendDisconnectMessage)
        {
            if (netServer == null) { return; }

            if (!(conn is SteamP2PConnection steamp2pConn)) { return; }
            if (connectedClients.Contains(steamp2pConn))
            {
                if (sendDisconnectMessage) SendDisconnectMessage(steamp2pConn.SteamID, msg);
                steamp2pConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.Remove(steamp2pConn);
                OnDisconnect?.Invoke(conn, msg);
                Steam.SteamManager.StopAuthSession(conn.SteamID);
            }
        }

        public override void Disconnect(NetworkConnection conn, string msg = null)
        {
            Disconnect(conn, msg, true);
        }
    }
}
