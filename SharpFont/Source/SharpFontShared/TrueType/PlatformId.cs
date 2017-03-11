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
	/// A list of valid values for the ‘platform_id’ identifier code in <see cref="CharMap"/> and
	/// <see cref="SfntName"/> structures.
	/// </summary>
	[CLSCompliant(false)]
	public enum PlatformId : ushort
	{
		/// <summary>
		/// Used by Apple to indicate a Unicode character map and/or name entry. See TT_APPLE_ID_XXX for corresponding
		/// ‘encoding_id’ values. Note that name entries in this format are coded as big-endian UCS-2 character codes
		/// only.
		/// </summary>
		AppleUnicode = 0,

		/// <summary>
		/// Used by Apple to indicate a MacOS-specific charmap and/or name entry. See TT_MAC_ID_XXX for corresponding
		/// ‘encoding_id’ values. Note that most TrueType fonts contain an Apple roman charmap to be usable on MacOS
		/// systems (even if they contain a Microsoft charmap as well).
		/// </summary>
		Macintosh = 1,

		/// <summary>
		/// This value was used to specify ISO/IEC 10646 charmaps. It is however now deprecated. See TT_ISO_ID_XXX for
		/// a list of corresponding ‘encoding_id’ values.
		/// </summary>
		Iso = 2,

		/// <summary>
		/// Used by Microsoft to indicate Windows-specific charmaps. See TT_MS_ID_XXX for a list of corresponding
		/// ‘encoding_id’ values. Note that most fonts contain a Unicode charmap using (TT_PLATFORM_MICROSOFT,
		/// TT_MS_ID_UNICODE_CS).
		/// </summary>
		Microsoft = 3,

		/// <summary>
		/// Used to indicate application-specific charmaps.
		/// </summary>
		Custom = 4,

		/// <summary>
		/// This value isn't part of any font format specification, but is used by FreeType to report Adobe-specific
		/// charmaps in an <see cref="CharMap"/> structure. See TT_ADOBE_ID_XXX.
		/// </summary>
		Adobe = 7
	}
}
