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

namespace SharpFont.Internal
{
	/// <summary>
	/// Internally represents a Face.
	/// </summary>
	/// <remarks>
	/// Refer to <see cref="Face"/> for FreeType documentation.
	/// </remarks>
	[StructLayout(LayoutKind.Sequential)]
	internal struct FaceRec
	{
		internal FT_Long num_faces;
		internal FT_Long face_index;

		internal FT_Long face_flags;
		internal FT_Long style_flags;

		internal FT_Long num_glyphs;

		internal IntPtr family_name;
		internal IntPtr style_name;

		internal int num_fixed_sizes;
		internal IntPtr available_sizes;

		internal int num_charmaps;
		internal IntPtr charmaps;

		internal GenericRec generic;

		internal BBox bbox;

		internal ushort units_per_EM;
		internal short ascender;
		internal short descender;
		internal short height;

		internal short max_advance_width;
		internal short max_advance_height;

		internal short underline_position;
		internal short underline_thickness;

		internal IntPtr glyph;
		internal IntPtr size;
		internal IntPtr charmap;

		private IntPtr driver;
		private IntPtr memory;
		private IntPtr stream;

		private IntPtr sizes_list;
		private GenericRec autohint;
		private IntPtr extensions;

		private IntPtr @internal;

		internal static int SizeInBytes { get { return Marshal.SizeOf(typeof(FaceRec)); } }
	}
}
