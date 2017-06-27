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

namespace SharpFont.PostScript.Internal
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct PrivateRec
	{
		internal int unique_id;
		internal int lenIV;

		internal byte num_blue_values;
		internal byte num_other_blues;
		internal byte num_family_blues;
		internal byte num_family_other_blues;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
		internal short[] blue_values;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
		internal short[] other_blues;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
		internal short[] family_blues;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
		internal short[] family_other_blues;

		internal FT_Long blue_scale;
		internal int blue_shift;
		internal int blue_fuzz;

		internal ushort standard_width;
		internal ushort standard_height;

		internal byte num_snap_widths;
		internal byte num_snap_heights;
		internal byte force_bold;
		internal byte round_stem_up;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
		internal short[] snap_widths;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
		internal short[] snap_heights;

		internal FT_Long expansion_factor;

		internal FT_Long language_group;
		internal FT_Long password;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
		internal short[] min_feature;
	}
}
