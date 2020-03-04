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

using System;
using System.Net;
using System.Runtime.InteropServices;

namespace Steamworks
{
    //-----------------------------------------------------------------------------
    // Purpose: Callback interface for receiving responses after pinging an individual server 
    //
    // These callbacks all occur in response to querying an individual server
    // via the ISteamMatchmakingServers()->PingServer() call below.  If you are 
    // destructing an object that implements this interface then you should call 
    // ISteamMatchmakingServers()->CancelServerQuery() passing in the handle to the query
    // which is in progress.  Failure to cancel in progress queries when destructing
    // a callback handler may result in a crash when a callback later occurs.
    //-----------------------------------------------------------------------------
    public class ISteamMatchmakingPingResponse
    {
        // Server has responded successfully and has updated data
        public delegate void ServerResponded(Steamworks.Data.ServerInfo server);

        // Server failed to respond to the ping request
        public delegate void ServerFailedToRespond();

        private VTable m_VTable;
        private IntPtr m_pVTable;
        private GCHandle m_pGCHandle;
        private ServerResponded m_ServerResponded;
        private ServerFailedToRespond m_ServerFailedToRespond;

        public ISteamMatchmakingPingResponse(ServerResponded onServerResponded, ServerFailedToRespond onServerFailedToRespond)
        {
            if (onServerResponded == null || onServerFailedToRespond == null)
            {
                throw new ArgumentNullException();
            }
            m_ServerResponded = onServerResponded;
            m_ServerFailedToRespond = onServerFailedToRespond;

            m_VTable = new VTable()
            {
                m_VTServerResponded = InternalOnServerResponded,
                m_VTServerFailedToRespond = InternalOnServerFailedToRespond,
            };
            m_pVTable = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(VTable)));
            Marshal.StructureToPtr(m_VTable, m_pVTable, false);

            m_pGCHandle = GCHandle.Alloc(m_pVTable, GCHandleType.Pinned);
        }

        private Data.HServerQuery hserverPing = 0;
        public bool QueryActive { get { return hserverPing != 0; } }

        public void Cancel()
        {
            if (hserverPing != 0) { ServerList.Base.Internal.CancelServerQuery(hserverPing); }
            hserverPing = 0;
        }

        public void HQueryPing(IPAddress ip, int port)
        {
            hserverPing = ServerList.Base.Internal.PingServer(ip.IpToInt32(), (ushort)port, (IntPtr)this);
        }

        ~ISteamMatchmakingPingResponse()
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

#if NOTHISPTR
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate void InternalServerResponded(gameserveritem_t server);
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate void InternalServerFailedToRespond();
		private void InternalOnServerResponded(gameserveritem_t server) {
			m_ServerResponded(server);
		}
		private void InternalOnServerFailedToRespond() {
			m_ServerFailedToRespond();
		}
#else
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void InternalServerResponded(IntPtr thisptr, Steamworks.Data.gameserveritem_t server);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void InternalServerFailedToRespond(IntPtr thisptr);
        private void InternalOnServerResponded(IntPtr thisptr, Steamworks.Data.gameserveritem_t server)
        {
            hserverPing = 0;

            m_ServerResponded(Steamworks.Data.ServerInfo.From(server));
        }
        private void InternalOnServerFailedToRespond(IntPtr thisptr)
        {
            hserverPing = 0;

            m_ServerFailedToRespond();
        }
#endif

        [StructLayout(LayoutKind.Sequential)]
        private class VTable
        {
            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalServerResponded m_VTServerResponded;

            [NonSerialized]
            [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalServerFailedToRespond m_VTServerFailedToRespond;
        }

        public static explicit operator System.IntPtr(ISteamMatchmakingPingResponse that)
        {
            return that.m_pGCHandle.AddrOfPinnedObject();
        }
    };

    //-----------------------------------------------------------------------------
    // Purpose: Callback interface for receiving responses after requesting details on
    // who is playing on a particular server.
    //
    // These callbacks all occur in response to querying an individual server
    // via the ISteamMatchmakingServers()->PlayerDetails() call below.  If you are 
    // destructing an object that implements this interface then you should call 
    // ISteamMatchmakingServers()->CancelServerQuery() passing in the handle to the query
    // which is in progress.  Failure to cancel in progress queries when destructing
    // a callback handler may result in a crash when a callback later occurs.
    //-----------------------------------------------------------------------------
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
        
        private Data.HServerQuery hserverRules = 0;
        public bool QueryActive { get { return hserverRules != 0; } }

        public void Cancel()
        {
            if (hserverRules != 0) { ServerList.Base.Internal.CancelServerQuery(hserverRules); }
            hserverRules = 0;
        }

        public void HQueryServerRules(IPAddress ip, int queryPort)
        {
            hserverRules = ServerList.Base.Internal.ServerRules(ip.IpToInt32(), (ushort)queryPort, (IntPtr)this);
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

#if NOTHISPTR
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate void InternalAddPlayerToList(IntPtr pchName, int nScore, float flTimePlayed);
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate void InternalPlayersFailedToRespond();
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate void InternalPlayersRefreshComplete();
		private void InternalOnAddPlayerToList(IntPtr pchName, int nScore, float flTimePlayed) {
			m_AddPlayerToList(InteropHelp.PtrToStringUTF8(pchName), nScore, flTimePlayed);
		}
		private void InternalOnPlayersFailedToRespond() {
			m_PlayersFailedToRespond();
		}
		private void InternalOnPlayersRefreshComplete() {
			m_PlayersRefreshComplete();
		}
#else
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalAddPlayerToList(IntPtr thisptr, IntPtr pchName, int nScore, float flTimePlayed);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalPlayersFailedToRespond(IntPtr thisptr);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalPlayersRefreshComplete(IntPtr thisptr);
        private void InternalOnAddPlayerToList(IntPtr thisptr, IntPtr pchName, int nScore, float flTimePlayed)
        {
            hserverRules = 0;

            m_AddPlayerToList(Utf8StringPointer.ConvertPtrToString(pchName), nScore, flTimePlayed);
        }
        private void InternalOnPlayersFailedToRespond(IntPtr thisptr)
        {
            hserverRules = 0;

            m_PlayersFailedToRespond();
        }
        private void InternalOnPlayersRefreshComplete(IntPtr thisptr)
        {
            hserverRules = 0;

            m_PlayersRefreshComplete();
        }
#endif

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

    //-----------------------------------------------------------------------------
    // Purpose: Callback interface for receiving responses after requesting rules
    // details on a particular server.
    //
    // These callbacks all occur in response to querying an individual server
    // via the ISteamMatchmakingServers()->ServerRules() call below.  If you are 
    // destructing an object that implements this interface then you should call 
    // ISteamMatchmakingServers()->CancelServerQuery() passing in the handle to the query
    // which is in progress.  Failure to cancel in progress queries when destructing
    // a callback handler may result in a crash when a callback later occurs.
    //-----------------------------------------------------------------------------
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

#if NOTHISPTR
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate void InternalRulesResponded(IntPtr pchRule, IntPtr pchValue);
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate void InternalRulesFailedToRespond();
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		public delegate void InternalRulesRefreshComplete();
		private void InternalOnRulesResponded(IntPtr pchRule, IntPtr pchValue) {
			m_RulesResponded(InteropHelp.PtrToStringUTF8(pchRule), InteropHelp.PtrToStringUTF8(pchValue));
		}
		private void InternalOnRulesFailedToRespond() {
			m_RulesFailedToRespond();
		}
		private void InternalOnRulesRefreshComplete() {
			m_RulesRefreshComplete();
		}
#else
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalRulesResponded(IntPtr thisptr, IntPtr pchRule, IntPtr pchValue);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalRulesFailedToRespond(IntPtr thisptr);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate void InternalRulesRefreshComplete(IntPtr thisptr);
        private void InternalOnRulesResponded(IntPtr thisptr, IntPtr pchRule, IntPtr pchValue)
        {
            m_RulesResponded(Utf8StringPointer.ConvertPtrToString(pchRule), Utf8StringPointer.ConvertPtrToString(pchValue));
        }
        private void InternalOnRulesFailedToRespond(IntPtr thisptr)
        {
            m_RulesFailedToRespond();
        }
        private void InternalOnRulesRefreshComplete(IntPtr thisptr)
        {
            m_RulesRefreshComplete();
        }
#endif

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
}
