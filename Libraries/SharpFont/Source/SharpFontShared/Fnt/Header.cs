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

using SharpFont.Fnt.Internal;

namespace SharpFont.Fnt
{
	/// <summary>
	/// Describes the general appearance of the font.
	/// </summary>
	public enum Family
	{
		/// <summary>
		/// Don't care or don't know which family.
		/// </summary>
		DontCare = 0,

		/// <summary>
		/// The font has a Roman appearance.
		/// </summary>
		Roman = 1,

		/// <summary>
		/// The font has a Swiss appearance.
		/// </summary>
		Swiss = 2,

		/// <summary>
		/// The font has a Modern appearance.
		/// </summary>
		Modern = 3,

		/// <summary>
		/// The font has a script-like appearance.
		/// </summary>
		Script = 4,

		/// <summary>
		/// The font is decorative.
		/// </summary>
		Decorative = 5
	}

	/// <summary>
	/// Provides flags for font proportions and color.
	/// </summary>
	[Flags]
	[CLSCompliant(false)]
	public enum Flags : ushort
	{
		/// <summary>
		/// Font is fixed.
		/// </summary>
		Fixed = 1 << 0,

		/// <summary>
		/// Font is proportional.
		/// </summary>
		Proportional = 1 << 1,

		/// <summary>
		/// Font is ABC fixed.
		/// </summary>
		AbcFixed = 1 << 2,

		/// <summary>
		/// Font is ABC proportional.
		/// </summary>
		AbcProportional = 1 << 3,

		/// <summary>
		/// Font is 2-bit color.
		/// </summary>
		Color1 = 1 << 4,

		/// <summary>
		/// Font is 4-bit color.
		/// </summary>
		Color16 = 1 << 5,

		/// <summary>
		/// Font is 8-bit color.
		/// </summary>
		Color256 = 1 << 6,

		/// <summary>
		/// Font is RGB color.
		/// </summary>
		RgbColor = 1 << 7
	}

	/// <summary>
	/// Windows FNT Header info.
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
		/// Gets the version format of the file (e.g. 0x0200).
		/// </summary>
		[CLSCompliant(false)]
		public ushort Version
		{
			get
			{
				return rec.version;
			}
		}

		/// <summary>
		/// Gets the size of the file in bytes.
		/// </summary>
		[CLSCompliant(false)]
		public uint FileSize
		{
			get
			{
				return (uint)rec.file_size;
			}
		}

		/// <summary>
		/// Gets the copyright text.
		/// Limited to 60 bytes.
		/// </summary>
		public byte[] Copyright
		{
			get
			{
				return rec.copyright;
			}
		}

		/// <summary>
		/// Gets the filetype (vector or bitmap). This is exclusively for GDI use.
		/// </summary>
		[CLSCompliant(false)]
		public ushort FileType
		{
			get
			{
				return rec.file_type;
			}
		}

		/// <summary>
		/// Gets the nominal point size determined by the designer at which the font looks
		/// best.
		/// </summary>
		[CLSCompliant(false)]
		public ushort NominalPointSize
		{
			get
			{
				return rec.nominal_point_size;
			}
		}

		/// <summary>
		/// Gets the nominal vertical resolution in dots per inch.
		/// </summary>
		[CLSCompliant(false)]
		public ushort VerticalResolution
		{
			get
			{
				return rec.vertical_resolution;
			}
		}

		/// <summary>
		/// Gets the nominal horizontal resolution in dots per inch.
		/// </summary>
		[CLSCompliant(false)]
		public ushort HorizontalResolution
		{
			get
			{
				return rec.horizontal_resolution;
			}
		}

		/// <summary>
		/// Gets the height of the font's ascent from the baseline.
		/// </summary>
		[CLSCompliant(false)]
		public ushort Ascent
		{
			get
			{
				return rec.ascent;
			}
		}

		/// <summary>
		/// Gets the amount of leading inside the bounds of <see cref="PixelHeight"/>.
		/// </summary>
		[CLSCompliant(false)]
		public ushort InternalLeading
		{
			get
			{
				return rec.internal_leading;
			}
		}

		/// <summary>
		/// Gets the amount of leading the designer recommends to be added between
		/// rows.
		/// </summary>
		[CLSCompliant(false)]
		public ushort ExternalLeading
		{
			get
			{
				return rec.external_leading;
			}
		}

		/// <summary>
		/// Gets whether the font is italic.
		/// </summary>
		public bool Italic
		{
			get
			{
				return (0x01 & rec.italic) == 0x01;
			}
		}

		/// <summary>
		/// Ges whether the font includes underlining.
		/// </summary>
		public bool Underline
		{
			get
			{
				return (0x01 & rec.underline) == 0x01;
			}
		}

		/// <summary>
		/// Ges whether the font includes strikeout.
		/// </summary>
		public bool Strikeout
		{
			get
			{
				return (0x01 & rec.strike_out) == 0x01;
			}
		}

		/// <summary>
		/// Gets the weight of characters on a scale of 1 to 1000, with
		/// 400 being regular weight.
		/// </summary>
		[CLSCompliant(false)]
		public ushort Weight
		{
			get
			{
				return rec.weight;
			}
		}

		/// <summary>
		/// Gets the character set specified by the font.
		/// </summary>
		public byte Charset
		{
			get
			{
				return rec.charset;
			}
		}

		/// <summary>
		/// Gets the width of the vector grid (vector fonts). For raster fonts,
		/// a zero value indicates that characters have variables widths,
		/// otherwise, the value is the width of the bitmap for all characters.
		/// </summary>
		[CLSCompliant(false)]
		public ushort PixelWidth
		{
			get
			{
				return rec.pixel_width;
			}
		}

		/// <summary>
		/// Gets the height of the vector grid (vector fonts) or the height
		/// of the bitmap for all characters (raster fonts).
		/// </summary>
		[CLSCompliant(false)]
		public ushort PixelHeight
		{
			get
			{
				return rec.pixel_height;
			}
		}

		/// <summary>
		/// Gets whether the font is variable pitch.
		/// </summary>
		public byte PitchAndFamily
		{
			get
			{
				return rec.pitch_and_family;
			}
		}

		/// <summary>
		/// Gets the width of characters in the font, based on the width of 'X'.
		/// </summary>
		[CLSCompliant(false)]
		public ushort AverageWidth
		{
			get
			{
				return rec.avg_width;
			}
		}

		/// <summary>
		/// Gets the maximum width of all characters in the font.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaximumWidth
		{
			get
			{
				return rec.max_width;
			}
		}

		/// <summary>
		/// Gets the first character code specified in the font.
		/// </summary>
		public byte FirstChar
		{
			get
			{
				return rec.first_char;
			}
		}

		/// <summary>
		/// Gets the last character code specified in the font.
		/// </summary>
		public byte LastChar
		{
			get
			{
				return rec.last_char;
			}
		}

		/// <summary>
		/// Gets the character to substitute when a character is needed that
		/// isn't defined in the font.
		/// </summary>
		public byte DefaultChar
		{
			get
			{
				return rec.default_char;
			}
		}

		/// <summary>
		/// Gets the character that defines word breaks, for purposes of word
		/// wrapping and word spacing justification. This value is relative to
		/// the <see cref="FirstChar"/>, so the character code is this value
		/// minus <see cref="FirstChar"/>.
		/// </summary>
		public byte BreakChar
		{
			get
			{
				return rec.break_char;
			}
		}

		/// <summary>
		/// Gets the number of bytes in each row of the bitmap (raster fonts).
		/// </summary>
		[CLSCompliant(false)]
		public ushort BytesPerRow
		{
			get
			{
				return rec.bytes_per_row;
			}
		}

		/// <summary>
		/// Gets the offset in the file, in bytes, to the string that gives the device name.
		/// The value is 0 for generic fonts.
		/// </summary>
		[CLSCompliant(false)]
		public uint DeviceOffset
		{
			get
			{
				return (uint)rec.device_offset;
			}
		}

		/// <summary>
		/// Gets the offset in the file, in bytes, to the string that gives the face name
		/// (null-terminated).
		/// </summary>
		[CLSCompliant(false)]
		public uint FaceNameOffset
		{
			get
			{
				return (uint)rec.face_name_offset;
			}
		}

		/// <summary>
		/// Gets the absolute machine address of the bitmap,
		/// which is set by GDI at load time.
		/// </summary>
		[CLSCompliant(false)]
		public uint BitsPointer
		{
			get
			{
				return (uint)rec.bits_pointer;
			}
		}

		/// <summary>
		/// Gets the offset in the file, in bytes, to the beginning of the character data
		/// (raster or vector).
		/// </summary>
		[CLSCompliant(false)]
		public uint BitsOffset
		{
			get
			{
				return (uint)rec.bits_offset;
			}
		}

		/// <summary>
		/// Reservied.
		/// </summary>
		public byte Reserved
		{
			get
			{
				return rec.reserved;
			}
		}

		/// <summary>
		/// Gets <see cref="Flags"/> that describe font proportion and color.
		/// </summary>
		[CLSCompliant(false)]
		public Flags Flags
		{
			get
			{
				return (Flags)rec.flags;
			}
		}

		/// <summary>
		/// ASpace has not been used since before Windows 3.0.
		/// Set it to 0 for compatibility.
		/// </summary>
		[CLSCompliant(false)]
		public ushort ASpace
		{
			get
			{
				return rec.A_space;
			}
		}

		/// <summary>
		/// BSpace has not been used since before Windows 3.0.
		/// Set it to 0 for compatibility.
		/// </summary>
		[CLSCompliant(false)]
		public ushort BSpace
		{
			get
			{
				return rec.B_space;
			}
		}

		/// <summary>
		/// CSpace has not been used since before Windows 3.0.
		/// Set it to 0 for compatibility.
		/// </summary>
		[CLSCompliant(false)]
		public ushort CSpace
		{
			get
			{
				return rec.C_space;
			}
		}

		/// <summary>
		/// Gets the offset of the color table.
		/// </summary>
		[CLSCompliant(false)]
		public ushort ColorTableOffset
		{
			get
			{
				return rec.color_table_offset;
			}
		}

		/// <summary>
		/// This field is reserved.
		/// </summary>
		[CLSCompliant(false)]
		public uint[] Reserved1
		{
			get
			{
				return rec.reserved1.Select(x => (uint) x).ToArray();
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
