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
	/// A list of bit flag constants as used in the ‘flags’ field of a <see cref="RasterParams"/> structure.
	/// </summary>
	[Flags]
	public enum RasterFlags
	{
		/// <summary>
		/// This value is 0.
		/// </summary>
		Default = 0x0,

		/// <summary>
		/// This flag is set to indicate that an anti-aliased glyph image should be generated. Otherwise, it will be
		/// monochrome (1-bit).
		/// </summary>
		AntiAlias = 0x1,

		/// <summary><para>
		/// This flag is set to indicate direct rendering. In this mode, client applications must provide their own
		/// span callback. This lets them directly draw or compose over an existing bitmap. If this bit is not set, the
		/// target pixmap's buffer must be zeroed before rendering.
		/// </para><para>
		/// Note that for now, direct rendering is only possible with anti-aliased glyphs.
		/// </para></summary>
		Direct = 0x2,

		/// <summary><para>
		/// This flag is only used in direct rendering mode. If set, the output will be clipped to a box specified in
		/// the ‘clip_box’ field of the <see cref="RasterParams"/> structure.
		/// </para><para>
		/// Note that by default, the glyph bitmap is clipped to the target pixmap, except in direct rendering mode
		/// where all spans are generated if no clipping box is set.
		/// </para></summary>
		Clip = 0x4
	}
}
