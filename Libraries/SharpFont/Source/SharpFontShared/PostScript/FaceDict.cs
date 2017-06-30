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

using SharpFont.PostScript.Internal;

namespace SharpFont.PostScript
{
	/// <summary>
	/// A structure used to represent data in a CID top-level dictionary.
	/// </summary>
	public class FaceDict
	{
		#region Fields

		private IntPtr reference;
		private FaceDictRec rec;

		#endregion

		#region Constructors

		internal FaceDict(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the Private structure containing more information.
		/// </summary>
		public Private PrivateDictionary
		{
			get
			{
				return new Private(rec.private_dict);
			}
		}

		/// <summary>
		/// Gets the length of the BuildChar entry.
		/// </summary>
		[CLSCompliant(false)]
		public uint BuildCharLength
		{
			get
			{
				return rec.len_buildchar;
			}
		}

		/// <summary>
		/// Gets whether to force bold characters when a regular character has
		/// strokes drawn 1-pixel wide.
		/// </summary>
		public int ForceBoldThreshold
		{
			get
			{
				return (int)rec.forcebold_threshold;
			}
		}

		/// <summary>
		/// Gets the width of stroke.
		/// </summary>
		public int StrokeWidth
		{
			get
			{
				return (int)rec.stroke_width;
			}
		}

		/// <summary>
		/// Gets hinting useful for rendering glyphs such as barcodes and logos that
		/// have many counters.
		/// </summary>
		public int ExpansionFactor
		{
			get
			{
				return (int)rec.expansion_factor;
			}
		}

		/// <summary>
		/// Gets the method for painting strokes (fill or outline).
		/// </summary>
		public byte PaintType
		{
			get
			{
				return rec.paint_type;
			}
		}

		/// <summary>
		/// Gets the type of font. Must be set to 1 for all Type 1 fonts.
		/// </summary>
		public byte FontType
		{
			get
			{
				return rec.font_type;
			}
		}

		/// <summary>
		/// Gets the matrix that indicates scaling of space units.
		/// </summary>
		public FTMatrix FontMatrix
		{
			get
			{
				return rec.font_matrix;
			}
		}

		/// <summary>
		/// Gets the offset of the font.
		/// </summary>
		public FTVector FontOffset
		{
			get
			{
				return rec.font_offset;
			}
		}

		/// <summary>
		/// Gets the number of subroutines.
		/// </summary>
		[CLSCompliant(false)]
		public uint SubrsCount
		{
			get
			{
				return rec.num_subrs;
			}
		}

		/// <summary>
		/// Gets the offset in bytes, from the start of the
		/// data section of the CIDFont to the beginning of the SubrMap.
		/// </summary>
		[CLSCompliant(false)]
		public uint SubrmapOffset
		{
			get
			{
				return (uint)rec.subrmap_offset;
			}
		}

		/// <summary>
		/// Gets the number of bytes needed to store the SD value.
		/// </summary>
		public int SDBytes
		{
			get
			{
				return rec.sd_bytes;
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
				rec = PInvokeHelper.PtrToStructure<FaceDictRec>(reference);
			}
		}

		#endregion
	}
}
