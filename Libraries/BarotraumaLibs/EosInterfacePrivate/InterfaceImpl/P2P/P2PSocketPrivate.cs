#nullable enable
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using Barotrauma;

namespace EosInterfacePrivate;

public sealed class P2PSocketPrivate : EosInterface.P2PSocket
{
    private readonly record struct CallbackIds(
        ulong OnConnectionRequested,
        ulong OnConnectionClosed);
    private CallbackIds callbackIds;

    private readonly Epic.OnlineServices.P2P.SocketId socketIdInternal;
    private readonly Epic.OnlineServices.ProductUserId selfPuid;
    private P2PSocketPrivate(Epic.OnlineServices.P2P.SocketId socketIdInternal, Epic.OnlineServices.ProductUserId selfPuid)
    {
        this.socketIdInternal = socketIdInternal;
        this.selfPuid = selfPuid;
    }

    internal static Result<EosInterface.P2PSocket, CreationError> CreatePrivate(EosInterface.ProductUserId selfPuid, EosInterface.SocketId socketId)
    {
        var p2pInterface = CorePrivate.P2PInterface;
        if (p2pInterface is null) { return Result.Failure(CreationError.EosNotInitialized); }

        var socketIdInternal = new Epic.OnlineServices.P2P.SocketId { SocketName = socketId.SocketName };
        var selfPuidInternal = Epic.OnlineServices.ProductUserId.FromString(selfPuid.Value);

        using var janitor = Janitor.Start();

        var socket = new P2PSocketPrivate(socketIdInternal, selfPuidInternal);
        
        var addNotifyPeerConnectionRequestOptions = new Epic.OnlineServices.P2P.AddNotifyPeerConnectionRequestOptions
        {
            LocalUserId = selfPuidInternal,
            SocketId = socketIdInternal
        };

        var onConnectionRequestCallbackId = p2pInterface.AddNotifyPeerConnectionRequest(
            ref addNotifyPeerConnectionRequestOptions,
            socket,
            ConnectionRequestHandler);

        if (onConnectionRequestCallbackId == Epic.OnlineServices.Common.InvalidNotificationid)
        {
            return Result.Failure(CreationError.RequestBindFailed);
        }
        
        janitor.AddAction(() => p2pInterface.RemoveNotifyPeerConnectionRequest(onConnectionRequestCallbackId));
        
        var addNotifyPeerConnectionClosedOptions = new Epic.OnlineServices.P2P.AddNotifyPeerConnectionClosedOptions
        {
            LocalUserId = selfPuidInternal,
            SocketId = socketIdInternal
        };
        
        var onConnectionClosedCallbackId = p2pInterface.AddNotifyPeerConnectionClosed(
            ref addNotifyPeerConnectionClosedOptions,
            socket,
            ConnectionClosedHandler);

        if (onConnectionClosedCallbackId == Epic.OnlineServices.Common.InvalidNotificationid)
        {
            return Result.Failure(CreationError.CloseBindFailed);
        }
        
        janitor.AddAction(() => p2pInterface.RemoveNotifyPeerConnectionClosed(onConnectionClosedCallbackId));

        socket.callbackIds = new CallbackIds(
            OnConnectionRequested: onConnectionRequestCallbackId,
            OnConnectionClosed: onConnectionClosedCallbackId);

        janitor.Dismiss();

        return Result.Success<EosInterface.P2PSocket>(socket);
    }

    private static void ConnectionRequestHandler(ref Epic.OnlineServices.P2P.OnIncomingConnectionRequestInfo info)
    {
        if (info.ClientData is P2PSocketPrivate p2pSocket
            && string.Equals(info.SocketId?.SocketName, p2pSocket.socketIdInternal.SocketName))
        {
            p2pSocket.HandleIncomingConnection.Invoke(new IncomingConnectionRequest(
                Socket: p2pSocket,
                RemoteUserId: new EosInterface.ProductUserId(info.RemoteUserId.ToString())));
        }
    }
    
    private static void ConnectionClosedHandler(ref Epic.OnlineServices.P2P.OnRemoteConnectionClosedInfo info)
    {
        if (info.ClientData is P2PSocketPrivate p2pSocket
            && string.Equals(info.SocketId?.SocketName, p2pSocket.socketIdInternal.SocketName))
        {
            p2pSocket.HandleClosedConnection.Invoke(new RemoteConnectionClosed(
                RemoteUserId: new EosInterface.ProductUserId(info.RemoteUserId.ToString()),
                Reason: info.Reason switch
                {
                    Epic.OnlineServices.P2P.ConnectionClosedReason.Unknown
                        => RemoteConnectionClosed.ConnectionClosedReason.Unknown,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.ClosedByLocalUser
                        => RemoteConnectionClosed.ConnectionClosedReason.ClosedByLocalUser,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.ClosedByPeer
                        => RemoteConnectionClosed.ConnectionClosedReason.ClosedByPeer,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.TimedOut
                        => RemoteConnectionClosed.ConnectionClosedReason.TimedOut,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.TooManyConnections
                        => RemoteConnectionClosed.ConnectionClosedReason.TooManyConnections,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.InvalidMessage
                        => RemoteConnectionClosed.ConnectionClosedReason.InvalidMessage,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.InvalidData
                        => RemoteConnectionClosed.ConnectionClosedReason.InvalidData,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.ConnectionFailed
                        => RemoteConnectionClosed.ConnectionClosedReason.ConnectionFailed,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.ConnectionClosed
                        => RemoteConnectionClosed.ConnectionClosedReason.ConnectionClosed,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.NegotiationFailed
                        => RemoteConnectionClosed.ConnectionClosedReason.NegotiationFailed,
                    Epic.OnlineServices.P2P.ConnectionClosedReason.UnexpectedError
                        => RemoteConnectionClosed.ConnectionClosedReason.UnexpectedError,
                    _
                        => RemoteConnectionClosed.ConnectionClosedReason.Unhandled
                }));
        }
    }
    
    public override void AcceptConnectionRequest(IncomingConnectionRequest request)
    {
        var remoteUserIdInternal = Epic.OnlineServices.ProductUserId.FromString(request.RemoteUserId.Value);

        var acceptConnectionOptions = new Epic.OnlineServices.P2P.AcceptConnectionOptions
        {
            LocalUserId = selfPuid,
            RemoteUserId = remoteUserIdInternal,
            SocketId = socketIdInternal
        };
        CorePrivate.P2PInterface?.AcceptConnection(ref acceptConnectionOptions);
    }

    public override void CloseConnection(EosInterface.ProductUserId remoteUserId)
    {
        var remoteUserIdInternal = Epic.OnlineServices.ProductUserId.FromString(remoteUserId.Value);
        
        var closeConnectionOptions = new Epic.OnlineServices.P2P.CloseConnectionOptions
        {
            LocalUserId = selfPuid,
            RemoteUserId = remoteUserIdInternal,
            SocketId = socketIdInternal
        };
        CorePrivate.P2PInterface?.CloseConnection(ref closeConnectionOptions);
    }

    public override IEnumerable<IncomingMessage> GetMessageBatch()
    {
        var p2pInterface = CorePrivate.P2PInterface;
        if (p2pInterface is null) { yield break; }
        
        var packetQueueOptions = new Epic.OnlineServices.P2P.GetPacketQueueInfoOptions();
        p2pInterface.GetPacketQueueInfo(ref packetQueueOptions, out var packetQueueInfo);

        byte[] buf = new byte[Epic.OnlineServices.P2P.P2PInterface.MaxPacketSize];

        for (ulong i = 0; i < packetQueueInfo.IncomingPacketQueueCurrentPacketCount; i++)
        {
            var receivePacketOptions = new Epic.OnlineServices.P2P.ReceivePacketOptions
            {
                LocalUserId = selfPuid,
                MaxDataSizeBytes = (uint)buf.Length,
                RequestedChannel = null
            };

            var result = p2pInterface.ReceivePacket(
                ref receivePacketOptions,
                out var senderId,
                out var senderSocketId,
                out _,
                buf,
                out uint bytesWritten);

            if (result != Epic.OnlineServices.Result.Success) { continue; }
            if (senderSocketId.SocketName != socketIdInternal.SocketName) { continue; }

            yield return new IncomingMessage(
                buf, (int)bytesWritten, new EosInterface.ProductUserId(senderId.ToString()));
        }
    }

    public override Result<Unit, SendError> SendMessage(OutgoingMessage msg)
    {
        var p2pInterface = CorePrivate.P2PInterface;
        if (p2pInterface is null) { return Result.Failure(SendError.EosNotInitialized); }

        var reliability = msg.DeliveryMethod switch
        {
            DeliveryMethod.Reliable
                => Epic.OnlineServices.P2P.PacketReliability.ReliableOrdered,
            _
                => Epic.OnlineServices.P2P.PacketReliability.UnreliableUnordered
        };
        
        var sendPacketOptions = new Epic.OnlineServices.P2P.SendPacketOptions
        {
            LocalUserId = selfPuid,
            RemoteUserId = Epic.OnlineServices.ProductUserId.FromString(msg.Destination.Value),
            SocketId = socketIdInternal,
            Channel = 0,
            Data = new ArraySegment<byte>(array: msg.Buffer, offset: 0, count: msg.ByteLength),
            AllowDelayedDelivery = true,
            Reliability = reliability,
            DisableAutoAcceptConnection = false
        };
        var result = p2pInterface.SendPacket(ref sendPacketOptions);

        return result switch
        {
            Epic.OnlineServices.Result.Success
                => Result.Success(Unit.Value),
            Epic.OnlineServices.Result.InvalidParameters
                => Result.Failure(SendError.InvalidParameters),
            Epic.OnlineServices.Result.LimitExceeded
                => Result.Failure(SendError.LimitExceeded),
            Epic.OnlineServices.Result.NoConnection
                => Result.Failure(SendError.NoConnection),
            _
                => Result.Failure(SendError.UnhandledErrorCondition)
        };
    }

    public override void Dispose()
    {
        var p2pInterface = CorePrivate.P2PInterface;
        if (p2pInterface is null) { return; }

        var closeConnectionsOptions = new Epic.OnlineServices.P2P.CloseConnectionsOptions
        {
            LocalUserId = selfPuid,
            SocketId = socketIdInternal
        };
        p2pInterface.RemoveNotifyPeerConnectionRequest(callbackIds.OnConnectionRequested);
        p2pInterface.RemoveNotifyPeerConnectionClosed(callbackIds.OnConnectionClosed);
        p2pInterface.CloseConnections(ref closeConnectionsOptions);
        callbackIds = default;
    }
}

internal sealed partial class ImplementationPrivate : EosInterface.Implementation
{
    public override Result<EosInterface.P2PSocket, EosInterface.P2PSocket.CreationError> CreateP2PSocket(EosInterface.ProductUserId puid, EosInterface.SocketId socketId)
        => P2PSocketPrivate.CreatePrivate(puid, socketId);
}
