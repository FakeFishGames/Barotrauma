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
	/// These values determine how two joining lines are rendered in a stroker.
	/// </summary>
	public enum StrokerLineJoin
	{
		/// <summary>
		/// Used to render rounded line joins. Circular arcs are used to join two lines smoothly.
		/// </summary>
		Round = 0,

		/// <summary>
		/// Used to render beveled line joins. The outer corner of the joined lines is filled by enclosing the
		/// triangular region of the corner with a straight line between the outer corners of each stroke.
		/// </summary>
		Bevel = 1,

		/// <summary>
		/// Used to render mitered line joins, with fixed bevels if the miter limit is exceeded. The outer edges of the
		/// strokes for the two segments are extended until they meet at an angle. If the segments meet at too sharp an
		/// angle (such that the miter would extend from the intersection of the segments a distance greater than the
		/// product of the miter limit value and the border radius), then a bevel join (see above) is used instead.
		/// This prevents long spikes being created. <see cref="MiterFixed"/> generates a miter line join as used in
		/// PostScript and PDF.
		/// </summary>
		MiterVariable = 2,

		/// <summary>
		/// An alias for <see cref="MiterVariable"/>, retained for backwards compatibility.
		/// </summary>
		Miter = MiterVariable,

		/// <summary>
		/// Used to render mitered line joins, with variable bevels if the miter limit is exceeded. The intersection of
		/// the strokes is clipped at a line perpendicular to the bisector of the angle between the strokes, at the
		/// distance from the intersection of the segments equal to the product of the miter limit value and the border
		/// radius. This prevents long spikes being created. <see cref="MiterVariable"/> generates a mitered line join
		/// as used in XPS.
		/// </summary>
		MiterFixed = 3
	}
}
