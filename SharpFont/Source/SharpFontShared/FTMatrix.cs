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

using SharpFont.Internal;

namespace SharpFont
{
	/// <summary>
	/// A simple structure used to store a 2x2 matrix. Coefficients are in 16.16 fixed float format. The computation
	/// performed is:
	/// <code>
	/// x' = x*xx + y*xy
	/// y' = x*yx + y*yy
	/// </code>
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct FTMatrix : IEquatable<FTMatrix>
	{
		#region Fields

		private IntPtr xx, xy;
		private IntPtr yx, yy;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="FTMatrix"/> struct.
		/// </summary>
		/// <param name="xx">Matrix coefficient XX.</param>
		/// <param name="xy">Matrix coefficient XY.</param>
		/// <param name="yx">Matrix coefficient YX.</param>
		/// <param name="yy">Matrix coefficient YY.</param>
		public FTMatrix(int xx, int xy, int yx, int yy)
			: this()
		{
			this.xx = (IntPtr)xx;
			this.xy = (IntPtr)xy;
			this.yx = (IntPtr)yx;
			this.yy = (IntPtr)yy;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="FTMatrix"/> struct.
		/// </summary>
		/// <param name="row0">Matrix coefficients XX, XY.</param>
		/// <param name="row1">Matrix coefficients YX, YY.</param>
		public FTMatrix(FTVector row0, FTVector row1)
			: this(row0.X.Value, row0.Y.Value, row1.X.Value, row1.Y.Value)
		{
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the matrix coefficient.
		/// </summary>
		public Fixed16Dot16 XX
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)xx);
			}

			set
			{
				xx = (IntPtr)value.Value;
			}
		}

		/// <summary>
		/// Gets or sets the matrix coefficient.
		/// </summary>
		public Fixed16Dot16 XY
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)xy);
			}

			set
			{
				xy = (IntPtr)value.Value;
			}
		}

		/// <summary>
		/// Gets or sets the matrix coefficient.
		/// </summary>
		public Fixed16Dot16 YX
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)yx);
			}

			set
			{
				yx = (IntPtr)value.Value;
			}
		}

		/// <summary>
		/// Gets or sets the matrix coefficient.
		/// </summary>
		public Fixed16Dot16 YY
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)yy);
			}

			set
			{
				yy = (IntPtr)value.Value;
			}
		}

		#endregion

		#region Operators

		/// <summary>
		/// Compares two instances of <see cref="FTMatrix"/> for equality.
		/// </summary>
		/// <param name="left">A <see cref="FTMatrix"/>.</param>
		/// <param name="right">Another <see cref="FTMatrix"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public static bool operator ==(FTMatrix left, FTMatrix right)
		{
			return left.Equals(right);
		}

		/// <summary>
		/// Compares two instances of <see cref="FTMatrix"/> for inequality.
		/// </summary>
		/// <param name="left">A <see cref="FTMatrix"/>.</param>
		/// <param name="right">Another <see cref="FTMatrix"/>.</param>
		/// <returns>A value indicating inequality.</returns>
		public static bool operator !=(FTMatrix left, FTMatrix right)
		{
			return !left.Equals(right);
		}

		#endregion

		#region Methods

		/// <summary>
		/// Perform the matrix operation ‘b = a*b’.
		/// </summary>
		/// <remarks>
		/// The result is undefined if either ‘a’ or ‘b’ is zero.
		/// </remarks>
		/// <param name="a">A pointer to matrix ‘a’.</param>
		/// <param name="b">A pointer to matrix ‘b’.</param>
		public static void Multiply(FTMatrix a, FTMatrix b)
		{
			FT.FT_Matrix_Multiply(ref a, ref b);
		}

		/// <summary>
		/// Perform the matrix operation ‘b = a*b’.
		/// </summary>
		/// <remarks>
		/// The result is undefined if either ‘a’ or ‘b’ is zero.
		/// </remarks>
		/// <param name="b">A pointer to matrix ‘b’.</param>
		public void Multiply(FTMatrix b)
		{
			FT.FT_Matrix_Multiply(ref this, ref b);
		}

		/// <summary>
		/// Invert a 2x2 matrix. Return an error if it can't be inverted.
		/// </summary>
		public void Invert()
		{
			Error err = FT.FT_Matrix_Invert(ref this);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Compares this instance of <see cref="FTMatrix"/> to another for equality.
		/// </summary>
		/// <param name="other">A <see cref="FTMatrix"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public bool Equals(FTMatrix other)
		{
			return
				xx == other.xx &&
				xy == other.xy &&
				yx == other.yx &&
				yy == other.yy;
		}

		/// <summary>
		/// Compares this instance of <see cref="FTMatrix"/> to an object for equality.
		/// </summary>
		/// <param name="obj">An object.</param>
		/// <returns>A value indicating equality.</returns>
		public override bool Equals(object obj)
		{
			if (obj is FTMatrix)
				return this.Equals((FTMatrix)obj);
			else
				return false;
		}

		/// <summary>
		/// Gets a unique hash code for this instance.
		/// </summary>
		/// <returns>A hash code.</returns>
		public override int GetHashCode()
		{
			return xx.GetHashCode() ^ xy.GetHashCode() ^ yx.GetHashCode() ^ yy.GetHashCode();
		}

		#endregion
	}
}
