using Steamworks.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace Steamworks
{
	public struct P2PSessionState
	{
		public byte ConnectionActive;
		public byte Connecting;
		public P2PSessionError P2PSessionError;
		public byte UsingRelay;
		public int BytesQueuedForSend;
		public int PacketsQueuedForSend;
		public uint RemoteIP;
		public ushort RemotePort;

		internal P2PSessionState(P2PSessionState_t s)
		{
			this.ConnectionActive = s.ConnectionActive;
			this.Connecting = s.Connecting;
			this.P2PSessionError = (P2PSessionError)s.P2PSessionError;
			this.UsingRelay = s.UsingRelay;
			this.BytesQueuedForSend = s.BytesQueuedForSend;
			this.PacketsQueuedForSend = s.PacketsQueuedForSend;
			this.RemoteIP = s.RemoteIP;
			this.RemotePort = s.RemotePort;
		}
	}
}
