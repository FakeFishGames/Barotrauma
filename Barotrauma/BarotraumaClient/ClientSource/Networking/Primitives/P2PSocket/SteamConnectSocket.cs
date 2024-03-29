using System;
using System.Runtime.InteropServices;
using Barotrauma.Steam;
namespace Barotrauma.Networking;

sealed class SteamConnectSocket : P2PSocket
{
    private sealed class ConnectionManager : Steamworks.ConnectionManager, Steamworks.IConnectionManager
    {
        private SteamP2PEndpoint endpoint;
        private Callbacks callbacks;
        public void SetEndpointAndCallbacks(SteamP2PEndpoint endpoint, Callbacks callbacks)
        {
            this.endpoint = endpoint;
            this.callbacks = callbacks;
        }

        public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            var dataArray = new byte[size];
            Marshal.Copy(source: data, destination: dataArray, startIndex: 0, length: size);

            callbacks.OnData(endpoint, new ReadWriteMessage(dataArray, bitPos: 0, lBits: size * 8, copyBuf: false));
        }

        public override void OnDisconnected(Steamworks.Data.ConnectionInfo info)
        {
            if (!info.Identity.IsSteamId) { return; }
            var remoteEndpoint = new SteamP2PEndpoint(new SteamId((Steamworks.SteamId)info.Identity));
            var peerDisconnectPacket = PeerDisconnectPacket.WithReason(info.EndReason switch
            {
                Steamworks.NetConnectionEnd.App_Generic => DisconnectReason.Disconnected,
                Steamworks.NetConnectionEnd.AppException_Generic => DisconnectReason.Unknown,

                Steamworks.NetConnectionEnd.Local_OfflineMode => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Local_ManyRelayConnectivity => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Local_HostedServerPrimaryRelay => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Local_NetworkConfig => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Local_Rights => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Local_P2P_ICE_NoPublicAddresses => DisconnectReason.SteamP2PError,

                Steamworks.NetConnectionEnd.Remote_Timeout => DisconnectReason.SteamP2PTimeOut,
                Steamworks.NetConnectionEnd.Remote_BadCrypt => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Remote_BadCert => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Remote_BadProtocolVersion => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Remote_P2P_ICE_NoPublicAddresses => DisconnectReason.SteamP2PError,

                Steamworks.NetConnectionEnd.Misc_Generic => DisconnectReason.Unknown,
                Steamworks.NetConnectionEnd.Misc_InternalError => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Misc_Timeout => DisconnectReason.SteamP2PTimeOut,
                Steamworks.NetConnectionEnd.Misc_SteamConnectivity => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Misc_NoRelaySessionsToClient => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Misc_P2P_Rendezvous => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Misc_P2P_NAT_Firewall => DisconnectReason.SteamP2PError,
                Steamworks.NetConnectionEnd.Misc_PeerSentNoConnection => DisconnectReason.SteamP2PError,

                _ => DisconnectReason.Unknown
            });
            callbacks.OnConnectionClosed(remoteEndpoint, peerDisconnectPacket);
            base.OnDisconnected(info);
        }
    }

    private readonly SteamP2PEndpoint expectedEndpoint;
    private readonly ConnectionManager connectionManager;

    private SteamConnectSocket(SteamP2PEndpoint expectedEndpoint, Callbacks callbacks, ConnectionManager connectionManager) : base(callbacks)
    {
        this.expectedEndpoint = expectedEndpoint;
        this.connectionManager = connectionManager;
    }

    public static Result<P2PSocket, Error> Create(SteamP2PEndpoint endpoint, Callbacks callbacks)
    {
        if (!SteamManager.IsInitialized) { return Result.Failure(new Error(ErrorCode.SteamNotInitialized)); }

        var connectionManager = Steamworks.SteamNetworkingSockets.ConnectRelay<ConnectionManager>(endpoint.SteamId.Value);
        if (connectionManager is null) { return Result.Failure(new Error(ErrorCode.FailedToCreateSteamP2PSocket)); }
        connectionManager.SetEndpointAndCallbacks(endpoint, callbacks);

        return Result.Success((P2PSocket)new SteamConnectSocket(endpoint, callbacks, connectionManager));
    }

    public override void ProcessIncomingMessages()
    {
        connectionManager.Receive();
    }

    public override bool SendMessage(P2PEndpoint endpoint, IWriteMessage outMsg, DeliveryMethod deliveryMethod)
    {
        if (endpoint != expectedEndpoint) { return false; }
        var result = connectionManager.Connection.SendMessage(
            data: outMsg.Buffer,
            offset: 0,
            length: outMsg.LengthBytes,
            sendType: deliveryMethod switch
            {
                DeliveryMethod.Reliable => Steamworks.Data.SendType.Reliable,
                _ => Steamworks.Data.SendType.Unreliable
            });
        return result == Steamworks.Result.OK;
    }

    public override void CloseConnection(P2PEndpoint endpoint)
    {
        if (endpoint != expectedEndpoint) { return; }
        connectionManager.Close();
    }

    public override void Dispose()
    {
        connectionManager.Close();
    }
}