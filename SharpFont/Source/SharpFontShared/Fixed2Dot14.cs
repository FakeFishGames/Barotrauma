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
	/// Represents a fixed-point decimal value with 14 bits of decimal precision.
	/// </summary>
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct Fixed2Dot14 : IEquatable<Fixed2Dot14>, IComparable<Fixed2Dot14>
	{
		#region Fields

		/// <summary>
		/// The raw 2.14 short.
		/// </summary>
		private short value;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="Fixed2Dot14"/> struct.
		/// </summary>
		/// <param name="value">An integer value.</param>
		public Fixed2Dot14(short value)
		{
			this.value = (short)(value << 14);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Fixed2Dot14"/> struct.
		/// </summary>
		/// <param name="value">A floating point value.</param>
		public Fixed2Dot14(float value)
		{
			this.value = (short)(value * 16384);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Fixed2Dot14"/> struct.
		/// </summary>
		/// <param name="value">A floating point value.</param>
		public Fixed2Dot14(double value)
		{
			this.value = (short)(value * 16384);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Fixed2Dot14"/> struct.
		/// </summary>
		/// <param name="value">A floating point value.</param>
		public Fixed2Dot14(decimal value)
		{
			this.value = (short)(value * 16384);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the raw 2.14 integer.
		/// </summary>
		public int Value
		{
			get
			{
				return value;
			}
		}

		#endregion

		#region Methods

		#region Static

		/// <summary>
		/// Creates a <see cref="Fixed2Dot14"/> from an int containing a 2.14 value.
		/// </summary>
		/// <param name="value">A 2.14 value.</param>
		/// <returns>An instance of <see cref="Fixed2Dot14"/>.</returns>
		public static Fixed2Dot14 FromRawValue(short value)
		{
			Fixed2Dot14 f = new Fixed2Dot14();
			f.value = value;
			return f;
		}

		/// <summary>
		/// Creates a new <see cref="Fixed2Dot14"/> from a <see cref="System.Int16"/>
		/// </summary>
		/// <param name="value">A <see cref="System.Int16"/> value.</param>
		/// <returns>The equivalent <see cref="Fixed2Dot14"/> value.</returns>
		public static Fixed2Dot14 FromInt16(short value)
		{
			return new Fixed2Dot14(value);
		}

		/// <summary>
		/// Creates a new <see cref="Fixed2Dot14"/> from <see cref="System.Single"/>.
		/// </summary>
		/// <param name="value">A floating-point value.</param>
		/// <returns>A fixed 2.14 value.</returns>
		public static Fixed2Dot14 FromSingle(float value)
		{
			return new Fixed2Dot14(value);
		}

		/// <summary>
		/// Creates a new <see cref="Fixed2Dot14"/> from <see cref="System.Double"/>.
		/// </summary>
		/// <param name="value">A floating-point value.</param>
		/// <returns>A fixed 2.14 value.</returns>
		public static Fixed2Dot14 FromDouble(double value)
		{
			return new Fixed2Dot14(value);
		}

		/// <summary>
		/// Creates a new <see cref="Fixed2Dot14"/> from <see cref="System.Decimal"/>.
		/// </summary>
		/// <param name="value">A floating-point value.</param>
		/// <returns>A fixed 2.14 value.</returns>
		public static Fixed2Dot14 FromDecimal(decimal value)
		{
			return new Fixed2Dot14(value);
		}

		/// <summary>
		/// Adds two 2.14 values together.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the addition.</returns>
		public static Fixed2Dot14 Add(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return Fixed2Dot14.FromRawValue((short)(left.value + right.value));
		}

		/// <summary>
		/// Subtacts one 2.14 values from another.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the subtraction.</returns>
		public static Fixed2Dot14 Subtract(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return Fixed2Dot14.FromRawValue((short)(left.value - right.value));
		}

		/// <summary>
		/// Multiplies two 2.14 values together.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the multiplication.</returns>
		public static Fixed2Dot14 Multiply(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			int mul = (int)left.value * (int)right.value;
			Fixed2Dot14 ans;
			ans.value = (short)(mul >> 14);
			return ans;
		}

		/// <summary>
		/// Divides one 2.14 values from another.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the division.</returns>
		public static Fixed2Dot14 Divide(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			int div = ((int)left.Value << 6) / right.value;
			Fixed2Dot14 ans;
			ans.value = (short)div;
			return ans;
		}

		#endregion

		#region Operators

		/// <summary>
		/// Casts a <see cref="System.Single"/> to a <see cref="Fixed2Dot14"/>.
		/// </summary>
		/// <param name="value">A <see cref="System.Single"/> value.</param>
		/// <returns>The equivalent <see cref="Fixed2Dot14"/> value.</returns>
		public static explicit operator Fixed2Dot14(float value)
		{
			return new Fixed2Dot14(value);
		}

		/// <summary>
		/// Casts a <see cref="System.Double"/> to a <see cref="Fixed2Dot14"/>.
		/// </summary>
		/// <param name="value">A <see cref="System.Double"/> value.</param>
		/// <returns>The equivalent <see cref="Fixed2Dot14"/> value.</returns>
		public static explicit operator Fixed2Dot14(double value)
		{
			return new Fixed2Dot14(value);
		}

		/// <summary>
		/// Casts a <see cref="System.Single"/> to a <see cref="Fixed2Dot14"/>.
		/// </summary>
		/// <param name="value">A <see cref="System.Decimal"/> value.</param>
		/// <returns>The equivalent <see cref="Fixed2Dot14"/> value.</returns>
		public static explicit operator Fixed2Dot14(decimal value)
		{
			return new Fixed2Dot14(value);
		}

		/// <summary>
		/// Casts a <see cref="Fixed2Dot14"/> to a <see cref="System.Single"/>.
		/// </summary>
		/// <remarks>
		/// This operation can result in a loss of data.
		/// </remarks>
		/// <param name="value">A <see cref="Fixed2Dot14"/> value.</param>
		/// <returns>The equivalent <see cref="System.Single"/> value.</returns>
		public static explicit operator float(Fixed2Dot14 value)
		{
			return value.ToSingle();
		}

		/// <summary>
		/// Casts a <see cref="Fixed2Dot14"/> to a <see cref="System.Double"/>.
		/// </summary>
		/// <param name="value">A <see cref="Fixed2Dot14"/> value.</param>
		/// <returns>The equivalent <see cref="System.Double"/> value.</returns>
		public static implicit operator double(Fixed2Dot14 value)
		{
			return value.ToDouble();
		}

		/// <summary>
		/// Casts a <see cref="Fixed2Dot14"/> to a <see cref="System.Decimal"/>.
		/// </summary>
		/// <param name="value">A <see cref="Fixed2Dot14"/> value.</param>
		/// <returns>The equivalent <see cref="System.Single"/> value.</returns>
		public static implicit operator decimal(Fixed2Dot14 value)
		{
			return value.ToDecimal();
		}

		/// <summary>
		/// Adds two 2.14 values together.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the addition.</returns>
		public static Fixed2Dot14 operator +(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return Add(left, right);
		}

		/// <summary>
		/// Subtacts one 2.14 values from another.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the subtraction.</returns>
		public static Fixed2Dot14 operator -(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return Subtract(left, right);
		}

		/// <summary>
		/// Multiplies two 2.14 values together.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the multiplication.</returns>
		public static Fixed2Dot14 operator *(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return Multiply(left, right);
		}

		/// <summary>
		/// Divides one 2.14 values from another.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>The result of the division.</returns>
		public static Fixed2Dot14 operator /(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return Divide(left, right);
		}

		/// <summary>
		/// Compares two instances of <see cref="Fixed2Dot14"/> for equality.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>A value indicating whether the two instances are equal.</returns>
		public static bool operator ==(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return left.Equals(right);
		}

		/// <summary>
		/// Compares two instances of <see cref="Fixed2Dot14"/> for inequality.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>A value indicating whether the two instances are not equal.</returns>
		public static bool operator !=(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return !(left == right);
		}

		/// <summary>
		/// Checks if the left operand is less than the right operand.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>A value indicating whether left is less than right.</returns>
		public static bool operator <(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return left.CompareTo(right) < 0;
		}

		/// <summary>
		/// Checks if the left operand is less than or equal to the right operand.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>A value indicating whether left is less than or equal to right.</returns>
		public static bool operator <=(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return left.CompareTo(right) <= 0;
		}

		/// <summary>
		/// Checks if the left operand is greater than the right operand.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>A value indicating whether left is greater than right.</returns>
		public static bool operator >(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return left.CompareTo(right) > 0;
		}

		/// <summary>
		/// Checks if the left operand is greater than or equal to the right operand.
		/// </summary>
		/// <param name="left">The left operand.</param>
		/// <param name="right">The right operand.</param>
		/// <returns>A value indicating whether left is greater than or equal to right.</returns>
		public static bool operator >=(Fixed2Dot14 left, Fixed2Dot14 right)
		{
			return left.CompareTo(right) >= 0;
		}

		#endregion

		#region Instance

		/// <summary>
		/// Removes the decimal part of the value.
		/// </summary>
		/// <returns>The truncated number.</returns>
		public short Floor()
		{
			return (short)(value >> 14);
		}

		/// <summary>
		/// Rounds to the nearest whole number.
		/// </summary>
		/// <returns>The nearest whole number.</returns>
		public short Round()
		{
			//add 2^13, rounds the integer part up if the decimal value is >= 0.5
			return (short)((value + 8192) >> 14);
		}

		/// <summary>
		/// Rounds up to the next whole number.
		/// </summary>
		/// <returns>The next whole number.</returns>
		public short Ceiling()
		{
			//add 2^14 - 1, rounds the integer part up if there's any decimal value
			return (short)((value + 16383) >> 14);
		}

		/// <summary>
		/// Converts the value to a <see cref="System.Int16"/>. The value is floored.
		/// </summary>
		/// <returns>An integer value.</returns>
		public short ToInt16()
		{
			return Floor();
		}

		/// <summary>
		/// Converts the value to a <see cref="System.Single"/>.
		/// </summary>
		/// <returns>A floating-point value.</returns>
		public float ToSingle()
		{
			return value / 16384f;
		}

		/// <summary>
		/// Converts the value to a <see cref="System.Double"/>.
		/// </summary>
		/// <returns>A floating-point value.</returns>
		public double ToDouble()
		{
			return value / 16384d;
		}

		/// <summary>
		/// Converts the value to a <see cref="System.Decimal"/>.
		/// </summary>
		/// <returns>A decimal value.</returns>
		public decimal ToDecimal()
		{
			return value / 16384m;
		}

		/// <summary>
		/// Compares this instance to another <see cref="Fixed2Dot14"/> for equality.
		/// </summary>
		/// <param name="other">A <see cref="Fixed2Dot14"/>.</param>
		/// <returns>A value indicating whether the two instances are equal.</returns>
		public bool Equals(Fixed2Dot14 other)
		{
			return value == other.value;
		}

		/// <summary>
		/// Compares this instnace with another <see cref="Fixed2Dot14"/> and returns an integer that indicates
		/// whether the current instance precedes, follows, or occurs in the same position in the sort order as the
		/// other <see cref="Fixed2Dot14"/>.
		/// </summary>
		/// <param name="other">A <see cref="Fixed2Dot14"/>.</param>
		/// <returns>A value indicating the relative order of the instances.</returns>
		public int CompareTo(Fixed2Dot14 other)
		{
			return value.CompareTo(other.value);
		}

		#endregion

		#region Overrides

		/// <summary>
		/// Returns a string that represents the current object.
		/// </summary>
		/// <param name="provider">An object that supplies culture-specific formatting information.</param>
		/// <returns>A string that represents the current object.</returns>
		public string ToString(IFormatProvider provider)
		{
			return ToDecimal().ToString(provider);
		}

		/// <summary>
		/// Returns a string that represents the current object.
		/// </summary>
		/// <param name="format">A numeric format string.</param>
		/// <returns>A string that represents the current object.</returns>
		public string ToString(string format)
		{
			return ToDecimal().ToString(format);
		}

		/// <summary>
		/// Returns a string that represents the current object.
		/// </summary>
		/// <param name="format">A numeric format string.</param>
		/// <param name="provider">An object that supplies culture-specific formatting information.</param>
		/// <returns>A string that represents the current object.</returns>
		public string ToString(string format, IFormatProvider provider)
		{
			return ToDecimal().ToString(format, provider);
		}

		/// <summary>
		/// Returns a string that represents the current object.
		/// </summary>
		/// <returns>A string that represents the current object.</returns>
		public override string ToString()
		{
			return ToDecimal().ToString();
		}

		/// <summary>
		/// Calculates a hash code for the current object.
		/// </summary>
		/// <returns>A hash code for the current object.</returns>
		public override int GetHashCode()
		{
			return value.GetHashCode();
		}

		/// <summary>
		/// Determines whether the specified object isequal to the current object.
		/// </summary>
		/// <param name="obj">The object to compare with the current object.</param>
		/// <returns>A value indicating equality between the two objects.</returns>
		public override bool Equals(object obj)
		{
			if (obj is Fixed2Dot14)
				return this.Equals((Fixed2Dot14)obj);
			else if (obj is int)
				return value == ((Fixed2Dot14)obj).value;
			else
				return false;
		}

		#endregion

		#endregion
	}
}
