using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Barotrauma.Steam;
using Facepunch.Steamworks;
using System.Threading;

namespace Barotrauma.Networking
{
    class SteamP2PClientPeer : ClientPeer
    {
        private bool isActive;
        private UInt64 hostSteamId;
        private ConnectionInitialization initializationStep;
        private int passwordSalt;
        private Auth.Ticket steamAuthTicket;
        private double timeout;
        private double heartbeatTimer;

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

            Steam.SteamManager.Instance.Networking.OnIncomingConnection = OnIncomingConnection;
            Steam.SteamManager.Instance.Networking.OnP2PData = OnP2PData;
            Steam.SteamManager.Instance.Networking.SetListenChannel(0, true);

            ServerConnection = new SteamP2PConnection("Server", hostSteamId);

            incomingInitializationMessages = new List<IReadMessage>();
            incomingDataMessages = new List<IReadMessage>();

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write((byte)DeliveryMethod.Reliable);
            outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
            outMsg.Write((byte)ConnectionInitialization.ConnectionStarted);

            SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes,
                                                           Facepunch.Steamworks.Networking.SendType.Reliable);

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            timeout = NetworkConnection.TimeoutThreshold;
            heartbeatTimer = 1.0;

            isActive = true;
        }

        private bool OnIncomingConnection(UInt64 steamId)
        {
            if (!isActive) { return false; }
            return steamId == hostSteamId;
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
                ulong high = Lidgren.Network.NetBitWriter.ReadUInt32(data, 32, 8+32);
                ulong lobbyId = low + (high << 32);

                Steam.SteamManager.JoinLobby(lobbyId, false);
                IReadMessage inc = new ReadOnlyMessage(data, false, 1+8, dataLength - 9, ServerConnection);
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

            if (heartbeatTimer < 0.0)
            {
                IWriteMessage outMsg = new WriteOnlyMessage();
                outMsg.Write((byte)DeliveryMethod.Unreliable);
                outMsg.Write((byte)PacketHeader.IsHeartbeatMessage);

                SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes,
                                                                       Facepunch.Steamworks.Networking.SendType.Unreliable);

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
            //DebugConsole.NewMessage(step + " " + initializationStep);
            switch (step)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }
                    IWriteMessage outMsg = new WriteOnlyMessage();
                    outMsg.Write((byte)DeliveryMethod.Reliable);
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.SteamTicketAndVersion);
                    outMsg.Write(Name);
                    outMsg.Write(SteamManager.GetSteamID());
                    outMsg.Write((UInt16)steamAuthTicket.Data.Length);
                    outMsg.Write(steamAuthTicket.Data, 0, steamAuthTicket.Data.Length);

                    outMsg.Write(GameMain.Version.ToString());

                    IEnumerable<ContentPackage> mpContentPackages = GameMain.SelectedPackages.Where(cp => cp.HasMultiplayerIncompatibleContent && !cp.NeedsRestart);
                    outMsg.WriteVariableUInt32((UInt32)mpContentPackages.Count());
                    foreach (ContentPackage contentPackage in mpContentPackages)
                    {
                        outMsg.Write(contentPackage.Name);
                        outMsg.Write(contentPackage.MD5hash.Hash);
                    }

                    heartbeatTimer = 5.0;
                    SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes,
                                                                   Facepunch.Steamworks.Networking.SendType.Reliable);
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

            Facepunch.Steamworks.Networking.SendType sendType;
            switch (deliveryMethod)
            {
                case DeliveryMethod.Reliable:
                case DeliveryMethod.ReliableOrdered:
                    //the documentation seems to suggest that the Reliable send type
                    //enforces packet order (TODO: verify)
                    sendType = Facepunch.Steamworks.Networking.SendType.Reliable;
                    break;
                default:
                    sendType = Facepunch.Steamworks.Networking.SendType.Unreliable;
                    break;
            }

            if (length + 8 >= MsgConstants.MTU)
            {
                DebugConsole.Log("WARNING: message length comes close to exceeding MTU, forcing reliable send (" + length.ToString() + " bytes)");
                sendType = Facepunch.Steamworks.Networking.SendType.Reliable;
            }

            heartbeatTimer = 5.0;

#if DEBUG
            CoroutineManager.InvokeAfter(() =>
            {
                if (GameMain.Client == null || Rand.Range(0.0f, 1.0f) < GameMain.Client.SimulatedLoss && sendType != Facepunch.Steamworks.Networking.SendType.Reliable) { return; }
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

        private void Send(byte[] buf, int length, Facepunch.Steamworks.Networking.SendType sendType)
        {
            bool successSend = SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, buf, length + 4, sendType);
            if (!successSend)
            {
                if (sendType != Facepunch.Steamworks.Networking.SendType.Reliable)
                {
                    DebugConsole.Log("WARNING: message couldn't be sent unreliably, forcing reliable send (" + length.ToString() + " bytes)");
                    sendType = Facepunch.Steamworks.Networking.SendType.Reliable;
                    successSend = Steam.SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, buf, length + 4, sendType);
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
            byte[] saltedPw = ServerSettings.SaltPassword(Lidgren.Network.NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password)), passwordSalt);
            outMsg.Write((byte)saltedPw.Length);
            outMsg.Write(saltedPw, 0, saltedPw.Length);

            heartbeatTimer = 5.0;
            SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes,
                                                                   Facepunch.Steamworks.Networking.SendType.Reliable);
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

            SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, outMsg.Buffer, outMsg.LengthBytes,
                                                                   Facepunch.Steamworks.Networking.SendType.Reliable);

            Thread.Sleep(100);

            Steam.SteamManager.Instance.Networking.OnIncomingConnection = null;
            Steam.SteamManager.Instance.Networking.OnP2PData = null;
            Steam.SteamManager.Instance.Networking.SetListenChannel(0, false);

            Steam.SteamManager.Instance.Networking.CloseSession(hostSteamId);

            steamAuthTicket?.Cancel(); steamAuthTicket = null;
            hostSteamId = 0;

            OnDisconnect?.Invoke();
        }
    }
}
