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

using SharpFont.Internal;

namespace SharpFont
{
	/// <summary>
	/// A simple structure used to pass more or less generic parameters to <see cref="Library.OpenFace"/>.
	/// </summary>
	/// <remarks>
	/// The ID and function of parameters are driver-specific. See the various <see cref="ParamTag"/> flags for more
	/// information.
	/// </remarks>
	public sealed class Parameter
	{
		#region Fields

		private IntPtr reference;
		private ParameterRec rec;

		#endregion

		#region Constructors

		internal Parameter(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets a four-byte identification tag.
		/// </summary>
		[CLSCompliant(false)]
		public ParamTag Tag
		{
			get
			{
				return (ParamTag)rec.tag;
			}
		}

		/// <summary>
		/// Gets a pointer to the parameter data.
		/// </summary>
		public IntPtr Data
		{
			get
			{
				return rec.data;
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
				rec = PInvokeHelper.PtrToStructure<ParameterRec>(reference);
			}
		}

		internal ParameterRec Record
		{
			get
			{
				return rec;
			}
		}

		#endregion
	}
}
