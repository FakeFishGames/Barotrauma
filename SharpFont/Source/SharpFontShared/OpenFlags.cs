#region MIT License
/*Copyright (c) 2012-2014 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// A list of bit-field constants used within the ‘flags’ field of the <see cref="OpenArgs"/> structure.
	/// </summary>
	/// <remarks>
	/// The <see cref="OpenFlags.Memory"/>, <see cref="OpenFlags.Stream"/>, and <see cref="OpenFlags.PathName"/> flags
	/// are mutually exclusive.
	/// </remarks>
	[Flags]
	public enum OpenFlags
	{
		/// <summary>This is a memory-based stream.</summary>
		Memory = 0x01,

		/// <summary>Copy the stream from the ‘stream’ field.</summary>
		Stream = 0x02,

		/// <summary>Create a new input stream from a C path name.</summary>
		PathName = 0x04,

		/// <summary>Use the ‘driver’ field.</summary>
		Driver = 0x08,

		/// <summary>Use the ‘num_params’ and ‘params’ fields.</summary>
		Params = 0x10
	}
}
