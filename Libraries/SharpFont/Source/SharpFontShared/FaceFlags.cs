#region MIT License
/*Copyright (c) 2012-2014 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// A list of bit flags used in the ‘face_flags’ field of the <see cref="Face"/> structure. They inform client
	/// applications of properties of the corresponding face.
	/// </summary>
	[Flags]
	public enum FaceFlags : long
	{
		/// <summary>
		/// No style flags.
		/// </summary>
		None = 0x0000,

		/// <summary>
		/// Indicates that the face contains outline glyphs. This doesn't prevent bitmap strikes, i.e., a face can have
		/// both this and and <see cref="FaceFlags.FixedSizes"/> set.
		/// </summary>
		Scalable = 0x0001,

		/// <summary>
		/// Indicates that the face contains bitmap strikes. See also <see cref="Face.FixedSizesCount"/> and
		/// <see cref="Face.AvailableSizes"/>.
		/// </summary>
		FixedSizes = 0x0002,

		/// <summary>
		/// Indicates that the face contains fixed-width characters (like Courier, Lucido, MonoType, etc.).
		/// </summary>
		FixedWidth = 0x0004,

		/// <summary>
		/// Indicates that the face uses the ‘sfnt’ storage scheme. For now, this means TrueType and OpenType.
		/// </summary>
		Sfnt = 0x0008,

		/// <summary>
		/// Indicates that the face contains horizontal glyph metrics. This should be set for all common formats.
		/// </summary>
		Horizontal = 0x0010,

		/// <summary>
		/// Indicates that the face contains vertical glyph metrics. This is only available in some formats, not all of
		/// them.
		/// </summary>
		Vertical = 0x0020,

		/// <summary>
		/// Indicates that the face contains kerning information. If set, the kerning distance can be retrieved through
		/// the function <see cref="Face.GetKerning"/>. Otherwise the function always return the vector (0,0). Note
		/// that FreeType doesn't handle kerning data from the ‘GPOS’ table (as present in some OpenType fonts).
		/// </summary>
		Kerning = 0x0040,

		/// <summary>
		/// THIS FLAG IS DEPRECATED. DO NOT USE OR TEST IT.
		/// </summary>
		[Obsolete("THIS FLAG IS DEPRECATED. DO NOT USE OR TEST IT.")]
		FastGlyphs = 0x0080,

		/// <summary>
		/// Indicates that the font contains multiple masters and is capable of interpolating between them. See the
		/// multiple-masters specific API for details.
		/// </summary>
		MultipleMasters = 0x0100,

		/// <summary>
		/// Indicates that the font contains glyph names that can be retrieved through
		/// <see cref="Face.GetGlyphName(uint, int)"/>. Note that some TrueType fonts contain broken glyph name
		/// tables. Use the function <see cref="Face.HasPSGlyphNames"/> when needed.
		/// </summary>
		GlyphNames = 0x0200,

		/// <summary>
		/// Used internally by FreeType to indicate that a face's stream was provided by the client application and
		/// should not be destroyed when <see cref="Face.Dispose()"/> is called. Don't read or test this flag.
		/// </summary>
		ExternalStream = 0x0400,

		/// <summary>
		/// Set if the font driver has a hinting machine of its own. For example, with TrueType fonts, it makes sense
		/// to use data from the SFNT ‘gasp’ table only if the native TrueType hinting engine (with the bytecode
		/// interpreter) is available and active.
		/// </summary>
		Hinter = 0x0800,

		/// <summary><para>
		/// Set if the font is CID-keyed. In that case, the font is not accessed by glyph indices but by CID values.
		/// For subsetted CID-keyed fonts this has the consequence that not all index values are a valid argument to
		/// <see cref="Face.LoadGlyph"/>. Only the CID values for which corresponding glyphs in the subsetted font
		/// exist make <see cref="Face.LoadGlyph"/> return successfully; in all other cases you get an
		/// <see cref="Error.InvalidArgument"/> error.
		/// </para><para>
		/// Note that CID-keyed fonts which are in an SFNT wrapper don't have this flag set since the glyphs are
		/// accessed in the normal way (using contiguous indices); the ‘CID-ness’ isn't visible to the application.
		/// </para></summary>
		CidKeyed = 0x1000,

		/// <summary><para>
		/// Set if the font is ‘tricky’, this is, it always needs the font format's native hinting engine to get a
		/// reasonable result. A typical example is the Chinese font ‘mingli.ttf’ which uses TrueType bytecode
		/// instructions to move and scale all of its subglyphs.
		/// </para><para>
		/// It is not possible to autohint such fonts using <see cref="LoadFlags.ForceAutohint"/>; it will also ignore
		/// <see cref="LoadFlags.NoHinting"/>. You have to set both <see cref="LoadFlags.NoHinting"/> and
		/// <see cref="LoadFlags.ForceAutohint"/> to really disable hinting; however, you probably never want this
		/// except for demonstration purposes.
		/// </para><para>
		/// Currently, there are about a dozen TrueType fonts in the list of tricky fonts; they are hard-coded in file
		/// ‘ttobjs.c’.
		/// </para></summary>
		Tricky = 0x2000,

		/// <summary>
		/// Set if the font has color glyph tables. To access color glyphs use <see cref="LoadFlags.Color"/>.
		/// </summary>
		Color = 0x4000,
	}
}
