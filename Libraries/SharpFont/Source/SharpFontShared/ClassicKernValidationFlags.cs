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
	/// A list of bit-field constants used with <see cref="Face.ClassicKernValidate"/> to indicate the classic kern
	/// dialect or dialects. If the selected type doesn't fit, <see cref="Face.ClassicKernValidate"/> regards the table
	/// as invalid.
	/// </summary>
	[Flags]
	[CLSCompliant(false)]
	public enum ClassicKernValidationFlags : uint
	{
		/// <summary>Handle the ‘kern’ table as a classic Microsoft kern table.</summary>
		Microsoft = 0x4000 << 0,

		/// <summary>Handle the ‘kern’ table as a classic Apple kern table.</summary>
		Apple = 0x4000 << 1,

		/// <summary>Handle the ‘kern’ as either classic Apple or Microsoft kern table.</summary>
		All = Microsoft | Apple
	}
}
