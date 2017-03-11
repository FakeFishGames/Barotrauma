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
using SharpFont.TrueType;

namespace SharpFont
{
	/// <summary>
	/// The base charmap structure.
	/// </summary>
	public sealed class CharMap
	{
		#region Fields

		private IntPtr reference;
		private CharMapRec rec;

		private Face parentFace;

		#endregion

		#region Constructors

		internal CharMap(IntPtr reference, Face parent)
		{
			Reference = reference;
			this.parentFace = parent;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets a handle to the parent face object.
		/// </summary>
		public Face Face
		{
			get
			{
				return parentFace;
			}
		}

		/// <summary>
		/// Gets an <see cref="Encoding"/> tag identifying the charmap. Use this with
		/// <see cref="SharpFont.Face.SelectCharmap"/>.
		/// </summary>
		[CLSCompliant(false)]
		public Encoding Encoding
		{
			get
			{
				return rec.encoding;
			}
		}

		/// <summary>
		/// Gets an ID number describing the platform for the following encoding ID. This comes directly from the
		/// TrueType specification and should be emulated for other formats.
		/// </summary>
		[CLSCompliant(false)]
		public PlatformId PlatformId
		{
			get
			{
				return rec.platform_id;
			}
		}

		/// <summary>
		/// Gets a platform specific encoding number. This also comes from the TrueType specification and should be
		/// emulated similarly.
		/// </summary>
		[CLSCompliant(false)]
		public ushort EncodingId
		{
			get
			{
				//TODO find some way of getting a proper encoding ID enum...
				return rec.encoding_id;
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
				rec = PInvokeHelper.PtrToStructure<CharMapRec>(reference);
			}
		}

		#endregion

		#region Methods

		#region Base Interface

		/// <summary>
		/// Retrieve index of a given charmap.
		/// </summary>
		/// <returns>The index into the array of character maps within the face to which ‘charmap’ belongs.</returns>
		public int GetCharmapIndex()
		{
			return FT.FT_Get_Charmap_Index(Reference);
		}

		#endregion

		#region TrueType Tables

		/// <summary>
		/// Return TrueType/sfnt specific cmap language ID. Definitions of language ID values are in
		/// ‘freetype/ttnameid.h’.
		/// </summary>
		/// <returns>
		/// The language ID of ‘charmap’. If ‘charmap’ doesn't belong to a TrueType/sfnt face, just return 0 as the
		/// default value.
		/// </returns>
		[CLSCompliant(false)]
		public uint GetCMapLanguageId()
		{
			return FT.FT_Get_CMap_Language_ID(Reference);
		}

		/// <summary>
		/// Return TrueType/sfnt specific cmap format.
		/// </summary>
		/// <returns>The format of ‘charmap’. If ‘charmap’ doesn't belong to a TrueType/sfnt face, return -1.</returns>
		public int GetCMapFormat()
		{
			return FT.FT_Get_CMap_Format(Reference);
		}

		#endregion

		#endregion
	}
}
