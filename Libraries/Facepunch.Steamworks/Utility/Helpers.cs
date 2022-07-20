using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Collections.Concurrent;

namespace Steamworks
{
	internal static class Helpers
	{
		public const int MemoryBufferSize = 1024 * 32;

		internal struct Memory : IDisposable
        {
			private const int MaxBagSize = 4;
			private static readonly ConcurrentBag<IntPtr> BufferBag = new ConcurrentBag<IntPtr>();

			public IntPtr Ptr { get; private set; }

			public static implicit operator IntPtr(in Memory m) => m.Ptr;

			internal unsafe Memory(int sz)
			{
				Ptr = BufferBag.TryTake(out IntPtr ptr) ? ptr : Marshal.AllocHGlobal(sz);
				((byte*)Ptr)[0] = 0;
			}

			public void Dispose()
			{
				if (Ptr == IntPtr.Zero) { return; }
				if (BufferBag.Count < MaxBagSize)
				{
					BufferBag.Add(Ptr);
				}
                else
                {
					Marshal.FreeHGlobal(Ptr);
                }
				Ptr = IntPtr.Zero;
			}
        }
		
		public static Memory TakeMemory()
		{
			return new Memory(MemoryBufferSize);
		}

		private static byte[][] BufferPool = new byte[4][];
		private static int BufferPoolIndex;
		private static object BufferMutex = new object();

		/// <summary>
		/// Returns a buffer. This will get returned and reused later on.
		/// We shouldn't really be using this anymore. 
		/// </summary>
		public static byte[] TakeBuffer( int minSize )
		{
			lock ( BufferPool )
			{
				BufferPoolIndex++;

				if ( BufferPoolIndex >= BufferPool.Length )
					BufferPoolIndex = 0;

				if ( BufferPool[BufferPoolIndex] == null ) 
					BufferPool[BufferPoolIndex] = new byte[1024 * 256];

				if ( BufferPool[BufferPoolIndex].Length < minSize )
				{
					BufferPool[BufferPoolIndex] = new byte[minSize + 1024];
				}

				return BufferPool[BufferPoolIndex];
			}
		}

		internal unsafe static string MemoryToString( IntPtr ptr )
		{
			var len = 0;

			for( len = 0; len < MemoryBufferSize; len++ )
			{
				if ( ((byte*)ptr)[len] == 0 )
					break;
			}

			if ( len == 0 )
				return string.Empty;

			return UTF8Encoding.UTF8.GetString( (byte*)ptr, len );
		}
	}

	internal class MonoPInvokeCallbackAttribute : Attribute
	{
		public MonoPInvokeCallbackAttribute() { }
	}

	/// <summary>
	/// Prevent unity from stripping shit we depend on
	/// https://docs.unity3d.com/Manual/ManagedCodeStripping.html
	/// </summary>
	internal class PreserveAttribute : System.Attribute { }
}
