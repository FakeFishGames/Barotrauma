#region MIT License
/*Copyright (c) 2012-2014 Robert Rouhani <robert.rouhani@gmail.com>

SharpFont based on Tao.FreeType, Copyright (c) 2003-2007 Tao Framework Team

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.*/
#endregion

using System;
using System.Runtime.InteropServices;

using SharpFont.Internal;

namespace SharpFont
{
	/// <summary>
	/// A function used to allocate ‘size’ bytes from ‘memory’.
	/// </summary>
	/// <param name="memory">A handle to the source memory manager.</param>
	/// <param name="size">The size in bytes to allocate.</param>
	/// <returns>Address of new memory block. 0 in case of failure.</returns>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate IntPtr AllocFunc(NativeReference<Memory> memory, IntPtr size);

	/// <summary>
	/// A function used to release a given block of memory.
	/// </summary>
	/// <param name="memory">A handle to the source memory manager.</param>
	/// <param name="block">The address of the target memory block.</param>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void FreeFunc(NativeReference<Memory> memory, IntPtr block);

	/// <summary>
	/// A function used to re-allocate a given block of memory.
	/// </summary>
	/// <remarks>
	/// In case of error, the old block must still be available.
	/// </remarks>
	/// <param name="memory">A handle to the source memory manager.</param>
	/// <param name="currentSize">The block's current size in bytes.</param>
	/// <param name="newSize">The block's requested new size.</param>
	/// <param name="block">The block's current address.</param>
	/// <returns>New block address. 0 in case of memory shortage.</returns>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate IntPtr ReallocFunc(NativeReference<Memory> memory, IntPtr currentSize, IntPtr newSize, IntPtr block);

	/// <summary>
	/// A structure used to describe a given memory manager to FreeType 2.
	/// </summary>
	public class Memory: NativeObject
	{
		#region Fields

		private MemoryRec rec;

		#endregion

		#region Constructors

		internal Memory(IntPtr reference): base(reference)
		{
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets a generic typeless pointer for user data.
		/// </summary>
		public IntPtr User
		{
			get
			{
				return rec.user;
			}
		}

		/// <summary>
		/// Gets a pointer type to an allocation function.
		/// </summary>
		public AllocFunc Allocate
		{
			get
			{
				return rec.alloc;
			}
		}

		/// <summary>
		/// Gets a pointer type to an memory freeing function.
		/// </summary>
		public FreeFunc Free
		{
			get
			{
				return rec.free;
			}
		}

		/// <summary>
		/// Gets a pointer type to a reallocation function.
		/// </summary>
		public ReallocFunc Reallocate
		{
			get
			{
				return rec.realloc;
			}
		}

		internal override IntPtr Reference
		{
			get
			{
				return base.Reference;
			}

			set
			{
				base.Reference = value;
				rec = PInvokeHelper.PtrToStructure<MemoryRec>(value);
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Decompress a zipped input buffer into an output buffer. This function is modeled after zlib's ‘uncompress’
		/// function.
		/// </summary>
		/// <remarks>
		/// This function may return <see cref="Error.UnimplementedFeature"/> if your build of FreeType was not
		/// compiled with zlib support.
		/// </remarks>
		/// <param name="input">The input buffer.</param>
		/// <param name="output">The output buffer.</param>
		/// <returns>The length of the used data in output.</returns>
		public unsafe int GzipUncompress(byte[] input, byte[] output)
		{
			IntPtr len = (IntPtr)output.Length;

			fixed (byte* inPtr = input, outPtr = output)
			{
				Error err = FT.FT_Gzip_Uncompress(Reference, (IntPtr)outPtr, ref len, (IntPtr)inPtr, (IntPtr)input.Length);

				if (err != Error.Ok)
					throw new FreeTypeException(err);
			}

			return (int)len;
		}

		#endregion
	}
}
