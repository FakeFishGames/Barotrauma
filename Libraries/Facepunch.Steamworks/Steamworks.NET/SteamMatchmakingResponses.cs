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
    public class SteamMatchmakingPingResponse
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

        public SteamMatchmakingPingResponse(ServerResponded onServerResponded, ServerFailedToRespond onServerFailedToRespond)
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

        ~SteamMatchmakingPingResponse()
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

        public static explicit operator System.IntPtr(SteamMatchmakingPingResponse that)
        {
            return that.m_pGCHandle.AddrOfPinnedObject();
        }
    };
}
