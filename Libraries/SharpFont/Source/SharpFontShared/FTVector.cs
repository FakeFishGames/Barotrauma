#region MIT License
/*Copyright (c) 2012-2016 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// A simple structure used to store a 2D vector.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct FTVector : IEquatable<FTVector>
	{
		#region Fields

		private IntPtr x;
		private IntPtr y;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="FTVector"/> struct.
		/// </summary>
		/// <param name="x">The horizontal coordinate.</param>
		/// <param name="y">The vertical coordinate.</param>
		public FTVector(Fixed16Dot16 x, Fixed16Dot16 y)
			: this()
		{
			this.x = (IntPtr)x.Value;
			this.y = (IntPtr)y.Value;
		}

		internal FTVector(IntPtr reference)
			: this()
		{
			this.x = Marshal.ReadIntPtr(reference);
			this.y = Marshal.ReadIntPtr(reference, IntPtr.Size);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the horizontal coordinate.
		/// </summary>
		public Fixed16Dot16 X
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)x);
			}

			set
			{
				x = (IntPtr)value.Value;
			}
		}

		/// <summary>
		/// Gets or sets the vertical coordinate.
		/// </summary>
		public Fixed16Dot16 Y
		{
			get
			{
				return Fixed16Dot16.FromRawValue((int)y);
			}

			set
			{
				y = (IntPtr)value.Value;
			}
		}

		#endregion

		#region Operators

		/// <summary>
		/// Compares two instances of <see cref="FTVector"/> for equality.
		/// </summary>
		/// <param name="left">A <see cref="FTVector"/>.</param>
		/// <param name="right">Another <see cref="FTVector"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public static bool operator ==(FTVector left, FTVector right)
		{
			return left.Equals(right);
		}

		/// <summary>
		/// Compares two instances of <see cref="FTVector"/> for inequality.
		/// </summary>
		/// <param name="left">A <see cref="FTVector"/>.</param>
		/// <param name="right">Another <see cref="FTVector"/>.</param>
		/// <returns>A value indicating inequality.</returns>
		public static bool operator !=(FTVector left, FTVector right)
		{
			return !left.Equals(right);
		}

		#endregion

		#region Methods

		/// <summary><para>
		/// Return the unit vector corresponding to a given angle. After the call, the value of ‘vec.x’ will be
		/// ‘sin(angle)’, and the value of ‘vec.y’ will be ‘cos(angle)’.
		/// </para><para>
		/// This function is useful to retrieve both the sinus and cosinus of a given angle quickly.
		/// </para></summary>
		/// <param name="angle">The address of angle.</param>
		/// <returns>The address of target vector.</returns>
		public static FTVector Unit(Fixed16Dot16 angle)
		{
			FTVector vec;
			FT.FT_Vector_Unit(out vec, (IntPtr)angle.Value);

			return vec;
		}

		/// <summary>
		/// Compute vector coordinates from a length and angle.
		/// </summary>
		/// <param name="length">The vector length.</param>
		/// <param name="angle">The vector angle.</param>
		/// <returns>The address of source vector.</returns>
		public static FTVector FromPolar(Fixed16Dot16 length, Fixed16Dot16 angle)
		{
			FTVector vec;
			FT.FT_Vector_From_Polar(out vec, (IntPtr)length.Value, (IntPtr)angle.Value);

			return vec;
		}

		/// <summary>
		/// Transform a single vector through a 2x2 matrix.
		/// </summary>
		/// <remarks>
		/// The result is undefined if either ‘vector’ or ‘matrix’ is invalid.
		/// </remarks>
		/// <param name="matrix">A pointer to the source 2x2 matrix.</param>
		public void Transform(FTMatrix matrix)
		{
			FT.FT_Vector_Transform(ref this, ref matrix);
		}

		/// <summary>
		/// Rotate a vector by a given angle.
		/// </summary>
		/// <param name="angle">The address of angle.</param>
		public void Rotate(Fixed16Dot16 angle)
		{
			FT.FT_Vector_Rotate(ref this, (IntPtr)angle.Value);
		}

		/// <summary>
		/// Return the length of a given vector.
		/// </summary>
		/// <returns>The vector length, expressed in the same units that the original vector coordinates.</returns>
		public Fixed16Dot16 Length()
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_Vector_Length(ref this));
		}

		/// <summary>
		/// Compute both the length and angle of a given vector.
		/// </summary>
		/// <param name="length">The vector length.</param>
		/// <param name="angle">The vector angle.</param>
		public void Polarize(out Fixed16Dot16 length, out Fixed16Dot16 angle)
		{
			IntPtr tmpLength, tmpAngle;
			FT.FT_Vector_Polarize(ref this, out tmpLength, out tmpAngle);

			length = Fixed16Dot16.FromRawValue((int)tmpLength);
			angle = Fixed16Dot16.FromRawValue((int)tmpAngle);
		}

		/// <summary>
		/// Compares this instance of <see cref="FTVector"/> to another for equality.
		/// </summary>
		/// <param name="other">A <see cref="FTVector"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public bool Equals(FTVector other)
		{
			return x == other.x && y == other.y;
		}

		/// <summary>
		/// Compares this instance of <see cref="FTVector"/> to an object for equality.
		/// </summary>
		/// <param name="obj">An object.</param>
		/// <returns>A value indicating equality.</returns>
		public override bool Equals(object obj)
		{
			if (obj is FTVector)
				return this.Equals((FTVector)obj);
			else
				return false;
		}

		/// <summary>
		/// Gets a unique hash code for this instance.
		/// </summary>
		/// <returns>A hash code.</returns>
		public override int GetHashCode()
		{
			 return x.GetHashCode() ^ y.GetHashCode();
		}

		#endregion
	}
}
