#region MIT License
/*Copyright (c) 2015 Robert Rouhani <robert.rouhani@gmail.com>

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

namespace SharpFont
{
	/// <summary>
	/// A simple structure used to store a 2D vector.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct FTVector26Dot6 : IEquatable<FTVector26Dot6>
	{
		#region Fields

		private IntPtr x;
		private IntPtr y;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="FTVector26Dot6"/> struct.
		/// </summary>
		/// <param name="x">The horizontal coordinate.</param>
		/// <param name="y">The vertical coordinate.</param>
		public FTVector26Dot6(Fixed26Dot6 x, Fixed26Dot6 y)
			: this()
		{
			this.x = (IntPtr)x.Value;
			this.y = (IntPtr)y.Value;
		}

		internal FTVector26Dot6(IntPtr reference)
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
		public Fixed26Dot6 X
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)x);
			}

			set
			{
				x = (IntPtr)value.Value;
			}
		}

		/// <summary>
		/// Gets or sets the vertical coordinate.
		/// </summary>
		public Fixed26Dot6 Y
		{
			get
			{
				return Fixed26Dot6.FromRawValue((int)y);
			}

			set
			{
				y = (IntPtr)value.Value;
			}
		}

		#endregion

		#region Operators

		/// <summary>
		/// Compares two instances of <see cref="FTVector26Dot6"/> for equality.
		/// </summary>
		/// <param name="left">A <see cref="FTVector26Dot6"/>.</param>
		/// <param name="right">Another <see cref="FTVector26Dot6"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public static bool operator ==(FTVector26Dot6 left, FTVector26Dot6 right)
		{
			return left.Equals(right);
		}

		/// <summary>
		/// Compares two instances of <see cref="FTVector26Dot6"/> for inequality.
		/// </summary>
		/// <param name="left">A <see cref="FTVector26Dot6"/>.</param>
		/// <param name="right">Another <see cref="FTVector26Dot6"/>.</param>
		/// <returns>A value indicating inequality.</returns>
		public static bool operator !=(FTVector26Dot6 left, FTVector26Dot6 right)
		{
			return !left.Equals(right);
		}

		#endregion

		#region Methods

		/// <summary>
		/// Compares this instance of <see cref="FTVector26Dot6"/> to another for equality.
		/// </summary>
		/// <param name="other">A <see cref="FTVector26Dot6"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public bool Equals(FTVector26Dot6 other)
		{
			return x == other.x && y == other.y;
		}

		/// <summary>
		/// Compares this instance of <see cref="FTVector26Dot6"/> to an object for equality.
		/// </summary>
		/// <param name="obj">An object.</param>
		/// <returns>A value indicating equality.</returns>
		public override bool Equals(object obj)
		{
			if (obj is FTVector26Dot6)
				return this.Equals((FTVector26Dot6)obj);
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
