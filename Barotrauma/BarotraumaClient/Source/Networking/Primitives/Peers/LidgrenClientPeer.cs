using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Lidgren.Network;
using Facepunch.Steamworks;
using Barotrauma.Steam;
using System.Linq;

namespace Barotrauma.Networking
{
    class LidgrenClientPeer : ClientPeer
    {
        private bool isActive;
        private NetClient netClient;
        private NetPeerConfiguration netPeerConfiguration;

        private ConnectionInitialization initializationStep;
        private int passwordSalt;
        private Auth.Ticket steamAuthTicket;
        List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenClientPeer(string name)
        {
            ServerConnection = null;

            Name = name;

            netClient = null;
            isActive = false;
        }

        public override void Start(object endPoint)
        {
            if (isActive) { return; }

            netPeerConfiguration = new NetPeerConfiguration("barotrauma");

            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage | NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error);

            netClient = new NetClient(netPeerConfiguration);

            if (SteamManager.IsInitialized)
            {
                steamAuthTicket = SteamManager.GetAuthSessionTicket();
                //TODO: wait for GetAuthSessionTicketResponse_t

                if (steamAuthTicket == null)
                {
                    throw new Exception("GetAuthSessionTicket returned null");
                }
            }

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            if (!(endPoint is IPEndPoint ipEndPoint))
            {
                throw new InvalidCastException("endPoint is not IPEndPoint");
            }
            if (ServerConnection != null)
            {
                throw new InvalidOperationException("ServerConnection is not null");
            }

            netClient.Start();
            ServerConnection = new LidgrenConnection("Server", netClient.Connect(ipEndPoint), 0);
            ServerConnection.Status = NetworkConnectionStatus.Connected;

            isActive = true;
        }

        public override void Update()
        {
            if (!isActive) { return; }

            netClient.ReadMessages(incomingLidgrenMessages);

            foreach (NetIncomingMessage inc in incomingLidgrenMessages)
            {
                if (inc.SenderConnection != (ServerConnection as LidgrenConnection).NetConnection) { continue; }

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

            incomingLidgrenMessages.Clear();
        }

        private void HandleDataMessage(NetIncomingMessage inc)
        {
            if (!isActive) { return; }

            byte incByte = inc.ReadByte();
            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;

            //DebugConsole.NewMessage(isCompressed + " " + isConnectionInitializationStep + " " + (int)incByte);

            if (isConnectionInitializationStep && initializationStep != ConnectionInitialization.Success)
            {
                ReadConnectionInitializationStep(inc);
            }
            else
            {
                if (initializationStep != ConnectionInitialization.Success)
                {
                    OnInitializationComplete?.Invoke();
                    initializationStep = ConnectionInitialization.Success;
                }
                UInt16 length = inc.ReadUInt16();
                IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressed, inc.PositionInBytes, length, ServerConnection);
                OnMessageReceived?.Invoke(msg);
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (!isActive) { return; }

            NetConnectionStatus status = (NetConnectionStatus)inc.ReadByte();
            switch (status)
            {
                case NetConnectionStatus.Disconnected:
                    string disconnectMsg = inc.ReadString();
                    Close(disconnectMsg);
                    break;
            }
        }

        private void ReadConnectionInitializationStep(NetIncomingMessage inc)
        {
            if (!isActive) { return; }

            ConnectionInitialization step = (ConnectionInitialization)inc.ReadByte();
            //DebugConsole.NewMessage(step + " " + initializationStep);
            switch (step)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }
                    NetOutgoingMessage outMsg = netClient.CreateMessage();
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.SteamTicketAndVersion);
                    outMsg.Write(Name);
                    outMsg.Write(SteamManager.GetSteamID());
                    if (steamAuthTicket == null)
                    {
                        outMsg.Write((UInt16)0);
                    }
                    else
                    {
                        outMsg.Write((UInt16)steamAuthTicket.Data.Length);
                        outMsg.Write(steamAuthTicket.Data, 0, steamAuthTicket.Data.Length);
                    }

                    outMsg.Write(GameMain.Version.ToString());

                    IEnumerable<ContentPackage> mpContentPackages = GameMain.SelectedPackages.Where(cp => cp.HasMultiplayerIncompatibleContent);
                    outMsg.WriteVariableInt32(mpContentPackages.Count());
                    foreach (ContentPackage contentPackage in mpContentPackages)
                    {
                        outMsg.Write(contentPackage.Name);
                        outMsg.Write(contentPackage.MD5hash.Hash);
                    }

                    netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
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

        public override void SendPassword(string password)
        {
            if (!isActive) { return; }

            if (initializationStep != ConnectionInitialization.Password) { return; }
            NetOutgoingMessage outMsg = netClient.CreateMessage();
            outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
            outMsg.Write((byte)ConnectionInitialization.Password);
            byte[] saltedPw = ServerSettings.SaltPassword(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password)), passwordSalt);
            outMsg.Write((byte)saltedPw.Length);
            outMsg.Write(saltedPw, 0, saltedPw.Length);
            netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
        }

        public override void Close(string msg=null)
        {
            if (!isActive) { return; }

            isActive = false;

            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting"));
            netClient = null;
            steamAuthTicket?.Cancel(); steamAuthTicket = null;
            OnDisconnect?.Invoke(msg);
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            if (!isActive) { return; }

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

            NetOutgoingMessage lidgrenMsg = netClient.CreateMessage();
            byte[] msgData = new byte[1500];
            bool isCompressed; int length;
            msg.PrepareForSending(msgData, out isCompressed, out length);
            lidgrenMsg.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            netClient.SendMessage(lidgrenMsg, lidgrenDeliveryMethod);
        }
    }
}
