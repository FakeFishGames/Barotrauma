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
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SharpFont.Internal;

namespace SharpFont
{
	/// <summary>
	/// Miscellaneous FreeType2 functions that don't fit anywhere else.
	/// </summary>
	public static partial class FT
	{
		#region Computations

		/// <summary><para>
		/// A very simple function used to perform the computation ‘(a*b)/c’ with maximal accuracy (it uses a 64-bit
		/// intermediate integer whenever necessary).
		/// </para><para>
		/// This function isn't necessarily as fast as some processor specific operations, but is at least completely
		/// portable.
		/// </para></summary>
		/// <param name="a">The first multiplier.</param>
		/// <param name="b">The second multiplier.</param>
		/// <param name="c">The divisor.</param>
		/// <returns>
		/// The result of ‘(a*b)/c’. This function never traps when trying to divide by zero; it simply returns
		/// ‘MaxInt’ or ‘MinInt’ depending on the signs of ‘a’ and ‘b’.
		/// </returns>
		[Obsolete("Use Fixed16Dot16.MultiplyDivide() instead.")]
		public static Fixed16Dot16 MulDiv(Fixed16Dot16 a, Fixed16Dot16 b, Fixed16Dot16 c)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_MulDiv((IntPtr)a.Value, (IntPtr)b.Value, (IntPtr)c.Value));
		}

		/// <summary>
		/// A very simple function used to perform the computation ‘(a*b)/0x10000’ with maximal accuracy. Most of the
		/// time this is used to multiply a given value by a 16.16 fixed float factor.
		/// </summary>
		/// <remarks><para>
		/// This function has been optimized for the case where the absolute value of ‘a’ is less than 2048, and ‘b’ is
		/// a 16.16 scaling factor. As this happens mainly when scaling from notional units to fractional pixels in
		/// FreeType, it resulted in noticeable speed improvements between versions 2.x and 1.x.
		/// </para><para>
		/// As a conclusion, always try to place a 16.16 factor as the second argument of this function; this can make
		/// a great difference.
		/// </para></remarks>
		/// <param name="a">The first multiplier.</param>
		/// <param name="b">The second multiplier. Use a 16.16 factor here whenever possible (see note below).</param>
		/// <returns>The result of ‘(a*b)/0x10000’.</returns>
		[Obsolete("Use Fixed16Dot16.MultiplyFix() instead.")]
		public static Fixed16Dot16 MulFix(int a, Fixed16Dot16 b)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_MulFix((IntPtr)a, (IntPtr)b.Value));
		}

		/// <summary>
		/// A very simple function used to perform the computation ‘(a*0x10000)/b’ with maximal accuracy. Most of the
		/// time, this is used to divide a given value by a 16.16 fixed float factor.
		/// </summary>
		/// <remarks>
		/// The optimization for <see cref="DivFix"/> is simple: If (a &lt;&lt; 16) fits in 32 bits, then the division
		/// is computed directly. Otherwise, we use a specialized version of <see cref="MulDiv"/>.
		/// </remarks>
		/// <param name="a">The first multiplier.</param>
		/// <param name="b">The second multiplier. Use a 16.16 factor here whenever possible (see note below).</param>
		/// <returns>The result of ‘(a*0x10000)/b’.</returns>
		[Obsolete("Use Fixed16Dot16.DivideFix() instead.")]
		public static Fixed16Dot16 DivFix(int a, Fixed16Dot16 b)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_DivFix((IntPtr)a, (IntPtr)b.Value));
		}

		/// <summary>
		/// A very simple function used to round a 16.16 fixed number.
		/// </summary>
		/// <param name="a">The number to be rounded.</param>
		/// <returns>The result of ‘(a + 0x8000) &amp; -0x10000’.</returns>
		[Obsolete("Use Fixed16Dot16.RoundFix() instead.")]
		public static Fixed16Dot16 RoundFix(Fixed16Dot16 a)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_RoundFix((IntPtr)a.Value));
		}

		/// <summary>
		/// A very simple function used to compute the ceiling function of a 16.16 fixed number.
		/// </summary>
		/// <param name="a">The number for which the ceiling function is to be computed.</param>
		/// <returns>The result of ‘(a + 0x10000 - 1) &amp; -0x10000’.</returns>
		[Obsolete("Use Fixed16Dot16.CeilingFix() instead.")]
		public static Fixed16Dot16 CeilFix(Fixed16Dot16 a)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_CeilFix((IntPtr)a.Value));
		}

		/// <summary>
		/// A very simple function used to compute the floor function of a 16.16 fixed number.
		/// </summary>
		/// <param name="a">The number for which the floor function is to be computed.</param>
		/// <returns>The result of ‘a &amp; -0x10000’.</returns>
		[Obsolete("Use Fixed16Dot16.FloorFix() instead.")]
		public static Fixed16Dot16 FloorFix(Fixed16Dot16 a)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_FloorFix((IntPtr)a.Value));
		}

		/// <summary>
		/// Return the sinus of a given angle in fixed point format.
		/// </summary>
		/// <remarks>
		/// If you need both the sinus and cosinus for a given angle, use the function <see cref="FTVector.Unit"/>.
		/// </remarks>
		/// <param name="angle">The input angle.</param>
		/// <returns>The sinus value.</returns>
		[Obsolete("Use Fixed16Dot16.Sin() instead.")]
		public static Fixed16Dot16 Sin(Fixed16Dot16 angle)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_Sin((IntPtr)angle.Value));
		}

		/// <summary>
		/// Return the cosinus of a given angle in fixed point format.
		/// </summary>
		/// <remarks>
		/// If you need both the sinus and cosinus for a given angle, use the function <see cref="FTVector.Unit"/>.
		/// </remarks>
		/// <param name="angle">The input angle.</param>
		/// <returns>The cosinus value.</returns>
		[Obsolete("Use Fixed16Dot16.Cos() instead.")]
		public static Fixed16Dot16 Cos(Fixed16Dot16 angle)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_Cos((IntPtr)angle.Value));
		}

		/// <summary>
		/// Return the tangent of a given angle in fixed point format.
		/// </summary>
		/// <param name="angle">The input angle.</param>
		/// <returns>The tangent value.</returns>
		[Obsolete("Use Fixed16Dot16.Tan() instead.")]
		public static Fixed16Dot16 Tan(Fixed16Dot16 angle)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_Tan((IntPtr)angle.Value));
		}

		/// <summary>
		/// Return the arc-tangent corresponding to a given vector (x,y) in the 2d plane.
		/// </summary>
		/// <param name="x">The horizontal vector coordinate.</param>
		/// <param name="y">The vertical vector coordinate.</param>
		/// <returns>The arc-tangent value (i.e. angle).</returns>
		[Obsolete("Use Fixed16Dot16.Atan2() instead.")]
		public static Fixed16Dot16 Atan2(Fixed16Dot16 x, Fixed16Dot16 y)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_Atan2((IntPtr)x.Value, (IntPtr)y.Value));
		}

		/// <summary>
		/// Return the difference between two angles. The result is always constrained to the [-PI..PI] interval.
		/// </summary>
		/// <param name="angle1">First angle.</param>
		/// <param name="angle2">Second angle.</param>
		/// <returns>Constrained value of ‘value2-value1’.</returns>
		[Obsolete("Use Fixed16Dot16.AngleDiff() instead.")]
		public static Fixed16Dot16 AngleDiff(Fixed16Dot16 angle1, Fixed16Dot16 angle2)
		{
			return Fixed16Dot16.FromRawValue((int)FT.FT_Angle_Diff((IntPtr)angle1.Value, (IntPtr)angle2.Value));
		}

		#endregion

#if !SHARPFONT_PLATFORM_IOS
		#region Mac Specific Interface

		/// <summary>
		/// Return an FSSpec for the disk file containing the named font.
		/// </summary>
		/// <param name="fontName">Mac OS name of the font (e.g., Times New Roman Bold).</param>
		/// <param name="faceIndex">Index of the face. For passing to <see cref="Library.NewFaceFromFSSpec"/>.</param>
		/// <returns>FSSpec to the file. For passing to <see cref="Library.NewFaceFromFSSpec"/>.</returns>
		public static IntPtr GetFileFromMacName(string fontName, out int faceIndex)
		{
			IntPtr fsspec;

			Error err = FT_GetFile_From_Mac_Name(fontName, out fsspec, out faceIndex);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return fsspec;
		}

		/// <summary>
		/// Return an FSSpec for the disk file containing the named font.
		/// </summary>
		/// <param name="fontName">Mac OS name of the font in ATS framework.</param>
		/// <param name="faceIndex">Index of the face. For passing to <see cref="Library.NewFaceFromFSSpec"/>.</param>
		/// <returns>FSSpec to the file. For passing to <see cref="Library.NewFaceFromFSSpec"/>.</returns>
		public static IntPtr GetFileFromMacAtsName(string fontName, out int faceIndex)
		{
			IntPtr fsspec;

			Error err = FT_GetFile_From_Mac_ATS_Name(fontName, out fsspec, out faceIndex);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return fsspec;
		}

		/// <summary>
		/// Return a pathname of the disk file and face index for given font name which is handled by ATS framework.
		/// </summary>
		/// <param name="fontName">Mac OS name of the font in ATS framework.</param>
		/// <param name="path">
		/// Buffer to store pathname of the file. For passing to <see cref="Library.NewFace"/>. The client must
		/// allocate this buffer before calling this function.
		/// </param>
		/// <returns>Index of the face. For passing to <see cref="Library.NewFace"/>.</returns>
		public static unsafe int GetFilePathFromMacAtsName(string fontName, byte[] path)
		{
			int faceIndex;

			fixed (void* ptr = path)
			{
				Error err = FT_GetFilePath_From_Mac_ATS_Name(fontName, (IntPtr)ptr, path.Length, out faceIndex);

				if (err != Error.Ok)
					throw new FreeTypeException(err);
			}

			return faceIndex;
		}

		#endregion
#endif
	}
}
