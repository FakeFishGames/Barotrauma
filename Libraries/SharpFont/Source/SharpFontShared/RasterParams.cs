#region MIT License
/*Copyright (c) 2012-2013, 2015 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// A function used as a call-back by the anti-aliased renderer in order to let client applications draw themselves
	/// the gray pixel spans on each scan line.
	/// </summary>
	/// <remarks><para>
	/// This callback allows client applications to directly render the gray spans of the anti-aliased bitmap to any
	/// kind of surfaces.
	/// </para><para>
	/// This can be used to write anti-aliased outlines directly to a given background bitmap, and even perform
	/// translucency.
	/// </para><para>
	/// Note that the ‘count’ field cannot be greater than a fixed value defined by the ‘FT_MAX_GRAY_SPANS’
	/// configuration macro in ‘ftoption.h’. By default, this value is set to 32, which means that if there are more
	/// than 32 spans on a given scanline, the callback is called several times with the same ‘y’ parameter in order to
	/// draw all callbacks.
	/// </para><para>
	/// Otherwise, the callback is only called once per scan-line, and only for those scanlines that do have ‘gray’
	/// pixels on them.
	/// </para></remarks>
	/// <param name="y">The scanline's y coordinate.</param>
	/// <param name="count">The number of spans to draw on this scanline.</param>
	/// <param name="spans">A table of ‘count’ spans to draw on the scanline.</param>
	/// <param name="user">User-supplied data that is passed to the callback.</param>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RasterSpanFunc(int y, int count, NativeReference<Span> spans, IntPtr user);

	/// <summary><para>
	/// THIS TYPE IS DEPRECATED. DO NOT USE IT.
	/// </para><para>
	/// A function used as a call-back by the monochrome scan-converter to test whether a given target pixel is already
	/// set to the drawing ‘color’. These tests are crucial to implement drop-out control per-se the TrueType spec.
	/// </para></summary>
	/// <param name="y">The pixel's y coordinate.</param>
	/// <param name="x">The pixel's x coordinate.</param>
	/// <param name="user">User-supplied data that is passed to the callback.</param>
	/// <returns>1 if the pixel is ‘set’, 0 otherwise.</returns>
    [Obsolete("This type is deprecated. Do not use it. See FreeType docuementation.")]
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int RasterBitTestFunc(int y, int x, IntPtr user);

	/// <summary><para>
	/// THIS TYPE IS DEPRECATED. DO NOT USE IT.
	/// </para><para>
	/// A function used as a call-back by the monochrome scan-converter to set an individual target pixel. This is
	/// crucial to implement drop-out control according to the TrueType specification.
	/// </para></summary>
	/// <param name="y">The pixel's y coordinate.</param>
	/// <param name="x">The pixel's x coordinate.</param>
	/// <param name="user">User-supplied data that is passed to the callback.</param>
    [Obsolete("This type is deprecated. Do not use it. See FreeType docuementation.")]
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RasterBitSetFunc(int y, int x, IntPtr user);

	/// <summary>
	/// A structure to hold the arguments used by a raster's render function.
	/// </summary>
	/// <remarks><para>
	/// An anti-aliased glyph bitmap is drawn if the <see cref="RasterFlags.AntiAlias"/> bit flag is set in the ‘flags’
	/// field, otherwise a monochrome bitmap is generated.
	/// </para><para>
	/// If the <see cref="RasterFlags.Direct"/> bit flag is set in ‘flags’, the raster will call the ‘gray_spans’
	/// callback to draw gray pixel spans, in the case of an aa glyph bitmap, it will call ‘black_spans’, and
	/// ‘bit_test’ and ‘bit_set’ in the case of a monochrome bitmap. This allows direct composition over a pre-existing
	/// bitmap through user-provided callbacks to perform the span drawing/composition.
	/// </para><para>
	/// Note that the ‘bit_test’ and ‘bit_set’ callbacks are required when rendering a monochrome bitmap, as they are
	/// crucial to implement correct drop-out control as defined in the TrueType specification.
	/// </para></remarks>
	public class RasterParams : NativeObject
	{
		#region Fields

		private RasterParamsRec rec;

		#endregion

		#region Constructors

		internal RasterParams(IntPtr reference) : base(reference)
		{
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the target bitmap.
		/// </summary>
		public FTBitmap Target
		{
			get
			{
				return new FTBitmap(rec.target, null);
			}
		}

		/// <summary>
		/// Gets a pointer to the source glyph image (e.g., an <see cref="Outline"/>).
		/// </summary>
		public IntPtr Source
		{
			get
			{
				return rec.source;
			}
		}

		/// <summary>
		/// Gets the rendering flags.
		/// </summary>
		public RasterFlags Flags
		{
			get
			{
				return rec.flags;
			}
		}

		/// <summary>
		/// Gets the gray span drawing callback.
		/// </summary>
		public RasterSpanFunc GraySpans
		{
			get
			{
				return rec.gray_spans;
			}
		}

		/// <summary>
		/// Gets the black span drawing callback. UNIMPLEMENTED!
		/// </summary>
        [Obsolete]
		public RasterSpanFunc BlackSpans
		{
			get
			{
				return rec.black_spans;
			}
		}

		/// <summary>
		/// Gets the bit test callback. UNIMPLEMENTED!
		/// </summary>
        [Obsolete]
		public RasterBitTestFunc BitTest
		{
			get
			{
				return rec.bit_test;
			}
		}

		/// <summary>
		/// Gets the bit set callback. UNIMPLEMENTED!
		/// </summary>
        [Obsolete]
		public RasterBitSetFunc BitSet
		{
			get
			{
				return rec.bit_set;
			}
		}

		/// <summary>
		/// Gets the user-supplied data that is passed to each drawing callback.
		/// </summary>
		public IntPtr User
		{
			get
			{
				return rec.user;
			}
		}

		/// <summary>
		/// Gets an optional clipping box. It is only used in direct rendering mode. Note that coordinates here should
		/// be expressed in integer pixels (and not in 26.6 fixed-point units).
		/// </summary>
		public BBox ClipBox
		{
			get
			{
				return rec.clip_box;
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
				rec = PInvokeHelper.PtrToStructure<RasterParamsRec>(value);
			}
		}

		#endregion
	}
}
