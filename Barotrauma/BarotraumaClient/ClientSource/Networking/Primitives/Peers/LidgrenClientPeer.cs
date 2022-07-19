using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Lidgren.Network;
using Barotrauma.Steam;
using System.Linq;

namespace Barotrauma.Networking
{
    class LidgrenClientPeer : ClientPeer
    {
        private bool isActive;
        private NetClient netClient;
        private NetPeerConfiguration netPeerConfiguration;

        List<NetIncomingMessage> incomingLidgrenMessages;

        public LidgrenClientPeer(string name)
        {
            ServerConnection = null;

            Name = name;

            netClient = null;
            isActive = false;
        }

        public override void Start(object endPoint, int ownerKey)
        {
            if (isActive) { return; }

            this.ownerKey = ownerKey;

            contentPackageOrderReceived = false;

            netPeerConfiguration = new NetPeerConfiguration("barotrauma")
            {
                UseDualModeSockets = GameSettings.CurrentConfig.UseDualModeSockets
            };

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
            ServerConnection = new LidgrenConnection("Server", netClient.Connect(ipEndPoint), 0)
            {
                Status = NetworkConnectionStatus.Connected
            };

            isActive = true;
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            if (ownerKey != 0 && (ChildServerRelay.Process?.HasExited ?? true))
            {
                Close();
                var msgBox = new GUIMessageBox(TextManager.Get("ConnectionLost"), ChildServerRelay.CrashMessage);
                msgBox.Buttons[0].OnClicked += (btn, obj) => { GameMain.MainMenuScreen.Select(); return false; };
                return;
            }

            incomingLidgrenMessages.Clear();
            netClient.ReadMessages(incomingLidgrenMessages);

            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.ReceivedBytes, netClient.Statistics.ReceivedBytes);
            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.SentBytes, netClient.Statistics.SentBytes);

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
        }

        private void HandleDataMessage(NetIncomingMessage inc)
        {
            if (!isActive) { return; }

            PacketHeader packetHeader = (PacketHeader)inc.ReadByte();

            if (packetHeader.IsConnectionInitializationStep() && initializationStep != ConnectionInitialization.Success)
            {
                ReadConnectionInitializationStep(new ReadWriteMessage(inc.Data, (int)inc.Position, inc.LengthBits, false));
            }
            else
            {
                if (initializationStep != ConnectionInitialization.Success)
                {
                    OnInitializationComplete?.Invoke();
                    initializationStep = ConnectionInitialization.Success;
                }
                UInt16 length = inc.ReadUInt16();
                IReadMessage msg = new ReadOnlyMessage(inc.Data, packetHeader.IsCompressed(), inc.PositionInBytes, length, ServerConnection);
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
                    OnDisconnectMessageReceived?.Invoke(disconnectMsg);
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
            byte[] saltedPw = ServerSettings.SaltPassword(Encoding.UTF8.GetBytes(password), passwordSalt);
            outMsg.Write((byte)saltedPw.Length);
            outMsg.Write(saltedPw, 0, saltedPw.Length);
            NetSendResult result = netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
            if (result != NetSendResult.Queued && result != NetSendResult.Sent)
            {
                DebugConsole.NewMessage("Failed to send " + initializationStep.ToString() + " message to host: " + result);
            }
        }

        public override void Close(string msg = null, bool disableReconnect = false)
        {
            if (!isActive) { return; }

            isActive = false;

            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting").Value);
            netClient = null;
            steamAuthTicket?.Cancel(); steamAuthTicket = null;
            OnDisconnect?.Invoke(disableReconnect);
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
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

#if DEBUG
            netPeerConfiguration.SimulatedDuplicatesChance = GameMain.Client.SimulatedDuplicatesChance;
            netPeerConfiguration.SimulatedMinimumLatency = GameMain.Client.SimulatedMinimumLatency;
            netPeerConfiguration.SimulatedRandomLatency = GameMain.Client.SimulatedRandomLatency;
            netPeerConfiguration.SimulatedLoss = GameMain.Client.SimulatedLoss;
#endif

            NetOutgoingMessage lidgrenMsg = netClient.CreateMessage();
            byte[] msgData = new byte[msg.LengthBytes];
            msg.PrepareForSending(ref msgData, compressPastThreshold, out bool isCompressed, out int length);
            lidgrenMsg.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            NetSendResult result = netClient.SendMessage(lidgrenMsg, lidgrenDeliveryMethod);
            if (result != NetSendResult.Queued && result != NetSendResult.Sent)
            {
                DebugConsole.NewMessage("Failed to send message to host: " + result);
            }
        }

        protected override void SendMsgInternal(DeliveryMethod deliveryMethod, IWriteMessage msg)
        {
            NetOutgoingMessage lidgrenMsg = netClient.CreateMessage();
            lidgrenMsg.Write(msg.Buffer, 0, msg.LengthBytes);

            NetSendResult result = netClient.SendMessage(lidgrenMsg, NetDeliveryMethod.ReliableUnordered);
            if (result != NetSendResult.Queued && result != NetSendResult.Sent)
            {
                DebugConsole.NewMessage("Failed to send message to host: " + result + "\n" + Environment.StackTrace);
            }
        }

#if DEBUG
        public override void ForceTimeOut()
        {
            netClient?.ServerConnection?.ForceTimeOut();
        }
#endif
    }
}
