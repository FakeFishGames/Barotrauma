#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    sealed class P2POwnerPeer : ClientPeer<PipeEndpoint>
    {
        private P2PSocket? socket;
        private readonly ImmutableDictionary<AuthenticationTicketKind, Authenticator> authenticators;

        private readonly P2PEndpoint selfPrimaryEndpoint;
        private AccountInfo selfAccountInfo;

        private long sentBytes, receivedBytes;

        private sealed class RemotePeer
        {
            public enum AuthenticationStatus
            {
                NotAuthenticated,
                AuthenticationPending,
                SuccessfullyAuthenticated
            }

            public readonly P2PEndpoint Endpoint;
            public AccountInfo AccountInfo;

            public readonly record struct DisconnectInfo(
                double TimeToGiveUp,
                PeerDisconnectPacket Packet);
            
            public Option<DisconnectInfo> PendingDisconnect;
            public AuthenticationStatus AuthStatus;

            public readonly record struct UnauthedMessage(byte[] Bytes, int LengthBytes);

            public readonly List<UnauthedMessage> UnauthedMessages;

            public RemotePeer(P2PEndpoint endpoint)
            {
                Endpoint = endpoint;
                AccountInfo = AccountInfo.None;
                PendingDisconnect = Option.None;
                AuthStatus = AuthenticationStatus.NotAuthenticated;

                UnauthedMessages = new List<UnauthedMessage>();
            }
        }

        private readonly List<RemotePeer> remotePeers = new();

        public P2POwnerPeer(Callbacks callbacks, int ownerKey, ImmutableArray<P2PEndpoint> allEndpoints) : 
            base(new PipeEndpoint(), allEndpoints.Cast<Endpoint>().ToImmutableArray(), callbacks, Option<int>.Some(ownerKey))
        {
            ServerConnection = null;

            isActive = false;

            var selfSteamEndpoint = allEndpoints.FirstOrNone(e => e is SteamP2PEndpoint);
            var selfEosEndpoint = allEndpoints.FirstOrNone(e => e is EosP2PEndpoint);
            var selfPrimaryEndpointOption = selfSteamEndpoint.Fallback(selfEosEndpoint);
            if (!selfPrimaryEndpointOption.TryUnwrap(out var selfPrimaryEndpointNotNull))
            {
                throw new Exception("Could not determine endpoint for P2POwnerPeer");
            }
            selfPrimaryEndpoint = selfPrimaryEndpointNotNull;
            selfAccountInfo = AccountInfo.None;
            authenticators = Authenticator.GetAuthenticatorsForHost(Option.Some<Endpoint>(selfPrimaryEndpoint));
        }

        public override void Start()
        {
            if (isActive) { return; }

            initializationStep = ConnectionInitialization.AuthInfoAndVersion;

            ServerConnection = new PipeConnection(Option.None)
            {
                Status = NetworkConnectionStatus.Connected
            };

            remotePeers.Clear();

            var socketCallbacks = new P2PSocket.Callbacks(OnIncomingConnection, OnConnectionClosed, OnP2PData);
            var socketCreateResult = DualStackP2PSocket.Create(socketCallbacks);
            socket = socketCreateResult.TryUnwrapSuccess(out var s)
                ? s
                : throw new Exception($"Failed to create dual-stack socket: {socketCreateResult}");

            TaskPool.Add("P2POwnerPeer.GetAccountId", GetAccountId(), t =>
            {
                if (t.TryGetResult(out Option<AccountId> accountIdOption) && accountIdOption.TryUnwrap(out var accountId))
                {
                    selfAccountInfo = new AccountInfo(accountId);
                }

                if (selfAccountInfo.IsNone)
                {
                    Close(PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationFailed));
                }
            });

            isActive = true;
        }

        private bool OnIncomingConnection(P2PEndpoint remoteEndpoint)
        {
            if (!isActive) { return false; }

            if (remotePeers.None(p => p.Endpoint == remoteEndpoint))
            {
                remotePeers.Add(new RemotePeer(remoteEndpoint));
            }

            return true;
        }

        private void OnConnectionClosed(P2PEndpoint remoteEndpoint, PeerDisconnectPacket disconnectPacket)
        {
            var remotePeer
                = remotePeers.Find(p => p.Endpoint == remoteEndpoint);
            if (remotePeer is null) { return; }
            CommunicatePeerDisconnectToServerProcess(
                remotePeer,
                remotePeer.PendingDisconnect.Select(d => d.Packet).Fallback(disconnectPacket));
        }

        private void OnP2PData(P2PEndpoint senderEndpoint, IReadMessage inc)
        {
            if (!isActive) { return; }
            
            receivedBytes += inc.LengthBytes;

            var remotePeer = remotePeers.Find(p => p.Endpoint == senderEndpoint);
            if (remotePeer is null) { return; }
            if (remotePeer.PendingDisconnect.IsSome()) { return; }

            if (!INetSerializableStruct.TryRead(inc, remotePeer.AccountInfo, out PeerPacketHeaders peerPacketHeaders))
            {
                CommunicateDisconnectToRemotePeer(remotePeer, PeerDisconnectPacket.WithReason(DisconnectReason.MalformedData));
                return;
            }

            PacketHeader packetHeader = peerPacketHeaders.PacketHeader;

            if (packetHeader.IsConnectionInitializationStep())
            {
                if (peerPacketHeaders.Initialization == null)
                {
                    //can happen if the packet is crafted in a way to leave the Initialization value as null
                    DebugConsole.ThrowErrorOnce(
                        $"P2POwnerPeer.OnP2PData:{remotePeer.Endpoint.StringRepresentation}", 
                        $"Failed to initialize remote peer {remotePeer.Endpoint.StringRepresentation}: initialization step missing.");
                    CommunicateDisconnectToRemotePeer(remotePeer, PeerDisconnectPacket.WithReason(DisconnectReason.MalformedData));
                    return;
                }
                ConnectionInitialization initialization = peerPacketHeaders.Initialization.Value;
                if (initialization == ConnectionInitialization.AuthInfoAndVersion
                    && remotePeer.AuthStatus == RemotePeer.AuthenticationStatus.NotAuthenticated)
                {
                    StartAuthTask(inc, remotePeer);
                }
            }

            if (remotePeer.AuthStatus == RemotePeer.AuthenticationStatus.AuthenticationPending)
            {
                remotePeer.UnauthedMessages.Add(new RemotePeer.UnauthedMessage(inc.Buffer, inc.LengthBytes));
            }
            else
            {
                IWriteMessage outMsg = new WriteOnlyMessage();
                outMsg.WriteNetSerializableStruct(new P2POwnerToServerHeader
                {
                    EndpointStr = remotePeer.Endpoint.StringRepresentation,
                    AccountInfo = remotePeer.AccountInfo
                });
                outMsg.WriteBytes(inc.Buffer, 0, inc.LengthBytes);

                ForwardToServerProcess(outMsg);
            }
        }

        private void StartAuthTask(IReadMessage inc, RemotePeer remotePeer)
        {
            remotePeer.AuthStatus = RemotePeer.AuthenticationStatus.AuthenticationPending;

            if (!INetSerializableStruct.TryRead(inc, remotePeer.AccountInfo, out ClientAuthTicketAndVersionPacket packet))
            {
                failAuth();
                return;
            }
            if (!packet.AuthTicket.TryUnwrap(out var authenticationTicket))
            {
                failAuth();
                return;
            }
            if (!authenticators.TryGetValue(authenticationTicket.Kind, out var authenticator))
            {
                failAuth();
                return;
            }
            TaskPool.Add($"P2POwnerPeer.VerifyRemotePeerAccountId",
                authenticator.VerifyTicket(authenticationTicket),
                t =>
                {
                    if (!t.TryGetResult(out AccountInfo accountInfo)
                        || accountInfo.IsNone)
                    {
                        failAuth();
                        return;
                    }

                    remotePeer.AccountInfo = accountInfo;
                    remotePeer.AuthStatus = RemotePeer.AuthenticationStatus.SuccessfullyAuthenticated;
                    foreach (var unauthedMessage in remotePeer.UnauthedMessages)
                    {
                        IWriteMessage msg = new WriteOnlyMessage();
                        msg.WriteNetSerializableStruct(new P2POwnerToServerHeader
                        {
                            EndpointStr = remotePeer.Endpoint.StringRepresentation,
                            AccountInfo = accountInfo
                        });
                        msg.WriteBytes(unauthedMessage.Bytes, 0, unauthedMessage.LengthBytes);
                        ForwardToServerProcess(msg);
                    }
                    remotePeer.UnauthedMessages.Clear();
                });

            void failAuth()
            {
                CommunicateDisconnectToRemotePeer(remotePeer, PeerDisconnectPacket.WithReason(DisconnectReason.AuthenticationFailed));
            }
        }

        public override void Update(float deltaTime)
        {
            if (!isActive) { return; }

            if (ChildServerRelay.HasShutDown || ChildServerRelay.Process is not { HasExited: false })
            {
                Close(PeerDisconnectPacket.WithReason(DisconnectReason.ServerCrashed));
                var msgBox = new GUIMessageBox(TextManager.Get("ConnectionLost"), ChildServerRelay.CrashMessage);
                msgBox.Buttons[0].OnClicked += (btn, obj) =>
                {
                    GameMain.MainMenuScreen.Select();
                    return false;
                };
                return;
            }

            if (selfAccountInfo.IsNone) { return; }

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                if (remotePeers[i].PendingDisconnect.TryUnwrap(out var pendingDisconnect) && pendingDisconnect.TimeToGiveUp < Timing.TotalTime)
                {
                    CommunicatePeerDisconnectToServerProcess(remotePeers[i], pendingDisconnect.Packet);
                }
            }

            socket?.ProcessIncomingMessages();

            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.ReceivedBytes, receivedBytes);
            GameMain.Client?.NetStats?.AddValue(NetStats.NetStatType.SentBytes, sentBytes);

            foreach (var incBuf in ChildServerRelay.Read())
            {
                ChildServerRelay.DisposeLocalHandles();
                IReadMessage inc = new ReadOnlyMessage(incBuf, false, 0, incBuf.Length, ServerConnection);
                HandleServerMessage(inc);
            }
        }

        private void HandleServerMessage(IReadMessage inc)
        {
            if (!isActive) { return; }

            var recipientInfo = INetSerializableStruct.Read<P2PServerToOwnerHeader>(inc);
            if (!recipientInfo.Endpoint.TryUnwrap(out var recipientEndpoint)) { return; }
            var peerPacketHeaders = INetSerializableStruct.Read<PeerPacketHeaders>(inc);

            if (recipientEndpoint != selfPrimaryEndpoint)
            {
                HandleMessageForRemotePeer(peerPacketHeaders, recipientEndpoint, inc);
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
        
        private void HandleMessageForRemotePeer(PeerPacketHeaders peerPacketHeaders, P2PEndpoint recipientEndpoint, IReadMessage inc)
        {
            var (deliveryMethod, packetHeader, initialization) = peerPacketHeaders;
            
            if (!packetHeader.IsServerMessage())
            {
                DebugConsole.ThrowError("Received non-server message meant for remote peer");
                return;
            }

            RemotePeer? peer = remotePeers.Find(p => p.Endpoint == recipientEndpoint);
            if (peer is null) { return; }

            if (packetHeader.IsDisconnectMessage())
            {
                var packet = INetSerializableStruct.Read<PeerDisconnectPacket>(inc);
                CommunicateDisconnectToRemotePeer(peer, packet);
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
                var initRelayPacket = new P2PInitializationRelayPacket
                {
                    LobbyID = 0,
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

            ForwardToRemotePeer(deliveryMethod, recipientEndpoint, outMsg);
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
                if (selfAccountInfo.IsNone) { throw new InvalidOperationException($"Cannot initialize {nameof(P2POwnerPeer)} because {nameof(selfAccountInfo)} is not defined"); }
                IWriteMessage outMsg = new WriteOnlyMessage();
                outMsg.WriteNetSerializableStruct(new P2POwnerToServerHeader
                {
                    EndpointStr = selfPrimaryEndpoint.StringRepresentation,
                    AccountInfo = selfAccountInfo
                });
                outMsg.WriteNetSerializableStruct(new PeerPacketHeaders
                {
                    DeliveryMethod = DeliveryMethod.Reliable,
                    PacketHeader = PacketHeader.IsConnectionInitializationStep,
                    Initialization = ConnectionInitialization.AuthInfoAndVersion
                });
                outMsg.WriteNetSerializableStruct(new P2PInitializationOwnerPacket(
                    Name: GameMain.Client.Name,
                    AccountId: selfAccountInfo.AccountId.Fallback(default(AccountId)!)));
                ForwardToServerProcess(outMsg);
            }
            else
            {
                OnInitializationComplete();

                var packet = INetSerializableStruct.Read<PeerPacketMessage>(inc);
                IReadMessage msg = new ReadOnlyMessage(packet.Buffer, packetHeader.IsCompressed(), 0, packet.Length, ServerConnection);
                callbacks.OnMessageReceived.Invoke(msg);
            }
        }
        
        private void CommunicateDisconnectToRemotePeer(RemotePeer peer, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (peer.PendingDisconnect.IsNone())
            {
                peer.PendingDisconnect = Option.Some(
                    new RemotePeer.DisconnectInfo(
                        Timing.TotalTime + 3f,
                        peerDisconnectPacket));
            }

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.WriteNetSerializableStruct(new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsServerMessage | PacketHeader.IsDisconnectMessage
            });
            outMsg.WriteNetSerializableStruct(peerDisconnectPacket);

            ForwardToRemotePeer(DeliveryMethod.Reliable, peer.Endpoint, outMsg);
        }

        private void CommunicatePeerDisconnectToServerProcess(RemotePeer peer, PeerDisconnectPacket peerDisconnectPacket)
        {
            if (!remotePeers.Remove(peer)) { return; }

            IWriteMessage outMsg = new WriteOnlyMessage();
            outMsg.WriteNetSerializableStruct(new P2POwnerToServerHeader
            {
                EndpointStr = peer.Endpoint.StringRepresentation,
                AccountInfo = peer.AccountInfo
            });
            outMsg.WriteNetSerializableStruct(new PeerPacketHeaders
            {
                DeliveryMethod = DeliveryMethod.Reliable,
                PacketHeader = PacketHeader.IsDisconnectMessage
            });
            outMsg.WriteNetSerializableStruct(peerDisconnectPacket);
            if (peer.AccountInfo.AccountId.TryUnwrap(out var accountId))
            {
                authenticators.Values.ForEach(authenticator => authenticator.EndAuthSession(accountId));
            }

            ForwardToServerProcess(outMsg);

            socket?.CloseConnection(peer.Endpoint);
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
                CommunicateDisconnectToRemotePeer(remotePeers[i], peerDisconnectPacket);
            }

            Thread.Sleep(100);

            for (int i = remotePeers.Count - 1; i >= 0; i--)
            {
                CommunicatePeerDisconnectToServerProcess(remotePeers[i], peerDisconnectPacket);
            }

            socket?.Dispose();
            socket = null;
            
            callbacks.OnDisconnect.Invoke(peerDisconnectPacket);
        }

        public override void Send(IWriteMessage msg, DeliveryMethod deliveryMethod, bool compressPastThreshold = true)
        {
            if (!isActive) { return; }

            IWriteMessage msgToSend = new WriteOnlyMessage();
            byte[] msgData = msg.PrepareForSending(compressPastThreshold, out bool isCompressed, out _);
            msgToSend.WriteNetSerializableStruct(new P2POwnerToServerHeader
            {
                EndpointStr = selfPrimaryEndpoint.StringRepresentation,
                AccountInfo = selfAccountInfo
            });
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
            //not currently used by P2POwnerPeer
            throw new NotImplementedException();
        }

        private static void ForwardToServerProcess(IWriteMessage msg)
        {
            byte[] bufToSend = new byte[msg.LengthBytes];
            msg.Buffer[..msg.LengthBytes].CopyTo(bufToSend.AsSpan());
            ChildServerRelay.Write(bufToSend);
        }

        private void ForwardToRemotePeer(DeliveryMethod deliveryMethod, P2PEndpoint recipient, IWriteMessage outMsg)
        {
            if (socket is null) { return; }

            int length = outMsg.LengthBytes;

            if (length + 4 >= MsgConstants.MTU)
            {
                DebugConsole.Log($"WARNING: message length comes close to exceeding MTU, forcing reliable send ({length} bytes)");
                deliveryMethod = DeliveryMethod.Reliable;
            }

            var success = socket.SendMessage(recipient, outMsg, deliveryMethod);

            sentBytes += length;

            if (success) { return; }

            if (deliveryMethod is DeliveryMethod.Unreliable)
            {
                DebugConsole.Log($"WARNING: message couldn't be sent unreliably, forcing reliable send ({length} bytes)");
                success = socket.SendMessage(recipient, outMsg, DeliveryMethod.Reliable);
                sentBytes += length;
            }

            if (!success)
            {
                DebugConsole.AddWarning($"Failed to send message to remote peer! ({length} bytes)");
            }
        }

        protected override async Task<Option<AccountId>> GetAccountId()
        {
            if (SteamManager.IsInitialized) { return SteamManager.GetSteamId().Select(id => (AccountId)id); }

            if (EosInterface.IdQueries.GetLoggedInPuids() is not { Length: > 0 } puids)
            {
                return Option.None;
            }
            var externalAccountIdsResult = await EosInterface.IdQueries.GetSelfExternalAccountIds(puids[0]);
            if (!externalAccountIdsResult.TryUnwrapSuccess(out var externalAccountIds)
                || externalAccountIds is not { Length: > 0 })
            {
                return Option.None;
            }
            return Option.Some(externalAccountIds[0]);
        }

#if DEBUG
        public override void ForceTimeOut()
        {
            //TODO: reimplement?
        }

        public override void DebugSendRawMessage(IWriteMessage msg)
            => ForwardToServerProcess(msg);
#endif
    }
}