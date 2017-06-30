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
	/// <summary><para>
	/// An enumeration type that lists the render modes supported by FreeType 2. Each mode corresponds to a specific
	/// type of scanline conversion performed on the outline.
	/// </para><para>
	/// For bitmap fonts and embedded bitmaps the <see cref="FTBitmap.PixelMode"/> field in the <see cref="GlyphSlot"/>
	/// structure gives the format of the returned bitmap.
	/// </para><para>
	/// All modes except <see cref="RenderMode.Mono"/> use 256 levels of opacity.
	/// </para></summary>
	/// <remarks><para>
	/// The LCD-optimized glyph bitmaps produced by <see cref="GlyphSlot.RenderGlyph"/> can be filtered to reduce
	/// color-fringes by using <see cref="Library.SetLcdFilter"/> (not active in the default builds). It is up to the
	/// caller to either call <see cref="Library.SetLcdFilter"/> (if available) or do the filtering itself.
	/// </para><para>
	/// The selected render mode only affects vector glyphs of a font. Embedded bitmaps often have a different pixel
	/// mode like <see cref="PixelMode.Mono"/>. You can use <see cref="FTBitmap.Convert"/> to transform them into 8-bit
	/// pixmaps.
	/// </para></remarks>
	public enum RenderMode
	{
		/// <summary>
		/// This is the default render mode; it corresponds to 8-bit anti-aliased bitmaps.
		/// </summary>
		Normal = 0,

		/// <summary>
		/// This is equivalent to <see cref="RenderMode.Normal"/>. It is only defined as a separate value because
		/// render modes are also used indirectly to define hinting algorithm selectors.
		/// </summary>
		/// <see cref="LoadTarget"/>
		Light,

		/// <summary>
		/// This mode corresponds to 1-bit bitmaps (with 2 levels of opacity).
		/// </summary>
		Mono,

		/// <summary>
		/// This mode corresponds to horizontal RGB and BGR sub-pixel displays like LCD screens. It produces 8-bit
		/// bitmaps that are 3 times the width of the original glyph outline in pixels, and which use the
		/// <see cref="PixelMode.Lcd"/> mode.
		/// </summary>
		Lcd,

		/// <summary>
		/// This mode corresponds to vertical RGB and BGR sub-pixel displays (like PDA screens, rotated LCD displays,
		/// etc.). It produces 8-bit bitmaps that are 3 times the height of the original glyph outline in pixels and
		/// use the <see cref="PixelMode.VerticalLcd"/> mode.
		/// </summary>
		VerticalLcd,
	}
}
