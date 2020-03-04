using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Threading;

namespace Barotrauma.Networking
{
    class SteamP2PServerPeer : ServerPeer
    {
        private bool started;

        private ServerSettings serverSettings;

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
                UpdateTime = Timing.TotalTime+Timing.Step*3.0;
                TimeOut = NetworkConnection.TimeoutThreshold;
                AuthSessionStarted = false;
            }

            public void Heartbeat()
            {
                TimeOut = NetworkConnection.TimeoutThreshold;
            }
        }

        private List<SteamP2PConnection> connectedClients;
        private List<PendingClient> pendingClients;

        public SteamP2PServerPeer(UInt64 steamId, ServerSettings settings)
        {
            serverSettings = settings;

            connectedClients = new List<SteamP2PConnection>();
            pendingClients = new List<PendingClient>();

            ownerKey = null;

            OwnerSteamID = steamId;

            started = false;
        }

        public override void Start()
        {
            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write(OwnerSteamID);
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)(PacketHeader.IsConnectionInitializationStep | PacketHeader.IsServerMessage));

            byte[] msgToSend = (byte[])outMsg.Buffer.Clone();
            Array.Resize(ref msgToSend, outMsg.LengthBytes);
            ChildServerRelay.Write(msgToSend);

            started = true;
        }

        public override void Close(string msg = null)
        {
            if (!started) { return; }

            if (OwnerConnection != null) OwnerConnection.Status = NetworkConnectionStatus.Disconnected;

            for (int i = pendingClients.Count - 1; i >= 0; i--)
            {
                RemovePendingClient(pendingClients[i], DisconnectReason.ServerShutdown, msg);
            }

            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                Disconnect(connectedClients[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            pendingClients.Clear();
            connectedClients.Clear();

            ChildServerRelay.ShutDown();

            OnShutdown?.Invoke();
        }

        public override void Update(float deltaTime)
        {
            if (!started) { return; }

            if (OnOwnerDetermined != null && OwnerConnection != null)
            {
                OnOwnerDetermined?.Invoke(OwnerConnection);
                OnOwnerDetermined = null;
            }

            //backwards for loop so we can remove elements while iterating
            for (int i = connectedClients.Count - 1; i >= 0; i--)
            {
                connectedClients[i].Decay(deltaTime);
                if (connectedClients[i].Timeout < 0.0)
                {
                    Disconnect(connectedClients[i], "Timed out");
                }
            }

            try
            {
                while (ChildServerRelay.Read(out byte[] incBuf))
                {
                    IReadMessage inc = new ReadOnlyMessage(incBuf, false, 0, incBuf.Length, OwnerConnection);

                    HandleDataMessage(inc);
                }
            }

            catch (Exception e)
            {
                string errorMsg = "Server failed to read an incoming message. {" + e + "}\n" + e.StackTrace;
                GameAnalyticsManager.AddErrorEventOnce("SteamP2PServerPeer.Update:ClientReadException" + e.TargetSite.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
#endif
            }

            for (int i = 0; i < pendingClients.Count; i++)
            {
                PendingClient pendingClient = pendingClients[i];
                UpdatePendingClient(pendingClient);
                if (i >= pendingClients.Count || pendingClients[i] != pendingClient) { i--; }
            }
        }
        
        private void HandleDataMessage(IReadMessage inc)
        {
            if (!started) { return; }

            UInt64 senderSteamId = inc.ReadUInt64();

            byte incByte = inc.ReadByte();
            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;
            bool isDisconnectMessage = (incByte & (byte)PacketHeader.IsDisconnectMessage) != 0;
            bool isServerMessage = (incByte & (byte)PacketHeader.IsServerMessage) != 0;
            bool isHeartbeatMessage = (incByte & (byte)PacketHeader.IsHeartbeatMessage) != 0;
            
            if (isServerMessage)
            {
                DebugConsole.ThrowError("Got server message from" + senderSteamId.ToString());
                return;
            }

            if (senderSteamId != OwnerSteamID) //sender is remote, handle disconnects and heartbeats
            {
                PendingClient pendingClient = pendingClients.Find(c => c.SteamID == senderSteamId);
                SteamP2PConnection connectedClient = connectedClients.Find(c => c.SteamID == senderSteamId);
                
                pendingClient?.Heartbeat();
                connectedClient?.Heartbeat();

                if (serverSettings.BanList.IsBanned(senderSteamId, out string banReason))
                {
                    if (pendingClient != null)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.Banned, banReason);
                    }
                    else if (connectedClient != null)
                    {
                        Disconnect(connectedClient, DisconnectReason.Banned.ToString() + "/ "+ banReason);
                    }
                    return;
                }
                else if (isDisconnectMessage)
                {
                    if (pendingClient != null)
                    {
                        string disconnectMsg = $"ServerMessage.HasDisconnected~[client]={pendingClient.Name}";
                        RemovePendingClient(pendingClient, DisconnectReason.Unknown, disconnectMsg);
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
                        ReadConnectionInitializationStep(pendingClient, new ReadOnlyMessage(inc.Buffer, false, inc.BytePosition, inc.LengthBytes - inc.BytePosition, null));
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
                    
                    IReadMessage msg = new ReadOnlyMessage(inc.Buffer, isCompressed, inc.BytePosition, length, connectedClient);
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
                        OwnerConnection = new SteamP2PConnection(ownerName, OwnerSteamID)
                        {
                            Status = NetworkConnectionStatus.Connected
                        };

                        OnInitializationComplete?.Invoke(OwnerConnection);
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

                    IReadMessage msg = new ReadOnlyMessage(inc.Buffer, isCompressed, inc.BytePosition, length, OwnerConnection);
                    OnMessageReceived?.Invoke(OwnerConnection, msg);
                }
            }
        }

        private void ReadConnectionInitializationStep(PendingClient pendingClient, IReadMessage inc)
        {
            if (!started) { return; }

            pendingClient.TimeOut = NetworkConnection.TimeoutThreshold;

            ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();

            //DebugConsole.NewMessage(initializationStep+" "+pendingClient.InitializationStep);

            if (pendingClient.InitializationStep != initializationStep) return;

            pendingClient.UpdateTime = Timing.TotalTime+Timing.Step;

            switch (initializationStep)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    string name = Client.SanitizeName(inc.ReadString());
                    UInt64 steamId = inc.ReadUInt64();
                    UInt16 ticketLength = inc.ReadUInt16();
                    inc.BitPosition += ticketLength * 8; //skip ticket, owner handles steam authentication

                    if (!Client.IsValidName(name, serverSettings))
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.InvalidName, "The name \"" + name + "\" is invalid");
                        return;
                    }

                    string version = inc.ReadString();
                    bool isCompatibleVersion = NetworkMember.IsCompatible(version, GameMain.Version.ToString()) ?? false;
                    if (!isCompatibleVersion)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.InvalidVersion,
                                    $"DisconnectMessage.InvalidVersion~[version]={GameMain.Version.ToString()}~[clientversion]={version}");

                        GameServer.Log(name + " (" + pendingClient.SteamID.ToString() + ") couldn't join the server (incompatible game version)", ServerLog.MessageType.Error);
                        DebugConsole.NewMessage(name + " (" + pendingClient.SteamID.ToString() + ") couldn't join the server (incompatible game version)", Microsoft.Xna.Framework.Color.Red);
                        return;
                    }

                    int contentPackageCount = (int)inc.ReadVariableUInt32();
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
                        GameServer.Log(name + " (" + pendingClient.SteamID + ") couldn't join the server (missing content package " + GetPackageStr(missingPackages[0]) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    else if (missingPackages.Count > 1)
                    {
                        List<string> packageStrs = new List<string>();
                        missingPackages.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                        RemovePendingClient(pendingClient, DisconnectReason.MissingContentPackage,
                            $"DisconnectMessage.MissingContentPackages~[missingcontentpackages]={string.Join(", ", packageStrs)}");
                        GameServer.Log(name + " (" + pendingClient.SteamID + ") couldn't join the server (missing content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    if (redundantPackages.Count == 1)
                    {
                        RemovePendingClient(pendingClient, DisconnectReason.IncompatibleContentPackage,
                            $"DisconnectMessage.IncompatibleContentPackage~[incompatiblecontentpackage]={GetPackageStr(redundantPackages[0])}");
                        GameServer.Log(name + " (" + pendingClient.SteamID + ") couldn't join the server (using an incompatible content package " + GetPackageStr(redundantPackages[0]) + ")", ServerLog.MessageType.Error);
                        return;
                    }
                    if (redundantPackages.Count > 1)
                    {
                        List<string> packageStrs = new List<string>();
                        redundantPackages.ForEach(cp => packageStrs.Add(GetPackageStr(cp)));
                        RemovePendingClient(pendingClient, DisconnectReason.IncompatibleContentPackage,
                            $"DisconnectMessage.IncompatibleContentPackages~[incompatiblecontentpackages]={string.Join(", ", packageStrs)}");
                        GameServer.Log(name + " (" + pendingClient.SteamID + ") couldn't join the server (using incompatible content packages " + string.Join(", ", packageStrs) + ")", ServerLog.MessageType.Error);
                        return;
                    }

                    if (!pendingClient.AuthSessionStarted)
                    {
                        pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;

                        pendingClient.Name = name;
                        pendingClient.AuthSessionStarted = true;
                    }
                    break;
                case ConnectionInitialization.Password:
                    int pwLength = inc.ReadByte();
                    byte[] incPassword = inc.ReadBytes(pwLength);
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
                            serverSettings.BanList.BanPlayer(pendingClient.Name, pendingClient.SteamID, banMsg, null);

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


        private void UpdatePendingClient(PendingClient pendingClient)
        {
            if (!started) { return; }

            if (serverSettings.BanList.IsBanned(pendingClient.SteamID, out string banReason))
            {
                RemovePendingClient(pendingClient, DisconnectReason.Banned, banReason);
                return;
            }

            //DebugConsole.NewMessage("pending client status: " + pendingClient.InitializationStep);

            if (connectedClients.Count >= serverSettings.MaxPlayers - 1)
            {
                RemovePendingClient(pendingClient, DisconnectReason.ServerFull, "");
            }

            if (pendingClient.InitializationStep == ConnectionInitialization.Success)
            {
                SteamP2PConnection newConnection = new SteamP2PConnection(pendingClient.Name, pendingClient.SteamID)
                {
                    Status = NetworkConnectionStatus.Connected
                };
                connectedClients.Add(newConnection);
                pendingClients.Remove(pendingClient);
                OnInitializationComplete?.Invoke(newConnection);
            }

            pendingClient.TimeOut -= Timing.Step;
            if (pendingClient.TimeOut < 0.0)
            {
                RemovePendingClient(pendingClient, DisconnectReason.Unknown, Lidgren.Network.NetConnection.NoResponseMessage);
            }

            if (Timing.TotalTime < pendingClient.UpdateTime) { return; }
            pendingClient.UpdateTime = Timing.TotalTime + 1.0;
            
            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write(pendingClient.SteamID);
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)(PacketHeader.IsConnectionInitializationStep |
                                PacketHeader.IsServerMessage));
            outMsg.Write((byte)pendingClient.InitializationStep);
            switch (pendingClient.InitializationStep)
            {
                case ConnectionInitialization.ContentPackageOrder:
                    var mpContentPackages = GameMain.SelectedPackages.Where(cp => cp.HasMultiplayerIncompatibleContent).ToList();
                    outMsg.WriteVariableUInt32((UInt32)mpContentPackages.Count);
                    for (int i = 0; i < mpContentPackages.Count; i++)
                    {
                        outMsg.Write(mpContentPackages[i].MD5hash.Hash);
                    }
                    break;
                case ConnectionInitialization.Password:
                    outMsg.Write(pendingClient.PasswordSalt == null); outMsg.WritePadBits();
                    if (pendingClient.PasswordSalt == null)
                    {
                        pendingClient.PasswordSalt = Lidgren.Network.CryptoRandom.Instance.Next();
                        outMsg.Write(pendingClient.PasswordSalt.Value);
                    }
                    else
                    {
                        outMsg.Write(pendingClient.Retries);
                    }
                    break;
            }

            byte[] msgToSend = (byte[])outMsg.Buffer.Clone();
            Array.Resize(ref msgToSend, outMsg.LengthBytes);
            ChildServerRelay.Write(msgToSend);
        }

        private void RemovePendingClient(PendingClient pendingClient, DisconnectReason reason, string msg)
        {
            if (!started) { return; }

            if (pendingClients.Contains(pendingClient))
            {
                SendDisconnectMessage(pendingClient.SteamID, reason + "/" + msg);

                pendingClients.Remove(pendingClient);

                if (pendingClient.AuthSessionStarted)
                {
                    Steam.SteamManager.StopAuthSession(pendingClient.SteamID);
                    pendingClient.SteamID = 0;
                    pendingClient.AuthSessionStarted = false;
                }
            }
        }

        public override void InitializeSteamServerCallbacks()
        {
            throw new InvalidOperationException("Called InitializeSteamServerCallbacks on SteamP2PServerPeer!");
        }
        
        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod)
        {
            if (!started) { return; }

            if (!(conn is SteamP2PConnection steamp2pConn)) return;
            if (!connectedClients.Contains(steamp2pConn) && conn != OwnerConnection)
            {
                DebugConsole.ThrowError("Tried to send message to unauthenticated connection: " + steamp2pConn.SteamID.ToString());
                return;
            }

            IWriteMessage msgToSend = new WriteOnlyMessage();
            byte[] msgData = new byte[msg.LengthBytes];
            msg.PrepareForSending(ref msgData, out bool isCompressed, out int length);
            msgToSend.Write(conn.SteamID);
            msgToSend.Write((byte)deliveryMethod);
            msgToSend.Write((byte)((isCompressed ? PacketHeader.IsCompressed : PacketHeader.None) | PacketHeader.IsServerMessage));
            msgToSend.Write((UInt16)length);
            msgToSend.Write(msgData, 0, length);

            byte[] bufToSend = (byte[])msgToSend.Buffer.Clone();
            Array.Resize(ref bufToSend, msgToSend.LengthBytes);
            ChildServerRelay.Write(bufToSend);
        }

        private void SendDisconnectMessage(UInt64 steamId, string msg)
        {
            if (!started) { return; }
            if (string.IsNullOrWhiteSpace(msg)) { return; }

            IWriteMessage msgToSend = new WriteOnlyMessage();
            msgToSend.Write(steamId);
            msgToSend.Write((byte)DeliveryMethod.Reliable);
            msgToSend.Write((byte)(PacketHeader.IsDisconnectMessage | PacketHeader.IsServerMessage));
            msgToSend.Write(msg);

            byte[] bufToSend = (byte[])msgToSend.Buffer.Clone();
            Array.Resize(ref bufToSend, msgToSend.LengthBytes);
            ChildServerRelay.Write(bufToSend);
        }

        private void Disconnect(NetworkConnection conn, string msg, bool sendDisconnectMessage)
        {
            if (!started) { return; }

            if (!(conn is SteamP2PConnection steamp2pConn)) { return; }
            if (connectedClients.Contains(steamp2pConn))
            {
                if (sendDisconnectMessage) SendDisconnectMessage(steamp2pConn.SteamID, msg);
                steamp2pConn.Status = NetworkConnectionStatus.Disconnected;
                connectedClients.Remove(steamp2pConn);
                OnDisconnect?.Invoke(conn, msg);
                Steam.SteamManager.StopAuthSession(conn.SteamID);
            }
            else if (steamp2pConn == OwnerConnection)
            {
                //TODO: fix?
            }
        }

        public override void Disconnect(NetworkConnection conn, string msg = null)
        {
            Disconnect(conn, msg, true);
        }
    }
}
