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
	/// A list of constants used to describe subglyphs. Please refer to the TrueType specification for the meaning of
	/// the various flags.
	/// </summary>
	[Flags]
	public enum SubGlyphFlags
	{
		/// <summary>
		/// Set this to indicate arguments are word size; otherwise, they are byte size.
		/// </summary>
		ArgsAreWords = 0x0001,

		/// <summary>
		/// Set this to indicate arguments are X and Y values; otherwise, X and Y indicate point coordinates.
		/// </summary>
		ArgsAreXYValues = 0x0002,

		/// <summary>
		/// Set this to round XY values to the grid.
		/// </summary>
		RoundXYToGrid = 0x0004,

		/// <summary>
		/// Set this to indicate the component has a simple scale; otherwise, the scale is 1.0.
		/// </summary>
		Scale = 0x0008,

		/// <summary>
		/// Set this to indicate that X and Y are scaled independently.
		/// </summary>
		XYScale = 0x0040,

		/// <summary>
		/// Set this to indicate there is a 2 by 2 transformation used to scale the component.
		/// </summary>
		TwoByTwo = 0x0080,

		/// <summary>
		/// Set this to forse aw, lsb and rsb for the composite to be equal to those from the original glyph.
		/// </summary>
		UseMyMetrics = 0x0200
	}
}
