#nullable enable

namespace Barotrauma.Networking;

sealed class EosP2PSocket : P2PSocket
{
    private readonly EosInterface.P2PSocket eosSocket;

    private EosP2PSocket(
        Callbacks callbacks,
        EosInterface.P2PSocket eosSocket)
        : base(callbacks)
    {
        this.eosSocket = eosSocket;
    }

    public static Result<P2PSocket, Error> Create(Callbacks callbacks)
    {
        if (!EosInterface.Core.IsInitialized) { return Result.Failure(new Error(ErrorCode.EosNotInitialized)); }

        var eosSocketId = new EosInterface.SocketId { SocketName = EosP2PEndpoint.SocketName };
        if (EosInterface.IdQueries.GetLoggedInPuids() is not { Length: > 0 } puids)
        {
            return Result.Failure(new Error(ErrorCode.EosNotLoggedIn));
        }
        var socketCreateResult = EosInterface.P2PSocket.Create(puids[0], eosSocketId);

        if (!socketCreateResult.TryUnwrapSuccess(out var eosSocket)) { return Result.Failure(new Error(ErrorCode.FailedToCreateEosP2PSocket, socketCreateResult.ToString())); }
        var retVal = new EosP2PSocket(callbacks, eosSocket);

        eosSocket.HandleIncomingConnection.Register("Event".ToIdentifier(), retVal.OnIncomingConnection);
        eosSocket.HandleClosedConnection.Register("Event".ToIdentifier(), retVal.OnConnectionClosed);

        return Result.Success((P2PSocket)retVal);
    }

    public override void ProcessIncomingMessages()
    {
        foreach (var msg in eosSocket.GetMessageBatch())
        {
            callbacks.OnData(new EosP2PEndpoint(msg.Sender), new ReadWriteMessage(msg.Buffer, 0, msg.ByteLength * 8, false));
        }
    }

    public override bool SendMessage(P2PEndpoint endpoint, IWriteMessage outMsg, DeliveryMethod deliveryMethod)
    {
        if (endpoint is not EosP2PEndpoint { ProductUserId: var puid }) { return false; }
        var sendResult = eosSocket.SendMessage(new EosInterface.P2PSocket.OutgoingMessage(
            Buffer: outMsg.Buffer,
            ByteLength: outMsg.LengthBytes,
            Destination: puid,
            DeliveryMethod: deliveryMethod));
        return sendResult.IsSuccess;
    }

    private void OnIncomingConnection(EosInterface.P2PSocket.IncomingConnectionRequest request)
    {
        var remoteEndpoint = new EosP2PEndpoint(request.RemoteUserId);

        if (callbacks.OnIncomingConnection(remoteEndpoint))
        {
            request.Accept();
        }
    }

    private void OnConnectionClosed(EosInterface.P2PSocket.RemoteConnectionClosed data)
    {
        var remoteEndpoint = new EosP2PEndpoint(data.RemoteUserId);

        var peerDisconnectPacket = PeerDisconnectPacket.WithReason(data.Reason switch
        {
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.Unknown => DisconnectReason.Unknown,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.ClosedByLocalUser => DisconnectReason.Disconnected,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.ClosedByPeer => DisconnectReason.Disconnected,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.TimedOut => DisconnectReason.Timeout,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.TooManyConnections => DisconnectReason.ServerFull,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.InvalidMessage => DisconnectReason.Unknown,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.InvalidData => DisconnectReason.Unknown,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.ConnectionFailed => DisconnectReason.AuthenticationFailed,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.ConnectionClosed => DisconnectReason.Disconnected,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.NegotiationFailed => DisconnectReason.AuthenticationFailed,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.UnexpectedError => DisconnectReason.Unknown,
            EosInterface.P2PSocket.RemoteConnectionClosed.ConnectionClosedReason.Unhandled => DisconnectReason.Unknown,
            _ => DisconnectReason.Unknown
        });
        callbacks.OnConnectionClosed(remoteEndpoint, peerDisconnectPacket);
    }

    public override void CloseConnection(P2PEndpoint endpoint)
    {
        if (endpoint is not EosP2PEndpoint { ProductUserId: var puid }) { return; }
        eosSocket.CloseConnection(puid);
    }

    public override void Dispose()
    {
        eosSocket.Dispose();
    }
}