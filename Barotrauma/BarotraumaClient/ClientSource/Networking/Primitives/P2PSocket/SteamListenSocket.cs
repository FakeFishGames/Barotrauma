using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Barotrauma.Steam;

namespace Barotrauma.Networking;

sealed class SteamListenSocket : P2PSocket
{
    private sealed class SocketManager : Steamworks.SocketManager, Steamworks.ISocketManager
    {
        private Callbacks callbacks;
        private readonly Dictionary<SteamP2PEndpoint, Steamworks.Data.Connection> endpointToConnection = new();

        public void SetCallbacks(Callbacks callbacks)
        {
            this.callbacks = callbacks;
        }

        public override void OnConnecting(Steamworks.Data.Connection connection, Steamworks.Data.ConnectionInfo info)
        {
            if (!info.Identity.IsSteamId) { return; }
            var remoteEndpoint = new SteamP2PEndpoint(new SteamId((Steamworks.SteamId)info.Identity));
            endpointToConnection[remoteEndpoint] = connection;
            if (callbacks.OnIncomingConnection(remoteEndpoint))
            {
                connection.Accept();
            }
        }

        public override void OnDisconnected(Steamworks.Data.Connection connection, Steamworks.Data.ConnectionInfo info)
        {
            if (!info.Identity.IsSteamId) { return; }
            var remoteEndpoint = new SteamP2PEndpoint(new SteamId((Steamworks.SteamId)info.Identity));
            endpointToConnection.Remove(remoteEndpoint);
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
            base.OnDisconnected(connection, info);
        }
        
        public override void OnMessage(Steamworks.Data.Connection connection, Steamworks.Data.NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            if (!identity.IsSteamId || data == IntPtr.Zero) { return; }
            var endpoint = new SteamP2PEndpoint(new SteamId((Steamworks.SteamId)identity));

            var dataArray = new byte[size];
            Marshal.Copy(source: data, destination: dataArray, startIndex: 0, length: size);

            callbacks.OnData(endpoint, new ReadWriteMessage(dataArray, bitPos: 0, lBits: size * 8, copyBuf: false));
        }

        internal bool SendMessage(SteamP2PEndpoint endpoint, IWriteMessage outMsg, DeliveryMethod deliveryMethod)
        {
            if (!endpointToConnection.TryGetValue(endpoint, out var connection))
            {
                return false;
            }

            var result = connection.SendMessage(
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

        internal void CloseConnection(SteamP2PEndpoint endpoint)
        {
            if (!endpointToConnection.TryGetValue(endpoint, out var connection)) { return; }
            connection.Close();
        }
    }
    
    private readonly SocketManager socketManager;
    
    private SteamListenSocket(
        Callbacks callbacks,
        SocketManager socketManager)
        : base(callbacks)
    {
        this.socketManager = socketManager;
    }

    public static Result<P2PSocket, Error> Create(Callbacks callbacks)
    {
        if (!SteamManager.IsInitialized) { return Result.Failure(new Error(ErrorCode.SteamNotInitialized)); }

        var socketManager = Steamworks.SteamNetworkingSockets.CreateRelaySocket<SocketManager>();
        if (socketManager is null) { return Result.Failure(new Error(ErrorCode.FailedToCreateSteamP2PSocket)); }
        socketManager.SetCallbacks(callbacks);

        return Result.Success((P2PSocket)new SteamListenSocket(callbacks, socketManager));
    }

    public override void ProcessIncomingMessages()
    {
        socketManager.Receive();
    }

    public override bool SendMessage(P2PEndpoint endpoint, IWriteMessage outMsg, DeliveryMethod deliveryMethod)
    {
        if (endpoint is not SteamP2PEndpoint steamP2PEndpoint) { return false; }
        return socketManager.SendMessage(steamP2PEndpoint, outMsg, deliveryMethod);
    }

    public override void CloseConnection(P2PEndpoint endpoint)
    {
        if (endpoint is not SteamP2PEndpoint steamP2PEndpoint) { return; }
        socketManager.CloseConnection(steamP2PEndpoint);
    }

    public override void Dispose()
    {
        socketManager.Close();
    }
}