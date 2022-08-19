using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Barotrauma.Networking
{
    class SteamP2PClientPeer : ClientPeer
    {
        private bool isActive;
        private readonly SteamId hostSteamId;
        private double timeout;
        private double heartbeatTimer;
        private double connectionStatusTimer;

        private long sentBytes, receivedBytes;

        private List<IReadMessage> incomingInitializationMessages;
        private List<IReadMessage> incomingDataMessages;

        public SteamP2PClientPeer(SteamP2PEndpoint endpoint, Callbacks callbacks) : base(endpoint, callbacks, Option<int>.None())
        {
            ServerConnection = null;

            isActive = false;
            
            if (!(ServerEndpoint is SteamP2PEndpoint steamIdEndpoint))
            {
                throw new InvalidCastException("endPoint is not SteamId");
            }
            
            hostSteamId = steamIdEndpoint.SteamId;
        }

        public override void Start()
        {
            ContentPackageOrderReceived = false;

            steamAuthTicket = SteamManager.GetAuthSessionTicket();
            //TODO: wait for GetAuthSessionTicketResponse_t

            if (steamAuthTicket == null)
            {
                throw new Exception("GetAuthSessionTicket returned null");
            }

            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamNetworking.OnP2PSessionRequest = OnIncomingConnection;
            Steamworks.SteamNetworking.OnP2PConnectionFailed = OnConnectionFailed;

            Steamworks.SteamNetworking.AllowP2PPacketRelay(true);

            ServerConnection = new SteamP2PConnection(hostSteamId);
            ServerConnection.SetAccountInfo(new AccountInfo(hostSteamId));

            incomingInitializationMessages = new List<IReadMessage>();
            incomingDataMessages = new List<IReadMessage>();

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
            outMsg.Write((byte)ConnectionInitialization.ConnectionStarted);

            Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
            sentBytes += outMsg.LengthBytes;

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            timeout = NetworkConnection.TimeoutThreshold;
            heartbeatTimer = 1.0;
            connectionStatusTimer = 0.0;

            isActive = true;
        }

        private void OnIncomingConnection(Steamworks.SteamId steamId)
        {
            if (!isActive) { return; }
            if (steamId == hostSteamId.Value)
            {
                Steamworks.SteamNetworking.AcceptP2PSessionWithUser(steamId);
            }
            else if (initializationStep != ConnectionInitialization.Password &&
                     initializationStep != ConnectionInitialization.ContentPackageOrder &&
                     initializationStep != ConnectionInitialization.Success)
            {
                DebugConsole.ThrowError($"Connection from incorrect SteamID was rejected: "+
                    $"expected {hostSteamId}," +
                    $"got {new SteamId(steamId)}");
            }
        }

        private void OnConnectionFailed(Steamworks.SteamId steamId, Steamworks.P2PSessionError error)
        {
            if (!isActive) { return; }
            if (steamId != hostSteamId.Value) { return; }
            Close($"SteamP2P connection failed: {error}");
            callbacks.OnDisconnectMessageReceived.Invoke($"{DisconnectReason.SteamP2PError}/SteamP2P connection failed: {error}");
        }

        private void OnP2PData(ulong steamId, byte[] data, int dataLength)
        {
            if (!isActive) { return; }
            if (steamId != hostSteamId.Value) { return; }

            timeout = Screen.Selected == GameMain.GameScreen ?
                NetworkConnection.TimeoutThresholdInGame :
                NetworkConnection.TimeoutThreshold;
            
            PacketHeader packetHeader = (PacketHeader)data[0];

            if (!packetHeader.IsServerMessage()) { return; }

            if (packetHeader.IsConnectionInitializationStep())
            {
                ulong low = Lidgren.Network.NetBitWriter.ReadUInt32(data, 32, 8);
                ulong high = Lidgren.Network.NetBitWriter.ReadUInt32(data, 32, 8 + 32);
                ulong lobbyId = low + (high << 32);

                Steam.SteamManager.JoinLobby(lobbyId, false);
                IReadMessage inc = new ReadOnlyMessage(data, false, 1 + 8, dataLength - (1 + 8), ServerConnection);
                if (initializationStep != ConnectionInitialization.Success)
                {
                    incomingInitializationMessages.Add(inc);
                }
            }
            else if (packetHeader.IsHeartbeatMessage())
            {
                return; //TODO: implement heartbeats
            }
            else if (packetHeader.IsDisconnectMessage())
            {
                IReadMessage inc = new ReadOnlyMessage(data, false, 1, dataLength - 1, ServerConnection);
                string msg = inc.ReadString();
                Close(msg);
                callbacks.OnDisconnectMessageReceived.Invoke(msg);
            }
            else
            {
                UInt16 length = Lidgren.Network.NetBitWriter.ReadUInt16(data, 16, 8);

                IReadMessage inc = new ReadOnlyMessage(data, packetHeader.IsCompressed(), 3, length, ServerConnection);
                incomingDataMessages.Add(inc);
            }
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            if (GameMain.Client == null || !GameMain.Client.RoundStarting)
            {
                timeout -= deltaTime;
            }
            heartbeatTimer -= deltaTime;

            if (initializationStep != ConnectionInitialization.Password &&
                initializationStep != ConnectionInitialization.ContentPackageOrder &&
                initializationStep != ConnectionInitialization.Success)
            {
                connectionStatusTimer -= deltaTime;
                if (connectionStatusTimer <= 0.0)
                {
                    var state = Steamworks.SteamNetworking.GetP2PSessionState(hostSteamId.Value);
                    if (state == null)
                    {
                        Close("SteamP2P connection could not be established");
                        callbacks.OnDisconnectMessageReceived.Invoke(DisconnectReason.SteamP2PError.ToString());
                    }
                    else
                    {
                        if (state?.P2PSessionError != Steamworks.P2PSessionError.None)
                        {
                            Close($"SteamP2P error code: {state?.P2PSessionError}");
                            callbacks.OnDisconnectMessageReceived.Invoke($"{DisconnectReason.SteamP2PError}/SteamP2P error code: {state?.P2PSessionError}");
                        }
                    }
                    connectionStatusTimer = 1.0f;
                }
            }

            for (int i = 0; i < 100; i++)
            {
                if (!Steamworks.SteamNetworking.IsP2PPacketAvailable()) { break; }
                var packet = Steamworks.SteamNetworking.ReadP2PPacket();
                if (packet.HasValue)
                {
                    OnP2PData(packet?.SteamId ?? 0, packet?.Data, packet?.Data.Length ?? 0);
                    receivedBytes += packet?.Data.Length ?? 0;
                }
            }

            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.ReceivedBytes, receivedBytes);
            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.SentBytes, sentBytes);

            if (heartbeatTimer < 0.0)
            {
                IWriteMessage outMsg = new WriteOnlyMessage();
                outMsg.Write((byte)DeliveryMethod.Unreliable);
                outMsg.Write((byte)PacketHeader.IsHeartbeatMessage);

                Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Unreliable);
                sentBytes += outMsg.LengthBytes;

                heartbeatTimer = 5.0;
            }

            if (timeout < 0.0)
            {
                Close("Timed out");
                callbacks.OnDisconnectMessageReceived.Invoke(DisconnectReason.SteamP2PTimeOut.ToString());
                return;
            }

            if (initializationStep != ConnectionInitialization.Success)
            {
                if (incomingDataMessages.Count > 0)
                {
                    var incomingMessage = incomingDataMessages.First();
                    byte incomingHeader = incomingMessage.LengthBytes > 0 ? incomingMessage.PeekByte() : (byte)0;
                    if (ContentPackageOrderReceived)
                    {
#warning: TODO: do not allow completing initialization until content package order has been received?
                        string errorMsg = $"Error during connection initialization: completed initialization before receiving content package order. Incoming header: {incomingHeader}";
                        GameAnalyticsManager.AddErrorEventOnce("SteamP2PClientPeer.OnInitializationComplete:ContentPackageOrderNotReceived", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                        DebugConsole.ThrowError(errorMsg);
                    }
                    if (ServerContentPackages.Length == 0)
                    {
                        string errorMsg = $"Error during connection initialization: list of content packages enabled on the server was empty when completing initialization. Incoming header: {incomingHeader}";
                        GameAnalyticsManager.AddErrorEventOnce("SteamP2PClientPeer.OnInitializationComplete:NoContentPackages", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                        DebugConsole.ThrowError(errorMsg);
                    }
                    callbacks.OnInitializationComplete.Invoke();
                    initializationStep = ConnectionInitialization.Success;
                }
                else
                {
                    foreach (IReadMessage inc in incomingInitializationMessages)
                    {
                        ReadConnectionInitializationStep(inc);
                    }
                }
            }

            if (initializationStep == ConnectionInitialization.Success)
            {
                foreach (IReadMessage inc in incomingDataMessages)
                {
                    callbacks.OnMessageReceived.Invoke(inc);
                }
            }

            incomingInitializationMessages.Clear();
            incomingDataMessages.Clear();
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!isActive) { return; }

            byte[] buf = new byte[msg.LengthBytes + 4];
            buf[0] = (byte)deliveryMethod;

            byte[] bufAux = new byte[msg.LengthBytes];
            msg.PrepareForSending(ref bufAux, compressPastThreshold, out bool isCompressed, out int length);

            buf[1] = (byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None);

            buf[2] = (byte)(length & 0xff);
            buf[3] = (byte)((length >> 8) & 0xff);

            Array.Copy(bufAux, 0, buf, 4, length);

            Steamworks.P2PSend sendType;
            switch (deliveryMethod)
            {
                case DeliveryMethod.Reliable:
                case DeliveryMethod.ReliableOrdered:
                    //the documentation seems to suggest that the Reliable send type
                    //enforces packet order (TODO: verify)
                    sendType = Steamworks.P2PSend.Reliable;
                    break;
                default:
                    sendType = Steamworks.P2PSend.Unreliable;
                    break;
            }

            if (length + 8 >= MsgConstants.MTU)
            {
                DebugConsole.Log("WARNING: message length comes close to exceeding MTU, forcing reliable send (" + length.ToString() + " bytes)");
                sendType = Steamworks.P2PSend.Reliable;
            }

            heartbeatTimer = 5.0;

#if DEBUG
            CoroutineManager.Invoke(() =>
            {
                if (GameMain.Client == null) { return; }
                if (Rand.Range(0.0f, 1.0f) < GameMain.Client.SimulatedLoss && sendType != Steamworks.P2PSend.Reliable) { return; }
                int count = Rand.Range(0.0f, 1.0f) < GameMain.Client.SimulatedDuplicatesChance ? 2 : 1;
                for (int i = 0; i < count; i++)
                {
                    Send(buf, length + 4, sendType);
                }
            },
            GameMain.Client.SimulatedMinimumLatency + Rand.Range(0.0f, GameMain.Client.SimulatedRandomLatency));
#else
            Send(buf, length + 4, sendType);
#endif
        }

        private void Send(byte[] buf, int length, Steamworks.P2PSend sendType)
        {
            bool successSend = Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, buf, length + 4, 0, sendType);
            sentBytes += length + 4;
            if (!successSend)
            {
                if (sendType != Steamworks.P2PSend.Reliable)
                {
                    DebugConsole.Log("WARNING: message couldn't be sent unreliably, forcing reliable send (" + length.ToString() + " bytes)");
                    sendType = Steamworks.P2PSend.Reliable;
                    successSend = Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, buf, length + 4, 0, sendType);
                    sentBytes += length + 4;
                }
                if (!successSend)
                {
                    DebugConsole.AddWarning("Failed to send message to remote peer! (" + length.ToString() + " bytes)");
                }
            }
        }

        public override void SendPassword(string password)
        {
            if (!isActive) { return; }

            if (initializationStep != ConnectionInitialization.Password) { return; }
            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
            outMsg.Write((byte)ConnectionInitialization.Password);
            byte[] saltedPw = ServerSettings.SaltPassword(Encoding.UTF8.GetBytes(password), passwordSalt);
            outMsg.Write((byte)saltedPw.Length);
            outMsg.Write(saltedPw, 0, saltedPw.Length);

            heartbeatTimer = 5.0;
            Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
            sentBytes += outMsg.LengthBytes;
        }
        
        public override void Close(string msg = null, bool disableReconnect = false)
        {
            if (!isActive) { return; }

            SteamManager.LeaveLobby();

            isActive = false;

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)PacketHeader.IsDisconnectMessage);
            outMsg.Write(msg ?? "Disconnected");

            try
            {
                Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
                sentBytes += outMsg.LengthBytes;
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to send a disconnect message to the server using SteamP2P.", e);
            }

            Thread.Sleep(100);

            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamNetworking.CloseP2PSessionWithUser(hostSteamId.Value);

            steamAuthTicket?.Cancel(); steamAuthTicket = null;

            callbacks.OnDisconnect.Invoke(disableReconnect);
        }

        protected override void SendMsgInternal(DeliveryMethod deliveryMethod, IWriteMessage msg)
        {
            Steamworks.P2PSend sendType;
            switch (deliveryMethod)
            {
                case DeliveryMethod.Reliable:
                case DeliveryMethod.ReliableOrdered:
                    //the documentation seems to suggest that the Reliable send type
                    //enforces packet order (TODO: verify)
                    sendType = Steamworks.P2PSend.Reliable;
                    break;
                default:
                    sendType = Steamworks.P2PSend.Unreliable;
                    break;
            }

            IWriteMessage msgToSend = new WriteOnlyMessage();
            msgToSend.Write((byte)deliveryMethod);
            msgToSend.Write(msg.Buffer, 0, msg.LengthBytes);

            heartbeatTimer = 5.0;
            Steamworks.SteamNetworking.SendP2PPacket(hostSteamId.Value, msgToSend.Buffer, msgToSend.LengthBytes, 0, sendType);
            sentBytes += msg.LengthBytes;
        }

#if DEBUG
        public override void ForceTimeOut()
        {
            timeout = 0.0f;
        }
#endif
    }
}
