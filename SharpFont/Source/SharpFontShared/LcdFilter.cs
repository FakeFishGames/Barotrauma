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
	/// A list of values to identify various types of LCD filters.
	/// </summary>
	public enum LcdFilter
	{
		/// <summary>
		/// Do not perform filtering. When used with subpixel rendering, this results in sometimes severe color
		/// fringes.
		/// </summary>
		None = 0,

		/// <summary>
		/// The default filter reduces color fringes considerably, at the cost of a slight blurriness in the output.
		/// </summary>
		Default = 1,

		/// <summary>
		/// The light filter is a variant that produces less blurriness at the cost of slightly more color fringes than
		/// the default one. It might be better, depending on taste, your monitor, or your personal vision.
		/// </summary>
		Light = 2,

		/// <summary><para>
		/// This filter corresponds to the original libXft color filter. It provides high contrast output but can
		/// exhibit really bad color fringes if glyphs are not extremely well hinted to the pixel grid. In other words,
		/// it only works well if the TrueType bytecode interpreter is enabled and high-quality hinted fonts are used.
		/// </para><para>
		/// This filter is only provided for comparison purposes, and might be disabled or stay unsupported in the
		/// future.
		/// </para></summary>
		Legacy = 16
	}
}
