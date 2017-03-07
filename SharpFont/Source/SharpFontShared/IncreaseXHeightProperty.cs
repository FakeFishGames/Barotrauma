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

using SharpFont.Internal;

namespace SharpFont
{
	/// <summary>
	/// The data exchange structure for the increase-x-height property.
	/// </summary>
	public class IncreaseXHeightProperty
	{
		private IncreaseXHeightPropertyRec rec;
		private Face face;

		/// <summary>
		/// Initializes a new instance of the <see cref="IncreaseXHeightProperty"/> class.
		/// </summary>
		/// <param name="face">The face to increase the X height of.</param>
		public IncreaseXHeightProperty(Face face)
		{
			this.rec.face = face.Reference;
			this.face = face;
		}

		internal IncreaseXHeightProperty(IncreaseXHeightPropertyRec rec, Face face)
		{
			this.rec = rec;
			this.face = face;
		}

		/// <summary>
		/// Gets or sets the associated face.
		/// </summary>
		public Face Face
		{
			get
			{
				return face;
			}

			set
			{
				face = value;
				rec.face = face.Reference;
			}
		}

		/// <summary>
		/// Gets or sets the limit property.
		/// </summary>
		[CLSCompliant(false)]
		public uint Limit
		{
			get
			{
				return rec.limit;
			}

			set
			{
				rec.limit = value;
			}
		}

		internal IncreaseXHeightPropertyRec Rec
		{
			get
			{
				return rec;
			}
		}
	}
}
