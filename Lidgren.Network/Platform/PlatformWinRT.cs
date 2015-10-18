#if WINDOWS_RUNTIME
//
//
//
//    Completely broken right now
//
//
//
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Lidgren.Network
{
	public class NetAddress
	{
		public static readonly HostName Any = null;
	}

	public class NetEndPoint
	{
		public NetEndPoint(HostName hostname, int port) { HostName = hostname; Port = port; }
		public NetEndPoint(HostName hostname, string port) { HostName = hostname; Port = int.Parse(port); }
		public NetEndPoint(string hostname, int port) { HostName = (hostname == null) ? null : new HostName(hostname); Port = port; }
		public HostName HostName;
		public int Port;
		public override string ToString() { return string.Format("{0}:{1}", HostName, Port); }
		public override int GetHashCode()
		{
			return HostName.RawName.GetHashCode() + Port;
		}
		public override bool Equals(object obj)
		{
			var ep = obj as NetEndPoint;
			if (ep == null) return false;
			if (Port != ep.Port) return false;
			return HostName.RawName.Equals(ep.HostName.RawName);
		}
	};

	public static partial class NetUtility
	{
		[CLSCompliant(false)]
		public static ulong GetPlatformSeed(int seedInc)
		{
			ulong seed = (ulong)Environment.TickCount + (ulong)seedInc;
			return seed ^ ((ulong)(new object().GetHashCode()) << 32);
		}

		/// <summary>
		/// Returns the physical (MAC) address for the first usable network interface
		/// </summary>
		public static PhysicalAddress GetMacAddress()
		{
			throw new NotImplementedException();
		}

		public static IPAddress GetBroadcastAddress()
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Gets my local IPv4 address (not necessarily external) and subnet mask
		/// </summary>
		public static IPAddress GetMyAddress(out IPAddress mask)
		{
			throw new NotImplementedException();
		}

		public static void Sleep(int milliseconds)
		{
			Task.Delay(50).Wait();
		}

		public static NetAddress CreateAddressFromBytes(byte[] bytes)
		{
			throw new NotImplementedException();
		}

		private static readonly SHA1CryptoServiceProvider s_sha = new SHA1CryptoServiceProvider();
		public static byte[] ComputeSHAHash(byte[] bytes, int offset, int count)
		{
			return s_sha.ComputeHash(bytes, offset, count);
		}
	}

	public static partial class NetTime
	{
		private static readonly long s_timeInitialized = Environment.TickCount;

		/// <summary>
		/// Get number of seconds since the application started
		/// </summary>
		public static double Now { get { return (double)((uint)Environment.TickCount - s_timeInitialized) / 1000.0; } }
	}
}
#endif