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

        public LidgrenClientPeer()
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
                IReadMessage msg = new ReadOnlyMessage(inc.Data, isCompressed, 1, inc.LengthBytes - 1, ServerConnection);
                OnMessageReceived?.Invoke(msg);
            }
        }

        private void HandleStatusChanged(NetIncomingMessage inc)
        {
            throw new NotImplementedException();
        }

        private void ReadConnectionInitializationStep(NetIncomingMessage inc)
        {
            throw new NotImplementedException();
        }

        public override void Close(string msg=null)
        {
            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting"));
            OnStatusChanged?.Invoke(ConnectionStatus.Disconnected);
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            throw new NotImplementedException();
        }
    }
}
