using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using Lidgren.Network;
using Facepunch.Steamworks;

namespace Barotrauma.Networking
{
    class LidgrenServerPeer : ServerPeer
    {
        private ServerSettings serverSettings;

        private NetPeerConfiguration netPeerConfiguration;
        private NetServer netServer;

        private int maximumConnections
        {
            get { return netPeerConfiguration.MaximumConnections / 2; }
        }

        public int ConnectedClientsCount
        {
            get { return connectedClients.Count; }
        }

        private class PendingClient
        {
            public string Name;
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
                Connection = conn;
                InitializationStep = ConnectionInitialization.SteamTicketAndVersion;
                Retries = 0;
                SteamID = null;
                PasswordSalt = null;
                UpdateTime = Timing.TotalTime;
                TimeOut = Timing.TotalTime + 20.0;
                AuthSessionStarted = false;
            }
        }

        private List<LidgrenConnection> connectedClients;
        private List<PendingClient> pendingClients;

        private List<NetIncomingMessage> incomingLidgrenMessages;

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

            netPeerConfiguration = new NetPeerConfiguration("barotrauma");
            netPeerConfiguration.AcceptIncomingConnections = true;
            netPeerConfiguration.AutoExpandMTU = false;
            netPeerConfiguration.MaximumConnections = serverSettings.MaxPlayers * 2;
            netPeerConfiguration.EnableUPnP = serverSettings.EnableUPnP;
            netPeerConfiguration.Port = serverSettings.Port;

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

        public override void Close(string msg=null)
        {
            if (netServer == null) { return; }

            for (int i=pendingClients.Count-1;i>=0;i--)
            {
                RemovePendingClient(pendingClients[i], DisconnectReason.ServerShutdown.ToString());
            }

            for (int i=connectedClients.Count-1;i>=0;i--)
            {
                Disconnect(connectedClients[i], DisconnectReason.ServerShutdown.ToString());
            }

            netServer.Shutdown(msg ?? DisconnectReason.ServerShutdown.ToString());

            pendingClients.Clear();
            connectedClients.Clear();

            netServer = null;
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

            for (int i=0;i<pendingClients.Count;i++)
            {
                PendingClient pendingClient = pendingClients[i];
                UpdatePendingClient(pendingClient);
                if (i>=pendingClients.Count || pendingClients[i] != pendingClient) { i--; }
            }

            incomingLidgrenMessages.Clear();
        }

        private void InitUPnP()
        {
            if (netServer == null) { return; }

            netServer.UPnP.ForwardPort(netPeerConfiguration.Port, "barotrauma");
            if (Steam.SteamManager.USE_STEAM)
            {
                netServer.UPnP.ForwardPort(serverSettings.QueryPort, "barotrauma");
            }
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
            
            if (inc.MessageType == NetIncomingMessageType.ConnectionApproval)
            {
                if (connectedClients.Count >= serverSettings.MaxPlayers)
                {
                    inc.SenderConnection.Deny(DisconnectReason.ServerFull.ToString());
                    return;
                }

                if (serverSettings.BanList.IsBanned(inc.SenderConnection.RemoteEndPoint.Address, 0))
                {
                    //IP banned: deny immediately
                    //TODO: use TextManager
                    inc.SenderConnection.Deny("IP banned");
                    return;
                }

                PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);

                if (pendingClient == null)
                {
                    pendingClient = new PendingClient(inc.SenderConnection);
                    pendingClients.Add(pendingClient);

                    DebugConsole.NewMessage("Approved");
                }

                inc.SenderConnection.Approve();
            }
        }

        private void HandleDataMessage(NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);

            byte incByte = inc.ReadByte();
            bool isCompressed = (incByte & 0x1) != 0;
            bool isConnectionInitializationStep = (incByte & 0x2) != 0;

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
                        RemovePendingClient(pendingClient, "Received data message from unauthenticated client");
                    }
                    else if (inc.SenderConnection.Status != NetConnectionStatus.Disconnected &&
                             inc.SenderConnection.Status != NetConnectionStatus.Disconnecting)
                    {
                        inc.SenderConnection.Disconnect("Received data message from unauthenticated client");
                    }
                    return;
                }
                if (pendingClient != null) { pendingClients.Remove(pendingClient); }
                if (serverSettings.BanList.IsBanned(conn.IPEndPoint.Address, conn.SteamID))
                {
                    Disconnect(conn, "Received data message from banned client");
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
                    string disconnectMsg = inc.ReadString();
                    LidgrenConnection conn = connectedClients.Find(c => c.NetConnection == inc.SenderConnection);
                    if (conn != null)
                    {
                        Disconnect(conn, disconnectMsg);
                    }
                    else
                    {
                        PendingClient pendingClient = pendingClients.Find(c => c.Connection == inc.SenderConnection);
                        if (pendingClient != null)
                        {
                            RemovePendingClient(pendingClient, disconnectMsg);
                        }
                    }
                    break;
            }
        }

        private void ReadConnectionInitializationStep(PendingClient pendingClient, NetIncomingMessage inc)
        {
            if (netServer == null) { return; }

            pendingClient.TimeOut = Timing.TotalTime + 20.0;

            ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();

            //DebugConsole.NewMessage(initializationStep+" "+pendingClient.InitializationStep);

            if (pendingClient.InitializationStep != initializationStep) return;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    string name = Client.SanitizeName(inc.ReadString());
                    UInt64 steamId = inc.ReadUInt64();
                    UInt16 ticketLength = inc.ReadUInt16();
                    byte[] ticket = inc.ReadBytes(ticketLength);

                    if (!Client.IsValidName(name, serverSettings))
                    {
                        RemovePendingClient(pendingClient, "The name \"" +name+"\" is invalid");
                        return;
                    }

                    string version = inc.ReadString();
                    bool isCompatibleVersion = NetworkMember.IsCompatible(version, GameMain.Version.ToString()) ?? false;
                    if (!isCompatibleVersion)
                    {
                        RemovePendingClient(pendingClient,
                                    $"DisconnectMessage.InvalidVersion~[version]={GameMain.Version.ToString()}~[clientversion]={version}");

                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible game version)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(name + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (incompatible game version)", Microsoft.Xna.Framework.Color.Red);
                        return;
                    }

                    Int32 contentPackageCount = inc.ReadVariableInt32();
                    List<ClientContentPackage> contentPackages = new List<ClientContentPackage>();
                    for (int i=0;i<contentPackageCount;i++)
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
                        for (int i = 0; i < contentPackageCount; i++)
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
                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (missing content package " + GetPackageStr(missingPackages[0]) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    else if (missingPackages.Count > 1)
                    {
                        List<string> packageStrs = new List<string>();
                        missingPackages.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                        RemovePendingClient(pendingClient,
                            $"DisconnectMessage.MissingContentPackages~[missingcontentpackages]={string.Join(", ", packageStrs)}");
                        GameServer.Log(name + " (" + inc.SenderConnection.RemoteEndPoint.Address.ToString() + ") couldn't join the server (missing content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                        return;
                    }

                    if (pendingClient.SteamID == null)
                    {
                        ServerAuth.StartAuthSessionResult authSessionStartState = Steam.SteamManager.StartAuthSession(ticket, steamId);
                        if (authSessionStartState != ServerAuth.StartAuthSessionResult.OK)
                        {
                            RemovePendingClient(pendingClient, "Steam auth session failed to start: " +authSessionStartState.ToString());
                            return;
                        }
                        pendingClient.SteamID = steamId;
                        pendingClient.Name = name;
                        pendingClient.AuthSessionStarted = true;
                    }
                    else //TODO: could remove since this seems impossible
                    {
                        if (pendingClient.SteamID != steamId)
                        {
                            RemovePendingClient(pendingClient, "SteamID mismatch");
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
                        pendingClient.InitializationStep = ConnectionInitialization.Success;
                    }
                    else
                    {
                        pendingClient.Retries++;

                        if (pendingClient.Retries >= 3)
                        {
                            string banMsg = "Failed to enter correct password too many times";
                            pendingClient.Connection.Disconnect(banMsg);
                            if (pendingClient.SteamID != null)
                            {
                                serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.SteamID.Value, banMsg, null);
                            }
                            serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.Connection.RemoteEndPoint.Address, banMsg, null);
                            RemovePendingClient(pendingClient, banMsg);
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

            if (serverSettings.BanList.IsBanned(pendingClient.Connection.RemoteEndPoint.Address, pendingClient.SteamID ?? 0))
            {
                RemovePendingClient(pendingClient, "Initialization interrupted by ban");
                return;
            }

            //DebugConsole.NewMessage("pending client status: " + pendingClient.InitializationStep);

            if (connectedClients.Count >= serverSettings.MaxPlayers)
            {
                RemovePendingClient(pendingClient, DisconnectReason.ServerFull.ToString());
            }

            if (pendingClient.InitializationStep == ConnectionInitialization.Success)
            {
                LidgrenConnection newConnection = new LidgrenConnection(pendingClient.Name, pendingClient.Connection, pendingClient.SteamID ?? 0);
                connectedClients.Add(newConnection);
                pendingClients.Remove(pendingClient);
                OnInitializationComplete?.Invoke(newConnection);
            }

            if (Timing.TotalTime > pendingClient.TimeOut)
            {
                RemovePendingClient(pendingClient, "Timed out");
            }

            if (Timing.TotalTime < pendingClient.UpdateTime) { return; }
            pendingClient.UpdateTime = Timing.TotalTime + 1.0;

            NetOutgoingMessage outMsg = netServer.CreateMessage();
            outMsg.Write((byte)0x2);
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

            NetSendResult result = netServer.SendMessage(outMsg, pendingClient.Connection, NetDeliveryMethod.ReliableUnordered);
            //DebugConsole.NewMessage("sent update to pending client: "+result);
        }

        private void RemovePendingClient(PendingClient pendingClient, string reason)
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

                pendingClient.Connection.Disconnect(reason);
            }
        }

        public override void InitializeSteamServerCallbacks(Server steamServer)
        {
            steamServer.Auth.OnAuthChange = OnAuthChange;
        }

        private void OnAuthChange(ulong steamID, ulong ownerID, ServerAuth.Status status)
        {
            if (netServer == null) { return; }

            PendingClient pendingClient = pendingClients.Find(c => c.SteamID == steamID);
            DebugConsole.NewMessage(steamID + " validation: " + status+", "+(pendingClient!=null));
            
            if (pendingClient == null)
            {
                if (status != ServerAuth.Status.OK)
                {
                    LidgrenConnection connection = connectedClients.Find(c => c.SteamID == steamID);
                    if (connection != null)
                    {
                        Disconnect(connection, "Steam authentication status changed: " + status.ToString());
                    }
                }
                return;
            }

            if (serverSettings.BanList.IsBanned(pendingClient.Connection.RemoteEndPoint.Address, steamID))
            {
                RemovePendingClient(pendingClient,"SteamID banned");
                return;
            }

            if (status == ServerAuth.Status.OK)
            {
                pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.Success;
                pendingClient.UpdateTime = Timing.TotalTime;
            }
            else
            {
                RemovePendingClient(pendingClient, "Steam authentication failed: " +status.ToString());
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
            byte[] msgData = new byte[1500];
            bool isCompressed; int length;
            msg.PrepareForSending(msgData, out isCompressed, out length);
            lidgrenMsg.Write((byte)(isCompressed ? 0x1 : 0x0));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            netServer.SendMessage(lidgrenMsg, lidgrenConn.NetConnection, lidgrenDeliveryMethod);
        }
        
        public override void Disconnect(NetworkConnection conn,string msg=null)
        {
            if (netServer == null) { return; }

            if (!(conn is LidgrenConnection lidgrenConn)) { return; }
            if (connectedClients.Contains(lidgrenConn))
            {
                connectedClients.Remove(lidgrenConn);
                OnDisconnect?.Invoke(conn, msg);
                Steam.SteamManager.StopAuthSession(conn.SteamID);
            }
            lidgrenConn.NetConnection.Disconnect(msg ?? "Disconnected");
        }
    }
}
