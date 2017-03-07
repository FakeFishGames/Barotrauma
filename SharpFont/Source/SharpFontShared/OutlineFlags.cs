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

namespace SharpFont
{
	/// <summary>
	/// A list of bit-field constants use for the flags in an outline's ‘flags’ field.
	/// </summary>
	/// <remarks><para>
	/// The flags <see cref="OutlineFlags.IgnoreDropouts"/>, <see cref="OutlineFlags.SmartDropouts"/>, and
	/// <see cref="OutlineFlags.IncludeStubs"/> are ignored by the smooth rasterizer.
	/// </para><para>
	/// There exists a second mechanism to pass the drop-out mode to the B/W rasterizer; see the ‘tags’ field in
	/// <see cref="Outline"/>.
	/// </para><para>
	/// Please refer to the description of the ‘SCANTYPE’ instruction in the OpenType specification (in file
	/// ‘ttinst1.doc’) how simple drop-outs, smart drop-outs, and stubs are defined.
	/// </para></remarks>
	[Flags]
	public enum OutlineFlags
	{
		/// <summary>
		/// Value 0 is reserved.
		/// </summary>
		None = 0x0000,

		/// <summary>
		/// If set, this flag indicates that the outline's field arrays (i.e., ‘points’, ‘flags’, and ‘contours’) are
		/// ‘owned’ by the outline object, and should thus be freed when it is destroyed.
		/// </summary>
		Owner = 0x0001,

		/// <summary>
		/// By default, outlines are filled using the non-zero winding rule. If set to 1, the outline will be filled
		/// using the even-odd fill rule (only works with the smooth rasterizer).
		/// </summary>
		EvenOddFill = 0x0002,

		/// <summary>
		/// By default, outside contours of an outline are oriented in clock-wise direction, as defined in the TrueType
		/// specification. This flag is set if the outline uses the opposite direction (typically for Type 1 fonts).
		/// This flag is ignored by the scan converter.
		/// </summary>
		ReverseFill = 0x0004,

		/// <summary>
		/// By default, the scan converter will try to detect drop-outs in an outline and correct the glyph bitmap to
		/// ensure consistent shape continuity. If set, this flag hints the scan-line converter to ignore such cases.
		/// See below for more information.
		/// </summary>
		IgnoreDropouts = 0x0008,

		/// <summary>
		/// Select smart dropout control. If unset, use simple dropout control. Ignored if
		/// <see cref="OutlineFlags.IgnoreDropouts"/> is set. See below for more information.
		/// </summary>
		SmartDropouts = 0x0010,

		/// <summary>
		/// If set, turn pixels on for ‘stubs’, otherwise exclude them. Ignored if
		/// <see cref="OutlineFlags.IgnoreDropouts"/> is set. See below for more information.
		/// </summary>
		IncludeStubs =		0x0020,

		/// <summary>
		/// This flag indicates that the scan-line converter should try to convert this outline to bitmaps with the
		/// highest possible quality. It is typically set for small character sizes. Note that this is only a hint that
		/// might be completely ignored by a given scan-converter.
		/// </summary>
		HighPrecision =		0x0100,

		/// <summary>
		/// This flag is set to force a given scan-converter to only use a single pass over the outline to render a
		/// bitmap glyph image. Normally, it is set for very large character sizes. It is only a hint that might be
		/// completely ignored by a given scan-converter.
		/// </summary>
		SinglePass =		0x0200
	}
}
