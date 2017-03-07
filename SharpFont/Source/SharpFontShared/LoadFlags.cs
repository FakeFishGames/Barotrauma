#region MIT License
/*Copyright (c) 2012-2016 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// A list of bit-field constants used with <see cref="Face.LoadGlyph"/> to indicate what kind of operations to
	/// perform during glyph loading.
	/// </summary>
	/// <remarks><para>
	/// By default, hinting is enabled and the font's native hinter (see <see cref="FaceFlags.Hinter"/>) is preferred
	/// over the auto-hinter. You can disable hinting by setting <see cref="LoadFlags.NoHinting"/> or change the
	/// precedence by setting <see cref="LoadFlags.ForceAutohint"/>. You can also set
	/// <see cref="LoadFlags.NoAutohint"/> in case you don't want the auto-hinter to be used at all.
	/// </para><para>
	/// See the description of <see cref="FaceFlags.Tricky"/> for a special exception (affecting only a handful of
	/// Asian fonts).
	/// </para><para>
	/// Besides deciding which hinter to use, you can also decide which hinting algorithm to use. See
	/// <see cref="LoadTarget"/> for details.
	/// </para></remarks>
	[Flags]
	[CLSCompliant(false)]
	public enum LoadFlags : uint
	{
		/// <summary>
		/// Corresponding to 0, this value is used as the default glyph load operation. In this case, the following
		/// happens:
		/// <list type="number">
		/// <item><description>
		/// FreeType looks for a bitmap for the glyph corresponding to the face's current size. If one is found, the
		/// function returns. The bitmap data can be accessed from the glyph slot (see note below).
		/// </description></item>
		/// <item><description>
		/// If no embedded bitmap is searched or found, FreeType looks for a scalable outline. If one is found, it is
		/// loaded from the font file, scaled to device pixels, then ‘hinted’ to the pixel grid in order to optimize
		/// it. The outline data can be accessed from the glyph slot (see note below).
		/// </description></item>
		/// </list>
		/// Note that by default, the glyph loader doesn't render outlines into bitmaps. The following flags are used
		/// to modify this default behaviour to more specific and useful cases.
		/// </summary>
		Default = 0x000000,

		/// <summary><para>
		/// Don't scale the outline glyph loaded, but keep it in font units.
		/// </para><para>
		/// This flag implies <see cref="LoadFlags.NoHinting"/> and <see cref="LoadFlags.NoBitmap"/>, and unsets
		/// <see cref="LoadFlags.Render"/>.
		/// </para></summary>
		NoScale = 0x000001,

		/// <summary><para>
		/// Disable hinting. This generally generates ‘blurrier’ bitmap glyph when the glyph is rendered in any of the
		/// anti-aliased modes. See also the note below.
		/// </para><para>
		/// This flag is implied by <see cref="LoadFlags.NoScale"/>.
		/// </para></summary>
		NoHinting = 0x000002,

		/// <summary><para>
		/// Call <see cref="GlyphSlot.RenderGlyph"/> after the glyph is loaded. By default, the glyph is rendered in
		/// <see cref="RenderMode.Normal"/> mode. This can be overridden by <see cref="LoadTarget"/> or
		/// <see cref="LoadFlags.Monochrome"/>.
		/// </para><para>
		/// This flag is unset by <see cref="LoadFlags.NoScale"/>.
		/// </para></summary>
		Render = 0x000004,

		/// <summary><para>
		/// Ignore bitmap strikes when loading. Bitmap-only fonts ignore this flag.
		/// </para><para>
		/// <see cref="LoadFlags.NoScale"/> always sets this flag.
		/// </para></summary>
		NoBitmap = 0x000008,

		/// <summary>
		/// Load the glyph for vertical text layout. Don't use it as it is problematic currently.
		/// </summary>
		VerticalLayout = 0x000010,

		/// <summary>
		/// Indicates that the auto-hinter is preferred over the font's native hinter. See also the note below.
		/// </summary>
		ForceAutohint = 0x000020,

		/// <summary>
		/// Indicates that the font driver should crop the loaded bitmap glyph (i.e., remove all space around its black
		/// bits). Not all drivers implement this.
		/// </summary>
		CropBitmap = 0x000040,

		/// <summary>
		/// Indicates that the font driver should perform pedantic verifications during glyph loading. This is mostly
		/// used to detect broken glyphs in fonts. By default, FreeType tries to handle broken fonts also.
		/// </summary>
		Pedantic = 0x000080,

		/// <summary>
		/// Ignored. Deprecated.
		/// </summary>
		[Obsolete("Ignored. Deprecated.")]
		IgnoreGlobalAdvanceWidth = 0x000200,

		/// <summary><para>
		/// This flag is only used internally. It merely indicates that the font driver should not load composite
		/// glyphs recursively. Instead, it should set the ‘num_subglyph’ and ‘subglyphs’ values of the glyph slot
		/// accordingly, and set ‘glyph->format’ to <see cref="GlyphFormat.Composite"/>.
		/// </para><para>
		/// The description of sub-glyphs is not available to client applications for now.
		/// </para><para>
		/// This flag implies <see cref="LoadFlags.NoScale"/> and <see cref="LoadFlags.IgnoreTransform"/>.
		/// </para></summary>
		NoRecurse = 0x000400,

		/// <summary>
		/// Indicates that the transform matrix set by <see cref="Face.SetTransform()"/> should be ignored.
		/// </summary>
		IgnoreTransform = 0x000800,

		/// <summary><para>
		/// This flag is used with <see cref="LoadFlags.Render"/> to indicate that you want to render an outline glyph
		/// to a 1-bit monochrome bitmap glyph, with 8 pixels packed into each byte of the bitmap data.
		/// </para><para>
		/// Note that this has no effect on the hinting algorithm used. You should rather use
		/// <see cref="LoadTarget.Mono"/> so that the monochrome-optimized hinting algorithm is used.
		/// </para></summary>
		Monochrome = 0x001000,

		/// <summary>
		/// Indicates that the ‘linearHoriAdvance’ and ‘linearVertAdvance’ fields of <see cref="GlyphSlot"/> should be
		/// kept in font units. See <see cref="GlyphSlot"/> for details.
		/// </summary>
		LinearDesign = 0x002000,

		/// <summary>
		/// Disable auto-hinter. See also the note below.
		/// </summary>
		NoAutohint = 0x008000,

		/// <summary>
		/// This flag is used to request loading of color embedded-bitmap images. The resulting color bitmaps, if
		/// available, will have the <see cref="PixelMode.Bgra"/> format. When the flag is not used and color bitmaps
		/// are found, they will be converted to 256-level gray bitmaps transparently. Those bitmaps will be in the
		/// <see cref="PixelMode.Gray"/> format.
		/// </summary>
		Color = 0x100000,

		/// <summary>
		/// This flag sets computing glyph metrics without the use of bundled
		/// metrics tables. Well-behaving fonts have optimized bundled metrics
		/// and these should be used. This flag is mainly used by font
		/// validating or font editing applications which need to ignore, verify
		/// or edit those tables.
		/// </summary>
		ComputeMetrics = 0x200000,

		/// <summary><para>
		/// A bit-flag to be OR-ed with the ‘flags’ parameter of the <see cref="Face.GetAdvance"/> and
		/// <see cref="Face.GetAdvances"/> functions.
		/// </para><para>
		/// If set, it indicates that you want these functions to fail if the corresponding hinting mode or font driver
		/// doesn't allow for very quick advance computation.
		/// </para><para>
		/// Typically, glyphs which are either unscaled, unhinted, bitmapped, or light-hinted can have their advance
		/// width computed very quickly.
		/// </para><para>
		/// Normal and bytecode hinted modes, which require loading, scaling, and hinting of the glyph outline, are
		/// extremely slow by comparison.
		/// </para></summary>
		AdvanceFlagFastOnly = 0x20000000
	}
}
