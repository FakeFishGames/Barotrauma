using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using SteamNative;

namespace Facepunch.Steamworks
{
    //ISteamMatchmakingPlayersResponse taken from:
    //  https://github.com/rlabrecque/Steamworks.NET/blob/master/Plugins/Steamworks.NET/ISteamMatchmakingResponses.cs

    /**
         
    The MIT License (MIT)

    Copyright (c) 2013-2019 Riley Labrecque

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
        
    **/
    public class ISteamMatchmakingRulesResponse
    {
        // Got data on a rule on the server -- you'll get one of these per rule defined on
        // the server you are querying
        public delegate void RulesResponded(string pchRule, string pchValue);

        // The server failed to respond to the request for rule details
        public delegate void RulesFailedToRespond();

        // The server has finished responding to the rule details request 
        // (ie, you won't get anymore RulesResponded callbacks)
        public delegate void RulesRefreshComplete();

        private VTable m_VTable;
        private IntPtr m_pVTable;
        private GCHandle m_pGCHandle;
        private RulesResponded m_RulesResponded;
        private RulesFailedToRespond m_RulesFailedToRespond;
        private RulesRefreshComplete m_RulesRefreshComplete;

        public ISteamMatchmakingRulesResponse(RulesResponded onRulesResponded, RulesFailedToRespond onRulesFailedToRespond, RulesRefreshComplete onRulesRefreshComplete)
        {
            if (onRulesResponded == null || onRulesFailedToRespond == null || onRulesRefreshComplete == null)
            {
                throw new ArgumentNullException();
            }
            m_RulesResponded = onRulesResponded;
            m_RulesFailedToRespond = onRulesFailedToRespond;
            m_RulesRefreshComplete = onRulesRefreshComplete;

            m_VTable = new VTable()
            {
                m_VTRulesResponded = InternalOnRulesResponded,
                m_VTRulesFailedToRespond = InternalOnRulesFailedToRespond,
                m_VTRulesRefreshComplete = InternalOnRulesRefreshComplete
            };
            m_pVTable = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(VTable)));
            Marshal.StructureToPtr(m_VTable, m_pVTable, false);

            m_pGCHandle = GCHandle.Alloc(m_pVTable, GCHandleType.Pinned);
        }

        ~ISteamMatchmakingRulesResponse()
        {
            if (m_pVTable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(m_pVTable);
            }

            if (m_pGCHandle.IsAllocated)
            {
                m_pGCHandle.Free();
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalRulesResponded(IntPtr thisptr, IntPtr pchRule, IntPtr pchValue);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalRulesFailedToRespond(IntPtr thisptr);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalRulesRefreshComplete(IntPtr thisptr);
        private void InternalOnRulesResponded(IntPtr thisptr, IntPtr pchRule, IntPtr pchValue)
        {
            List<byte> bytes = new List<byte>();
            IntPtr seekPointer = pchRule;
            byte b = Marshal.ReadByte(seekPointer);
            while (b != 0)
            {
                bytes.Add(b);
                seekPointer = (IntPtr)(seekPointer.ToInt64() + sizeof(byte));
                b = Marshal.ReadByte(seekPointer);
            }

            string pchRuleDecoded = Encoding.UTF8.GetString(bytes.ToArray());

            bytes.Clear();
            seekPointer = pchValue;
            b = Marshal.ReadByte(seekPointer);
            while (b != 0)
            {
                bytes.Add(b);
                seekPointer = (IntPtr)(seekPointer.ToInt64() + sizeof(byte));
                b = Marshal.ReadByte(seekPointer);
            }

            string pchValueDecoded = Encoding.UTF8.GetString(bytes.ToArray());

            m_RulesResponded(pchRuleDecoded, pchValueDecoded);
        }
        private void InternalOnRulesFailedToRespond(IntPtr thisptr)
        {
            m_RulesFailedToRespond();
        }
        private void InternalOnRulesRefreshComplete(IntPtr thisptr)
        {
            m_RulesRefreshComplete();
        }

        [StructLayout(LayoutKind.Sequential)]
        private class VTable
        {
            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalRulesResponded m_VTRulesResponded;

            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalRulesFailedToRespond m_VTRulesFailedToRespond;

            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalRulesRefreshComplete m_VTRulesRefreshComplete;
        }

        public static explicit operator System.IntPtr(ISteamMatchmakingRulesResponse that)
        {
            return that.m_pGCHandle.AddrOfPinnedObject();
        }
    };

    public class ISteamMatchmakingPlayersResponse
    {
        // Got data on a new player on the server -- you'll get this callback once per player
        // on the server which you have requested player data on.
        public delegate void AddPlayerToList(string pchName, int nScore, float flTimePlayed);

        // The server failed to respond to the request for player details
        public delegate void PlayersFailedToRespond();

        // The server has finished responding to the player details request 
        // (ie, you won't get anymore AddPlayerToList callbacks)
        public delegate void PlayersRefreshComplete();

        private VTable m_VTable;
        private IntPtr m_pVTable;
        private GCHandle m_pGCHandle;
        private AddPlayerToList m_AddPlayerToList;
        private PlayersFailedToRespond m_PlayersFailedToRespond;
        private PlayersRefreshComplete m_PlayersRefreshComplete;

        public ISteamMatchmakingPlayersResponse(AddPlayerToList onAddPlayerToList, PlayersFailedToRespond onPlayersFailedToRespond, PlayersRefreshComplete onPlayersRefreshComplete)
        {
            if (onAddPlayerToList == null || onPlayersFailedToRespond == null || onPlayersRefreshComplete == null)
            {
                throw new ArgumentNullException();
            }
            m_AddPlayerToList = onAddPlayerToList;
            m_PlayersFailedToRespond = onPlayersFailedToRespond;
            m_PlayersRefreshComplete = onPlayersRefreshComplete;

            m_VTable = new VTable()
            {
                m_VTAddPlayerToList = InternalOnAddPlayerToList,
                m_VTPlayersFailedToRespond = InternalOnPlayersFailedToRespond,
                m_VTPlayersRefreshComplete = InternalOnPlayersRefreshComplete
            };
            m_pVTable = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(VTable)));
            Marshal.StructureToPtr(m_VTable, m_pVTable, false);

            m_pGCHandle = GCHandle.Alloc(m_pVTable, GCHandleType.Pinned);
        }

        ~ISteamMatchmakingPlayersResponse()
        {
            if (m_pVTable != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(m_pVTable);
            }

            if (m_pGCHandle.IsAllocated)
            {
                m_pGCHandle.Free();
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalAddPlayerToList(IntPtr thisptr, IntPtr pchName, int nScore, float flTimePlayed);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalPlayersFailedToRespond(IntPtr thisptr);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalPlayersRefreshComplete(IntPtr thisptr);
        private void InternalOnAddPlayerToList(IntPtr thisptr, IntPtr pchName, int nScore, float flTimePlayed)
        {
            List<byte> bytes = new List<byte>();
            IntPtr seekPointer = pchName;
            byte b = Marshal.ReadByte(seekPointer);
            while (b != 0)
            {
                bytes.Add(b);
                seekPointer = (IntPtr)(seekPointer.ToInt64() + sizeof(byte));
                b = Marshal.ReadByte(seekPointer);
            }

            string pchNameDecoded = Encoding.UTF8.GetString(bytes.ToArray());

            m_AddPlayerToList(pchNameDecoded, nScore, flTimePlayed);
        }
        private void InternalOnPlayersFailedToRespond(IntPtr thisptr)
        {
            m_PlayersFailedToRespond();
        }
        private void InternalOnPlayersRefreshComplete(IntPtr thisptr)
        {
            m_PlayersRefreshComplete();
        }

        [StructLayout(LayoutKind.Sequential)]
        private class VTable
        {
            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalAddPlayerToList m_VTAddPlayerToList;

            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalPlayersFailedToRespond m_VTPlayersFailedToRespond;

            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalPlayersRefreshComplete m_VTPlayersRefreshComplete;
        }

        public static explicit operator System.IntPtr(ISteamMatchmakingPlayersResponse that)
        {
            return that.m_pGCHandle.AddrOfPinnedObject();
        }
    };

    public partial class ServerList : IDisposable
    {
        internal Client client;

        internal ServerList( Client client )
        {
            this.client = client;

            UpdateFavouriteList();
        }

        HashSet<ulong> FavouriteHash = new HashSet<ulong>();
        HashSet<ulong> HistoryHash = new HashSet<ulong>();

        internal void UpdateFavouriteList()
        {
            FavouriteHash.Clear();
            HistoryHash.Clear();

            for ( int i=0; i< client.native.matchmaking.GetFavoriteGameCount(); i++ )
            {
                AppId_t appid = 0;
                uint ip;
                ushort conPort;
                ushort queryPort;
                uint lastplayed;
                uint flags;

                client.native.matchmaking.GetFavoriteGame( i, ref appid, out ip, out conPort, out queryPort, out flags, out lastplayed );

                ulong encoded = ip;
                encoded = encoded << 32;
                encoded = encoded | (uint)conPort;

                if ( ( flags & Server.k_unFavoriteFlagFavorite ) == Server.k_unFavoriteFlagFavorite )
                    FavouriteHash.Add( encoded );

                if ( ( flags & Server.k_unFavoriteFlagFavorite ) == Server.k_unFavoriteFlagFavorite )
                    HistoryHash.Add( encoded );
            }
        }

        public void Dispose()
        {
            client = null;
        }

        public class Filter : List<KeyValuePair<string, string>>
        {
            public void Add( string k, string v )
            {
                Add( new KeyValuePair<string, string>( k, v ) );
            }

            internal IntPtr NativeArray;
            private IntPtr m_pArrayEntries;

            private int AppId = 0;

            internal void Start()
            {
                var filters = this.Select( x =>
                {
                    if ( x.Key == "appid" ) AppId = int.Parse( x.Value );

                    return new SteamNative.MatchMakingKeyValuePair_t()
                    {
                        Key  = x.Key,
                        Value = x.Value
                    };
                } ).ToArray();

                int sizeOfMMKVP = Marshal.SizeOf(typeof(SteamNative.MatchMakingKeyValuePair_t));
                NativeArray = Marshal.AllocHGlobal( Marshal.SizeOf( typeof( IntPtr ) ) * filters.Length );
                m_pArrayEntries = Marshal.AllocHGlobal( sizeOfMMKVP * filters.Length );

                for ( int i = 0; i < filters.Length; ++i )
                {
                    Marshal.StructureToPtr( filters[i], new IntPtr( m_pArrayEntries.ToInt64() + ( i * sizeOfMMKVP ) ), false );
                }

                Marshal.WriteIntPtr( NativeArray, m_pArrayEntries );
            }

            internal void Free()
            {
                if ( m_pArrayEntries != IntPtr.Zero )
                {
                    Marshal.FreeHGlobal( m_pArrayEntries );
                }

                if ( NativeArray != IntPtr.Zero )
                {
                    Marshal.FreeHGlobal( NativeArray );
                }
            }

            internal bool Test( gameserveritem_t info )
            {
                if ( AppId != 0 && AppId != info.AppID )
                    return false;

                return true;
            }
        }

 

        [StructLayout( LayoutKind.Sequential )]
        private struct MatchPair
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string key;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string value;
        }

        public int RequestSpecificServer(ISteamMatchmakingRulesResponse response, IPAddress ip, int port)
        {
            return client.native.servers.ServerRules(ip.IpToInt32(), (ushort)port, (IntPtr)response).Value;
        }

        public Request Internet( Filter filter = null )
        {
            if ( filter == null )
            {
                filter = new Filter();
                filter.Add( "appid", client.AppId.ToString() );
            }

            filter.Start();

            var request = new Request( client );
            request.Filter = filter;
            request.AddRequest( client.native.servers.RequestInternetServerList( client.AppId, filter.NativeArray, (uint) filter.Count, IntPtr.Zero ) );

            filter.Free();

            return request;
        }

        /// <summary>
        /// Query a list of addresses. No filters applied.
        /// </summary>
        public Request Custom( IEnumerable<string> serverList )
        {
            var request = new Request( client );
            request.ServerList = serverList;
            request.StartCustomQuery();
            return request;
        }

        /// <summary>
        /// Request a list of servers we've been on. History isn't applied automatically
        /// You need to call server.AddtoHistoryList() when you join a server etc.
        /// </summary>
        public Request History( Filter filter = null )
        {
            if ( filter == null )
            {
                filter = new Filter();
                filter.Add( "appid", client.AppId.ToString() );
            }

            filter.Start();

            var request = new Request( client );
            request.Filter = filter;
            request.AddRequest( client.native.servers.RequestHistoryServerList( client.AppId, filter.NativeArray, (uint)filter.Count, IntPtr.Zero ) );

            filter.Free();

            return request;
        }

        /// <summary>
        /// Request a list of servers we've favourited
        /// </summary>
        public Request Favourites( Filter filter = null )
        {
            if ( filter == null )
            {
                filter = new Filter();
                filter.Add( "appid", client.AppId.ToString() );
            }

            filter.Start();

            var request = new Request( client );
            request.Filter = filter;
            request.AddRequest( client.native.servers.RequestFavoritesServerList( client.AppId, filter.NativeArray, (uint)filter.Count, IntPtr.Zero ) );

            filter.Free();

            return request;
        }

        /// <summary>
        /// Request a list of servers that our friends are on
        /// </summary>
        public Request Friends( Filter filter = null )
        {
            if ( filter == null )
            {
                filter = new Filter();
                filter.Add( "appid", client.AppId.ToString() );
            }

            filter.Start();

            var request = new Request( client );
            request.Filter = filter;
            request.AddRequest( client.native.servers.RequestFriendsServerList( client.AppId, filter.NativeArray, (uint)filter.Count, IntPtr.Zero ) );

            filter.Free();

            return request;
        }

        /// <summary>
        /// Request a list of servers that are running on our LAN
        /// </summary>
        public Request Local( Filter filter = null )
        {
            if ( filter == null )
            {
                filter = new Filter();
                filter.Add( "appid", client.AppId.ToString() );
            }

            filter.Start();

            var request = new Request( client );
            request.Filter = filter;
            request.AddRequest( client.native.servers.RequestLANServerList( client.AppId, IntPtr.Zero ) );

            filter.Free();

            return request;
        }


        internal bool IsFavourite( Server server )
        {
            ulong encoded = Utility.IpToInt32( server.Address );
            encoded = encoded << 32;
            encoded = encoded | (uint)server.ConnectionPort;

            return FavouriteHash.Contains( encoded );
        }

        internal bool IsHistory( Server server )
        {
            ulong encoded = Utility.IpToInt32( server.Address );
            encoded = encoded << 32;
            encoded = encoded | (uint)server.ConnectionPort;

            return HistoryHash.Contains( encoded );
        }
    }
}
