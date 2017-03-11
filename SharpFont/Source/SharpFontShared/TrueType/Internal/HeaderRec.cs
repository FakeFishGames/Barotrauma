#region MIT License
/*Copyright (c) 2012-2015 Robert Rouhani <robert.rouhani@gmail.com>

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

using FT_Long = System.IntPtr;
using FT_ULong = System.UIntPtr;

namespace SharpFont.TrueType.Internal
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct HeaderRec
	{
		internal FT_Long Table_Version;
		internal FT_Long Font_Revision;

		internal FT_Long Checksum_Adjust;
		internal FT_Long Magic_Number;

		internal ushort Flags;
		internal ushort Units_Per_EM;

		private FT_Long created1;
		private FT_Long created2;
		internal FT_Long[] Created { get { return new[] {created1, created2}; } }

		private FT_Long modified1;
		private FT_Long modified2;
		internal FT_Long[] Modified { get { return new[] { modified1, modified2 }; } }

		internal short xMin;
		internal short yMin;
		internal short xMax;
		internal short yMax;

		internal ushort Mac_Style;
		internal ushort Lowest_Rec_PPEM;

		internal short Font_Direction;
		internal short Index_To_Loc_Format;
		internal short Glyph_Data_Format;
	}
}
