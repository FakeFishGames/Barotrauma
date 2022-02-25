using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Barotrauma.Networking
{
    class SteamP2POwnerPeer : ClientPeer
    {
        private bool isActive;

        private readonly UInt64 selfSteamID;

        private long sentBytes, receivedBytes;

        class RemotePeer
        {
            public UInt64 SteamID;
            public UInt64 OwnerSteamID;
            public double? DisconnectTime;
            public bool Authenticating;
            public bool Authenticated;

            public class UnauthedMessage
            {
                public DeliveryMethod DeliveryMethod;
                public IWriteMessage Message;
            }
            public List<UnauthedMessage> UnauthedMessages;

            public RemotePeer(UInt64 steamId)
            {
                SteamID = steamId;
                OwnerSteamID = 0;
                DisconnectTime = null;
                Authenticating = false;
                Authenticated = false;

                UnauthedMessages = new List<UnauthedMessage>();
            }

        }
        List<RemotePeer> remotePeers;

        public SteamP2POwnerPeer(string name)
        {
            ServerConnection = null;

            Name = name;

            isActive = false;

            selfSteamID = Steam.SteamManager.GetSteamID();
        }

        public override void Start(object endPoint, int ownerKey)
        {
            if (isActive) { return; }

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            ServerConnection = new PipeConnection(selfSteamID);
            ServerConnection.Status = NetworkConnectionStatus.Connected;

            remotePeers = new List<RemotePeer>();

            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamNetworking.OnP2PSessionRequest = OnIncomingConnection;
            Steamworks.SteamUser.OnValidateAuthTicketResponse += OnAuthChange;

            Steamworks.SteamNetworking.AllowP2PPacketRelay(true);

            isActive = true;
        }

        private void OnAuthChange(Steamworks.SteamId steamID, Steamworks.SteamId ownerID, Steamworks.AuthResponse status)
        {
            RemotePeer remotePeer = remotePeers.Find(p => p.SteamID == steamID);
            DebugConsole.Log(steamID + " validation: " + status + ", " + (remotePeer != null));

            if (remotePeer == null) { return; }

            if (remotePeer.Authenticated)
            {
                if (status != Steamworks.AuthResponse.OK)
                {
                    DisconnectPeer(remotePeer, DisconnectReason.SteamAuthenticationFailed.ToString() + "/ Steam authentication status changed: " + status.ToString());
                }
                return;
            }

            if (status == Steamworks.AuthResponse.OK)
            {
                remotePeer.OwnerSteamID = ownerID;
                remotePeer.Authenticated = true;
                remotePeer.Authenticating = false;
                foreach (var msg in remotePeer.UnauthedMessages)
                {
                    //rewrite the owner id before
                    //forwarding the messages to
                    //the server, since it's only
                    //known now
                    int prevBitPosition = msg.Message.BitPosition;
                    msg.Message.BitPosition = sizeof(ulong) * 8;
                    msg.Message.Write(ownerID);
                    msg.Message.BitPosition = prevBitPosition;
                    byte[] msgToSend = (byte[])msg.Message.Buffer.Clone();
                    Array.Resize(ref msgToSend, msg.Message.LengthBytes);
                    ChildServerRelay.Write(msgToSend);
                }
                remotePeer.UnauthedMessages.Clear();
            }
            else
            {
                DisconnectPeer(remotePeer, DisconnectReason.SteamAuthenticationFailed.ToString() + "/ Steam authentication failed: " + status.ToString());
                return;
            }
        }

        private void OnIncomingConnection(Steamworks.SteamId steamId)
        {
            if (!isActive) { return; }

            if (!remotePeers.Any(p => p.SteamID == steamId))
            {
                remotePeers.Add(new RemotePeer(steamId));
            }

            Steamworks.SteamNetworking.AcceptP2PSessionWithUser(steamId); //accept all connections, the server will figure things out later
        }

        private void OnP2PData(ulong steamId, byte[] data, int dataLength, int channel)
        {
            if (!isActive) { return; }

            RemotePeer remotePeer = remotePeers.Find(p => p.SteamID == steamId);
            if (remotePeer == null || remotePeer.DisconnectTime != null)
            {
                return;
            }

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.Write(steamId);
            outMsg.Write(remotePeer.OwnerSteamID);
            outMsg.Write(data, 1, dataLength - 1);

            DeliveryMethod deliveryMethod = (DeliveryMethod)data[0];

            PacketHeader packetHeader = (PacketHeader)data[1];

            if (!remotePeer.Authenticated & !remotePeer.Authenticating && packetHeader.IsConnectionInitializationStep())
            {
                remotePeer.DisconnectTime = null;

                IReadMessage authMsg = new ReadOnlyMessage(data, packetHeader.IsCompressed(), 2, dataLength - 2, null);
                ConnectionInitialization initializationStep = (ConnectionInitialization)authMsg.ReadByte();
                if (initializationStep == ConnectionInitialization.SteamTicketAndVersion)
                {
                    remotePeer.Authenticating = true;
                    
                    authMsg.ReadString(); //skip name
                    authMsg.ReadInt32(); //skip owner key
                    authMsg.ReadUInt64(); //skip steamid
                    UInt16 ticketLength = authMsg.ReadUInt16();
                    byte[] ticket = authMsg.ReadBytes(ticketLength);

                    Steamworks.BeginAuthResult authSessionStartState = Steam.SteamManager.StartAuthSession(ticket, steamId);
                    if (authSessionStartState != Steamworks.BeginAuthResult.OK)
                    {
                        DisconnectPeer(remotePeer, DisconnectReason.SteamAuthenticationFailed.ToString() + "/ Steam auth session failed to start: " + authSessionStartState.ToString());
                        return;
                    }
                }
            }

            if (remotePeer.Authenticating)
            {
                remotePeer.UnauthedMessages.Add(new RemotePeer.UnauthedMessage() { DeliveryMethod = deliveryMethod, Message = outMsg });
            }
            else
            {
                byte[] msgToSend = (byte[])outMsg.Buffer.Clone();
                Array.Resize(ref msgToSend, outMsg.LengthBytes);
                ChildServerRelay.Write(msgToSend);
            }
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            if (ChildServerRelay.HasShutDown || (ChildServerRelay.Process?.HasExited ?? true))
            {
                Close();
                var msgBox = new GUIMessageBox(TextManager.Get("ConnectionLost"), TextManager.Get("ServerProcessClosed"));
                msgBox.Buttons[0].OnClicked += (btn, obj) => { GameMain.MainMenuScreen.Select(); return false; };
                return;
            }

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                if (remotePeers[i].DisconnectTime != null && remotePeers[i].DisconnectTime < Timing.TotalTime)
                {
                    ClosePeerSession(remotePeers[i]);
                }
            }

            for (int i = 0; i < 100; i++)
            {
                if (!Steamworks.SteamNetworking.IsP2PPacketAvailable()) { break; }
                var packet = Steamworks.SteamNetworking.ReadP2PPacket();
                if (packet.HasValue)
                {
                    OnP2PData(packet?.SteamId ?? 0, packet?.Data, packet?.Data.Length ?? 0, 0);
                    receivedBytes += packet?.Data.Length ?? 0;
                }
            }

            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.ReceivedBytes, receivedBytes);
            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.SentBytes, sentBytes);

            while (ChildServerRelay.Read(out byte[] incBuf))
            {
                ChildServerRelay.DisposeLocalHandles();
                IReadMessage inc = new ReadOnlyMessage(incBuf, false, 0, incBuf.Length, ServerConnection);
                HandleDataMessage(inc);
            }
        }

        private void HandleDataMessage(IReadMessage inc)
        {
            if (!isActive) { return; }

            UInt64 recipientSteamId = inc.ReadUInt64();
            DeliveryMethod deliveryMethod = (DeliveryMethod)inc.ReadByte();

            int p2pDataStart = inc.BytePosition;

            PacketHeader packetHeader = (PacketHeader)inc.ReadByte();

            if (recipientSteamId != selfSteamID)
            {
                if (!packetHeader.IsServerMessage())
                {
                    DebugConsole.ThrowError("Received non-server message meant for remote peer");
                    return;
                }

                RemotePeer peer = remotePeers.Find(p => p.SteamID == recipientSteamId);
                
                if (peer == null) { return; }

                if (packetHeader.IsDisconnectMessage())
                {
                    DisconnectPeer(peer, inc.ReadString());
                    return;
                }

                Steamworks.P2PSend sendType;
                switch (deliveryMethod)
                {
                    case DeliveryMethod.Reliable:
                    case DeliveryMethod.ReliableOrdered:
                        //the documentation seems to suggest that the
                        //Reliable send type enforces packet order
                        sendType = Steamworks.P2PSend.Reliable;
                        break;
                    default:
                        sendType = Steamworks.P2PSend.Unreliable;
                        break;
                }

                byte[] p2pData;

                if (packetHeader.IsConnectionInitializationStep())
                {
                    p2pData = new byte[inc.LengthBytes - p2pDataStart + 8];
                    p2pData[0] = inc.Buffer[p2pDataStart];
                    Lidgren.Network.NetBitWriter.WriteUInt64(SteamManager.CurrentLobbyID, 8 * 8, p2pData, 1 * 8);
                    Array.Copy(inc.Buffer, p2pDataStart+1, p2pData, 1 + 8, inc.LengthBytes - p2pDataStart - 1);
                }
                else
                {
                    p2pData = new byte[inc.LengthBytes - p2pDataStart];
                    Array.Copy(inc.Buffer, p2pDataStart, p2pData, 0, p2pData.Length);

                    if (!packetHeader.IsHeartbeatMessage() && !packetHeader.IsDisconnectMessage())
                    {
                        UInt16 length = Lidgren.Network.NetBitWriter.ReadUInt16(p2pData, 16, 8);
                        if (length > p2pData.Length - 2)
                        {
                            string errorMsg = $"Length written in message to send to client is larger than buffer size ({length} > {p2pData.Length - 2})";
                            DebugConsole.ThrowError(errorMsg);
                            GameAnalyticsManager.AddErrorEventOnce(
                                "SteamP2POwnerPeerLengthValidationFail",
                                GameAnalyticsManager.ErrorSeverity.Error,
                                errorMsg);
                        }
                    }
                }

                if (p2pData.Length + 4 >= MsgConstants.MTU)
                {
                    DebugConsole.Log("WARNING: message length comes close to exceeding MTU, forcing reliable send (" + p2pData.Length.ToString() + " bytes)");
                    sendType = Steamworks.P2PSend.Reliable;
                }

                bool successSend = Steamworks.SteamNetworking.SendP2PPacket(recipientSteamId, p2pData, p2pData.Length, 0, sendType);
                sentBytes += p2pData.Length;

                if (!successSend)
                {
                    if (sendType != Steamworks.P2PSend.Reliable)
                    {
                        DebugConsole.Log("WARNING: message couldn't be sent unreliably, forcing reliable send (" + p2pData.Length.ToString() + " bytes)");
                        sendType = Steamworks.P2PSend.Reliable;
                        successSend = Steamworks.SteamNetworking.SendP2PPacket(recipientSteamId, p2pData, p2pData.Length, 0, sendType);
                        sentBytes += p2pData.Length;
                    }
                    if (!successSend)
                    {
                        DebugConsole.AddWarning("Failed to send message to remote peer! (" + p2pData.Length.ToString() + " bytes)");
                    }
                }
            }
            else
            {
                if (packetHeader.IsDisconnectMessage())
                {
                    DebugConsole.ThrowError("Received disconnect message from owned server");
                    return;
                }
                if (!packetHeader.IsServerMessage())
                {
                    DebugConsole.ThrowError("Received non-server message from owned server");
                    return;
                }
                if (packetHeader.IsHeartbeatMessage())
                {
                    return; //no timeout since we're using pipes, ignore this message
                }
                if (packetHeader.IsConnectionInitializationStep())
                {
                    IWriteMessage outMsg = new WriteOnlyMessage();
                    outMsg.Write(selfSteamID);
                    outMsg.Write(selfSteamID);
                    outMsg.Write((byte)(PacketHeader.IsConnectionInitializationStep));
                    outMsg.Write(Name);

                    byte[] msgToSend = (byte[])outMsg.Buffer.Clone();
                    Array.Resize(ref msgToSend, outMsg.LengthBytes);
                    ChildServerRelay.Write(msgToSend);
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
                    IReadMessage msg = new ReadOnlyMessage(inc.Buffer, packetHeader.IsCompressed(), inc.BytePosition, length, ServerConnection);
                    OnMessageReceived?.Invoke(msg);

                    return;
                }
            }
        }

        private void DisconnectPeer(RemotePeer peer, string msg)
        {
            if (!string.IsNullOrWhiteSpace(msg))
            {
                if (peer.DisconnectTime == null)
                {
                    peer.DisconnectTime = Timing.TotalTime + 1.0;
                }

                IWriteMessage outMsg = new WriteOnlyMessage();
                outMsg.Write((byte)(PacketHeader.IsServerMessage | PacketHeader.IsDisconnectMessage));
                outMsg.Write(msg);

                Steamworks.SteamNetworking.SendP2PPacket(peer.SteamID, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
                sentBytes += outMsg.LengthBytes;
            }
            else
            {
                ClosePeerSession(peer);
            }
        }

        private void ClosePeerSession(RemotePeer peer)
        {
            Steamworks.SteamNetworking.CloseP2PSessionWithUser(peer.SteamID);
            remotePeers.Remove(peer);
        }

        public override void SendPassword(string password)
        {
            return; //owner doesn't send passwords
        }

        public override void Close(string msg = null, bool disableReconnect = false)
        {
            if (!isActive) { return; }

            isActive = false;

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                DisconnectPeer(remotePeers[i], msg ?? DisconnectReason.ServerShutdown.ToString());
            }

            Thread.Sleep(100);

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                ClosePeerSession(remotePeers[i]);
            }

            ChildServerRelay.ClosePipes();

            OnDisconnect?.Invoke(disableReconnect);

            SteamManager.LeaveLobby();
            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamUser.OnValidateAuthTicketResponse -= OnAuthChange;
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!isActive) { return; }

            IWriteMessage msgToSend = new WriteOnlyMessage();
            byte[] msgData = new byte[msg.LengthBytes];
            msg.PrepareForSending(ref msgData, compressPastThreshold, out bool isCompressed, out int length);
            msgToSend.Write(selfSteamID);
            msgToSend.Write(selfSteamID);
            msgToSend.Write((byte)(isCompressed ? PacketHeader.IsCompressed : PacketHeader.None));
            msgToSend.Write((UInt16)length);
            msgToSend.Write(msgData, 0, length);

            byte[] bufToSend = (byte[])msgToSend.Buffer.Clone();
            Array.Resize(ref bufToSend, msgToSend.LengthBytes);
            ChildServerRelay.Write(bufToSend);
        }

        ~SteamP2POwnerPeer()
        {
            OnDisconnect = null;
            Close();
        }

        protected override void SendMsgInternal(DeliveryMethod deliveryMethod, IWriteMessage msg)
        {
            //not currently used by SteamP2POwnerPeer
            throw new NotImplementedException();
        }

#if DEBUG
        public override void ForceTimeOut()
        {
            //TODO: reimplement?
        }
#endif
    }
}
