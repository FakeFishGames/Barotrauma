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
	internal struct FaceInfoRec
	{
		[MarshalAs(UnmanagedType.LPStr)]
		internal string cid_font_name;
		internal FT_Long cid_version;
		internal int cid_font_type;

		[MarshalAs(UnmanagedType.LPStr)]
		internal string registry;

		[MarshalAs(UnmanagedType.LPStr)]
		internal string ordering;
		internal int supplement;

		internal FontInfoRec font_info;
		internal BBox font_bbox;
		internal FT_ULong uid_base;

		internal int num_xuid;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
		internal FT_ULong[] xuid;

		internal FT_ULong cidmap_offset;
		internal int fd_bytes;
		internal int gd_bytes;
		internal FT_ULong cid_count;

		internal int num_dicts;
		internal IntPtr font_dicts;

		internal FT_ULong data_offset;
	}
}
