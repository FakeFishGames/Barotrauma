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
	/// <summary>
	/// A list of bit-field constants used with <see cref="Face.OpenTypeValidate"/> to indicate which OpenType tables
	/// should be validated.
	/// </summary>
	[Flags]
	[CLSCompliant(false)]
	public enum OpenTypeValidationFlags : uint
	{
		/// <summary>Validate BASE table.</summary>
		Base = 0x0100,

		/// <summary>Validate GDEF table.</summary>
		Gdef = 0x0200,

		/// <summary>Validate GPOS table.</summary>
		Gpos = 0x0400,

		/// <summary>Validate GSUB table.</summary>
		Gsub = 0x0800,

		/// <summary>Validate JSTF table.</summary>
		Jstf = 0x1000,

		/// <summary>Validate MATH table.</summary>
		Math = 0x2000,

		/// <summary>Validate all OpenType tables.</summary>
		All = Base | Gdef | Gpos | Gsub | Jstf | Math
	}
}
