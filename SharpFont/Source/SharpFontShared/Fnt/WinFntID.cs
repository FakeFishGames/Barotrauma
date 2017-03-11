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

namespace SharpFont.Fnt
{
	/// <summary>
	/// A list of valid values for the ‘charset’ byte in <see cref="Header"/>. Exact mapping tables for the various
	/// cpXXXX encodings (except for cp1361) can be found at <see href="ftp://ftp.unicode.org" /> in the
	/// MAPPINGS/VENDORS/MICSFT/WINDOWS subdirectory. cp1361 is roughly a superset of
	/// MAPPINGS/OBSOLETE/EASTASIA/KSC/JOHAB.TXT.
	/// </summary>
	public enum WinFntId : byte
	{
		/// <summary>
		/// ANSI encoding. A superset of ISO 8859-1.
		/// </summary>
		CP1252 = 0,

		/// <summary>
		/// This is used for font enumeration and font creation as a ‘don't care’ value. Valid font files don't contain
		/// this value. When querying for information about the character set of the font that is currently selected
		/// into a specified device context, this return value (of the related Windows API) simply denotes failure.
		/// </summary>
		Default = 1,

		/// <summary>
		/// There is no known mapping table available.
		/// </summary>
		Symbol = 2,

		/// <summary>
		/// Mac Roman encoding.
		/// </summary>
		Mac = 77,

		/// <summary>
		/// A superset of Japanese Shift-JIS (with minor deviations).
		/// </summary>
		CP932 = 128,

		/// <summary>
		/// A superset of Korean Hangul KS C 5601-1987 (with different ordering and minor deviations).
		/// </summary>
		CP949 = 129,

		/// <summary>
		/// Korean (Johab).
		/// </summary>
		CP1361 = 130,

		/// <summary>
		/// A superset of simplified Chinese GB 2312-1980 (with different ordering and minor deviations).
		/// </summary>
		CP936 = 134,

		/// <summary>
		/// A superset of traditional Chinese Big 5 ETen (with different ordering and minor deviations).
		/// </summary>
		CP950 = 136,

		/// <summary>
		/// A superset of Greek ISO 8859-7 (with minor modifications).
		/// </summary>
		CP1253 = 161,

		/// <summary>
		/// A superset of Turkish ISO 8859-9.
		/// </summary>
		CP1254 = 162,

		/// <summary>
		/// For Vietnamese. This encoding doesn't cover all necessary characters.
		/// </summary>
		CP1258 = 163,

		/// <summary>
		/// A superset of Hebrew ISO 8859-8 (with some modifications).
		/// </summary>
		CP1255 = 177,

		/// <summary>
		/// A superset of Arabic ISO 8859-6 (with different ordering).
		/// </summary>
		CP1256 = 178,

		/// <summary>
		/// A superset of Baltic ISO 8859-13 (with some deviations).
		/// </summary>
		CP1257 = 186,

		/// <summary>
		/// A superset of Russian ISO 8859-5 (with different ordering).
		/// </summary>
		CP1251 = 204,

		/// <summary>
		/// A superset of Thai TIS 620 and ISO 8859-11.
		/// </summary>
		CP874 = 222,

		/// <summary>
		/// A superset of East European ISO 8859-2 (with slightly different ordering).
		/// </summary>
		CP1250 = 238,

		/// <summary><para>
		/// From Michael Pöttgen &lt;michael@poettgen.de&gt;:
		/// The ‘Windows Font Mapping’ article says that <see cref="WinFntId.Oem"/> is used for the charset of vector
		/// fonts, like ‘modern.fon’, ‘roman.fon’, and ‘script.fon’ on Windows.
		/// </para><para>
		/// The ‘CreateFont’ documentation says: The <see cref="WinFntId.Oem"/> value specifies a character set that is
		/// operating-system dependent.
		/// </para><para>
		/// The ‘IFIMETRICS’ documentation from the ‘Windows Driver Development Kit’ says: This font supports an
		/// OEM-specific character set. The OEM character set is system dependent.
		/// </para><para>
		/// In general OEM, as opposed to ANSI (i.e., cp1252), denotes the second default codepage that most
		/// international versions of Windows have. It is one of the OEM codepages from 
		/// <see href="http://www.microsoft.com/globaldev/reference/cphome.mspx"/>, and is used for the ‘DOS boxes’, to
		/// support legacy applications. A German Windows version for example usually uses ANSI codepage 1252 and OEM
		/// codepage 850.
		/// </para></summary>
		Oem = 255
	}
}
