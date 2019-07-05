using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Lidgren.Network;
using Facepunch.Steamworks;
using Barotrauma.Steam;

namespace Barotrauma.Networking
{
    class LidgrenClientPeer : ClientPeer
    {
        private NetClient netClient;
        private NetPeerConfiguration netPeerConfiguration;

        private ConnectionInitialization initializationStep;
        private int passwordNonce;
        private Auth.Ticket steamAuthTicket;
        List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenClientPeer(string name)
        {
            netPeerConfiguration = new NetPeerConfiguration("barotrauma");

            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage | NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error);

            netClient = new NetClient(netPeerConfiguration);

            ServerConnection = null;

            steamAuthTicket = SteamManager.GetAuthSessionTicket();
            //TODO: wait for GetAuthSessionTicketResponse_t

            if (steamAuthTicket == null)
            {
                throw new Exception("GetAuthSessionTicket returned null");
            }

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            Name = name;
        }

        public override void Start(object endPoint)
        {
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
        }

        public override void Update()
        {
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
            byte incByte = inc.ReadByte();
            bool isCompressed = (incByte & 0x1) != 0;
            bool isConnectionValidationStep = (incByte & 0x2) != 0;
            if (isConnectionValidationStep && initializationStep != ConnectionInitialization.Success)
            {
                ReadConnectionInitializationStep(inc);
            }
            else
            {
                if (initializationStep != ConnectionInitialization.Success)
                {
                    OnInitializationComplete();
                }
                UInt16 length = inc.ReadUInt16();
                IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressed, inc.PositionInBytes, length, ServerConnection);
                OnMessageReceived?.Invoke(msg);
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            NetConnectionStatus status = (NetConnectionStatus)inc.ReadByte();
            switch (status)
            {
                case NetConnectionStatus.Disconnected:
                    string disconnectMsg = inc.ReadString();
                    steamAuthTicket?.Cancel(); steamAuthTicket = null;
                    OnStatusChanged?.Invoke(ConnectionStatus.Disconnected, disconnectMsg);
                    break;
            }
        }

        private void ReadConnectionInitializationStep(NetIncomingMessage inc)
        {
            ConnectionInitialization step = (ConnectionInitialization)inc.ReadByte();
            switch (step)
            {
                case ConnectionInitialization.SteamTicket:
                    if (initializationStep != ConnectionInitialization.SteamTicket) { return; }
                    NetOutgoingMessage outMsg = netClient.CreateMessage();
                    outMsg.Write(0x2);
                    outMsg.Write((byte)ConnectionInitialization.SteamTicket);
                    outMsg.Write(Name);
                    outMsg.Write(SteamManager.GetSteamID());
                    outMsg.Write((UInt16)steamAuthTicket.Data.Length);
                    outMsg.Write(steamAuthTicket.Data, 0, steamAuthTicket.Data.Length);
                    netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
                    break;
                case ConnectionInitialization.Password:
                    if (initializationStep == ConnectionInitialization.SteamTicket) { initializationStep = ConnectionInitialization.Password; }
                    if (initializationStep != ConnectionInitialization.Password) { return; }
                    bool incomingNonce = inc.ReadBoolean(); inc.ReadPadBits();
                    int retries = 0;
                    if (incomingNonce)
                    {
                        passwordNonce = inc.ReadInt32();
                    }
                    else
                    {
                        retries = inc.ReadInt32();
                    }
                    OnRequestPassword?.Invoke(passwordNonce, retries);
                    break;
            }
        }

        private void SendPassword(string password, int nonce)
        {
            if (initializationStep != ConnectionInitialization.Password) { return; }
            NetOutgoingMessage outMsg = netClient.CreateMessage();
            outMsg.Write(0x2);
            outMsg.Write((byte)ConnectionInitialization.Password);
            string saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(password)));
            saltedPw = saltedPw + Convert.ToString(nonce);
            saltedPw = Encoding.UTF8.GetString(NetUtility.ComputeSHAHash(Encoding.UTF8.GetBytes(saltedPw)));
            outMsg.Write(saltedPw);
            netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
        }

        public override void Close(string msg=null)
        {
            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting"));
            steamAuthTicket?.Cancel(); steamAuthTicket = null;
            OnStatusChanged?.Invoke(ConnectionStatus.Disconnected, null);
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
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
            lidgrenMsg.Write((byte)(isCompressed ? 0x1 : 0x0));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            netClient.SendMessage(lidgrenMsg, lidgrenDeliveryMethod);
        }
    }
}
