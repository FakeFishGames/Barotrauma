using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Lidgren.Network;
using Facepunch.Steamworks;
using Barotrauma.Steam;
using System.Linq;
using System.Threading;

namespace Barotrauma.Networking
{
    class SteamP2POwnerPeer : ClientPeer
    {
        private NetClient netClient;
        private NetPeerConfiguration netPeerConfiguration;

        private ConnectionInitialization initializationStep;
        private UInt64 selfSteamID;
        List<NetIncomingMessage> incomingLidgrenMessages;

        class RemotePeer
        {
            public UInt64 SteamID;
            public double? DisconnectTime;

            public RemotePeer(UInt64 steamId)
            {
                SteamID = steamId;
                DisconnectTime = null;
            }
        }
        List<RemotePeer> remotePeers;

        public SteamP2POwnerPeer(string name)
        {
            ServerConnection = null;

            Name = name;

            netClient = null;

            selfSteamID = Steam.SteamManager.GetSteamID();
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
            ServerConnection.Status = NetworkConnectionStatus.Connected;

            remotePeers = new List<RemotePeer>();

            Steam.SteamManager.Instance.Networking.OnIncomingConnection = OnIncomingConnection;
            Steam.SteamManager.Instance.Networking.OnP2PData = OnP2PData;
            Steam.SteamManager.Instance.Networking.SetListenChannel(0, true);
        }

        private bool OnIncomingConnection(UInt64 steamId)
        {
            if (!remotePeers.Any(p => p.SteamID == steamId))
            {
                remotePeers.Add(new RemotePeer(steamId));
            }

            return true; //accept all connections, the server will figure things out later
        }

        private void OnP2PData(ulong steamId, byte[] data, int dataLength, int channel)
        {
            if (!remotePeers.Any(p => p.SteamID == steamId))
            {
                return;
            }

            NetOutgoingMessage outMsg = netClient.CreateMessage();
            outMsg.Write(steamId);
            outMsg.Write(data, 1, dataLength - 1);

            NetDeliveryMethod lidgrenDeliveryMethod = NetDeliveryMethod.Unreliable;
            switch ((DeliveryMethod)data[0])
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

            netClient.SendMessage(outMsg, lidgrenDeliveryMethod);
        }

        public override void Update()
        {
            if (netClient == null) { return; }

            for (int i=remotePeers.Count-1;i>=0;i--)
            {
                if (remotePeers[i].DisconnectTime != null && remotePeers[i].DisconnectTime < Timing.TotalTime)
                {
                    ClosePeerSession(remotePeers[i]);
                }
            }

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

            int p2pDataStart = inc.PositionInBytes;

            byte incByte = inc.ReadByte();

            bool isCompressed = (incByte & (byte)PacketHeader.IsCompressed) != 0;
            bool isConnectionInitializationStep = (incByte & (byte)PacketHeader.IsConnectionInitializationStep) != 0;
            bool isDisconnectMessage = (incByte & (byte)PacketHeader.IsDisconnectMessage) != 0;
            bool isServerMessage = (incByte & (byte)PacketHeader.IsServerMessage) != 0;
            bool isHeartbeatMessage = (incByte & (byte)PacketHeader.IsHeartbeatMessage) != 0;

            if (recipientSteamId != selfSteamID)
            {
                if (!isServerMessage)
                {
                    DebugConsole.ThrowError("Received non-server message meant for remote peer");
                    return;
                }

                RemotePeer peer = remotePeers.Find(p => p.SteamID == recipientSteamId);
                
                if (peer == null) { return; }

                if (isDisconnectMessage)
                {
                    DisconnectPeer(peer, inc.ReadString());
                    return;
                }

                Facepunch.Steamworks.Networking.SendType sendType;
                switch (inc.DeliveryMethod)
                {
                    case NetDeliveryMethod.ReliableUnordered:
                    case NetDeliveryMethod.ReliableSequenced:
                    case NetDeliveryMethod.ReliableOrdered:
                        //the documentation seems to suggest that the Reliable send type
                        //enforces packet order (TODO: verify)
                        sendType = Facepunch.Steamworks.Networking.SendType.Reliable;
                        break;
                    default:
                        sendType = Facepunch.Steamworks.Networking.SendType.Unreliable;
                        break;
                }

                byte[] p2pData = new byte[inc.LengthBytes - p2pDataStart];
                Array.Copy(inc.Data, p2pDataStart, p2pData, 0, p2pData.Length);

                bool successSend = Steam.SteamManager.Instance.Networking.SendP2PPacket(recipientSteamId, p2pData, p2pData.Length, sendType);
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
                    return; //timeout is handled by Lidgren, ignore this message
                }
                if (isConnectionInitializationStep)
                {
                    NetOutgoingMessage outMsg = netClient.CreateMessage();
                    outMsg.Write(selfSteamID);
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

        private void DisconnectPeer(RemotePeer peer, string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
            {
                if (peer.DisconnectTime != null)
                {
                    peer.DisconnectTime = Timing.TotalTime + 1.0;
                }

                IWriteMessage outMsg = new WriteOnlyMessage();
                outMsg.Write((byte)(PacketHeader.IsServerMessage | PacketHeader.IsDisconnectMessage));
                outMsg.Write(msg);

                Steam.SteamManager.Instance.Networking.SendP2PPacket(peer.SteamID, outMsg.Buffer, outMsg.LengthBytes,
                                                                     Facepunch.Steamworks.Networking.SendType.Reliable);
            }
            else
            {
                ClosePeerSession(peer);
            }
        }

        private void ClosePeerSession(RemotePeer peer)
        {
            Steam.SteamManager.Instance.Networking.CloseSession(peer.SteamID);
            remotePeers.Remove(peer);
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

            for (int i=remotePeers.Count-1;i>=0;i--)
            {
                DisconnectPeer(remotePeers[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            Thread.Sleep(100);

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                ClosePeerSession(remotePeers[i]);
            }

            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting"));
            OnDisconnect?.Invoke(msg);
            netClient = null;

            Steam.SteamManager.Instance.Networking.OnIncomingConnection = null;
            Steam.SteamManager.Instance.Networking.OnP2PData = null;
            Steam.SteamManager.Instance.Networking.SetListenChannel(0, false);
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
            lidgrenMsg.Write(selfSteamID);
            lidgrenMsg.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            netClient.SendMessage(lidgrenMsg, lidgrenDeliveryMethod);
        }
    }
}
