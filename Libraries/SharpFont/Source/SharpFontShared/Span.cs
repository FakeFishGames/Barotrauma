#region MIT License
/*Copyright (c) 2012-2013 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// A structure used to model a single span of gray (or black) pixels when rendering a monochrome or anti-aliased
	/// bitmap.
	/// </summary>
	/// <remarks><para>
	/// This structure is used by the span drawing callback type named <see cref="RasterSpanFunc"/> which takes the y
	/// coordinate of the span as a a parameter.
	/// </para><para>
	/// The coverage value is always between 0 and 255. If you want less gray values, the callback function has to
	/// reduce them.
	/// </para></remarks>
	public class Span : NativeObject
	{
		#region Fields

		private SpanRec rec;

		#endregion

		#region Constructors

		internal Span(IntPtr reference) : base(reference)
		{
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the span's horizontal start position.
		/// </summary>
		public short X
		{
			get
			{
				return rec.x;
			}
		}

		/// <summary>
		/// Gets the span's length in pixels.
		/// </summary>
		[CLSCompliant(false)]
		public ushort Length
		{
			get
			{
				return rec.len;
			}
		}

		/// <summary>
		/// Gets the span color/coverage, ranging from 0 (background) to 255 (foreground). Only used for anti-aliased
		/// rendering.
		/// </summary>
		public byte Coverage
		{
			get
			{
				return rec.coverage;
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
				rec = PInvokeHelper.PtrToStructure<SpanRec>(value);
			}
		}

		#endregion
	}
}
