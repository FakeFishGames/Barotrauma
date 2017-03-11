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
	/// An enumeration used to specify the index of an SFNT table. Used in the <see cref="Face.GetSfntTable"/> API
	/// function.
	/// </summary>
	public enum SfntTag
	{
		/// <summary>
		/// The 'head' (header) table.
		/// </summary>
		Header = 0,

		/// <summary>
		/// The 'maxp' (maximum profile) table.
		/// </summary>
		MaxProfile = 1,

		/// <summary>
		/// The 'os/2' (OS/2 and Windows) table.
		/// </summary>
		OS2 = 2,

		/// <summary>
		/// The 'hhea' (horizontal metrics header) table.
		/// </summary>
		HorizontalHeader = 3,

		/// <summary>
		/// The 'vhea' (vertical metrics header) table.
		/// </summary>
		VertHeader = 4,

		/// <summary>
		/// The 'post' (PostScript) table.
		/// </summary>
		Postscript = 5,

		/// <summary>
		/// The 'pclt' (PCL5 data) table.
		/// </summary>
		Pclt = 6
	}
}
