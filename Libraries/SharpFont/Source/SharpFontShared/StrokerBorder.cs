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
	/// These values are used to select a given stroke border in <see cref="Stroker.GetBorderCounts"/> and
	/// <see cref="Stroker.ExportBorder"/>.
	/// </summary>
	/// <remarks><para>
	/// Applications are generally interested in the ‘inside’ and ‘outside’ borders. However, there is no direct
	/// mapping between these and the ‘left’ and ‘right’ ones, since this really depends on the glyph's drawing
	/// orientation, which varies between font formats.
	/// </para><para>
	/// You can however use <see cref="Outline.GetInsideBorder"/> and <see cref="Outline.GetOutsideBorder"/> to get
	/// these.
	/// </para></remarks>
	public enum StrokerBorder
	{
		/// <summary>
		/// Select the left border, relative to the drawing direction.
		/// </summary>
		Left = 0,

		/// <summary>
		/// Select the right border, relative to the drawing direction.
		/// </summary>
		Right
	}
}
