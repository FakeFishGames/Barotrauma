using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Lidgren.Network
{
	/// <summary>
	/// Specialized version of NetPeer used for "server" peers
	/// </summary>
	public class NetServer : NetPeer
	{
		/// <summary>
		/// NetServer constructor
		/// </summary>
		public NetServer(NetPeerConfiguration config)
			: base(config)
		{
			config.AcceptIncomingConnections = true;
		}

		/// <summary>
		/// Send a message to all connections
		/// </summary>
		/// <param name="msg">The message to send</param>
		/// <param name="method">How to deliver the message</param>
		public void SendToAll(NetOutgoingMessage msg, NetDeliveryMethod method)
		{
			var all = this.Connections;
			if (all.Count <= 0)
				return;

			SendMessage(msg, all, method, 0);
		}

		/// <summary>
		/// Send a message to all connections except one
		/// </summary>
		/// <param name="msg">The message to send</param>
		/// <param name="method">How to deliver the message</param>
		/// <param name="except">Don't send to this particular connection</param>
		/// <param name="sequenceChannel">Which sequence channel to use for the message</param>
		public void SendToAll(NetOutgoingMessage msg, NetConnection except, NetDeliveryMethod method, int sequenceChannel)
		{
			var all = this.Connections;
			if (all.Count <= 0)
				return;

			if (except == null)
			{
				SendMessage(msg, all, method, sequenceChannel);
				return;
			}

			List<NetConnection> recipients = new List<NetConnection>(all.Count - 1);
			foreach (var conn in all)
				if (conn != except)
					recipients.Add(conn);

			if (recipients.Count > 0)
				SendMessage(msg, recipients, method, sequenceChannel);
		}

		/// <summary>
		/// Returns a string that represents this object
		/// </summary>
		public override string ToString()
		{
			return "[NetServer " + ConnectionsCount + " connections]";
		}

		/// <summary>
		/// Changes the number of maximum allowed connections, closing existing ones if the limit is lowered below the current amount.
		/// </summary>
		public void ChangeMaximumConnections(int num)
		{
			m_configuration.ChangeMaximumConnectionsInternal(num);

			int reservedSlots = m_handshakes.Count + m_connections.Count;
			while (reservedSlots >= m_configuration.m_maximumConnections)
			{
				if (m_handshakes.Count > 0)
				{
					IPEndPoint endpoint = m_handshakes.Keys.Last();

					// server full
					NetOutgoingMessage full = CreateMessage("Server full");
					full.m_messageType = NetMessageType.Disconnect;
					SendLibrary(full, endpoint);
				}
				else
				{
					m_connections.Last().Disconnect("Server full");
				}

				reservedSlots = m_handshakes.Count + m_connections.Count;
			}
		}
	}
}
