using Barotrauma.Steam;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Steamworks.Data;
using Color = Microsoft.Xna.Framework.Color;
using Socket = System.Net.Sockets.Socket;

namespace Barotrauma.Networking
{
    static class PingUtils
    {
        private static readonly Dictionary<IPEndPoint, int> activePings = new Dictionary<IPEndPoint, int>();

        private static bool steamPingInfoReady;

        public static void QueryPingData()
        {
            steamPingInfoReady = false;
            if (SteamManager.IsInitialized)
            {
                TaskPool.Add("WaitForPingDataAsync (serverlist)", Steamworks.SteamNetworkingUtils.WaitForPingDataAsync(), task =>
                {
                    steamPingInfoReady = true;
                });
            }
        }

        public static void GetServerPing(ServerInfo serverInfo, Action<ServerInfo> onPingDiscovered)
        {
            if (CoroutineManager.IsCoroutineRunning("ConnectToServer")) { return; }

            switch (serverInfo.Endpoint)
            {
                case LidgrenEndpoint { NetEndpoint: var endPoint }:

                    GetIPAddressPing(serverInfo, endPoint, onPingDiscovered);
                    break;
                case SteamP2PEndpoint steamP2PEndpoint:
                    TaskPool.Add($"EstimateSteamLobbyPing ({steamP2PEndpoint.StringRepresentation})",
                        EstimateSteamLobbyPing(serverInfo),
                        t =>
                        {
                            if (!t.TryGetResult(out Option<int> ping)) { return; }
                            serverInfo.Ping = ping;
                            onPingDiscovered(serverInfo);
                        });
                    break;
            }
        }

        private readonly struct LobbyDataChangedEventHandler : IDisposable
        {
            private readonly Action<Lobby> action;
            
            public LobbyDataChangedEventHandler(Action<Lobby> action)
            {
                this.action = action;
                Steamworks.SteamMatchmaking.OnLobbyDataChanged += action;
            }

            public void Dispose()
            {
                Steamworks.SteamMatchmaking.OnLobbyDataChanged -= action;
            }
        }

        public static async Task<Lobby?> GetSteamLobbyForUser(SteamId steamId)
        {
            var steamFriend = new Steamworks.Friend(steamId.Value);
            await steamFriend.RequestInfoAsync();

            var friendLobby = steamFriend.GameInfo?.Lobby;
            if (!(friendLobby is { } lobby)) { return null; }

            bool waiting = true;
            Lobby loadedLobby = default;

            void finishWaiting(Steamworks.Data.Lobby l)
            {
                loadedLobby = l;
                waiting = false;
            }

            using (new LobbyDataChangedEventHandler(finishWaiting))
            {
                lobby.Refresh();

                for (int i = 0;; i++)
                {
                    if (!waiting) { break; }
                    if (i >= 100) { return null; }
                }
            }

            return loadedLobby;
        }
        
        private static async Task<Option<int>> EstimateSteamLobbyPing(ServerInfo serverInfo)
        {
            if (!(serverInfo.Endpoint is SteamP2PEndpoint { SteamId: var ownerId })) { return Option<int>.None(); }
            while (!steamPingInfoReady) { await Task.Delay(50); }

            Lobby lobby;

            if (serverInfo.MetadataSource.TryUnwrap(out SteamP2PServerProvider.DataSource src))
            {
                lobby = src.Lobby;
            }
            else
            {
                var friendLobby = await GetSteamLobbyForUser(ownerId);
                if (friendLobby is null) { return Option<int>.None(); }
                lobby = friendLobby.Value;
            }

            var pingLocation = NetPingLocation.TryParseFromString(lobby.GetData("pinglocation"));
            
            if (pingLocation.HasValue && Steamworks.SteamNetworkingUtils.LocalPingLocation.HasValue)
            {
                int ping = Steamworks.SteamNetworkingUtils.LocalPingLocation.Value.EstimatePingTo(pingLocation.Value);
                return ping >= 0 ? Option<int>.Some(ping) : Option<int>.None();
            }
            else
            {
                return Option<int>.None();
            }
        }

        private static void GetIPAddressPing(ServerInfo serverInfo, IPEndPoint endPoint, Action<ServerInfo> onPingDiscovered)
        {
            if (IPAddress.IsLoopback(endPoint.Address))
            {
                serverInfo.Ping = Option<int>.Some(0);
                onPingDiscovered(serverInfo);
            }
            else
            {
                lock (activePings)
                {
                    if (activePings.ContainsKey(endPoint)) { return; }
                    activePings.Add(endPoint, activePings.Any() ? activePings.Values.Max() + 1 : 0);
                }
                serverInfo.Ping = Option<int>.None();
                TaskPool.Add($"PingServerAsync ({endPoint})", PingServerAsync(endPoint, 1000),
                    rtt =>
                    {
                        if (!rtt.TryGetResult(out serverInfo.Ping)) { serverInfo.Ping = Option<int>.None(); }
                        onPingDiscovered(serverInfo);
                        lock (activePings)
                        {
                            activePings.Remove(endPoint);
                        }
                    });
            }
        }

        private static async Task<Option<int>> PingServerAsync(IPEndPoint endPoint, int timeOut)
        {
            await Task.Yield();
            bool shouldGo = false;
            while (!shouldGo)
            {
                lock (activePings)
                {
                    shouldGo = activePings.Count(kvp => kvp.Value < activePings[endPoint]) < 25;
                }
                await Task.Delay(25);
            }

            if (endPoint?.Address == null) { return Option<int>.None(); }
            
            //don't attempt to ping if the address is IPv6 and it's not supported
            if (endPoint.Address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6) { return Option<int>.None(); }
            
            Ping ping = new Ping();
            byte[] buffer = new byte[32];
            try
            {
                PingReply pingReply = await ping.SendPingAsync(endPoint.Address, timeOut, buffer, new PingOptions(128, true));

                return pingReply.Status switch
                {
                    IPStatus.Success => Option<int>.Some((int)pingReply.RoundtripTime),
                    _ => Option<int>.None(),
                };
            }
            catch (Exception ex)
            {
                GameAnalyticsManager.AddErrorEventOnce("ServerListScreen.PingServer:PingException" + endPoint.Address, GameAnalyticsManager.ErrorSeverity.Warning, "Failed to ping a server - " + (ex?.InnerException?.Message ?? ex.Message));
#if DEBUG
                DebugConsole.NewMessage("Failed to ping a server (" + endPoint.Address + ") - " + (ex?.InnerException?.Message ?? ex.Message), Color.Red);
#endif

                return Option<int>.None();
            }
        }
    }
}
