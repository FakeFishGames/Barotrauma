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
        }

        public override void Start(object endPoint)
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

            timeout = 20.0;
            heartbeatTimer = 1.0;
        }

        private bool OnIncomingConnection(UInt64 steamId)
        {
            return steamId == hostSteamId;
        }

        private void OnP2PData(ulong steamId, byte[] data, int dataLength, int channel)
        {
            if (steamId != hostSteamId) { return; }

            timeout = 20.0;

            byte incByte = data[0];
            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;
            bool isDisconnectMessage = (incByte & (byte)PacketHeader.IsDisconnectMessage) != 0;
            bool isServerMessage = (incByte & (byte)PacketHeader.IsServerMessage) != 0;
            bool isHeartbeatMessage = (incByte & (byte)PacketHeader.IsHeartbeatMessage) != 0;
            
            if (!isServerMessage) { return; }

            if (isConnectionInitializationStep)
            {
                IReadMessage inc = new ReadOnlyMessage(data, false, 1, dataLength - 1, ServerConnection);
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
            }
            else
            {
                UInt16 length = data[1];
                length |= (UInt16)(((UInt32)data[2]) << 8);

                IReadMessage inc = new ReadOnlyMessage(data, isCompressed, 3, length, ServerConnection);
                incomingDataMessages.Add(inc);
            }
        }

        public override void Update()
        {
            timeout -= Timing.Step;
            heartbeatTimer -= Timing.Step;

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
                Close(Lidgren.Network.NetConnection.NoResponseMessage);
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

                    IEnumerable<ContentPackage> mpContentPackages = GameMain.SelectedPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);
                    outMsg.Write7BitEncoded((UInt64)mpContentPackages.Count());
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
            byte[] buf = new byte[msg.LengthBytes + 4];
            buf[0] = (byte)deliveryMethod;

            byte[] bufAux = new byte[msg.LengthBytes];
            bool isCompressed; int length;
            msg.PrepareForSending(bufAux, out isCompressed, out length);

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

            heartbeatTimer = 5.0;
            SteamManager.Instance.Networking.SendP2PPacket(hostSteamId, buf, length + 4, sendType);
        }

        public override void SendPassword(string password)
        {
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
            OnDisconnect?.Invoke(msg);
            hostSteamId = 0;
        }
    }
}
