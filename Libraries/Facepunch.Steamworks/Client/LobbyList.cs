using System;
using System.Collections.Generic;
using System.Text;
using SteamNative;


//TODO: this entire file is the main reason why we need to update this library
namespace Facepunch.Steamworks
{
    public partial class LobbyList : IDisposable
    {
        internal Client client;

        public Action<Lobby> OnLobbyDataReceived;

        internal LobbyList(Client client)
        {
            client.RegisterCallback<SteamNative.LobbyDataUpdate_t>(OnLobbyDataUpdated);

            this.client = client;
        }

        List<ulong> pendingCallbacks = new List<ulong>();
        public void Update()
        {
            lock (pendingCallbacks)
            {
                foreach (ulong lobbyId in pendingCallbacks)
                {
                    OnLobbyDataReceived?.Invoke(Lobby.FromSteam(client, lobbyId));
                }
                pendingCallbacks.Clear();
            }
        }

        /// <summary>
        /// Refresh the List of Lobbies. If no filter is passed in, a default one is created that filters based on AppId ("appid").
        /// </summary>
        /// <param name="filter"></param>
        public void Request(Filter filter = null)
        {
            //init out values
            if (filter == null)
            {
                filter = new Filter();
                filter.StringFilters.Add("appid", client.AppId.ToString());
                filter.DistanceFilter = Filter.Distance.Worldwide;
            }

            client.native.matchmaking.AddRequestLobbyListDistanceFilter((SteamNative.LobbyDistanceFilter)filter.DistanceFilter);

            if (filter.SlotsAvailable != null)
            {
                client.native.matchmaking.AddRequestLobbyListFilterSlotsAvailable((int)filter.SlotsAvailable);
            }

            if (filter.MaxResults != null)
            {
                client.native.matchmaking.AddRequestLobbyListResultCountFilter((int)filter.MaxResults);
            }

            foreach (KeyValuePair<string, string> fil in filter.StringFilters)
            {
                client.native.matchmaking.AddRequestLobbyListStringFilter(fil.Key, fil.Value, SteamNative.LobbyComparison.Equal);
            }
            foreach (KeyValuePair<string, int> fil in filter.NearFilters)
            {
                client.native.matchmaking.AddRequestLobbyListNearValueFilter(fil.Key, fil.Value);
            }

            // this will never return lobbies that are full (via the actual api)
            client.native.matchmaking.RequestLobbyList(OnLobbyList);

        }

        void OnLobbyList(LobbyMatchList_t callback, bool error)
        {
            if (error) return;

            //how many lobbies matched
            uint lobbiesMatching = callback.LobbiesMatching;

            // lobbies are returned in order of closeness to the user, so add them to the list in that order
            for (int i = 0; i < lobbiesMatching; i++)
            {
                //request lobby data
                ulong lobby = client.native.matchmaking.GetLobbyByIndex(i);

                client.native.matchmaking.RequestLobbyData(lobby);

            }
        }

        void OnLobbyDataUpdated(LobbyDataUpdate_t callback)
        {
            if (callback.Success == 1) //1 if success, 0 if failure
            {
                lock (pendingCallbacks)
                {
                    pendingCallbacks.Add(callback.SteamIDLobby);
                }
            }
        }

        public Lobby GetLobbyFromID(ulong lobbyId)
        {
            return Lobby.FromSteam(client, lobbyId);
        }

        public void RequestLobbyData(ulong lobby)
        {
            client.native.matchmaking.RequestLobbyData(lobby);
        }

        public void Dispose()
        {
            client = null;
        }

        public class Filter
        {
            // Filters that match actual metadata keys exactly
            public Dictionary<string, string> StringFilters = new Dictionary<string, string>();
            // Filters that are of string key and int value for that key to be close to
            public Dictionary<string, int> NearFilters = new Dictionary<string, int>();
            //Filters that are of string key and int value, with a comparison filter to say how we should relate to the value
            //public Dictionary<string, KeyValuePair<Comparison, int>> NumericalFilters = new Dictionary<string, KeyValuePair<Comparison, int>>();
            public Distance DistanceFilter = Distance.Worldwide;
            public int? SlotsAvailable { get; set; }
            public int? MaxResults { get; set; }

            public enum Distance : int
            {
                Close = SteamNative.LobbyDistanceFilter.Close,
                Default = SteamNative.LobbyDistanceFilter.Default,
                Far = SteamNative.LobbyDistanceFilter.Far,
                Worldwide = SteamNative.LobbyDistanceFilter.Worldwide
            }

            public enum Comparison : int
            {
                EqualToOrLessThan = SteamNative.LobbyComparison.EqualToOrLessThan,
                LessThan = SteamNative.LobbyComparison.LessThan,
                Equal = SteamNative.LobbyComparison.Equal,
                GreaterThan = SteamNative.LobbyComparison.GreaterThan,
                EqualToOrGreaterThan = SteamNative.LobbyComparison.EqualToOrGreaterThan,
                NotEqual = SteamNative.LobbyComparison.NotEqual
            }
        }
    }
}
