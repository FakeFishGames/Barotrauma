#nullable enable
using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Threading;
using Barotrauma.Extensions;

namespace Barotrauma.Networking
{
    sealed class SteamP2POwnerPeer : ClientPeer
    {
        private readonly SteamId selfSteamID;
        private UInt64 ownerKey64 => unchecked((UInt64)ownerKey.Fallback(0));

        private SteamId ReadSteamId(IReadMessage inc) => new SteamId(inc.ReadUInt64() ^ ownerKey64);
        private void WriteSteamId(IWriteMessage msg, SteamId val) => msg.WriteUInt64(val.Value ^ ownerKey64);

        private long sentBytes, receivedBytes;

        private sealed class RemotePeer
        {
            public readonly SteamId SteamId;
            public Option<SteamId> OwnerSteamId;
            public double? DisconnectTime;
            public bool Authenticating;
            public bool Authenticated;

            public readonly struct UnauthedMessage
            {
                public readonly SteamId Sender;
                public readonly byte[] Bytes;
                public readonly int Length;

                public UnauthedMessage(SteamId sender, byte[] bytes)
                {
                    Sender = sender;
                    Bytes = bytes;
                    Length = bytes.Length;
                }
            }

            public readonly List<UnauthedMessage> UnauthedMessages;

            public RemotePeer(SteamId steamId)
            {
                SteamId = steamId;
                OwnerSteamId = Option<SteamId>.None();
                DisconnectTime = null;
                Authenticating = false;
                Authenticated = false;

                UnauthedMessages = new List<UnauthedMessage>();
            }
        }

        private List<RemotePeer> remotePeers = null!;

        public SteamP2POwnerPeer(Callbacks callbacks, int ownerKey) : base(new PipeEndpoint(), callbacks, Option<int>.Some(ownerKey))
        {
            ServerConnection = null;

            isActive = false;

            selfSteamID = SteamManager.GetSteamId().TryUnwrap(out var steamId)
                ? steamId
                : throw new InvalidOperationException("Steamworks not initialized");
        }


        public override void Start()
        {
            if (isActive) { return; }

            initializationStep = ConnectionInitialization.SteamTicketAndVersion;

            ServerConnection = new PipeConnection(selfSteamID)
            {
                Status = NetworkConnectionStatus.Connected
            };

            remotePeers = new List<RemotePeer>();

            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamNetworking.OnP2PSessionRequest = OnIncomingConnection;
            Steamworks.SteamUser.OnValidateAuthTicketResponse += OnAuthChange;

            Steamworks.SteamNetworking.AllowP2PPacketRelay(true);

            isActive = true;
        }

        private void OnAuthChange(Steamworks.SteamId steamId, Steamworks.SteamId ownerId, Steamworks.AuthResponse status)
        {
            RemotePeer? remotePeer = remotePeers.Find(p => p.SteamId.Value == steamId);

            if (remotePeer == null) { return; }

            if (status == Steamworks.AuthResponse.OK)
            {
                if (remotePeer.Authenticated) { return; }

                SteamId ownerSteamId = new SteamId(ownerId);
                remotePeer.OwnerSteamId = Option<SteamId>.Some(ownerSteamId);
                remotePeer.Authenticated = true;
                remotePeer.Authenticating = false;
                foreach (var unauthedMessage in remotePeer.UnauthedMessages)
                {
                    IWriteMessage msg = new WriteOnlyMessage();
                    WriteSteamId(msg, unauthedMessage.Sender);
                    WriteSteamId(msg, ownerSteamId);
                    msg.WriteBytes(unauthedMessage.Bytes, 0, unauthedMessage.Length);
                    ForwardToServerProcess(msg);
                }

                remotePeer.UnauthedMessages.Clear();
            }
            else
            {
                DisconnectPeer(remotePeer, PeerDisconnectPacket.SteamAuthError(status));
            }
        }

        private void OnIncomingConnection(Steamworks.SteamId steamId)
        {
            if (!isActive) { return; }

            if (remotePeers.None(p => p.SteamId.Value == steamId))
            {
                remotePeers.Add(new RemotePeer(new SteamId(steamId)));
            }

            Steamworks.SteamNetworking.AcceptP2PSessionWithUser(steamId); //accept all connections, the server will figure things out later
        }

        private void OnP2PData(ulong steamId, IReadMessage inc)
        {
            if (!isActive) { return; }

            RemotePeer? remotePeer = remotePeers.Find(p => p.SteamId.Value == steamId);
            if (remotePeer == null) { return; }

            if (remotePeer.DisconnectTime != null) { return; }

            var peerPacketHeaders = INetSerializableStruct.Read<PeerPacketHeaders>(inc);
            
            PacketHeader packetHeader = peerPacketHeaders.PacketHeader;

            if (!remotePeer.Authenticated && !remotePeer.Authenticating && packetHeader.IsConnectionInitializationStep())
            {
                remotePeer.DisconnectTime = null;

                ConnectionInitialization initialization = peerPacketHeaders.Initialization ?? throw new Exception("Initialization step missing");
                if (initialization == ConnectionInitialization.SteamTicketAndVersion)
                {
                    remotePeer.Authenticating = true;

                    var packet = INetSerializableStruct.Read<ClientSteamTicketAndVersionPacket>(inc);

                    packet.SteamAuthTicket.TryUnwrap(out var ticket);

                    Steamworks.BeginAuthResult authSessionStartState = SteamManager.StartAuthSession(ticket, steamId);
                    if (authSessionStartState != Steamworks.BeginAuthResult.OK)
                    {
                        DisconnectPeer(remotePeer, PeerDisconnectPacket.SteamAuthError(authSessionStartState));
                        return;
                    }
                }
            }

            var steamUserId = new SteamId(steamId);
            if (remotePeer.Authenticating)
            {
                remotePeer.UnauthedMessages.Add(new RemotePeer.UnauthedMessage(steamUserId, inc.Buffer));
            }
            else
            {
                IWriteMessage outMsg = new WriteOnlyMessage();
                WriteSteamId(outMsg, steamUserId);
                WriteSteamId(outMsg, remotePeer.OwnerSteamId.Fallback(steamUserId));
                outMsg.WriteBytes(inc.Buffer, 0, inc.LengthBytes);

                ForwardToServerProcess(outMsg);
            }
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            if (ChildServerRelay.HasShutDown || !ChildServerRelay.IsProcessAlive)
            {
                var gameClient = GameMain.Client;
                Close(PeerDisconnectPacket.WithReason(DisconnectReason.ServerCrashed));
                gameClient?.CreateServerCrashMessage();
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
                if (packet is { SteamId: var steamId, Data: var data })
                {
                    OnP2PData(steamId, new ReadWriteMessage(data, 0, data.Length * 8, false));
                    receivedBytes += data.Length;
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

            SteamId recipientSteamId = ReadSteamId(inc);

            var peerPacketHeaders = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (recipientSteamId != selfSteamID)
            {
                HandleMessageForRemotePeer(peerPacketHeaders, recipientSteamId, inc);
            }
            else
            {
                HandleMessageForOwner(peerPacketHeaders, inc);
            }
        }

        private static byte[] GetRemainingBytes(IReadMessage msg)
        {
            return msg.Buffer[msg.BytePosition..msg.LengthBytes];
        }
        
        private void HandleMessageForRemotePeer(PeerPacketHeaders peerPacketHeaders, SteamId recipientSteamId, IReadMessage inc)
        {
            var (deliveryMethod, packetHeader, initialization) = peerPacketHeaders;
            
            if (!packetHeader.IsServerMessage())
            {
                DebugConsole.ThrowError("Received non-server message meant for remote peer");
                return;
            }

            RemotePeer? peer = remotePeers.Find(p => p.SteamId == recipientSteamId);
            if (peer is null) { return; }

            if (packetHeader.IsDisconnectMessage())
            {
                var packet = INetSerializableStruct.Read<PeerDisconnectPacket>(inc);
                DisconnectPeer(peer, packet);
                return;
            }

            IWriteMessage outMsg = new WriteOnlyMessage();

            outMsg.WriteNetSerializableStruct(new PeerPacketHeaders
            {
                DeliveryMethod = deliveryMethod,
                PacketHeader = packetHeader,
                Initialization = initialization
            });

            if (packetHeader.IsConnectionInitializationStep())
            {
                var initRelayPacket = new SteamP2PInitializationRelayPacket
                {
                    LobbyID = SteamManager.CurrentLobbyID,
                    Message = new PeerPacketMessage
                    {
                        Buffer = GetRemainingBytes(inc)
                    }
                };

                outMsg.WriteNetSerializableStruct(initRelayPacket);
            }
            else
            {
                byte[] userMessage = GetRemainingBytes(inc);
                outMsg.WriteBytes(userMessage, 0, userMessage.Length);
            }

            ForwardToRemotePeer(deliveryMethod, recipientSteamId, outMsg);
        }

        private void HandleMessageForOwner(PeerPacketHeaders peerPacketHeaders, IReadMessage inc)
        {
            var (_, packetHeader, _) = peerPacketHeaders;

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
                WriteSteamId(outMsg, selfSteamID);
                WriteSteamId(outMsg, selfSteamID);
                outMsg.WriteNetSerializableStruct(new PeerPacketHeaders
                {
                    DeliveryMethod = DeliveryMethod.Reliable,
                    PacketHeader = PacketHeader.IsConnectionInitializationStep,
                    Initialization = ConnectionInitialization.SteamTicketAndVersion
                });
                outMsg.WriteNetSerializableStruct(new SteamP2PInitializationOwnerPacket
                {
                    OwnerName = GameMain.Client.Name
                });
                ForwardToServerProcess(outMsg);
            }
            else
            {
                if (initializationStep != ConnectionInitialization.Success)
                {
                    callbacks.OnInitializationComplete.Invoke();
                    initializationStep = ConnectionInitialization.Success;
                }

                PeerPacketMessage packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                IReadMessage msg = new ReadOnlyMessage(packet.Buffer, packetHeader.IsCompressed(), 0, packet.Length, ServerConnection);
                callbacks.OnMessageReceived.Invoke(msg);
            }
        }
        
        private void DisconnectPeer(RemotePeer peer, PeerDisconnectPacket peerDisconnectPacket)
        {
            peer.DisconnectTime ??= Timing.TotalTime + 1.0;

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.WriteNetSerializableStruct(new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsServerMessage | PacketHeader.IsDisconnectMessage
            });
            outMsg.WriteNetSerializableStruct(peerDisconnectPacket);
            
            Steamworks.SteamNetworking.SendP2PPacket(peer.SteamId.Value, outMsg.Buffer, outMsg.LengthBytes, 0, Steamworks.P2PSend.Reliable);
            sentBytes += outMsg.LengthBytes;
        }

        private void ClosePeerSession(RemotePeer peer)
        {
            Steamworks.SteamNetworking.CloseP2PSessionWithUser(peer.SteamId.Value);
            remotePeers.Remove(peer);
        }

        public override void SendPassword(string password)
        {
            //owner doesn't send passwords
        }

        public override void Close(PeerDisconnectPacket peerDisconnectPacket)
        {
            if (!isActive) { return; }

            isActive = false;

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                DisconnectPeer(remotePeers[i], PeerDisconnectPacket.WithReason(DisconnectReason.ServerShutdown));
            }

            Thread.Sleep(100);

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                ClosePeerSession(remotePeers[i]);
            }

            callbacks.OnDisconnect.Invoke(peerDisconnectPacket);

            SteamManager.LeaveLobby();
            Steamworks.SteamNetworking.ResetActions();
            Steamworks.SteamUser.OnValidateAuthTicketResponse -= OnAuthChange;
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!isActive) { return; }

            IWriteMessage msgToSend = new WriteOnlyMessage();
            byte[] msgData = msg.PrepareForSending(compressPastThreshold, out bool isCompressed, out _);
            WriteSteamId(msgToSend, selfSteamID);
            WriteSteamId(msgToSend, selfSteamID);
            msgToSend.WriteNetSerializableStruct(new PeerPacketHeaders
            {
                DeliveryMethod = deliveryMethod,
                PacketHeader = isCompressed ? PacketHeader.IsCompressed : PacketHeader.None
            });
            msgToSend.WriteNetSerializableStruct(new PeerPacketMessage
            {
                Buffer = msgData
            });
            ForwardToServerProcess(msgToSend);
        }

        protected override void SendMsgInternal(PeerPacketHeaders headers, INetSerializableStruct? body)
        {
            //not currently used by SteamP2POwnerPeer
            throw new NotImplementedException();
        }

        private static void ForwardToServerProcess(IWriteMessage msg)
        {
            byte[] bufToSend = new byte[msg.LengthBytes];
            msg.Buffer[..msg.LengthBytes].CopyTo(bufToSend.AsSpan());
            ChildServerRelay.Write(bufToSend);
        }

        private void ForwardToRemotePeer(DeliveryMethod deliveryMethod, SteamId recipent, IWriteMessage outMsg)
        {
            byte[] buf = outMsg.PrepareForSending(compressPastThreshold: false, out _, out int length);

            if (length + 4 >= MsgConstants.MTU)
            {
                DebugConsole.Log($"WARNING: message length comes close to exceeding MTU, forcing reliable send ({length} bytes)");
                deliveryMethod = DeliveryMethod.Reliable;
            }

            bool successSend = Steamworks.SteamNetworking.SendP2PPacket(recipent.Value, buf, length, 0, deliveryMethod.ToSteam());
            sentBytes += length;

            if (successSend) { return; }

            if (deliveryMethod is DeliveryMethod.Unreliable)
            {
                DebugConsole.Log($"WARNING: message couldn't be sent unreliably, forcing reliable send ({length} bytes)");
                successSend = Steamworks.SteamNetworking.SendP2PPacket(recipent.Value, buf, length, 0, DeliveryMethod.Reliable.ToSteam());
                sentBytes += length;
            }

            if (!successSend)
            {
                DebugConsole.AddWarning($"Failed to send message to remote peer! ({length} bytes)");
            }
        }

#if DEBUG
        public override void ForceTimeOut()
        {
            //TODO: reimplement?
        }
#endif
    }
}