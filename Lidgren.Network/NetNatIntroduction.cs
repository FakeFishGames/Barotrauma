using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

#if !__NOIPENDPOINT__
using NetEndPoint = System.Net.IPEndPoint;
#endif

namespace Lidgren.Network
{
	public partial class NetPeer
	{
		/// <summary>
		/// Send NetIntroduction to hostExternal and clientExternal; introducing client to host
		/// </summary>
		public void Introduce(
			NetEndPoint hostInternal,
			NetEndPoint hostExternal,
			NetEndPoint clientInternal,
			NetEndPoint clientExternal,
			string token)
		{
			// send message to client
			NetOutgoingMessage um = CreateMessage(10 + token.Length + 1);
			um.m_messageType = NetMessageType.NatIntroduction;
			um.Write((byte)0);
			um.Write(hostInternal);
			um.Write(hostExternal);
			um.Write(token);
			Interlocked.Increment(ref um.m_recyclingCount);
			m_unsentUnconnectedMessages.Enqueue(new NetTuple<NetEndPoint, NetOutgoingMessage>(clientExternal, um));

			// send message to host
			um = CreateMessage(10 + token.Length + 1);
			um.m_messageType = NetMessageType.NatIntroduction;
			um.Write((byte)1);
			um.Write(clientInternal);
			um.Write(clientExternal);
			um.Write(token);
			Interlocked.Increment(ref um.m_recyclingCount);
			m_unsentUnconnectedMessages.Enqueue(new NetTuple<NetEndPoint, NetOutgoingMessage>(hostExternal, um));
		}

		/// <summary>
		/// Called when host/client receives a NatIntroduction message from a master server
		/// </summary>
		internal void HandleNatIntroduction(int ptr)
		{
			VerifyNetworkThread();

			// read intro
			NetIncomingMessage tmp = SetupReadHelperMessage(ptr, 1000); // never mind length

			byte hostByte = tmp.ReadByte();
			NetEndPoint remoteInternal = tmp.ReadIPEndPoint();
			NetEndPoint remoteExternal = tmp.ReadIPEndPoint();
			string token = tmp.ReadString();
			bool isHost = (hostByte != 0);

			LogDebug("NAT introduction received; we are designated " + (isHost ? "host" : "client"));

			NetOutgoingMessage punch;

			if (!isHost && m_configuration.IsMessageTypeEnabled(NetIncomingMessageType.NatIntroductionSuccess) == false)
				return; // no need to punch - we're not listening for nat intros!

			// send internal punch
			punch = CreateMessage(1);
			punch.m_messageType = NetMessageType.NatPunchMessage;
			punch.Write(hostByte);
			punch.Write(token);
			Interlocked.Increment(ref punch.m_recyclingCount);
			m_unsentUnconnectedMessages.Enqueue(new NetTuple<NetEndPoint, NetOutgoingMessage>(remoteInternal, punch));
			LogDebug("NAT punch sent to " + remoteInternal);

			// send external punch
			punch = CreateMessage(1);
			punch.m_messageType = NetMessageType.NatPunchMessage;
			punch.Write(hostByte);
			punch.Write(token);
			Interlocked.Increment(ref punch.m_recyclingCount);
			m_unsentUnconnectedMessages.Enqueue(new NetTuple<NetEndPoint, NetOutgoingMessage>(remoteExternal, punch));
			LogDebug("NAT punch sent to " + remoteExternal);

		}

		/// <summary>
		/// Called when receiving a NatPunchMessage from a remote endpoint
		/// </summary>
		private void HandleNatPunch(int ptr, NetEndPoint senderEndPoint)
		{
			NetIncomingMessage tmp = SetupReadHelperMessage(ptr, 1000); // never mind length

			byte fromHostByte = tmp.ReadByte();
			if (fromHostByte == 0)
			{
				// it's from client
				LogDebug("NAT punch received from " + senderEndPoint + " we're host, so we ignore this");
				return; // don't alert hosts about nat punch successes; only clients
			}
			string token = tmp.ReadString();

			LogDebug("NAT punch received from " + senderEndPoint + " we're client, so we've succeeded - token is " + token);

			//
			// Release punch success to client; enabling him to Connect() to msg.Sender if token is ok
			//
			NetIncomingMessage punchSuccess = CreateIncomingMessage(NetIncomingMessageType.NatIntroductionSuccess, 10);
			punchSuccess.m_senderEndPoint = senderEndPoint;
			punchSuccess.Write(token);
			ReleaseMessage(punchSuccess);

			// send a return punch just for good measure
			var punch = CreateMessage(1);
			punch.m_messageType = NetMessageType.NatPunchMessage;
			punch.Write((byte)0);
			punch.Write(token);
			Interlocked.Increment(ref punch.m_recyclingCount);
			m_unsentUnconnectedMessages.Enqueue(new NetTuple<NetEndPoint, NetOutgoingMessage>(senderEndPoint, punch));
		}
	}
}
