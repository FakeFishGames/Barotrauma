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
	/// A list of bit-field constants used with <see cref="Face.TrueTypeGXValidate"/> to indicate which TrueTypeGX/AAT
	/// Type tables should be validated.
	/// </summary>
	[Flags]
	[CLSCompliant(false)]
	public enum TrueTypeValidationFlags : uint
	{
		/// <summary>Validate ‘feat’ table.</summary>
		Feat = 0x4000 << 0,

		/// <summary>Validate ‘mort’ table.</summary>
		Mort = 0x4000 << 1,
		
		/// <summary>Validate ‘morx’ table.</summary>
		Morx = 0x4000 << 2,
		
		/// <summary>Validate ‘bsln’ table.</summary>
		Bsln = 0x4000 << 3,
		
		/// <summary>Validate ‘just’ table.</summary>
		Just = 0x4000 << 4,
		
		/// <summary>Validate ‘kern’ table.</summary>
		Kern = 0x4000 << 5,
		
		/// <summary>Validate ‘opbd’ table.</summary>
		Opbd = 0x4000 << 6,
		
		/// <summary>Validate ‘trak’ table.</summary>
		Trak = 0x4000 << 7,
		
		/// <summary>Validate ‘prop’ table.</summary>
		Prop = 0x4000 << 8,
		
		/// <summary>Validate ‘lcar’ table.</summary>
		Lcar = 0x4000 << 9,

		/// <summary>Validate all TrueTypeGX tables.</summary>
		All = Feat | Mort | Morx | Bsln | Just | Kern | Opbd | Trak | Prop | Lcar
	}
}
