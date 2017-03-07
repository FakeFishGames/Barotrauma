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

using SharpFont.Internal;

using FT_Long = System.IntPtr;
using FT_ULong = System.UIntPtr;

namespace SharpFont.PostScript.Internal
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct FaceDictRec
	{
		internal PrivateRec private_dict;

		internal uint len_buildchar;
		internal FT_Long forcebold_threshold;
		internal FT_Long stroke_width;
		internal FT_Long expansion_factor;

		internal byte paint_type;
		internal byte font_type;
		internal FTMatrix font_matrix;
		internal FTVector font_offset;

		internal uint num_subrs;
		internal FT_ULong subrmap_offset;
		internal int sd_bytes;
	}
}
