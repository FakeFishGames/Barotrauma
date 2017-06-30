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
using System.Linq;
using System.Runtime.InteropServices;

using SharpFont.TrueType.Internal;

namespace SharpFont.TrueType
{
	/// <summary>
	/// Provides Mac style flags for the header table.
	/// </summary>
	[Flags]
	[CLSCompliant(false)]
	public enum HeaderMacStyles : ushort
	{
		/// <summary>
		/// Bold style.
		/// </summary>
		Bold = 1 << 0,

		/// <summary>
		/// Italic style.
		/// </summary>
		Italic = 1 << 2,

		/// <summary>
		/// Underline style.
		/// </summary>
		Underline = 1 << 3,

		/// <summary>
		/// Outline style.
		/// </summary>
		Outline = 1 << 4,

		/// <summary>
		/// Shadow style.
		/// </summary>
		Shadow = 1 << 5,

		/// <summary>
		/// Condensed style.
		/// </summary>
		Condensed = 1 << 6,

		/// <summary>
		/// Extended style.
		/// </summary>
		Extended = 1 << 7,

		// bits 7-15 are reserved
	}

	/// <summary>
	/// Provides flags for the header table.
	/// </summary>
	[Flags]
	[CLSCompliant(false)]
	public enum HeaderFLags : ushort
	{
		/// <summary>
		/// The baseline for this font is at the y-origin.
		/// </summary>
		BaselineAtOrigin = 1 << 0,

		/// <summary>
		/// The left side bearing is at the x-origin.
		/// </summary>
		LeftSidebearingAtOrigin = 1 << 1,

		/// <summary>
		/// The instructions in this font depend on the point size.
		/// </summary>
		InstructionsDependOnPointSize = 1 << 2,

		/// <summary>
		/// Use integer values only (no fractional ppem sizes) for internal scaler math.
		/// </summary>
		ForcePpemToIntegers = 1 << 3,

		/// <summary>
		/// Instructions may alter the advance width and therefore, advance widths might not scale linearly.
		/// </summary>
		InstructionsMayAlterAdvance = 1 << 4,

		// Bits 5-10 are not implemented in TrueType, but should be set according to Apple's specification

		/// <summary>
		/// The font is designed to be laid out vertically, using the x-origin as the vertical baseline.
		/// </summary>
		FontIsVertical = 1 << 5,

		/// <summary>
		/// This bit is reserved and must be set to zero.
		/// </summary>
		Reserved6 = 1 << 6,

		/// <summary>
		/// The font requires layout for linguistic rendering, such as in Arabic fonts.
		/// </summary>
		RequiresLayout = 1 << 7,

		/// <summary>
		/// The font is an AAT font having metamorphosis effects.
		/// </summary>
		HasMetamorphosisEffects = 1 << 8,

		/// <summary>
		/// The font contains strong right-to-left glyphs.
		/// </summary>
		RightToLeft = 1 << 9,

		/// <summary>
		/// The font contains Indic effects.
		/// </summary>
		IsIndic = 1 << 10,

		/// <summary>
		/// The font data has been compressed and decompressed, making the data lossless.
		/// </summary>
		FontDataIsLossless = 1 << 11,

		/// <summary>
		/// The font has been converted.
		/// </summary>
		FontIsConverted = 1 << 12,

		/// <summary>
		/// The font is optimized for ClearType display.
		/// </summary>
		FontIsClearTypeOptimized = 1 << 13,

		/// <summary>
		/// The glyphs in the cmap subtables are generic representations of code points.
		/// If unset, those glyphs represent proper support for those code points.
		/// </summary>
		FontIsLastResort = 1 << 14,

		// Bit 15: reserved
	}

	/// <summary>
	/// A structure used to model a TrueType font header table. All fields follow the TrueType specification.
	/// </summary>
	public class Header
	{
		#region Fields

		private IntPtr reference;
		private HeaderRec rec;

		#endregion

		#region Constructors

		internal Header(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// The version number of this table definition.
		/// </summary>
		public int TableVersion
		{
			get
			{
				return (int)rec.Table_Version;
			}
		}

		/// <summary>
		/// The version number of the font, provided by the font manufacturer.
		/// </summary>
		public int FontRevision
		{
			get
			{
				return (int)rec.Font_Revision;
			}
		}

		/// <summary>
		/// Provides a checksum of the font.
		/// </summary>
		public int ChecksumAdjust
		{
			get
			{
				return (int)rec.Checksum_Adjust;
			}
		}

		/// <summary>
		/// Gets the constant 0x5F0F3CF5.
		/// </summary>
		public int MagicNumber
		{
			get
			{
				return (int)rec.Magic_Number;
			}
		}

		/// <summary>
		/// Gets font flags for miscellaneous information.
		/// </summary>
		[CLSCompliant(false)]
		public HeaderFLags Flags
		{
			get
			{
				return (HeaderFLags)rec.Flags;
			}
		}

		/// <summary>
		/// Gets the designed number of units per em, also referred to as FUnits.
		/// </summary>
		[CLSCompliant(false)]
		public ushort UnitsPerEM
		{
			get
			{
				return rec.Units_Per_EM;
			}
		}

		/// <summary>
		/// Gets the date and time created.
		/// </summary>
		public int[] Created
		{
			get
			{
				return rec.Created.Select(x => (int)x).ToArray();
			}
		}

		/// <summary>
		/// Gets the last time modified.
		/// </summary>
		public int[] Modified
		{
			get
			{
				return rec.Modified.Select(x => (int)x).ToArray();
			}
		}

		/// <summary>
		/// Get the minimum X value of all glyph bounding boxes.
		/// </summary>
		public short MinimumX
		{
			get
			{
				return rec.xMin;
			}
		}

		/// <summary>
		/// Get the minimum Y value of all glyph bounding boxes.
		/// </summary>
		public short MinimumY
		{
			get
			{
				return rec.yMin;
			}
		}

		/// <summary>
		/// Get the maximum X value of all glyph bounding boxes.
		/// </summary>
		public short MaximumX
		{
			get
			{
				return rec.xMax;
			}
		}

		/// <summary>
		/// Get the maximum Y value of all glyph bounding boxes.
		/// </summary>
		public short MaximumY
		{
			get
			{
				return rec.yMax;
			}
		}

		/// <summary>
		/// Gets the basic style of the font (bold, etc.).
		/// </summary>
		[CLSCompliant(false)]
		public HeaderMacStyles MacStyle
		{
			get
			{
				return (HeaderMacStyles)rec.Mac_Style;
			}
		}

		/// <summary>
		/// Gets the smallest readable size, in pixels.
		/// </summary>
		[CLSCompliant(false)]
		public ushort LowestRecPpem
		{
			get
			{
				return rec.Lowest_Rec_PPEM;
			}
		}

		/// <summary>
		/// Gets the direction of glyhps (deprecated).
		/// </summary>
		public short FontDirection
		{
			get
			{
				return rec.Font_Direction;
			}
		}

		/// <summary>
		/// Gets the length of index: 0 for short, 1 for long.
		/// </summary>
		public short IndexToLocFormat
		{
			get
			{
				return rec.Index_To_Loc_Format;
			}
		}

		/// <summary>
		/// Gets the format of glyph data.
		/// </summary>
		public short GlyphDataFormat
		{
			get
			{
				return rec.Glyph_Data_Format;
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
				rec = PInvokeHelper.PtrToStructure<HeaderRec>(reference);
			}
		}

		#endregion
	}
}
