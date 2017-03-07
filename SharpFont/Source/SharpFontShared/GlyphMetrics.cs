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
	/// A structure used to model the metrics of a single glyph. The values are expressed in 26.6 fractional pixel
	/// format; if the flag <see cref="LoadFlags.NoScale"/> has been used while loading the glyph, values are expressed
	/// in font units instead.
	/// </summary>
	/// <remarks>
	/// If not disabled with <see cref="LoadFlags.NoHinting"/>, the values represent dimensions of the hinted glyph (in
	/// case hinting is applicable). 
	/// </remarks>
	public sealed class GlyphMetrics
	{
		#region Fields

		private IntPtr reference;
		private GlyphMetricsRec rec;

		#endregion

		#region Constructors

		internal GlyphMetrics(IntPtr reference)
		{
			Reference = reference;
		}

		internal GlyphMetrics(GlyphMetricsRec glyphMetInt)
		{
			this.rec = glyphMetInt;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the glyph's width. If getting metrics from a face loaded with <see cref="LoadFlags.NoScale"/>, call
		/// <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 Width
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.width);
			}
		}

		/// <summary>
		/// Gets the glyph's height. If getting metrics from a face loaded with <see cref="LoadFlags.NoScale"/>, call
		/// <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 Height
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.height);
			}
		}

		/// <summary>
		/// Gets the left side bearing for horizontal layout. If getting metrics from a face loaded with
		/// <see cref="LoadFlags.NoScale"/>, call <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 HorizontalBearingX
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.horiBearingX);
			}
		}

		/// <summary>
		/// Gets the top side bearing for horizontal layout. If getting metrics from a face loaded with
		/// <see cref="LoadFlags.NoScale"/>, call <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 HorizontalBearingY
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.horiBearingY);
			}
		}

		/// <summary>
		/// Gets the advance width for horizontal layout. If getting metrics from a face loaded with
		/// <see cref="LoadFlags.NoScale"/>, call <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 HorizontalAdvance
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.horiAdvance);
			}
		}

		/// <summary>
		/// Gets the left side bearing for vertical layout. If getting metrics from a face loaded with
		/// <see cref="LoadFlags.NoScale"/>, call <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 VerticalBearingX
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.vertBearingX);
			}
		}

		/// <summary>
		/// Gets the top side bearing for vertical layout. Larger positive values mean further below the vertical glyph
		/// origin. If getting metrics from a face loaded with <see cref="LoadFlags.NoScale"/>, call
		/// <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 VerticalBearingY
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.vertBearingY);
			}
		}

		/// <summary>
		/// Gets the advance height for vertical layout. Positive values mean the glyph has a positive advance
		/// downward. If getting metrics from a face loaded with <see cref="LoadFlags.NoScale"/>, call
		/// <see cref="Fixed26Dot6.Value"/> to get the unscaled value.
		/// </summary>
		public Fixed26Dot6 VerticalAdvance
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)rec.vertAdvance);
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
				rec = PInvokeHelper.PtrToStructure<GlyphMetricsRec>(reference);
			}
		}

		#endregion
	}
}
