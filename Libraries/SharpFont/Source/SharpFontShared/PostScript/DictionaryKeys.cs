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
	/// An enumeration used in calls to <see cref="Face.GetPSFontValue"/> to identify the Type 1 dictionary entry to
	/// retrieve.
	/// </summary>
	/// <see href="http://partners.adobe.com/public/developer/en/font/T1_SPEC.PDF"/>
	public enum DictionaryKeys
	{
		/// <summary>
		/// The font's type. Type 1 fonts must have a value of 1.
		/// </summary>
		FontType,

		/// <summary>
		/// The font's matrix. Typically scaled 1000:1.
		/// </summary>
		FontMatrix,

		/// <summary>
		/// The font's general bounding box.
		/// </summary>
		FontBBox,

		/// <summary>
		/// The font's method of painting characters. Type 1 only supports fill (0) and outline (2).
		/// </summary>
		PaintType,

		/// <summary>
		/// The font's name.
		/// </summary>
		FontName,

		/// <summary>
		/// A unique identifier for popular fonts assigned by Adobe.
		/// </summary>
		UniqueId,

		/// <summary>
		/// The number of characters the font can draw.
		/// </summary>
		NumCharStrings,

		/// <summary>
		/// The char string key.
		/// </summary>
		CharStringKey,

		/// <summary>
		/// The char string entry.
		/// </summary>
		CharString,

		/// <summary>
		/// The font's encoding type.
		/// </summary>
		EncodingType,

		/// <summary>
		/// The font's encoding entry.
		/// </summary>
		EncodingEntry,

		/// <summary>
		/// The number of charstring subroutines in the font.
		/// </summary>
		NumSubrs,

		/// <summary>
		/// The font's subroutines.
		/// </summary>
		Subr,

		/// <summary>
		/// An array with only one real number entry expressing the dominant width of horizontal stems (measured
		/// vertically in character space units).
		/// </summary>
		StdHW,

		/// <summary>
		/// An array with only one real number entry expressing the dominant width of vertical stems (measured
		/// horizontally in character space units).
		/// </summary>
		StdVW,

		/// <summary>
		/// The number of BlueValues the font defines. The value must be at least 0 and at most 14. (7 integer pairs).
		/// </summary>
		NumBlueValues,

		/// <summary>
		/// An array of integer pairs. The first pair must be the base overshoot position and the base-line.
		/// </summary>
		BlueValue,

		/// <summary>
		/// An optional entry that speciﬁes the number of character space units to extend (in both directions) the
		/// effect of an alignment zone on a horizontal stem. The default value is 1.
		/// </summary>
		BlueFuzz,

		/// <summary>
		/// The number of OtherBlue values. The value must be at least 0 and at most 10 (5 integer pairs).
		/// </summary>
		NumOtherBlues,

		/// <summary>
		/// An optional array of integer pairs very similar to those in <see cref="BlueValue"/>.
		/// </summary>
		OtherBlue,

		/// <summary>
		/// The number of FamilyBlue values.
		/// </summary>
		NumFamilyBlues,

		/// <summary>
		/// An array of integer pairs very similar to those in <see cref="BlueValue"/>.
		/// </summary>
		FamilyBlue,

		/// <summary>
		/// The number of FamilyOtherBlue values.
		/// </summary>
		NumFamilyOtherBlues,

		/// <summary>
		/// An array of integer pairs very similar to those in <see cref="OtherBlue"/>.
		/// </summary>
		FamilyOtherBlue,

		/// <summary>
		/// An optional entry that controls the point size at which overshoot suppression ceases. The default value is
		/// 0.039625.
		/// </summary>
		BlueScale,

		/// <summary>
		/// An optional entry that indicates a character space distance beyond the ﬂat position of alignment zones at
		/// which overshoot enforcement for character features occurs. The default value is 7.
		/// </summary>
		BlueShift,

		/// <summary>
		/// The number of StemSnapH values. Cannot exceed 12.
		/// </summary>
		NumStemSnapH,

		/// <summary>
		/// An array of up to 12 real numbers of the most common widths (including the dominant width given in the
		/// StdHW array) for horizontal stems (measured vertically). These widths must be sorted in increasing order.
		/// </summary>
		StemSnapH,

		/// <summary>
		/// The number of StemSnapV values. Cannot exceed 12.
		/// </summary>
		NumStemSnapV,

		/// <summary>
		/// An array of up to 12 real numbers of the most common widths (including the dominant width given in the
		/// StdVW array) for vertical stems (measured horizontally). These widths must be sorted in increasing order.
		/// </summary>
		StemSnapV,

		/// <summary>
		/// A boolean value indicating whether to force bold characters when a regular character is drawn 1-pixel wide.
		/// </summary>
		ForceBold,

		/// <summary>
		/// Compatibility entry. Use only for font programs in language group 1.
		/// </summary>
		RndStemUp,

		/// <summary>
		/// Obsolete. Set to {16 16}. Required.
		/// </summary>
		MinFeature,

		/// <summary>
		/// An integer specifying the number of random bytes at the beginning of charstrings for encryption. By default
		/// this value is 4.
		/// </summary>
		LenIV,

		/// <summary>
		/// Compatibility entry. Set to 5839.
		/// </summary>
		Password,

		/// <summary>
		/// Identifies the language group of the font. A value of 0 indicates a language that uses Latin, Greek,
		/// Cyrillic, etc. characters. A value of 1 indicates a language that consists of Chinese ideographs, Jpaanese
		/// Kanji, and Korean Hangul. The default value is 0.
		/// </summary>
		LanguageGroup,

		/// <summary>
		/// The version identifier for this font.
		/// </summary>
		Version,

		/// <summary>
		/// The copyright notice of the font.
		/// </summary>
		Notice,

		/// <summary>
		/// The fullname of the font.
		/// </summary>
		FullName,

		/// <summary>
		/// The family name of the font.
		/// </summary>
		FamilyName,

		/// <summary>
		/// The name of the weight of the font.
		/// </summary>
		Weight,

		/// <summary>
		/// Whether the font is fixed pitch.
		/// </summary>
		IsFixedPitch,

		/// <summary>
		/// The position of the underline stroke.
		/// </summary>
		UnderlinePosition,

		/// <summary>
		/// What types of embedding and usages are allowed.
		/// </summary>
		FSType,

		/// <summary>
		/// The italic angle.
		/// </summary>
		ItalicAngle
	}
}
