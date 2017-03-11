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

namespace SharpFont.TrueType
{
	/// <summary>
	/// A list of valid values for the ‘encoding_id’ for TT_PLATFORM_APPLE_UNICODE charmaps and name entries.
	/// </summary>
	[CLSCompliant(false)]
	public enum AppleEncodingId : ushort
	{
		/// <summary>Unicode version 1.0.</summary>
		Default = 0,

		/// <summary>Unicode 1.1; specifies Hangul characters starting at U+34xx.</summary>
		Unicode11,

		/// <summary>Deprecated (identical to preceding).</summary>
		Iso10646,

		/// <summary>Unicode 2.0 and beyond (UTF-16 BMP only).</summary>
		Unicode20,

		/// <summary>Unicode 3.1 and beyond, using UTF-32.</summary>
		Unicode32,

		/// <summary>From Adobe, not Apple. Not a normal cmap. Specifies variations on a real cmap.</summary>
		VariantSelector,
	}

	/// <summary>
	/// A list of valid values for the ‘encoding_id’ for TT_PLATFORM_MACINTOSH charmaps and name entries.
	/// </summary>
	[CLSCompliant(false)]
	public enum MacEncodingId : ushort
	{
		/// <summary>Roman encoding.</summary>
		Roman = 0,

		/// <summary>Japanese encoding.</summary>
		Japanese = 1,

		/// <summary>Traditional Chinese encoding.</summary>
		TraditionalChinese = 2,

		/// <summary>Korean encoding.</summary>
		Korean = 3,

		/// <summary>Arabic encoding.</summary>
		Arabic = 4,

		/// <summary>Hebrew encoding.</summary>
		Hebrew = 5,

		/// <summary>Greek encoding.</summary>
		Greek = 6,

		/// <summary>Russian encoding.</summary>
		Russian = 7,

		/// <summary>RSymbol encoding.</summary>
		RSymbol = 8,

		/// <summary>Devanagari encoding.</summary>
		Devanagari = 9,

		/// <summary>Gurmukhi encoding.</summary>
		Gurmukhi = 10,

		/// <summary>Gujarati encoding.</summary>
		Gujarati = 11,

		/// <summary>Oriya encoding.</summary>
		Oriya = 12,

		/// <summary>Bengali encoding.</summary>
		Bengali = 13,

		/// <summary>Tamil encoding.</summary>
		Tamil = 14,

		/// <summary>Telugu encoding.</summary>
		Telugu = 15,

		/// <summary>Kannada encoding.</summary>
		Kannada = 16,

		/// <summary>Malayalam encoding.</summary>
		Malayalam = 17,

		/// <summary>Sinhalese encoding.</summary>
		Sinhalese = 18,

		/// <summary>Burmese encoding.</summary>
		Burmese = 19,

		/// <summary>Khmer encoding.</summary>
		Khmer = 20,

		/// <summary>Thai encoding.</summary>
		Thai = 21,

		/// <summary>Laotian encoding.</summary>
		Laotian = 22,

		/// <summary>Georgian encoding.</summary>
		Georgian = 23,

		/// <summary>Amernian encoding.</summary>
		Armenian = 24,

		/// <summary>Maldivian encoding.</summary>
		Maldivian = 25,

		/// <summary>Simplified Chinese encoding.</summary>
		SimplifiedChinese = 25,

		/// <summary>Tibetan encoding.</summary>
		Tibetan = 26,

		/// <summary>Mongolian encoding.</summary>
		Mongolian = 27,

		/// <summary>Geez encoding.</summary>
		Geez = 28,

		/// <summary>Slavic encoding.</summary>
		Slavic = 29,

		/// <summary>Vietnamese encoding.</summary>
		Vietnamese = 30,

		/// <summary>Sindhi encoding.</summary>
		Sindhi = 31,

		/// <summary>No encoding specified.</summary>
		Uninterpreted = 32,
	}

	/// <summary>
	/// A list of valid values for the ‘encoding_id’ for TT_PLATFORM_MICROSOFT charmaps and name entries.
	/// </summary>
	[CLSCompliant(false)]
	public enum MicrosoftEncodingId : ushort
	{
		/// <summary>
		/// Corresponds to Microsoft symbol encoding. See FT_ENCODING_MS_SYMBOL.
		/// </summary>
		Symbol = 0,

		/// <summary>
		/// Corresponds to a Microsoft WGL4 charmap, matching Unicode. See FT_ENCODING_UNICODE.
		/// </summary>
		Unicode = 1,

		/// <summary>
		/// Corresponds to SJIS Japanese encoding. See FT_ENCODING_SJIS.
		/// </summary>
		Sjis = 2,

		/// <summary>
		/// Corresponds to Simplified Chinese as used in Mainland China. See FT_ENCODING_GB2312.
		/// </summary>
		GB2312 = 3,

		/// <summary>
		/// Corresponds to Traditional Chinese as used in Taiwan and Hong Kong. See FT_ENCODING_BIG5.
		/// </summary>
		Big5 = 4,

		/// <summary>
		/// Corresponds to Korean Wansung encoding. See FT_ENCODING_WANSUNG.
		/// </summary>
		Wansung = 5,

		/// <summary>
		/// Corresponds to Johab encoding. See FT_ENCODING_JOHAB.
		/// </summary>
		Johab = 6,

		/// <summary>
		/// Corresponds to UCS-4 or UTF-32 charmaps. This has been added to the OpenType specification version 1.4
		/// (mid-2001.)
		/// </summary>
		Ucs4 = 10,
	}

	/// <summary>
	/// A list of valid values for the ‘encoding_id’ for TT_PLATFORM_ADOBE charmaps. This is a FreeType-specific
	/// extension!
	/// </summary>
	[CLSCompliant(false)]
	public enum AdobeEncodingId : ushort
	{
		/// <summary>Adobe standard encoding.</summary>
		Standard = 0,

		/// <summary>Adobe expert encoding.</summary>
		Expert = 1,

		/// <summary>Adobe custom encoding.</summary>
		Custom = 2,

		/// <summary>Adobe Latin 1 encoding.</summary>
		Latin1 = 3
	}
}
