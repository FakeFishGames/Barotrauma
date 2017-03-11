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

namespace SharpFont.PostScript
{
	/// <summary>
	/// A set of flags used to indicate which fields are present in a given blend dictionary (font info or private).
	/// Used to support Multiple Masters fonts.
	/// </summary>
	public enum BlendFlags
	{
		/// <summary>
		/// The position of the underline stroke.
		/// </summary>
		UnderlinePosition = 0,

		/// <summary>
		/// The thickness of the underline stroke.
		/// </summary>
		UnderlineThickness,

		/// <summary>
		/// The angle of italics.
		/// </summary>
		ItalicAngle,

		/// <summary>
		/// Set if the font contains BlueValues.
		/// </summary>
		BlueValues,

		/// <summary>
		/// Set if the font contains OtherBlues.
		/// </summary>
		OtherBlues,

		/// <summary>
		/// Set if the font contains StandardWidth values.
		/// </summary>
		StandardWidth,

		/// <summary>
		/// Set if the font contains StandardHeight values.
		/// </summary>
		StandardHeight,

		/// <summary>
		/// Set if the font contains StemSnapWidths.
		/// </summary>
		StemSnapWidths,

		/// <summary>
		/// Set if the font contains StemSnapHeights.
		/// </summary>
		StemSnapHeights,

		/// <summary>
		/// Set if the font contains BlueScale values.
		/// </summary>
		BlueScale,

		/// <summary>
		/// Set if the font contains BlueShift values.
		/// </summary>
		BlueShift,

		/// <summary>
		/// Set if the font contains FamilyBlues values.
		/// </summary>
		FamilyBlues,

		/// <summary>
		/// Set if the font contains FamilyOtherBlues values.
		/// </summary>
		FamilyOtherBlues,

		/// <summary>
		/// Force bold blending.
		/// </summary>
		ForceBold
	}
}
