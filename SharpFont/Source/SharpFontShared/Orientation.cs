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
	/// <summary><para>
	/// A list of values used to describe an outline's contour orientation.
	/// </para><para>
	/// The TrueType and PostScript specifications use different conventions to determine whether outline contours
	/// should be filled or unfilled.
	/// </para></summary>
	public enum Orientation
	{
		/// <summary>
		/// According to the TrueType specification, clockwise contours must be filled, and counter-clockwise ones must
		/// be unfilled.
		/// </summary>
		TrueType = 0,

		/// <summary>
		/// According to the PostScript specification, counter-clockwise contours must be filled, and clockwise ones
		/// must be unfilled.
		/// </summary>
		PostScript = 1,

		/// <summary>
		/// This is identical to <see cref="TrueType"/>, but is used to remember that in TrueType, everything that is
		/// to the right of the drawing direction of a contour must be filled.
		/// </summary>
		FillRight = TrueType,
		
		/// <summary>
		/// This is identical to <see cref="PostScript"/>, but is used to remember that in PostScript, everything that
		/// is to the left of the drawing direction of a contour must be filled.
		/// </summary>
		FillLeft = PostScript,

		/// <summary>
		/// The orientation cannot be determined. That is, different parts of the glyph have different orientation.
		/// </summary>
		None
	}
}
