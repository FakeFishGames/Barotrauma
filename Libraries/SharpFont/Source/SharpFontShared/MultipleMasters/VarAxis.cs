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
using System.Runtime.InteropServices;

using SharpFont.MultipleMasters.Internal;

namespace SharpFont.MultipleMasters
{
	/// <summary>
	/// A simple structure used to model a given axis in design space for Multiple Masters and GX var fonts.
	/// </summary>
	public class VarAxis
	{
		#region Fields

		private IntPtr reference;
		private VarAxisRec rec;

		#endregion

		#region Constructors

		internal VarAxis(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the axis's name. Not always meaningful for GX.
		/// </summary>
		public string Name
		{
			get
			{
				return rec.name;
			}
		}

		/// <summary>
		/// Gets the axis's minimum design coordinate.
		/// </summary>
		public int Minimum
		{
			get
			{
				return (int)rec.minimum;
			}
		}

		/// <summary>
		/// Gets the axis's default design coordinate. FreeType computes meaningful default values for MM; it is then
		/// an integer value, not in 16.16 format.
		/// </summary>
		public int Default
		{
			get
			{
				return (int)rec.def;
			}
		}

		/// <summary>
		/// Gets the axis's maximum design coordinate.
		/// </summary>
		public int Maximum
		{
			get
			{
				return (int)rec.maximum;
			}
		}

		/// <summary>
		/// Gets the axis's tag (the GX equivalent to ‘name’). FreeType provides default values for MM if possible.
		/// </summary>
		[CLSCompliant(false)]
		public uint Tag
		{
			get
			{
				return (uint)rec.tag;
			}
		}

		/// <summary>
		/// Gets the entry in ‘name’ table (another GX version of ‘name’). Not meaningful for MM.
		/// </summary>
		[CLSCompliant(false)]
		public uint StrId
		{
			get
			{
				return rec.strid;
			}
		}

		internal IntPtr Reference
		{
			get
			{
				return reference;
			}

			set
			{
				reference = value;
				rec = PInvokeHelper.PtrToStructure<VarAxisRec>(reference);
			}
		}

		#endregion
	}
}
