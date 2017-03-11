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
	/// The size metrics structure gives the metrics of a size object.
	/// </summary>
	/// <remarks><para>
	/// The scaling values, if relevant, are determined first during a size changing operation. The remaining fields
	/// are then set by the driver. For scalable formats, they are usually set to scaled values of the corresponding
	/// fields in <see cref="Face"/>.
	/// </para><para>
	/// Note that due to glyph hinting, these values might not be exact for certain fonts. Thus they must be treated as
	/// unreliable with an error margin of at least one pixel!
	/// </para><para>
	/// Indeed, the only way to get the exact metrics is to render all glyphs. As this would be a definite performance
	/// hit, it is up to client applications to perform such computations.
	/// </para><para>
	/// The <see cref="SizeMetrics"/> structure is valid for bitmap fonts also.
	/// </para></remarks>
	public sealed class SizeMetrics
	{
		#region Fields

		private SizeMetricsRec rec;

		#endregion

		#region Constructors

		internal SizeMetrics(SizeMetricsRec metricsInternal)
		{
			rec = metricsInternal;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the width of the scaled EM square in pixels, hence the term ‘ppem’ (pixels per EM). It is also referred to
		/// as ‘nominal width’.
		/// </summary>
		[CLSCompliant(false)]
		public ushort NominalWidth
		{
			get
			{
				return rec.x_ppem;
			}
		}

		/// <summary>
		/// Gets the height of the scaled EM square in pixels, hence the term ‘ppem’ (pixels per EM). It is also referred to
		/// as ‘nominal height’.
		/// </summary>
		[CLSCompliant(false)]
		public ushort NominalHeight
		{
			get
			{
				return rec.y_ppem;
			}
		}

		/// <summary>
		/// Gets a 16.16 fractional scaling value used to convert horizontal metrics from font units to 26.6 fractional
		/// pixels. Only relevant for scalable font formats.
		/// </summary>
		public Fixed16Dot16 ScaleX
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)rec.x_scale);
			}
		}

		/// <summary>
		/// Gets a 16.16 fractional scaling value used to convert vertical metrics from font units to 26.6 fractional
		/// pixels. Only relevant for scalable font formats.
		/// </summary>
		public Fixed16Dot16 ScaleY
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)rec.y_scale);
			}
		}

		/// <summary>
		/// Gets the ascender in 26.6 fractional pixels.
		/// </summary>
		/// <see cref="Face"/>
		public Fixed26Dot6 Ascender
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.ascender);
			}
		}

		/// <summary>
		/// Gets the descender in 26.6 fractional pixels.
		/// </summary>
		/// <see cref="Face"/>
		public Fixed26Dot6 Descender
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.descender);
			}
		}

		/// <summary>
		/// Gets the height in 26.6 fractional pixels.
		/// </summary>
		/// <see cref="Face"/>
		public Fixed26Dot6 Height
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.height);
			}
		}

		/// <summary>
		/// Gets the maximal advance width in 26.6 fractional pixels.
		/// </summary>
		/// <see cref="Face"/>
		public Fixed26Dot6 MaxAdvance
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.max_advance);
			}
		}

		#endregion
	}
}
