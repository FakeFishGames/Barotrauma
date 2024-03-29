using System;
using System.Collections.Generic;
using Barotrauma.Networking;

namespace Barotrauma;

public static partial class EosInterface
{
    public abstract class P2PSocket : IDisposable
    {
        public enum CreationError
        {
            EosNotInitialized,
            UserNotLoggedIn,
            RequestBindFailed,
            CloseBindFailed
        }

        public readonly record struct IncomingConnectionRequest(
            P2PSocket Socket,
            ProductUserId RemoteUserId)
        {
            public void Accept()
                => Socket.AcceptConnectionRequest(this);
        }

        public readonly record struct RemoteConnectionClosed(
            ProductUserId RemoteUserId,
            RemoteConnectionClosed.ConnectionClosedReason Reason)
        {
            public enum ConnectionClosedReason
            {
                Unknown,
                ClosedByLocalUser,
                ClosedByPeer,
                TimedOut,
                TooManyConnections,
                InvalidMessage,
                InvalidData,
                ConnectionFailed,
                ConnectionClosed,
                NegotiationFailed,
                UnexpectedError,
                Unhandled
            }
        }

        public readonly NamedEvent<IncomingConnectionRequest> HandleIncomingConnection
            = new NamedEvent<IncomingConnectionRequest>();

        public readonly NamedEvent<RemoteConnectionClosed> HandleClosedConnection
            = new NamedEvent<RemoteConnectionClosed>();

        public static Result<P2PSocket, CreationError> Create(ProductUserId puid, SocketId socketId)
            => Core.LoadedImplementation.IsInitialized()
                ? Core.LoadedImplementation.CreateP2PSocket(puid, socketId)
                : Result.Failure(CreationError.EosNotInitialized);

        public abstract void AcceptConnectionRequest(IncomingConnectionRequest request);

        public abstract void CloseConnection(ProductUserId remoteUserId);

        public readonly record struct IncomingMessage(
            byte[] Buffer,
            int ByteLength,
            ProductUserId Sender);

        public abstract IEnumerable<IncomingMessage> GetMessageBatch();

        public readonly record struct OutgoingMessage(
            byte[] Buffer,
            int ByteLength,
            ProductUserId Destination,
            DeliveryMethod DeliveryMethod);

        public enum SendError
        {
            EosNotInitialized,
            InvalidParameters,
            LimitExceeded,
            NoConnection,
            UnhandledErrorCondition
        }

        public abstract Result<Unit, SendError> SendMessage(OutgoingMessage msg);

        public abstract void Dispose();
    }

    internal abstract partial class Implementation
    {
        public abstract Result<P2PSocket, P2PSocket.CreationError> CreateP2PSocket(ProductUserId puid,
            SocketId socketId);
    }
}