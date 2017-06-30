#region MIT License
/*Copyright (c) 2012-2013, 2016 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// A function used to seek and read data from a given input stream.
	/// </summary>
	/// <remarks>
	/// This function might be called to perform a seek or skip operation with a ‘count’ of 0. A non-zero return value
	/// then indicates an error.
	/// </remarks>
	/// <param name="stream">A handle to the source stream.</param>
	/// <param name="offset">The offset of read in stream (always from start).</param>
	/// <param name="buffer">The address of the read buffer.</param>
	/// <param name="count">The number of bytes to read from the stream.</param>
	/// <returns>The number of bytes effectively read by the stream.</returns>
	[CLSCompliant(false)]
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate uint StreamIOFunc(NativeReference<FTStream> stream, uint offset, IntPtr buffer, uint count);

	/// <summary>
	/// A function used to close a given input stream.
	/// </summary>
	/// <param name="stream">A handle to the target stream.</param>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void StreamCloseFunc(NativeReference<FTStream> stream);

	/// <summary>
	/// A handle to an input stream.
	/// </summary>
	public sealed class FTStream : NativeObject
	{
		#region Fields

		private StreamRec rec;

		#endregion

		#region Constructors

		internal FTStream(IntPtr reference): base(reference)
		{
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets base. For memory-based streams, this is the address of the first stream byte in memory. This field
		/// should always be set to NULL for disk-based streams.
		/// </summary>
		public IntPtr Base
		{
			get
			{
				return rec.@base;
			}
		}
		
		/// <summary>
		/// Gets the stream size in bytes.
		/// </summary>
		[CLSCompliant(false)]
		public uint Size
		{
			get
			{
				return (uint)rec.size;
			}
		}

		/// <summary>
		/// Gets the current position within the stream.
		/// </summary>
		[CLSCompliant(false)]
		public uint Position
		{
			get
			{
				return (uint)rec.pos;
			}
		}

		/// <summary>
		/// Gets the descriptor. This field is a union that can hold an integer or a pointer. It is used by stream
		/// implementations to store file descriptors or ‘FILE*’ pointers.
		/// </summary>
		public StreamDesc Descriptor
		{
			get
			{
				return new StreamDesc(PInvokeHelper.AbsoluteOffsetOf<StreamRec>(Reference, "descriptor"));
			}
		}

		/// <summary>
		/// Gets the path name. This field is completely ignored by FreeType. However, it is often useful during
		/// debugging to use it to store the stream's filename (where available).
		/// </summary>
		public StreamDesc PathName
		{
			get
			{
				return new StreamDesc(PInvokeHelper.AbsoluteOffsetOf<StreamRec>(Reference, "pathname"));
			}
		}

		/// <summary>
		/// Gets the stream's input function.
		/// </summary>
		[CLSCompliant(false)]
		public StreamIOFunc Read
		{
			get
			{
				return rec.read;
			}
		}

		/// <summary>
		/// Gets the stream's close function.
		/// </summary>
		public StreamCloseFunc Close
		{
			get
			{
				return rec.close;
			}
		}

		/// <summary>
		/// Gets the memory manager to use to preload frames. This is set internally by FreeType and shouldn't be
		/// touched by stream implementations.
		/// </summary>
		public Memory Memory
		{
			get
			{
				return new Memory(PInvokeHelper.AbsoluteOffsetOf<StreamRec>(Reference, "memory"));
			}
		}

		/// <summary>
		/// Gets the cursor. This field is set and used internally by FreeType when parsing frames.
		/// </summary>
		public IntPtr Cursor
		{
			get
			{
				return rec.cursor;
			}
		}

		/// <summary>
		/// Gets the limit. This field is set and used internally by FreeType when parsing frames.
		/// </summary>
		public IntPtr Limit
		{
			get
			{
				return rec.limit;
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
				rec = PInvokeHelper.PtrToStructure<StreamRec>(value);
			}
		}

		#endregion

		#region Methods

		#region GZIP Streams

		/// <summary>
		/// Open a new stream to parse gzip-compressed font files. This is mainly used to support the compressed
		/// ‘*.pcf.gz’ fonts that come with XFree86.
		/// </summary>
		/// <remarks><para>
		/// The source stream must be opened before calling this function.
		/// </para><para>
		/// Calling the internal function ‘FT_Stream_Close’ on the new stream will not call ‘FT_Stream_Close’ on the
		/// source stream. None of the stream objects will be released to the heap.
		/// </para><para>
		/// The stream implementation is very basic and resets the decompression process each time seeking backwards is
		/// needed within the stream.
		/// </para><para>
		/// In certain builds of the library, gzip compression recognition is automatically handled when calling
		/// <see cref="Library.NewFace"/> or <see cref="Library.OpenFace"/>. This means that if no font driver is
		/// capable of handling the raw compressed file, the library will try to open a gzipped stream from it and
		/// re-open the face with it.
		/// </para><para>
		/// This function may return <see cref="Error.UnimplementedFeature"/> if your build of FreeType was not
		/// compiled with zlib support.
		/// </para></remarks>
		/// <param name="source">The source stream.</param>
		public void OpenGzip(FTStream source)
		{
			Error err = FT.FT_Stream_OpenGzip(Reference, source.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		#endregion

		#region LZW Streams

		/// <summary>
		/// Open a new stream to parse LZW-compressed font files. This is mainly used to support the compressed
		/// ‘*.pcf.Z’ fonts that come with XFree86.
		/// </summary>
		/// <remarks><para>
		/// The source stream must be opened before calling this function.
		/// </para><para>
		/// Calling the internal function ‘FT_Stream_Close’ on the new stream will not call ‘FT_Stream_Close’ on the
		/// source stream. None of the stream objects will be released to the heap.
		/// </para><para>
		/// The stream implementation is very basic and resets the decompression process each time seeking backwards is
		/// needed within the stream.
		/// </para><para>
		/// In certain builds of the library, LZW compression recognition is automatically handled when calling
		/// <see cref="Library.NewFace"/> or <see cref="Library.OpenFace"/>. This means that if no font driver is
		/// capable of handling the raw compressed file, the library will try to open a LZW stream from it and re-open
		/// the face with it.
		/// </para><para>
		/// This function may return <see cref="Error.UnimplementedFeature"/> if your build of FreeType was not
		/// compiled with LZW support.
		/// </para></remarks>
		/// <param name="source">The source stream.</param>
		public void OpenLzw(FTStream source)
		{
			Error err = FT.FT_Stream_OpenLZW(Reference, source.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		#endregion

		#region BZIP2 Streams

		/// <summary>
		/// Open a new stream to parse bzip2-compressed font files. This is mainly used to support the compressed
		/// ‘*.pcf.bz2’ fonts that come with XFree86.
		/// </summary>
		/// <remarks><para>
		/// The source stream must be opened before calling this function.
		/// </para><para>
		/// Calling the internal function ‘FT_Stream_Close’ on the new stream will not call ‘FT_Stream_Close’ on the
		/// source stream. None of the stream objects will be released to the heap.
		/// </para><para>
		/// The stream implementation is very basic and resets the decompression process each time seeking backwards is
		/// needed within the stream.
		/// </para><para>
		/// In certain builds of the library, bzip2 compression recognition is automatically handled when calling
		/// <see cref="Library.NewFace"/> or <see cref="Library.OpenFace"/>. This means that if no font driver is
		/// capable of handling the raw compressed file, the library will try to open a bzip2 stream from it and
		/// re-open the face with it.
		/// </para><para>
		/// This function may return <see cref="Error.UnimplementedFeature"/> if your build of FreeType was not
		/// compiled with bzip2 support.
		/// </para></remarks>
		/// <param name="source">The source stream.</param>
		public void StreamOpenBzip2(FTStream source)
		{
			Error err = FT.FT_Stream_OpenBzip2(Reference, source.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		#endregion

		#endregion
	}
}
