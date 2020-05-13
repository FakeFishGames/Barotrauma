using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Barotrauma.Steam;
using System.Threading;

namespace Barotrauma.Networking
{
    class SteamP2PClientPeer : ClientPeer
    {
        private bool isActive;
        private UInt64 hostSteamId;
        private ConnectionInitialization initializationStep;
        private bool contentPackageOrderReceived;
        private int passwordSalt;
        private Steamworks.AuthTicket steamAuthTicket;
        private double timeout;
        private double heartbeatTimer;

        private long sentBytes, receivedBytes;

        private List<IReadMessage> incomingInitializationMessages;
        private List<IReadMessage> incomingDataMessages;

        public SteamP2PClientPeer(string name)
        {
            ServerConnection = null;

            Name = name;

            isActive = false;
        }

        public override void Start(object endPoint, int ownerKey)
        {
            contentPackageOrderReceived = false;

            steamAuthTicket = SteamManager.GetAuthSessionTicket();
            //TODO: wait for GetAuthSessionTicketResponse_t

            if (steamAuthTicket == null)
            {
                throw new Exception("GetAuthSessionTicket returned null");
            }

            if (!(endPoint is UInt64 steamIdEndpoint))
            {
                throw new InvalidCastException("endPoint is not UInt64");
            }

            hostSteamId = steamIdEndpoint;

            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamNetworking.OnP2PSessionRequest = OnIncomingConnection;

            ServerConnection = new SteamP2PConnection("Server", hostSteamId);

            incomingInitializationMessages = new List<IReadMessage>();
            incomingDataMessages = new List<IReadMessage>();

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
            outMsg.Write((byte)ConnectionInitialization.ConnectionStarted);

            Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
            sentBytes += outMsg.LengthBytes;

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            timeout = NetworkConnection.TimeoutThreshold;
            heartbeatTimer = 1.0;

            isActive = true;
        }

        private void OnIncomingConnection(Steamworks.SteamId steamId)
        {
            if (!isActive) { return; }
            if (steamId == hostSteamId) { Steamworks.SteamNetworking.AcceptP2PSessionWithUser(steamId); }
        }

        private void OnP2PData(ulong steamId, byte[] data, int dataLength, int channel)
        {
            if (!isActive) { return; }
            if (steamId != hostSteamId) { return; }

            timeout = Screen.Selected == GameMain.GameScreen ?
                NetworkConnection.TimeoutThresholdInGame :
                NetworkConnection.TimeoutThreshold;

            byte incByte = data[0];
            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;
            bool isDisconnectMessage = (incByte & (byte)PacketHeader.IsDisconnectMessage) != 0;
            bool isServerMessage = (incByte & (byte)PacketHeader.IsServerMessage) != 0;
            bool isHeartbeatMessage = (incByte & (byte)PacketHeader.IsHeartbeatMessage) != 0;

            if (!isServerMessage) { return; }

            if (isConnectionInitializationStep)
            {
                ulong low = Lidgren.Network.NetBitWriter.ReadUInt32(data, 32, 8);
                ulong high = Lidgren.Network.NetBitWriter.ReadUInt32(data, 32, 8 + 32);
                ulong lobbyId = low + (high << 32);

                Steam.SteamManager.JoinLobby(lobbyId, false);
                IReadMessage inc = new ReadOnlyMessage(data, false, 1 + 8, dataLength - 9, ServerConnection);
                if (initializationStep != ConnectionInitialization.Success)
                {
                    incomingInitializationMessages.Add(inc);
                }
            }
            else if (isHeartbeatMessage)
            {
                return; //TODO: implement heartbeats
            }
            else if (isDisconnectMessage)
            {
                IReadMessage inc = new ReadOnlyMessage(data, false, 1, dataLength - 1, ServerConnection);
                string msg = inc.ReadString();
                Close(msg);
                OnDisconnectMessageReceived?.Invoke(msg);
            }
            else
            {
                UInt16 length = data[1];
                length |= (UInt16)(((UInt32)data[2]) << 8);

                IReadMessage inc = new ReadOnlyMessage(data, isCompressed, 3, length, ServerConnection);
                incomingDataMessages.Add(inc);
            }
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            timeout -= deltaTime;
            heartbeatTimer -= deltaTime;

            for (int i = 0; i < 100; i++)
            {
                if (!Steamworks.SteamNetworking.IsP2PPacketAvailable()) { break; }
                var packet = Steamworks.SteamNetworking.ReadP2PPacket();
                if (packet.HasValue)
                {
                    OnP2PData(packet?.SteamId ?? 0, packet?.Data, packet?.Data.Length ?? 0, 0);
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

                Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Unreliable);
                sentBytes += outMsg.LengthBytes;

                heartbeatTimer = 5.0;
            }

            if (timeout < 0.0)
            {
                Close("Timed out");
                OnDisconnectMessageReceived?.Invoke("");
                return;
            }

            if (initializationStep != ConnectionInitialization.Success)
            {
                if (incomingDataMessages.Count > 0)
                {
                    OnInitializationComplete?.Invoke();
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
                    OnMessageReceived?.Invoke(inc);
                }
            }

            incomingInitializationMessages.Clear();
            incomingDataMessages.Clear();
        }

        private void ReadConnectionInitializationStep(IReadMessage inc)
        {
            if (!isActive) { return; }

            ConnectionInitialization step = (ConnectionInitialization)inc.ReadByte();

            IWriteMessage outMsg;

            //DebugConsole.NewMessage(step + " " + initializationStep);
            switch (step)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }
                    outMsg = new WriteOnlyMessage();
                    outMsg.Write((byte)DeliveryMethod.Reliable);
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.SteamTicketAndVersion);
                    outMsg.Write(Name);
                    outMsg.Write(SteamManager.GetSteamID());
                    outMsg.Write((UInt16)steamAuthTicket.Data.Length);
                    outMsg.Write(steamAuthTicket.Data, 0, steamAuthTicket.Data.Length);

                    outMsg.Write(GameMain.Version.ToString());

                    IEnumerable<ContentPackage> mpContentPackages = GameMain.SelectedPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);
                    outMsg.WriteVariableUInt32((UInt32)mpContentPackages.Count());
                    foreach (ContentPackage contentPackage in mpContentPackages)
                    {
                        outMsg.Write(contentPackage.Name);
                        outMsg.Write(contentPackage.MD5hash.Hash);
                    }

                    heartbeatTimer = 5.0;
                    Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
                    sentBytes += outMsg.LengthBytes;
                    break;
                case ConnectionInitialization.ContentPackageOrder:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion ||
                        initializationStep == ConnectionInitialization.Password) { initializationStep = ConnectionInitialization.ContentPackageOrder; }
                    if (initializationStep != ConnectionInitialization.ContentPackageOrder) { return; }
                    outMsg = new WriteOnlyMessage();
                    outMsg.Write((byte)DeliveryMethod.Reliable);
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.ContentPackageOrder);

                    UInt32 cpCount = inc.ReadVariableUInt32();
                    List<ContentPackage> serverContentPackages = new List<ContentPackage>();
                    for (int i = 0; i < cpCount; i++)
                    {
                        string hash = inc.ReadString();
                        serverContentPackages.Add(GameMain.Config.SelectedContentPackages.Find(cp => cp.MD5hash.Hash == hash));
                    }

                    if (!contentPackageOrderReceived)
                    {
                        GameMain.Config.ReorderSelectedContentPackages(cp => serverContentPackages.Contains(cp) ?
                                                                             serverContentPackages.IndexOf(cp) :
                                                                             serverContentPackages.Count + GameMain.Config.SelectedContentPackages.IndexOf(cp));
                        contentPackageOrderReceived = true;
                    }

                    Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
                    sentBytes += outMsg.LengthBytes;
                    break;
                case ConnectionInitialization.Password:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion) { initializationStep = ConnectionInitialization.Password; }
                    if (initializationStep != ConnectionInitialization.Password) { return; }
                    bool incomingSalt = inc.ReadBoolean(); inc.ReadPadBits();
                    int retries = 0;
                    if (incomingSalt)
                    {
                        passwordSalt = inc.ReadInt32();
                    }
                    else
                    {
                        retries = inc.ReadInt32();
                    }
                    OnRequestPassword?.Invoke(passwordSalt, retries);
                    break;
            }
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            if (!isActive) { return; }

            byte[] buf = new byte[msg.LengthBytes + 4];
            buf[0] = (byte)deliveryMethod;

            byte[] bufAux = new byte[msg.LengthBytes];
            bool isCompressed; int length;
            msg.PrepareForSending(ref bufAux, out isCompressed, out length);

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
            CoroutineManager.InvokeAfter(() =>
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
            bool successSend = Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, buf, length + 4, 0, sendType);
            sentBytes += length + 4;
            if (!successSend)
            {
                if (sendType != Steamworks.P2PSend.Reliable)
                {
                    DebugConsole.Log("WARNING: message couldn't be sent unreliably, forcing reliable send (" + length.ToString() + " bytes)");
                    sendType = Steamworks.P2PSend.Reliable;
                    successSend = Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, buf, length + 4, 0, sendType);
                    sentBytes += length + 4;
                }
                if (!successSend)
                {
                    DebugConsole.ThrowError("Failed to send message to remote peer! (" + length.ToString() + " bytes)");
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
            Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
            sentBytes += outMsg.LengthBytes;
        }
        
        public override void Close(string msg = null)
        {
            if (!isActive) { return; }

            SteamManager.LeaveLobby();

            isActive = false;

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)PacketHeader.IsDisconnectMessage);
            outMsg.Write(msg ?? "Disconnected");

            Steamworks.SteamNetworking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
            sentBytes += outMsg.LengthBytes;

            Thread.Sleep(100);

            Steamworks.SteamNetworking.ResetActions();

            Steamworks.SteamNetworking.CloseP2PSessionWithUser(hostSteamId);

            steamAuthTicket?.Cancel(); steamAuthTicket = null;
            hostSteamId = 0;

            OnDisconnect?.Invoke();
        }

        ~SteamP2PClientPeer()
        {
            OnDisconnect = null;
            Close();
        }

#if DEBUG
        public override void ForceTimeOut()
        {
            timeout = 0.0f;
        }
#endif
    }
}
