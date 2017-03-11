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
	/// The maximum profile is a table containing many max values which can be used to pre-allocate arrays. This
	/// ensures that no memory allocation occurs during a glyph load.
	/// </summary>
	/// <remarks>
	/// This structure is only used during font loading.
	/// </remarks>
	public class MaxProfile
	{
		#region Fields

		private IntPtr reference;
		private MaxProfileRec rec;

		#endregion

		#region Constructors

		internal MaxProfile(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the version number.
		/// </summary>
		public int Version
		{
			get
			{
				return (int)rec.version;
			}
		}
		
		/// <summary>
		/// Gets the number of glyphs in this TrueType font.
		/// </summary>
		[CLSCompliant(false)]
		public ushort GlyphCount
		{
			get
			{
				return rec.numGlyphs;
			}
		}

		/// <summary>
		/// Gets the maximum number of points in a non-composite TrueType glyph. See also the structure element
		/// ‘maxCompositePoints’.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxPoints
		{
			get
			{
				return rec.maxPoints;
			}
		}

		/// <summary>
		/// Gets the maximum number of contours in a non-composite TrueType glyph. See also the structure element
		/// ‘maxCompositeContours’.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxContours
		{
			get
			{
				return rec.maxContours;
			}
		}

		/// <summary>
		/// Gets the maximum number of points in a composite TrueType glyph. See also the structure element
		/// ‘maxPoints’.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxCompositePoints
		{
			get
			{
				return rec.maxCompositePoints;
			}
		}

		/// <summary>
		/// Gets the maximum number of contours in a composite TrueType glyph. See also the structure element
		/// ‘maxContours’.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxCompositeContours
		{
			get
			{
				return rec.maxCompositeContours;
			}
		}

		/// <summary>
		/// Gets the maximum number of zones used for glyph hinting.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxZones
		{
			get
			{
				return rec.maxZones;
			}
		}

		/// <summary>
		/// Gets the maximum number of points in the twilight zone used for glyph hinting.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxTwilightPoints
		{
			get
			{
				return rec.maxTwilightPoints;
			}
		}

		/// <summary>
		/// Gets the maximum number of elements in the storage area used for glyph hinting.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxStorage
		{
			get
			{
				return rec.maxStorage;
			}
		}

		/// <summary>
		/// Gets the maximum number of function definitions in the TrueType bytecode for this font.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxFunctionDefs
		{
			get
			{
				return rec.maxFunctionDefs;
			}
		}

		/// <summary>
		/// Gets the maximum number of instruction definitions in the TrueType bytecode for this font.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxInstructionDefs
		{
			get
			{
				return rec.maxInstructionDefs;
			}
		}

		/// <summary>
		/// Gets the maximum number of stack elements used during bytecode interpretation.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxStackElements
		{
			get
			{
				return rec.maxStackElements;
			}
		}

		/// <summary>
		/// Gets the maximum number of TrueType opcodes used for glyph hinting.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxSizeOfInstructions
		{
			get
			{
				return rec.maxSizeOfInstructions;
			}
		}

		/// <summary>
		/// Gets the maximum number of simple (i.e., non- composite) glyphs in a composite glyph.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxComponentElements
		{
			get
			{
				return rec.maxComponentElements;
			}
		}

		/// <summary>
		/// Gets the maximum nesting depth of composite glyphs.
		/// </summary>
		[CLSCompliant(false)]
		public ushort MaxComponentDepth
		{
			get
			{
				return rec.maxComponentDepth;
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
				rec = PInvokeHelper.PtrToStructure<MaxProfileRec>(reference);
			}
		}

		#endregion
	}
}
