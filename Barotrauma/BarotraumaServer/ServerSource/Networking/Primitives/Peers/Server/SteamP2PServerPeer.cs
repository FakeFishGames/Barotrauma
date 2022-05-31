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

        public UInt64 OwnerSteamID
        {
            get;
            private set;
        }

        private UInt64 ownerKey64 => unchecked((UInt64)ownerKey.Value);
        
        private UInt64 ReadSteamId(IReadMessage inc)
            => inc.ReadUInt64() ^ ownerKey64;
        private void WriteSteamId(IWriteMessage msg, UInt64 val)
            => msg.Write(val ^ ownerKey64);

        public SteamP2PServerPeer(UInt64 steamId, int ownerKey, ServerSettings settings)
        {
            serverSettings = settings;

            connectedClients = new List<NetworkConnection>();
            pendingClients = new List<PendingClient>();

            this.ownerKey = ownerKey;

            OwnerSteamID = steamId;

            started = false;
        }

        public override void Start()
        {
            IWriteMessage outMsg = new WriteOnlyMessage();
            WriteSteamId(outMsg, OwnerSteamID);
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
                SteamP2PConnection conn = connectedClients[i] as SteamP2PConnection;
                conn.Decay(deltaTime);
                if (conn.Timeout < 0.0)
                {
                    Disconnect(conn, "Timed out");
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
                string errorMsg = "Server failed to read an incoming message. {" + e + "}\n" + e.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("SteamP2PServerPeer.Update:ClientReadException" + e.TargetSite.ToString(), GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                if (GameSettings.CurrentConfig.VerboseLogging) { DebugConsole.ThrowError(errorMsg); }
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

            UInt64 senderSteamId = ReadSteamId(inc);
            UInt64 ownerSteamId = ReadSteamId(inc);

            PacketHeader packetHeader = (PacketHeader)inc.ReadByte();
            
            if (packetHeader.IsServerMessage())
            {
                DebugConsole.ThrowError("Got server message from" + senderSteamId.ToString());
                return;
            }

            if (senderSteamId != OwnerSteamID) //sender is remote, handle disconnects and heartbeats
            {
                PendingClient pendingClient = pendingClients.Find(c => c.SteamID == senderSteamId);
                SteamP2PConnection connectedClient = connectedClients.Find(c => c.SteamID == senderSteamId) as SteamP2PConnection;
                
                pendingClient?.Heartbeat();
                connectedClient?.Heartbeat();

                string banReason;
                if (serverSettings.BanList.IsBanned(senderSteamId, out banReason) ||
                    serverSettings.BanList.IsBanned(ownerSteamId, out banReason))
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
                else if (packetHeader.IsDisconnectMessage())
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
                else if (packetHeader.IsHeartbeatMessage())
                {
                    //message exists solely as a heartbeat, ignore its contents
                    return;
                }
                else if (packetHeader.IsConnectionInitializationStep())
                {

                    if (pendingClient != null)
                    {
                        if (ownerSteamId != 0)
                        {
                            pendingClient.Connection.SetOwnerSteamIDIfUnknown(ownerSteamId);
                        }
                        ReadConnectionInitializationStep(pendingClient, new ReadOnlyMessage(inc.Buffer, false, inc.BytePosition, inc.LengthBytes - inc.BytePosition, null));
                    }
                    else
                    {
                        ConnectionInitialization initializationStep = (ConnectionInitialization)inc.ReadByte();
                        if (initializationStep == ConnectionInitialization.ConnectionStarted)
                        {
                            pendingClients.Add(new PendingClient(new SteamP2PConnection("PENDING", senderSteamId)) { SteamID = senderSteamId });
                        }
                    }
                }
                else if (connectedClient != null)
                {
                    UInt16 length = inc.ReadUInt16();
                    
                    IReadMessage msg = new ReadOnlyMessage(inc.Buffer, packetHeader.IsCompressed(), inc.BytePosition, length, connectedClient);
                    OnMessageReceived?.Invoke(connectedClient, msg);
                }
            }
            else //sender is owner
            {
                if (OwnerConnection != null) { (OwnerConnection as SteamP2PConnection).Heartbeat(); }

                if (packetHeader.IsDisconnectMessage())
                {
                    DebugConsole.ThrowError("Received disconnect message from owner");
                    return;
                }
                if (packetHeader.IsServerMessage())
                {
                    DebugConsole.ThrowError("Received server message from owner");
                    return;
                }
                if (packetHeader.IsConnectionInitializationStep())
                {
                    if (OwnerConnection == null)
                    {
                        string ownerName = inc.ReadString();
                        OwnerConnection = new SteamP2PConnection(ownerName, OwnerSteamID)
                        {
                            Language = GameSettings.CurrentConfig.Language
                        };
                        OwnerConnection.SetOwnerSteamIDIfUnknown(OwnerSteamID);

                        OnInitializationComplete?.Invoke(OwnerConnection);
                    }
                    return;
                }
                if (packetHeader.IsHeartbeatMessage())
                {
                    return;
                }
                else
                {
                    UInt16 length = inc.ReadUInt16();

                    IReadMessage msg = new ReadOnlyMessage(inc.Buffer, packetHeader.IsCompressed(), inc.BytePosition, length, OwnerConnection);
                    OnMessageReceived?.Invoke(OwnerConnection, msg);
                }
            }
        }

        public override void InitializeSteamServerCallbacks()
        {
            throw new InvalidOperationException("Called InitializeSteamServerCallbacks on SteamP2PServerPeer!");
        }
        
        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!started) { return; }

            if (!(conn is SteamP2PConnection steamp2pConn)) return;
            if (!connectedClients.Contains(steamp2pConn) && conn != OwnerConnection)
            {
                DebugConsole.ThrowError("Tried to send message to unauthenticated connection: " + steamp2pConn.SteamID.ToString());
                return;
            }

            IWriteMessage msgToSend = new WriteOnlyMessage();
            byte[] msgData = new byte[16];
            msg.PrepareForSending(ref msgData, compressPastThreshold, out bool isCompressed, out int length);
            WriteSteamId(msgToSend, conn.SteamID);
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
            WriteSteamId(msgToSend, steamId);
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
            if (sendDisconnectMessage) { SendDisconnectMessage(steamp2pConn.SteamID, msg); }
            if (connectedClients.Contains(steamp2pConn))
            {
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

        protected override void SendMsgInternal(NetworkConnection conn, DeliveryMethod deliveryMethod, IWriteMessage msg)
        {
            IWriteMessage msgToSend = new WriteOnlyMessage();
            WriteSteamId(msgToSend, conn.SteamID);
            msgToSend.Write((byte)deliveryMethod);
            msgToSend.Write(msg.Buffer, 0, msg.LengthBytes);
            byte[] bufToSend = (byte[])msgToSend.Buffer.Clone();
            Array.Resize(ref bufToSend, msgToSend.LengthBytes);
            ChildServerRelay.Write(bufToSend);
        }

        protected override void ProcessAuthTicket(string name, int ownKey, ulong steamId, PendingClient pendingClient, byte[] ticket)
        {
            pendingClient.InitializationStep = serverSettings.HasPassword ? ConnectionInitialization.Password : ConnectionInitialization.ContentPackageOrder;

            pendingClient.Connection.Name = name;
            pendingClient.Name = name;
            pendingClient.AuthSessionStarted = true;
        }
    }
}
