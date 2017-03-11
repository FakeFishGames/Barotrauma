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

using SharpFont.Cache.Internal;

namespace SharpFont.Cache
{
	/// <summary>
	/// A structure used to model the type of images in a glyph cache.
	/// </summary>
	public class ImageType
	{
		#region Fields

		private IntPtr reference;
		private ImageTypeRec rec;

		#endregion

		#region Constructors

		internal ImageType(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the face ID.
		/// </summary>
		public IntPtr FaceId
		{
			get
			{
				return rec.face_id;
			}
		}

		/// <summary>
		/// Gets the width in pixels.
		/// </summary>
		public int Width
		{
			get
			{
				return rec.width;
			}
		}

		/// <summary>
		/// Gets the height in pixels.
		/// </summary>
		public int Height
		{
			get
			{
				return rec.height;
			}
		}

		/// <summary>
		/// Gets the load flags, as in <see cref="Face.LoadGlyph"/>
		/// </summary>
		[CLSCompliant(false)]
		public LoadFlags Flags
		{
			get
			{
				return rec.flags;
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
				rec = PInvokeHelper.PtrToStructure<ImageTypeRec>(reference);
			}
		}

		#endregion
	}
}
