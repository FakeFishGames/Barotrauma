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
	/// A structure used for outline (vectorial) glyph images. This really is a ‘sub-class’ of <see cref="Glyph"/>.
	/// </summary>
	/// <remarks><para>
	/// You can typecast an <see cref="Glyph"/> to <see cref="OutlineGlyph"/> if you have ‘<see cref="Glyph.Format"/>
	/// == <see cref="GlyphFormat.Outline"/>’. This lets you access the outline's content easily.
	/// </para><para>
	/// As the outline is extracted from a glyph slot, its coordinates are expressed normally in 26.6 pixels, unless
	/// the flag <see cref="LoadFlags.NoScale"/> was used in <see cref="Face.LoadGlyph"/> or
	/// <see cref="Face.LoadChar"/>.
	/// </para><para>
	/// The outline's tables are always owned by the object and are destroyed with it.
	/// </para></remarks>
	public class OutlineGlyph : IDisposable
	{
		#region Fields

		private Glyph original;
		private OutlineGlyphRec rec;

		#endregion

		#region Constructors

		internal OutlineGlyph(Glyph original)
		{
			this.original = original;
			Reference = original.Reference; //sets the rec
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="OutlineGlyph"/> class.
		/// </summary>
		~OutlineGlyph()
		{
			Dispose(false);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets a value indicating whether the object has been disposed.
		/// </summary>
		public bool IsDisposed
		{
			get
			{
				return original.IsDisposed;
			}
		}

		/// <summary>
		/// Gets the root <see cref="Glyph"/> fields.
		/// </summary>
		public Glyph Root
		{
			get
			{
				if (IsDisposed)
					throw new ObjectDisposedException("Bitmap", "Cannot access a disposed object.");

				return original;
			}
		}

		/// <summary>
		/// Gets a descriptor for the outline.
		/// </summary>
		public Outline Outline
		{
			get
			{
				if (IsDisposed)
					throw new ObjectDisposedException("Bitmap", "Cannot access a disposed object.");

				return new Outline(PInvokeHelper.AbsoluteOffsetOf<OutlineGlyphRec>(Reference, "outline"), rec.outline);
			}
		}

		internal IntPtr Reference
		{
			get
			{
				if (IsDisposed)
					throw new ObjectDisposedException("Bitmap", "Cannot access a disposed object.");

				return original.Reference;
			}

			set
			{
				if (IsDisposed)
					throw new ObjectDisposedException("Bitmap", "Cannot access a disposed object.");

				rec = PInvokeHelper.PtrToStructure<OutlineGlyphRec>(original.Reference);
			}
		}

		#endregion

		#region Operators

		/// <summary>
		/// Casts a <see cref="OutlineGlyph"/> back up to a <see cref="Glyph"/>. The eqivalent of
		/// <see cref="OutlineGlyph.Root"/>.
		/// </summary>
		/// <param name="g">A <see cref="OutlineGlyph"/>.</param>
		/// <returns>A <see cref="Glyph"/>.</returns>
		public static implicit operator Glyph(OutlineGlyph g)
		{
			return g.original;
		}

		#endregion

		#region Methods

		/// <summary>
		/// A CLS-compliant version of the implicit cast to <see cref="Glyph"/>.
		/// </summary>
		/// <returns>A <see cref="Glyph"/>.</returns>
		public Glyph ToGlyph()
		{
			return (Glyph)this;
		}

		/// <summary>
		/// Disposes an instance of the <see cref="OutlineGlyph"/> class.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if (disposing)
				original.Dispose();
		}

		#endregion
	}
}
