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

using FT_Long = System.IntPtr;
using FT_ULong = System.UIntPtr;

namespace SharpFont
{
	/// <summary>
	/// A structure used to model a size request.
	/// </summary>
	/// <remarks>
	/// If <see cref="Width"/> is zero, then the horizontal scaling value is set equal to the vertical scaling value,
	/// and vice versa.
	/// </remarks>
	[StructLayout(LayoutKind.Sequential)]
	public struct SizeRequest : IEquatable<SizeRequest>
	{
		#region Fields

		private SizeRequestType requestType;
		private FT_Long width;
		private FT_Long height;
		private uint horiResolution;
		private uint vertResolution;

		#endregion

		#region Properties

		/// <summary>
		/// Gets the type of request. See <see cref="SizeRequestType"/>.
		/// </summary>
		public SizeRequestType RequestType
		{
			get
			{
				return requestType;
			}

			set
			{
				requestType = value;
			}
		}

		/// <summary>
		/// Gets or sets the desired width.
		/// </summary>
		public int Width
		{
			get
			{
				return (int)width;
			}

			set
			{
				width = (FT_Long)value;
			}
		}

		/// <summary>
		/// Gets or sets the desired height.
		/// </summary>
		public int Height
		{
			get
			{
				return (int)height;
			}

			set
			{
				height = (FT_Long)value;
			}
		}

		/// <summary>
		/// Gets or sets the horizontal resolution. If set to zero, <see cref="Width"/> is treated as a 26.6 fractional pixel
		/// value.
		/// </summary>
		[CLSCompliant(false)]
		public uint HorizontalResolution
		{
			get
			{
				return horiResolution;
			}

			set
			{
				horiResolution = value;
			}
		}

		/// <summary>
		/// Gets or sets the horizontal resolution. If set to zero, <see cref="Height"/> is treated as a 26.6 fractional pixel
		/// value.
		/// </summary>
		[CLSCompliant(false)]
		public uint VerticalResolution
		{
			get
			{
				return vertResolution;
			}

			set
			{
				vertResolution = value;
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Compares two <see cref="SizeRequest"/>s for equality.
		/// </summary>
		/// <param name="left">A <see cref="SizeRequest"/>.</param>
		/// <param name="right">Another <see cref="SizeRequest"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public static bool operator ==(SizeRequest left, SizeRequest right)
		{
			return left.Equals(right);
		}

		/// <summary>
		/// Compares two <see cref="SizeRequest"/>s for inequality.
		/// </summary>
		/// <param name="left">A <see cref="SizeRequest"/>.</param>
		/// <param name="right">Another <see cref="SizeRequest"/>.</param>
		/// <returns>A value indicating inequality.</returns>
		public static bool operator !=(SizeRequest left, SizeRequest right)
		{
			return !left.Equals(right);
		}

		/// <summary>
		/// Compares this instance of <see cref="SizeRequest"/> to another for equality.
		/// </summary>
		/// <param name="other">A <see cref="SizeRequest"/>.</param>
		/// <returns>A value indicating equality.</returns>
		public bool Equals(SizeRequest other)
		{
			return requestType == other.requestType &&
				width == other.width &&
				height == other.height &&
				horiResolution == other.horiResolution &&
				vertResolution == other.vertResolution;
		}

		/// <summary>
		/// Compares this instance of <see cref="SizeRequest"/> to another object for equality.
		/// </summary>
		/// <param name="obj">An object.</param>
		/// <returns>A value indicating equality.</returns>
		public override bool Equals(object obj)
		{
			if (obj is SizeRequest)
				return this.Equals((SizeRequest)obj);
			else
				return false;
		}

		/// <summary>
		/// Gets a unique hash code for this instance.
		/// </summary>
		/// <returns>A unique hash code.</returns>
		public override int GetHashCode()
		{
			return requestType.GetHashCode() ^ width.GetHashCode() ^ height.GetHashCode() ^ horiResolution.GetHashCode() ^ vertResolution.GetHashCode();
		}

		#endregion
	}
}
