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
	/// A list of values that are used to select a specific hinting algorithm to use by the hinter. You should OR one
	/// of these values to your ‘load_flags’ when calling <see cref="Face.LoadGlyph"/>.
	/// </para><para>
	/// Note that font's native hinters may ignore the hinting algorithm you  have specified (e.g., the TrueType
	/// bytecode interpreter). You can set <see cref="LoadFlags.ForceAutohint"/> to ensure that the auto-hinter is
	/// used.
	/// </para><para>
	/// Also note that <see cref="LoadTarget.Light"/> is an exception, in that it always implies
	/// <see cref="LoadFlags.ForceAutohint"/>.
	/// </para></summary>
	/// <remarks><para>
	/// You should use only one of the <see cref="LoadTarget"/> values in your ‘load_flags’. They can't be ORed.
	/// </para><para>
	/// If <see cref="LoadFlags.Render"/> is also set, the glyph is rendered in the corresponding mode (i.e., the mode
	/// which matches the used algorithm best) unless <see cref="LoadFlags.Monochrome"/> is set.
	/// </para><para>
	/// You can use a hinting algorithm that doesn't correspond to the same rendering mode. As an example, it is
	/// possible to use the ‘light’ hinting algorithm and have the results rendered in horizontal LCD pixel mode, with
	/// code like:
	/// <code>
	/// FT_Load_Glyph( face, glyph_index,
	///          load_flags | FT_LOAD_TARGET_LIGHT );
	///
	/// FT_Render_Glyph( face->glyph, FT_RENDER_MODE_LCD );
	/// </code>
	/// </para></remarks>
	public enum LoadTarget
	{
		/// <summary>
		/// This corresponds to the default hinting algorithm, optimized for standard gray-level rendering. For
		/// monochrome output, use <see cref="LoadTarget.Mono"/> instead.
		/// </summary>
		Normal = (RenderMode.Normal & 15) << 16,

		/// <summary><para>
		/// A lighter hinting algorithm for non-monochrome modes. Many generated glyphs are more fuzzy but better
		/// resemble its original shape. A bit like rendering on Mac OS X.
		/// </para><para>
		/// As a special exception, this target implies <see cref="LoadFlags.ForceAutohint"/>.
		/// </para></summary>
		Light = (RenderMode.Light & 15) << 16,

		/// <summary>
		/// Strong hinting algorithm that should only be used for monochrome output. The result is probably unpleasant
		/// if the glyph is rendered in non-monochrome modes.
		/// </summary>
		Mono = (RenderMode.Mono & 15) << 16,

		/// <summary>
		/// A variant of <see cref="LoadTarget.Normal"/> optimized for horizontally decimated LCD displays.
		/// </summary>
		Lcd = (RenderMode.Lcd & 15) << 16,

		/// <summary>
		/// A variant of <see cref="LoadTarget.Normal"/> optimized for vertically decimated LCD displays.
		/// </summary>
		VerticalLcd = (RenderMode.VerticalLcd & 15) << 16
	}
}
