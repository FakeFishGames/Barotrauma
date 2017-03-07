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
	/// Constants used as the tag of <see cref="Parameter"/> structures.
	/// </summary>
	[CLSCompliant(false)]
	public enum ParamTag : uint
	{
		/// <summary>
		/// A constant used as the tag of <see cref="Parameter"/> structures to make <see cref="Library.OpenFace"/>
		/// ignore preferred family subfamily names in ‘name’ table since OpenType version 1.4. For backwards
		/// compatibility with legacy systems which has 4-face-per-family restriction.
		/// </summary>
		IgnorePreferredFamily = ('i' << 24 | 'g' << 16 | 'p' << 8 | 'f'),

		/// <summary>
		/// A constant used as the tag of <see cref="Parameter"/> structures to make <see cref="Library.OpenFace"/>
		/// ignore preferred subfamily names in ‘name’ table since OpenType version 1.4. For backwards compatibility
		/// with legacy systems which has 4-face-per-family restriction.
		/// </summary>
		IgnorePreferredSubfamily = ('i' << 24 | 'g' << 16 | 'p' << 8 | 's'),

		/// <summary>
		/// A constant used as the tag of <see cref="Parameter"/> structures to indicate an incremental loading object
		/// to be used by FreeType.
		/// </summary>
		Incremental = ('i' << 24 | 'n' << 16 | 'c' << 8 | 'r'),

		/// <summary>
		/// A constant used as the tag of an <see cref="Parameter"/> structure to indicate that unpatented methods only
		/// should be used by the TrueType bytecode interpreter for a typeface opened by
		/// <see cref="Library.OpenFace"/>.
		/// </summary>
		UnpatentedHinting = ('u' << 24 | 'n' << 16 | 'p' << 8 | 'a')
	}
}
