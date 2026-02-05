#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Networking;

abstract class P2PSocket : IDisposable
{
    public readonly P2POwnerDoSProtection dosProtection;
    public readonly OwnerOrClient Type;

    public enum OwnerOrClient
    {
        Client,
        Owner
    }

    public enum ErrorCode
    {
        EosNotInitialized,
        EosNotLoggedIn,
        FailedToCreateEosP2PSocket,

        SteamNotInitialized,
        FailedToCreateSteamP2PSocket
    }

    public readonly record struct Error(
        ImmutableArray<(ErrorCode Code, string AdditionalInfo)> CodesAndInfo)
    {
        public Error(ErrorCode code, string? additionalInfo = "") : this((code, additionalInfo ?? "").ToEnumerable().ToImmutableArray()) { }
        public Error(params Error[] innerErrors) : this(innerErrors.SelectMany(ie => ie.CodesAndInfo).ToImmutableArray()) { }

        public override string? ToString()
        {
            if (CodesAndInfo.IsDefault)
            {
                return "default(Error)";
            }

            return $"Errors({string.Join("; ", CodesAndInfo)})";
        }
    }

    public readonly record struct Callbacks(
        Predicate<P2PEndpoint> OnIncomingConnection,
        Action<P2PEndpoint, PeerDisconnectPacket> OnConnectionClosed,
        P2POwnerDoSProtection.ExcessivePacketDelegate OnExcessivePackets,
        Action<P2PEndpoint, IReadMessage> OnData);
    protected readonly Callbacks callbacks;

    protected P2PSocket(Callbacks callbacks, OwnerOrClient type)
    {
        this.callbacks = callbacks;
        Type = type;

        dosProtection = new P2POwnerDoSProtection(callbacks.OnExcessivePackets);
    }

    public abstract void ProcessIncomingMessages();

    public abstract bool SendMessage(P2PEndpoint endpoint, IWriteMessage outMsg, DeliveryMethod deliveryMethod);

    public abstract void CloseConnection(P2PEndpoint endpoint);

    public abstract void Dispose();
}
