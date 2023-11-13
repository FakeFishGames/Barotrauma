#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.Networking;
using Barotrauma.Steam;

namespace Barotrauma
{
    sealed class SteamP2PServerProvider : ServerProvider
    {
        public class DataSource : ServerInfo.DataSource
        {
            public readonly Steamworks.Data.Lobby Lobby;

            public override void Write(XElement element) { /* do nothing */ }

            public DataSource(Steamworks.Data.Lobby lobby)
            {
                Lobby = lobby;
            }
        }

        private object? queryRef = null;
        
        protected override void RetrieveServersImpl(Action<ServerInfo> onServerDataReceived, Action onQueryCompleted)
        {
            if (!SteamManager.IsInitialized)
            {
                onQueryCompleted();
                return;
            }
            
            // All lambdas and local methods in here must only capture
            // this call's query, not the provider's latest query
            var selfQueryRef = new object();
            queryRef = selfQueryRef;
            
            Steamworks.Data.LobbyQuery lobbyQuery = Steamworks.SteamMatchmaking.CreateLobbyQuery()
                .FilterDistanceWorldwide()
                .WithMaxResults(50);
            // Steamworks is unable to retrieve more than 50 lobbies per request
            // (see https://partner.steamgames.com/doc/features/multiplayer/matchmaking#3)
            // To work around this, we'll make up to 10 requests, asking to ignore
            // all previous results in each subsequent request.
            #warning TODO: do something less horrible here?
            
            int requestCount = 0;
            HashSet<SteamId> retrieved = new HashSet<SteamId>();

            void startQuery()
            {
                if (requestCount >= 10) { return; }
                requestCount++;
                TaskPool.Add($"LobbyQuery.RequestAsync ({requestCount})", lobbyQuery.RequestAsync(), onRequestComplete);
            }
            
            void onRequestComplete(Task t)
            {
                // If queryRef != selfQueryRef, this query was cancelled
                if (!ReferenceEquals(selfQueryRef, queryRef)) { return; }
                
                if (!t.TryGetResult(out Steamworks.Data.Lobby[] lobbies)
                    || lobbies is null
                    || lobbies.Length == 0)
                {
                    onQueryCompleted();
                    return;
                }

                foreach (var lobby in lobbies)
                {
                    string lobbyOwnerStr = lobby.GetData("lobbyowner") ?? "";
                    lobbyQuery = lobbyQuery.WithoutKeyValue("lobbyowner", lobbyOwnerStr);

                    string serverName = lobby.GetData("name") ?? "";
                    if (string.IsNullOrEmpty(serverName)) { continue; }

                    var ownerId = SteamId.Parse(lobbyOwnerStr);
                    if (!ownerId.TryUnwrap(out var lobbyOwnerId)) { continue; }
                    
                    if (retrieved.Contains(lobbyOwnerId)) { continue; }
                    retrieved.Add(lobbyOwnerId);

                    var serverInfo = new ServerInfo(new SteamP2PEndpoint(lobbyOwnerId))
                    {
                        ServerName = serverName,
                        MetadataSource = Option<ServerInfo.DataSource>.Some(new DataSource(lobby))
                    };
                    serverInfo.UpdateInfo(key => lobby.GetData(key));
                    serverInfo.Checked = true;
                    
                    onServerDataReceived(serverInfo);
                }
                startQuery();
            }
            
            startQuery();
        }

        public override void Cancel()
        {
            queryRef = null;
        }
    }
}