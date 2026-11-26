#nullable enable

using System;

namespace Barotrauma.Networking;

// this class used to contain EOS socket, but since its removal it now contains only Steam,
// left here in case we ever want to implement cross-play again
[Obsolete("DualStackP2PSocket is obsolete, use SteamListenSocket instead")]
sealed class DualStackP2PSocket : P2PSocket
{
    private readonly Option<SteamListenSocket> steamSocket;

    private DualStackP2PSocket(
        Callbacks callbacks,
        Option<SteamListenSocket> steamSocket,
        OwnerOrClient type) :
        base(callbacks, type)
    {
        this.steamSocket = steamSocket;
    }

    public static Result<P2PSocket, Error> Create(Callbacks callbacks, OwnerOrClient type)
    {
        var steamP2PSocketResult = SteamListenSocket.Create(callbacks, type);
        if (steamP2PSocketResult.TryUnwrapFailure(out var steamError))
        {
            return Result.Failure(new Error(steamError));
        }
        return Result.Success<P2PSocket>(new DualStackP2PSocket(
                                            callbacks,
                                            steamP2PSocketResult.TryUnwrapSuccess(out var steamP2PSocket)
                                                ? Option.Some((SteamListenSocket)steamP2PSocket)
                                                : Option.None, type));
    }

    public override void ProcessIncomingMessages()
    {
        if (steamSocket.TryUnwrap(out var steamP2PSocket)) { steamP2PSocket.ProcessIncomingMessages(); }
    }

    public override bool SendMessage(P2PEndpoint endpoint, IWriteMessage outMsg, DeliveryMethod deliveryMethod)
    {
        return endpoint switch
        {
            SteamP2PEndpoint steamP2PEndpoint when steamSocket.TryUnwrap(out var steamP2PSocket)
                => steamP2PSocket.SendMessage(steamP2PEndpoint, outMsg, deliveryMethod),
            _
                => false
        };
    }

    public override void CloseConnection(P2PEndpoint endpoint)
    {
        switch (endpoint)
        {
            case SteamP2PEndpoint steamP2PEndpoint:
                if (steamSocket.TryUnwrap(out var steamP2PSocket))
                {
                    steamP2PSocket.CloseConnection(steamP2PEndpoint);
                }
                break;
        }
    }

    public override void Dispose()
    {
        if (steamSocket.TryUnwrap(out var steamP2PSocket)) { steamP2PSocket.Dispose(); }
    }
}