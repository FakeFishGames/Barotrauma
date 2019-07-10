using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Facepunch.Steamworks;

namespace Barotrauma.Networking
{
    class SteamP2PServerPeer : ServerPeer
    {
        private UInt64? SteamID;
        private ServerSettings serverSettings;

        private delegate bool CloseSteamP2PConnectionDelegate(ulong steamId);
        CloseSteamP2PConnectionDelegate CloseSteamP2PConnection;

        private class PendingClient
        {
            public string Name;
            public UInt64 SteamID;
            public ConnectionInitialization InitializationStep;

            public List<IReadMessage> InitializationMessages;

            public PendingClient(UInt64 steamId)
            {
                SteamID = steamId;

                InitializationMessages = new List<IReadMessage>();

                InitializationStep = ConnectionInitialization.SteamTicketAndVersion;
            }
        }

        private struct DisconnectingClient
        {
            public readonly UInt64 SteamID;
            public readonly double CleanupTime;

            public DisconnectingClient(UInt64 steamId)
            {
                SteamID = steamId;
                CleanupTime = Timing.TotalTime + 1.0;
            }
        }

        private List<PendingClient> pendingClients;
        private List<SteamP2PConnection> connectedClients;
        private List<DisconnectingClient> disconnectingClients;
        
        public SteamP2PServerPeer(ServerSettings settings)
        {
            SteamID = null;

            serverSettings = settings;

            pendingClients = new List<PendingClient>();
            connectedClients = new List<SteamP2PConnection>();
            disconnectingClients = new List<DisconnectingClient>();

            CloseSteamP2PConnection = null;
        }

        public override void Start()
        {
            if (SteamID != null) { return; }

            pendingClients.Clear();
            connectedClients.Clear();

            //SteamP2P performs connections through SteamIDs, so we need to initialize a SteamManager client
            Steam.SteamManager.InitializeClient();

            SteamID = Steam.SteamManager.GetSteamID();
        }

        public override void Update()
        {
            if (SteamID == null) { return; }

            for (int i = 0; i < disconnectingClients.Count; i++)
            {
                if (Timing.TotalTime > disconnectingClients[i].CleanupTime)
                {
                    CloseSteamP2PConnection(disconnectingClients[i].SteamID);
                    disconnectingClients.RemoveAt(i); i--;
                }
            }

            throw new NotImplementedException();

        }

        public override void Send(IWriteMessage msg, NetworkConnection conn, DeliveryMethod deliveryMethod)
        {
            if (SteamID == null) { return; }

            throw new NotImplementedException();
        }

        public override void Disconnect(NetworkConnection conn, string msg = null)
        {
            if (SteamID == null) { return; }

            if (!(conn is SteamP2PConnection steamConn)) { return; }
            if (connectedClients.Contains(steamConn))
            {
                connectedClients.Remove(steamConn);
                disconnectingClients.Add(new DisconnectingClient(steamConn.SteamID));
                OnDisconnect?.Invoke(conn, msg);
            }
        }

        public override void Close(string msg = null)
        {
            if (SteamID == null) { return; }

            throw new NotImplementedException();
        }

        public override void InitializeSteamServerCallbacks(Server steamServer)
        {
            steamServer.Networking.OnIncomingConnection = OnIncomingConnection;
            steamServer.Networking.OnP2PData = OnP2PData;

            CloseSteamP2PConnection = steamServer.Networking.CloseSession;
        }

        private void RemovePendingClient(PendingClient pendingClient)
        {
            disconnectingClients.Add(new DisconnectingClient(pendingClient.SteamID));
            pendingClients.Remove(pendingClient);
        }

        private bool OnIncomingConnection(ulong steamId)
        {
            if (SteamID == null) { return false; }

            disconnectingClients.RemoveAll(c => c.SteamID == steamId);

            if (!serverSettings.BanList.IsBanned(steamId))
            {
                if (pendingClients.Any(c => c.SteamID == steamId) || connectedClients.Any(c => c.SteamID == steamId))
                {
                    return false;
                }
                PendingClient pendingClient = new PendingClient(steamId);
                pendingClients.Add(pendingClient);

                return true;
            }

            return false;
        }

        private void OnP2PData(ulong steamId, byte[] data, int dataLength, int channel)
        {
            bool isCompressed = (data[0] & 0x1) != 0;
            bool isConnectionInitializationStep = (data[0] & 0x2) != 0;
            bool isDisconnecting = (data[0] & 0x4) != 0;

            PendingClient pendingClient = pendingClients.Find(c => c.SteamID == steamId);

            if (isDisconnecting)
            {
                if (pendingClient != null)
                {
                    RemovePendingClient(pendingClient);
                }
                else
                {
                    SteamP2PConnection conn = connectedClients.Find(c => c.SteamID == steamId);
                    Disconnect(conn, "Disconnected");
                }
                return;
            }

            if (isConnectionInitializationStep)
            {
                if (pendingClient == null) { return; }
                pendingClient.InitializationMessages.Add(new ReadOnlyMessage(data, isCompressed, 1, dataLength - 1, null));
            }
            else
            {
                if (pendingClient != null)
                {
                    RemovePendingClient(pendingClient);
                    return;
                }
            }
        }
    }
}
