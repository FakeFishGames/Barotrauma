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

        private ConnectionInitialization initializationStep;
        private bool contentPackageOrderReceived;
        private int ownerKey;
        private int passwordSalt;
        private Steamworks.AuthTicket steamAuthTicket;
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
            ServerConnection = new LidgrenConnection("Server", netClient.Connect(ipEndPoint), 0)
            {
                Status = NetworkConnectionStatus.Connected
            };

            isActive = true;
        }

        public override void Update(float deltaTime)
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

            //Console.WriteLine(isCompressed + " " + isConnectionInitializationStep + " " + (int)incByte);

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
                    OnDisconnectMessageReceived?.Invoke(disconnectMsg);
                    break;
            }
        }

        private void ReadConnectionInitializationStep(NetIncomingMessage inc)
        {
            if (!isActive) { return; }

            ConnectionInitialization step = (ConnectionInitialization)inc.ReadByte();
            //Console.WriteLine(step + " " + initializationStep);
            NetOutgoingMessage outMsg; NetSendResult result;

            switch (step)
            {
                case ConnectionInitialization.SteamTicketAndVersion:
                    if (initializationStep != ConnectionInitialization.SteamTicketAndVersion) { return; }
                    outMsg = netClient.CreateMessage();
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.SteamTicketAndVersion);
                    outMsg.Write(Name);
                    outMsg.Write(ownerKey);
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

                    result = netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
                    if (result != NetSendResult.Queued && result != NetSendResult.Sent)
                    {
                        DebugConsole.NewMessage("Failed to send "+initializationStep.ToString()+" message to host: " + result);
                    }
                    break;
                case ConnectionInitialization.ContentPackageOrder:
                    if (initializationStep == ConnectionInitialization.SteamTicketAndVersion ||
                        initializationStep == ConnectionInitialization.Password) { initializationStep = ConnectionInitialization.ContentPackageOrder; }
                    if (initializationStep != ConnectionInitialization.ContentPackageOrder) { return; }
                    outMsg = netClient.CreateMessage();
                    outMsg.Write((byte)PacketHeader.IsConnectionInitializationStep);
                    outMsg.Write((byte)ConnectionInitialization.ContentPackageOrder);

                    Int32 cpCount = inc.ReadVariableInt32();
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

                    result = netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
                    if (result != NetSendResult.Queued && result != NetSendResult.Sent)
                    {
                        DebugConsole.NewMessage("Failed to send " + initializationStep.ToString() + " message to host: " + result);
                    }

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
            byte[] saltedPw = ServerSettings.SaltPassword(Encoding.UTF8.GetBytes(password), passwordSalt);
            outMsg.Write((byte)saltedPw.Length);
            outMsg.Write(saltedPw, 0, saltedPw.Length);
            NetSendResult result = netClient.SendMessage(outMsg, NetDeliveryMethod.ReliableUnordered);
            if (result != NetSendResult.Queued && result != NetSendResult.Sent)
            {
                DebugConsole.NewMessage("Failed to send " + initializationStep.ToString() + " message to host: " + result);
            }
        }

        public override void Close(string msg = null)
        {
            if (!isActive) { return; }

            isActive = false;

            netClient.Shutdown(msg ?? TextManager.Get("Disconnecting"));
            netClient = null;
            steamAuthTicket?.Cancel(); steamAuthTicket = null;
            OnDisconnect?.Invoke();
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

#if DEBUG
            netPeerConfiguration.SimulatedDuplicatesChance = GameMain.Client.SimulatedDuplicatesChance;
            netPeerConfiguration.SimulatedMinimumLatency = GameMain.Client.SimulatedMinimumLatency;
            netPeerConfiguration.SimulatedRandomLatency = GameMain.Client.SimulatedRandomLatency;
            netPeerConfiguration.SimulatedLoss = GameMain.Client.SimulatedLoss;
#endif

            NetOutgoingMessage lidgrenMsg = netClient.CreateMessage();
            byte[] msgData = new byte[msg.LengthBytes];
            msg.PrepareForSending(ref msgData, out bool isCompressed, out int length);
            lidgrenMsg.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            lidgrenMsg.Write((UInt16)length);
            lidgrenMsg.Write(msgData, 0, length);

            NetSendResult result = netClient.SendMessage(lidgrenMsg, lidgrenDeliveryMethod);
            if (result != NetSendResult.Queued && result != NetSendResult.Sent)
            {
                DebugConsole.NewMessage("Failed to send message to host: " + result);
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
