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
	/// A list of bit-flags used to indicate the style of a given face. These are used in the ‘style_flags’ field of
	/// <see cref="Face"/>.
	/// </summary>
	/// <remarks>
	/// The style information as provided by FreeType is very basic. More details are beyond the scope and should be
	/// done on a higher level (for example, by analyzing various fields of the ‘OS/2’ table in SFNT based fonts).
	/// </remarks>
	[Flags]
	public enum StyleFlags
	{
		/// <summary>No style flags.</summary>
		None = 0x00,

		/// <summary>Indicates that a given face style is italic or oblique.</summary>
		Italic = 0x01,

		/// <summary>Indicates that a given face is bold.</summary>
		Bold = 0x02
	}
}
