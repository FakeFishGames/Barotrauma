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
	/// An enumeration type that lists the supported size request types.
	/// </summary>
	/// <remarks><para>
	/// The above descriptions only apply to scalable formats. For bitmap formats, the behaviour is up to the driver.
	/// </para><para>
	/// See the note section of <see cref="SizeMetrics"/> if you wonder how size
	/// requesting relates to scaling values.
	/// </para></remarks>
	public enum SizeRequestType
	{
		/// <summary>
		/// The nominal size. The ‘units_per_EM’ field of <see cref="Face"/> is used to determine both scaling values.
		/// </summary>
		Normal,

		/// <summary>
		/// The real dimension. The sum of the the ‘ascender’ and (minus of) the ‘descender’ fields of
		/// <see cref="Face"/> are used to determine both scaling values.
		/// </summary>
		RealDimensions,

		/// <summary>
		/// The font bounding box. The width and height of the ‘bbox’ field of <see cref="Face"/> are used to determine
		/// the horizontal and vertical scaling value, respectively.
		/// </summary>
		BoundingBox,

		/// <summary>
		/// The ‘max_advance_width’ field of <see cref="Face"/> is used to determine the horizontal scaling value; the
		/// vertical scaling value is determined the same way as <see cref="SizeRequestType.RealDimensions"/> does.
		/// Finally, both scaling values are set to the smaller one. This type is useful if you want to specify the
		/// font size for, say, a window of a given dimension and 80x24 cells.
		/// </summary>
		Cell,

		/// <summary>
		/// Specify the scaling values directly.
		/// </summary>
		Scales
	}
}
