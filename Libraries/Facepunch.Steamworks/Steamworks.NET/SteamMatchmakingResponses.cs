/**

The MIT License (MIT)

Copyright (c) 2013-2022 Riley Labrecque

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
using System.Runtime.InteropServices;

namespace Steamworks
{
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
    public class SteamMatchmakingRulesResponse
    {
        // Got data on a rule on the server -- you'll get one of these per rule defined on
        // the server you are querying
        public delegate void RulesResponded(string pchRule, string pchValue);

        // The server failed to respond to the request for rule details
        public delegate void RulesFailedToRespond();

        // The server has finished responding to the rule details request
        // (ie, you won't get anymore RulesResponded callbacks)
        public delegate void RulesRefreshComplete();

        private readonly VTable m_VTable;
        private readonly IntPtr m_pVTable;
        private GCHandle m_pGCHandle;
        private readonly RulesResponded m_RulesResponded;
        private readonly RulesFailedToRespond m_RulesFailedToRespond;
        private readonly RulesRefreshComplete m_RulesRefreshComplete;

        public SteamMatchmakingRulesResponse(
            RulesResponded onRulesResponded,
            RulesFailedToRespond onRulesFailedToRespond,
            RulesRefreshComplete onRulesRefreshComplete)
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

        ~SteamMatchmakingRulesResponse()
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

        private void InternalOnRulesResponded(IntPtr pchRule, IntPtr pchValue)
        {
            m_RulesResponded(Helpers.MemoryToString(pchRule), Helpers.MemoryToString(pchValue));
        }

        private void InternalOnRulesFailedToRespond()
        {
            m_RulesFailedToRespond();
        }

        private void InternalOnRulesRefreshComplete()
        {
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
            m_RulesResponded(Helpers.MemoryToString(pchRule), Helpers.MemoryToString(pchValue));
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
            [NonSerialized] [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalRulesResponded m_VTRulesResponded;

            [NonSerialized] [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalRulesFailedToRespond m_VTRulesFailedToRespond;

            [NonSerialized] [MarshalAs(UnmanagedType.FunctionPtr)]
            public InternalRulesRefreshComplete m_VTRulesRefreshComplete;
        }

        public static explicit operator System.IntPtr(SteamMatchmakingRulesResponse that)
        {
            return that.m_pGCHandle.AddrOfPinnedObject();
        }
    };
}
