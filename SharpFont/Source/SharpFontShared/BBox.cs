#region MIT License
/*Copyright (c) 2012-2015 Robert Rouhani <robert.rouhani@gmail.com>

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
using System.Runtime.InteropServices;

using FT_Long = System.IntPtr;
using FT_ULong = System.UIntPtr;

namespace SharpFont
{
	/// <summary>
	/// A structure used to hold an outline's bounding box, i.e., the
	/// coordinates of its extrema in the horizontal and vertical directions.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct BBox : IEquatable<BBox>
	{
		#region Fields

		private FT_Long xMin, yMin;
		private FT_Long xMax, yMax;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="BBox"/> struct.
		/// </summary>
		/// <param name="left">The left bound.</param>
		/// <param name="bottom">The bottom bound.</param>
		/// <param name="right">The right bound.</param>
		/// <param name="top">The upper bound.</param>
		public BBox(int left, int bottom, int right, int top)
		{
			xMin = (IntPtr)left;
			yMin = (IntPtr)bottom;
			xMax = (IntPtr)right;
			yMax = (IntPtr)top;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the horizontal minimum (left-most).
		/// </summary>
		public int Left
		{
			get
			{
				return (int)xMin;
			}
		}

		/// <summary>
		/// Gets the vertical minimum (bottom-most).
		/// </summary>
		public int Bottom
		{
			get
			{
				return (int)yMin;
			}
		}

		/// <summary>
		/// Gets the horizontal maximum (right-most).
		/// </summary>
		public int Right
		{
			get
			{
				return (int)xMax;
			}
		}

		/// <summary>
		/// Gets the vertical maximum (top-most).
		/// </summary>
		public int Top
		{
			get
			{
				return (int)yMax;
			}
		}

		#endregion

		#region Operators

		/// <summary>
		/// Compares two instances of <see cref="BBox"/> for equality.
		/// </summary>
		/// <param name="left">A <see cref="BBox"/>.</param>
		/// <param name="right">Another <see cref="BBox"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public static bool operator ==(BBox left, BBox right)
		{
			return left.Equals(right);
		}

		/// <summary>
		/// Compares two instances of <see cref="BBox"/> for inequality.
		/// </summary>
		/// <param name="left">A <see cref="BBox"/>.</param>
		/// <param name="right">Another <see cref="BBox"/>.</param>
		/// <returns>A value indicating inequality.</returns>
		public static bool operator !=(BBox left, BBox right)
		{
			return !left.Equals(right);
		}

		#endregion

		#region Methods

		/// <summary>
		/// Compares this instance of <see cref="BBox"/> to another for equality.
		/// </summary>
		/// <param name="other">A <see cref="BBox"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public bool Equals(BBox other)
		{
			return
				xMin == other.xMin &&
				yMin == other.yMin &&
				xMax == other.xMax &&
				yMax == other.yMax;
		}

		/// <summary>
		/// Compares this instance of <see cref="BBox"/> to an object for equality.
		/// </summary>
		/// <param name="obj">An object.</param>
		/// <returns>A value indicating equality.</returns>
		public override bool Equals(object obj)
		{
			if (obj is BBox)
				return this.Equals((BBox)obj);

			return false;
		}

		/// <summary>
		/// Gets a unique hash code for this instance.
		/// </summary>
		/// <returns>A hash code.</returns>
		public override int GetHashCode()
		{
			//TODO better hash algo
			return xMin.GetHashCode() ^ yMin.GetHashCode() ^ xMax.GetHashCode() ^ yMax.GetHashCode();
		}

		/// <summary>
		/// Gets a string that represents this instance.
		/// </summary>
		/// <returns>A string representation of this instance.</returns>
		public override string ToString()
		{
			return "Min: (" + (int)xMin + ", " + (int)yMin + "), Max: (" + (int)xMax + ", " + (int)yMax + ")";
		}

		#endregion
	}
}
