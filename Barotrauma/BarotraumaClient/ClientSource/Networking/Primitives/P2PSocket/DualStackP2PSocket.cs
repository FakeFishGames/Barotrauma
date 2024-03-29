#nullable enable

namespace Barotrauma.Networking;

sealed class DualStackP2PSocket : P2PSocket
{
    private readonly Option<EosP2PSocket> eosSocket;
    private readonly Option<SteamListenSocket> steamSocket;

    private DualStackP2PSocket(
        Callbacks callbacks,
        Option<EosP2PSocket> eosSocket,
        Option<SteamListenSocket> steamSocket) :
        base(callbacks)
    {
        this.eosSocket = eosSocket;
        this.steamSocket = steamSocket;
    }

    public static Result<P2PSocket, Error> Create(Callbacks callbacks)
    {
        var eosP2PSocketResult = EosP2PSocket.Create(callbacks);
        var steamP2PSocketResult = SteamListenSocket.Create(callbacks);
        if (eosP2PSocketResult.TryUnwrapFailure(out var eosError)
            && steamP2PSocketResult.TryUnwrapFailure(out var steamError))
        {
            return Result.Failure(new Error(eosError, steamError));
        }
        return Result.Success((P2PSocket)new DualStackP2PSocket(
            callbacks,
            eosP2PSocketResult.TryUnwrapSuccess(out var eosP2PSocket)
                ? Option.Some((EosP2PSocket)eosP2PSocket)
                : Option.None,
            steamP2PSocketResult.TryUnwrapSuccess(out var steamP2PSocket)
                ? Option.Some((SteamListenSocket)steamP2PSocket)
                : Option.None));
    }

    public override void ProcessIncomingMessages()
    {
        if (eosSocket.TryUnwrap(out var eosP2PSocket)) { eosP2PSocket.ProcessIncomingMessages(); }
        if (steamSocket.TryUnwrap(out var steamP2PSocket)) { steamP2PSocket.ProcessIncomingMessages(); }
    }

    public override bool SendMessage(P2PEndpoint endpoint, IWriteMessage outMsg, DeliveryMethod deliveryMethod)
    {
        return endpoint switch
        {
            EosP2PEndpoint eosP2PEndpoint when eosSocket.TryUnwrap(out var eosP2PSocket)
                => eosP2PSocket.SendMessage(eosP2PEndpoint, outMsg, deliveryMethod),
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
            case EosP2PEndpoint eosP2PEndpoint:
                if (eosSocket.TryUnwrap(out var eosP2PSocket))
                {
                    eosP2PSocket.CloseConnection(eosP2PEndpoint);
                }
                break;
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
        if (eosSocket.TryUnwrap(out var eosP2PSocket)) { eosP2PSocket.Dispose(); }
        if (steamSocket.TryUnwrap(out var steamP2PSocket)) { steamP2PSocket.Dispose(); }
    }
}