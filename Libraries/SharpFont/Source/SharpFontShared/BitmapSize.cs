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
	/// This structure models the metrics of a bitmap strike (i.e., a set of
	/// glyphs for a given point size and resolution) in a bitmap font. It is
	/// used for the <see cref="Face.AvailableSizes"/> field of
	/// <see cref="Face"/>.
	/// </summary>
	/// <remarks><para>
	/// Windows FNT: The nominal size given in a FNT font is not reliable. Thus
	/// when the driver finds it incorrect, it sets ‘size’ to some calculated
	/// values and sets ‘x_ppem’ and ‘y_ppem’ to the pixel width and height
	/// given in the font, respectively.
	/// </para><para>
	/// TrueType embedded bitmaps: ‘size’, ‘width’, and ‘height’ values are not
	/// contained in the bitmap strike itself. They are computed from the
	/// global font parameters.
	/// </para></remarks>
	public sealed class BitmapSize
	{
		#region Fields

		private IntPtr reference;
		private BitmapSizeRec rec;

		#endregion

		#region Constructors

		internal BitmapSize(IntPtr reference)
		{
			Reference = reference;
		}

		internal BitmapSize(BitmapSizeRec bmpSizeInt)
		{
			this.rec = bmpSizeInt;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the vertical distance, in pixels, between two consecutive
		/// baselines. It is always positive.
		/// </summary>
		public short Height
		{
			get
			{
				return rec.height;
			}
		}

		/// <summary>
		/// Gets the average width, in pixels, of all glyphs in the strike.
		/// </summary>
		public short Width
		{
			get
			{
				return rec.width;
			}
		}

		/// <summary>
		/// Gets the nominal size of the strike in 26.6 fractional points. This
		/// field is not very useful.
		/// </summary>
		public Fixed26Dot6 Size
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.size);
			}
		}

		/// <summary>
		/// Gets the horizontal ppem (nominal width) in 26.6 fractional pixels.
		/// </summary>
		public Fixed26Dot6 NominalWidth
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.x_ppem);
			}
		}

		/// <summary>
		/// Gets the vertical ppem (nominal height) in 26.6 fractional pixels.
		/// </summary>
		public Fixed26Dot6 NominalHeight
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.y_ppem);
			}
		}

		internal IntPtr Reference
		{
			get
			{
				return reference;
			}

			set
			{
				reference = value;
				rec = PInvokeHelper.PtrToStructure<BitmapSizeRec>(reference);
			}
		}

		#endregion
	}
}
