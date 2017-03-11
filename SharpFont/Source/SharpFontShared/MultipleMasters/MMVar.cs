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
	/// <summary><para>
	/// A structure used to model the axes and space of a Multiple Masters or GX var distortable font.
	/// </para><para>
	/// Some fields are specific to one format and not to the other.
	/// </para></summary>
	public class MMVar
	{
		#region Fields

		private IntPtr reference;
		private MMVarRec rec;

		#endregion

		#region Constructors

		internal MMVar(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the number of axes. The maximum value is 4 for MM; no limit in GX.
		/// </summary>
		[CLSCompliant(false)]
		public uint AxisCount
		{
			get
			{
				return rec.num_axis;
			}
		}

		/// <summary>
		/// Gets the number of designs; should be normally 2^num_axis for MM fonts. Not meaningful for GX (where every
		/// glyph could have a different number of designs).
		/// </summary>
		[CLSCompliant(false)]
		public uint DesignsCount
		{
			get
			{
				return rec.num_designs;
			}
		}

		/// <summary>
		/// Gets the number of named styles; only meaningful for GX which allows certain design coordinates to have a
		/// string ID (in the ‘name’ table) associated with them. The font can tell the user that, for example,
		/// Weight=1.5 is ‘Bold’.
		/// </summary>
		[CLSCompliant(false)]
		public uint NamedStylesCount
		{
			get
			{
				return rec.num_namedstyles;
			}
		}

		/// <summary>
		/// Gets a table of axis descriptors. GX fonts contain slightly more data than MM.
		/// </summary>
		public VarAxis Axis
		{
			get
			{
				return new VarAxis(rec.axis);
			}
		}

		/// <summary>
		/// Gets a table of named styles. Only meaningful with GX.
		/// </summary>
		public VarNamedStyle NamedStyle
		{
			get
			{
				return new VarNamedStyle(rec.namedstyle);
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
				rec = PInvokeHelper.PtrToStructure<MMVarRec>(reference);
			}
		}

		#endregion
	}
}
