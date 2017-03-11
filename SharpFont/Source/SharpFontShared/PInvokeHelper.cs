#region MIT License
/*Copyright (c) 2012-2013, 2015 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// Helpful methods to make marshalling simpler.
	/// </summary>
	internal static class PInvokeHelper
	{
		/// <summary>
		/// A generic wrapper for <see cref="Marshal.PtrToStructure(IntPtr, Type)"/>.
		/// </summary>
		/// <typeparam name="T">The type to cast to.</typeparam>
		/// <param name="reference">The pointer that holds the struct.</param>
		/// <returns>A marshalled struct.</returns>
		internal static T PtrToStructure<T>(IntPtr reference)
		{
			return (T)Marshal.PtrToStructure(reference, typeof(T));
		}

		/// <summary>
		/// A method to copy data from one pointer to another, byte by byte.
		/// </summary>
		/// <param name="source">The source pointer.</param>
		/// <param name="sourceOffset">An offset into the source buffer.</param>
		/// <param name="destination">The destination pointer.</param>
		/// <param name="destinationOffset">An offset into the destination buffer.</param>
		/// <param name="count">The number of bytes to copy.</param>
		internal static unsafe void Copy(IntPtr source, int sourceOffset, IntPtr destination, int destinationOffset, int count)
		{
			byte* src = (byte*)source + sourceOffset;
			byte* dst = (byte*)destination + destinationOffset;
			byte* end = dst + count;

			while (dst != end)
				*dst++ = *src++;
		}

		/// <summary>
		/// A common pattern in SharpFont is to pass a pointer to a memory address inside of a struct. This method
		/// works for all cases and provides a generic API.
		/// </summary>
		/// <see cref="Marshal.OffsetOf"/>
		/// <typeparam name="T">The type of the struct to take an offset from.</typeparam>
		/// <param name="start">A pointer to the start of a struct.</param>
		/// <param name="fieldName">The name of the field to get an offset to.</param>
		/// <returns><code>start</code> + the offset of the <code>fieldName</code> field in <code>T</code>.</returns>
		internal static IntPtr AbsoluteOffsetOf<T>(IntPtr start, string fieldName)
		{
			return new IntPtr(start.ToInt64() + Marshal.OffsetOf(typeof(T), fieldName).ToInt64());
		}
	}
}
