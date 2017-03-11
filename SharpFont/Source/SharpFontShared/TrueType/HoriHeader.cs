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

using SharpFont.TrueType.Internal;

namespace SharpFont.TrueType
{
	/// <summary>
	/// A structure used to model a TrueType horizontal header, the ‘hhea’ table, as well as the corresponding
	/// horizontal metrics table, i.e., the ‘hmtx’ table.
	/// </summary>
	/// <remarks><para>
	/// IMPORTANT: The <see cref="HoriHeader"/> and <see cref="VertHeader"/> structures should be identical except for
	/// the names of their fields which are different.
	/// </para><para>
	/// This ensures that a single function in the ‘ttload’ module is able to read both the horizontal and vertical
	/// headers.
	/// </para></remarks>
	public class HoriHeader
	{
		#region Fields

		private IntPtr reference;
		private HoriHeaderRec rec;

		#endregion

		#region Constructors

		internal HoriHeader(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the table version.
		/// </summary>
		public int Version
		{
			get
			{
				return (int)rec.Version;
			}
		}

		/// <summary><para>
		/// Gets the font's ascender, i.e., the distance from the baseline to the top-most of all glyph points found in
		/// the font.
		/// </para><para>
		/// This value is invalid in many fonts, as it is usually set by the font designer, and often reflects only a
		/// portion of the glyphs found in the font (maybe ASCII).
		/// </para><para>
		/// You should use the ‘sTypoAscender’ field of the OS/2 table instead if you want the correct one.
		/// </para></summary>
		public short Ascender
		{
			get
			{
				return rec.Ascender;
			}
		}

		/// <summary><para>
		/// Gets the font's descender, i.e., the distance from the baseline to the bottom-most of all glyph points
		/// found in the font. It is negative.
		/// </para><para>
		/// This value is invalid in many fonts, as it is usually set by the font designer, and often reflects only a
		/// portion of the glyphs found in the font (maybe ASCII).
		/// </para><para>
		/// You should use the ‘sTypoDescender’ field of the OS/2 table instead if you want the correct one.
		/// </para></summary>
		public short Descender
		{
			get
			{
				return rec.Descender;
			}
		}

		/// <summary>
		/// Gets the font's line gap, i.e., the distance to add to the ascender and descender to get the BTB, i.e., the
		/// baseline-to-baseline distance for the font.
		/// </summary>
		public short LineGap
		{
			get
			{
				return rec.Line_Gap;
			}
		}

		/// <summary>
		/// Gets the maximum of all advance widths found in the font. It can be used to compute the maximum width of an
		/// arbitrary string of text.
		/// </summary>
		[CLSCompliant(false)]
		public ushort AdvanceWidthMax
		{
			get
			{
				return rec.advance_Width_Max;
			}
		}

		/// <summary>
		/// Gets the minimum left side bearing of all glyphs within the font.
		/// </summary>
		public short MinimumLeftSideBearing
		{
			get
			{
				return rec.min_Left_Side_Bearing;
			}
		}

		/// <summary>
		/// Gets the minimum right side bearing of all glyphs within the font.
		/// </summary>
		public short MinimumRightSideBearing
		{
			get
			{
				return rec.min_Right_Side_Bearing;
			}
		}

		/// <summary>
		/// Gets the maximum horizontal extent (i.e., the ‘width’ of a glyph's bounding box) for all glyphs in the
		/// font.
		/// </summary>
		public short MaximumExtentX
		{
			get
			{
				return rec.xMax_Extent;
			}
		}

		/// <summary>
		/// Gets the rise coefficient of the cursor's slope of the cursor (slope=rise/run).
		/// </summary>
		public short CaretSlopeRise
		{
			get
			{
				return rec.caret_Slope_Rise;
			}
		}

		/// <summary>
		/// Gets the run coefficient of the cursor's slope.
		/// </summary>
		public short CaretSlopeRun
		{
			get
			{
				return rec.caret_Slope_Run;
			}
		}

		/// <summary>
		/// Gets the amount of offset needed to provide best appearance with slanted fonts.
		/// Set to 0 for non-slanted fonts.
		/// </summary>
		public short CaretOffset
		{
			get
			{
				return rec.caret_Offset;
			}
		}

		/// <summary>
		/// Gets the 8 reserved bytes.
		/// </summary>
		public short[] Reserved
		{
			get
			{
				return rec.Reserved;
			}
		}

		/// <summary>
		/// Gets 0, always.
		/// </summary>
		public short MetricDataFormat
		{
			get
			{
				return rec.metric_Data_Format;
			}
		}

		/// <summary>
		/// Gets the number of HMetrics entries in the ‘hmtx’ table -- this value can be smaller than the total number
		/// of glyphs in the font.
		/// </summary>
		[CLSCompliant(false)]
		public ushort HMetricsCount
		{
			get
			{
				return rec.number_Of_HMetrics;
			}
		}

		/// <summary>
		/// Gets a pointer into the ‘hmtx’ table.
		/// </summary>
		public IntPtr LongMetrics
		{
			get
			{
				return rec.long_metrics;
			}
		}

		/// <summary>
		/// Gets a pointer into the ‘hmtx’ table.
		/// </summary>
		public IntPtr ShortMetrics
		{
			get
			{
				return rec.short_metrics;
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
				rec = PInvokeHelper.PtrToStructure<HoriHeaderRec>(reference);
			}
		}

		#endregion
	}
}
