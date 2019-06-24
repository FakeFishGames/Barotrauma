using System;
using System.Collections.Generic;
using System.Text;
using Lidgren.Network;
using System.Net;

namespace Barotrauma.Networking
{
    class LidgrenClientPeer : ClientPeer
    {
        private NetClient netClient;
        private NetPeerConfiguration netPeerConfiguration;

        public LidgrenClientPeer()
        {
            netPeerConfiguration = new NetPeerConfiguration("barotrauma");

            netPeerConfiguration.DisableMessageType(NetIncomingMessageType.DebugMessage | NetIncomingMessageType.WarningMessage | NetIncomingMessageType.Receipt
                | NetIncomingMessageType.ErrorMessage | NetIncomingMessageType.Error);

            netClient = new NetClient(netPeerConfiguration);

            ServerConnection = null;
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
            ServerConnection = new LidgrenConnection(netClient.Connect(ipEndPoint), 0);
        }

        public override void Close(string msg=null)
        {
            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting"));
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod)
        {
            throw new NotImplementedException();
        }
    }
}
