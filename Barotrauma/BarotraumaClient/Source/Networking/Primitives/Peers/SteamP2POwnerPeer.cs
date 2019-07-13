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
    class SteamP2POwnerPeer : ClientPeer
    {
        private NetClient netClient;
        private NetPeerConfiguration netPeerConfiguration;

        private ConnectionInitialization initializationStep;
        private UInt64 steamID;
        List<NetIncomingMessage> incomingLidgrenMessages;

        public SteamP2POwnerPeer(string name)
        {
            ServerConnection = null;

            Name = name;

            netClient = null;

            steamID = Steam.SteamManager.GetSteamID();
        }

        public override void Start(object endPoint)
        {
            if (netClient != null) { return; }

            netPeerConfiguration = new NetPeerConfiguration("barotrauma");

            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage | NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error);

            netClient = new NetClient(netPeerConfiguration);

            incomingLidgrenMessages = new List<NetIncomingMessage>();

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Loopback, Steam.SteamManager.STEAMP2P_OWNER_PORT);

            netClient.Start();
            ServerConnection = new LidgrenConnection("Server", netClient.Connect(ipEndPoint), 0);
        }

        public override void Update()
        {
            if (netClient == null) { return; }

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
            if (netClient == null) { return; }

            UInt64 recipientSteamId = inc.ReadUInt64();
            byte incByte = inc.ReadByte();

            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;
            bool isDisconnectMessage = (incByte & (byte)PacketHeader.IsDisconnectMessage) != 0;
            bool isServerMessage = (incByte & (byte)PacketHeader.IsServerMessage) != 0;
            bool isHeartbeatMessage = (incByte & (byte)PacketHeader.IsHeartbeatMessage) != 0;

            if (recipientSteamId != steamID)
            {
                throw new NotImplementedException();
            }
            else
            {
                if (isDisconnectMessage)
                {
                    DebugConsole.ThrowError("Received disconnect message from owned server");
                    return;
                }
                if (!isServerMessage)
                {
                    DebugConsole.ThrowError("Received non-server message from owned server");
                    return;
                }
                if (isHeartbeatMessage)
                {
                    return; //TODO: implement timeout?
                }
                if (isConnectionInitializationStep)
                {
                    NetOutgoingMessage outMsg = netClient.CreateMessage();
                    outMsg.Write(steamID);
                    outMsg.Write((byte)(PacketHeader.IsConnectionInitializationStep));
                    outMsg.Write(Name);
                    netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);

                    return;
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

                    return;
                }
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            if (netClient == null) { return; }

            NetConnectionStatus status = (NetConnectionStatus)inc.ReadByte();
            switch (status)
            {
                case NetConnectionStatus.Disconnected:
                    string disconnectMsg = inc.ReadString();
                    Close(disconnectMsg);
                    break;
            }
        }
        
        public override void SendPassword(string password)
        {
            return; //owner doesn't send passwords
        }

        public override void Close(string msg = null)
        {
            if (netClient == null) { return; }

            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting"));
            OnDisconnect?.Invoke(msg);
            netClient = null;
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            if (netClient == null) { return; }

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
            lidgrenMsg.Write(steamID);
            lidgrenMsg.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            netClient.SendMessage(lidgrenMsg, lidgrenDeliveryMethod);
        }
    }
}
