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
using System.Runtime.InteropServices;

using SharpFont.Internal;

namespace SharpFont
{
	/// <summary>
	/// The root glyph structure contains a given glyph image plus its advance width in 16.16 fixed float format.
	/// </summary>
	public sealed class Glyph : IDisposable
	{
		#region Fields

		private bool disposed;

		private IntPtr reference;
		private GlyphRec rec;

		private Library parentLibrary;

		#endregion

		#region Constructors

		internal Glyph(IntPtr reference, Library parentLibrary)
		{
			Reference = reference;

			this.parentLibrary = parentLibrary;
			parentLibrary.AddChildGlyph(this);
		}

		/// <summary>
		/// Finalizes an instance of the Glyph class.
		/// </summary>
		~Glyph()
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
				return disposed;
			}
		}

		/// <summary>
		/// Gets a handle to the FreeType library object.
		/// </summary>
		public Library Library
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

				return parentLibrary;
			}
		}

		/// <summary>
		/// Gets the format of the glyph's image.
		/// </summary>
		[CLSCompliant(false)]
		public GlyphFormat Format
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Format", "Cannot access a disposed object.");

				return rec.format;
			}
		}

		/// <summary>
		/// Gets a 16.16 vector that gives the glyph's advance width.
		/// </summary>
		public FTVector Advance
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Advance", "Cannot access a disposed object.");

				return rec.advance;
			}
		}

		internal IntPtr Reference
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Reference", "Cannot access a disposed object.");

				return reference;
			}

			set
			{
				if (disposed)
					throw new ObjectDisposedException("Reference", "Cannot access a disposed object.");

				reference = value;
				rec = PInvokeHelper.PtrToStructure<GlyphRec>(reference);
			}
		}

		#endregion

		#region Operators

		/// <summary>
		/// Downcasts a <see cref="Glyph"/> to a <see cref="BitmapGlyph"/>
		/// </summary>
		/// <param name="g">A <see cref="Glyph"/>.</param>
		/// <returns>A <see cref="BitmapGlyph"/>.</returns>
		/// <exception cref="InvalidCastException">
		/// If the <see cref="Glyph"/>'s format is not <see cref="GlyphFormat.Bitmap"/>.
		/// </exception>
		public static explicit operator BitmapGlyph(Glyph g)
		{
			if (g.Format == GlyphFormat.Bitmap)
				return new BitmapGlyph(g);
			else
				throw new InvalidCastException("The glyph's format is not GlyphFormat.Bitmap.");
		}

		/// <summary>
		/// Downcasts a <see cref="Glyph"/> to a <see cref="OutlineGlyph"/>
		/// </summary>
		/// <param name="g">A <see cref="Glyph"/>.</param>
		/// <returns>A <see cref="OutlineGlyph"/>.</returns>
		/// <exception cref="InvalidCastException">
		/// If the <see cref="Glyph"/>'s format is not <see cref="GlyphFormat.Outline"/>.
		/// </exception>
		public static explicit operator OutlineGlyph(Glyph g)
		{
			if (g.Format == GlyphFormat.Outline)
				return new OutlineGlyph(g);
			else
				throw new InvalidCastException("The glyph's format is not GlyphFormat.Outline.");
		}

		#endregion

		#region Methods

		/// <summary>
		/// CLS-compliant equivalent of an explicit cast to <see cref="BitmapGlyph"/>.
		/// </summary>
		/// <returns>A <see cref="BitmapGlyph"/>.</returns>
		public BitmapGlyph ToBitmapGlyph()
		{
			return (BitmapGlyph)this;
		}

		/// <summary>
		/// CLS-compliant equivalent of an explicit cast to <see cref="OutlineGlyph"/>.
		/// </summary>
		/// <returns>A <see cref="OutlineGlyph"/>.</returns>
		public OutlineGlyph ToOutlineGlyph()
		{
			return (OutlineGlyph)this;
		}

		/// <summary>
		/// A function used to copy a glyph image. Note that the created <see cref="Glyph"/> object must be released
		/// with <see cref="Glyph.Dispose()"/>.
		/// </summary>
		/// <returns>A handle to the target glyph object. 0 in case of error.</returns>
		public Glyph Copy()
		{
			if (disposed)
				throw new ObjectDisposedException("Glyph", "Cannot access a disposed object.");

			IntPtr glyphRef;

			Error err = FT.FT_Glyph_Copy(Reference, out glyphRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return new Glyph(glyphRef, Library);
		}

		/// <summary>
		/// Transform a glyph image if its format is scalable.
		/// </summary>
		/// <param name="matrix">A pointer to a 2x2 matrix to apply.</param>
		/// <param name="delta">
		/// A pointer to a 2d vector to apply. Coordinates are expressed in 1/64th of a pixel.
		/// </param>
		public void Transform(FTMatrix matrix, FTVector delta)
		{
			if (disposed)
				throw new ObjectDisposedException("Glyph", "Cannot access a disposed object.");

			Error err = FT.FT_Glyph_Transform(Reference, ref matrix, ref delta);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary><para>
		/// Return a glyph's ‘control box’. The control box encloses all the outline's points, including Bézier control
		/// points. Though it coincides with the exact bounding box for most glyphs, it can be slightly larger in some
		/// situations (like when rotating an outline which contains Bézier outside arcs).
		/// </para><para>
		/// Computing the control box is very fast, while getting the bounding box can take much more time as it needs
		/// to walk over all segments and arcs in the outline. To get the latter, you can use the ‘ftbbox’ component
		/// which is dedicated to this single task.
		/// </para></summary>
		/// <remarks><para>
		/// Coordinates are relative to the glyph origin, using the y upwards convention.
		/// </para><para>
		/// If the glyph has been loaded with <see cref="LoadFlags.NoScale"/>, ‘bbox_mode’ must be set to
		/// <see cref="GlyphBBoxMode.Unscaled"/> to get unscaled font units in 26.6 pixel format. The value
		/// <see cref="GlyphBBoxMode.Subpixels"/> is another name for this constant.
		/// </para><para>
		/// If the font is tricky and the glyph has been loaded with <see cref="LoadFlags.NoScale"/>, the resulting
		/// CBox is meaningless. To get reasonable values for the CBox it is necessary to load the glyph at a large
		/// ppem value (so that the hinting instructions can properly shift and scale the subglyphs), then extracting
		/// the CBox which can be eventually converted back to font units.
		/// </para><para>
		/// Note that the maximum coordinates are exclusive, which means that one can compute the width and height of
		/// the glyph image (be it in integer or 26.6 pixels) as:
		/// </para><para>
		/// <code>
		/// width  = bbox.xMax - bbox.xMin;
		/// height = bbox.yMax - bbox.yMin;
		/// </code>
		/// </para><para>
		/// Note also that for 26.6 coordinates, if ‘bbox_mode’ is set to <see cref="GlyphBBoxMode.Gridfit"/>, the
		/// coordinates will also be grid-fitted, which corresponds to:
		/// </para><para>
		/// <code>
		/// bbox.xMin = FLOOR(bbox.xMin);
		/// bbox.yMin = FLOOR(bbox.yMin);
		/// bbox.xMax = CEILING(bbox.xMax);
		/// bbox.yMax = CEILING(bbox.yMax);
		/// </code>
		/// </para><para>
		/// To get the bbox in pixel coordinates, set ‘bbox_mode’ to <see cref="GlyphBBoxMode.Truncate"/>.
		/// </para><para>
		/// To get the bbox in grid-fitted pixel coordinates, set ‘bbox_mode’ to <see cref="GlyphBBoxMode.Pixels"/>.
		/// </para></remarks>
		/// <param name="mode">The mode which indicates how to interpret the returned bounding box values.</param>
		/// <returns>
		/// The glyph coordinate bounding box. Coordinates are expressed in 1/64th of pixels if it is grid-fitted.
		/// </returns>
		[CLSCompliant(false)]
		public BBox GetCBox(GlyphBBoxMode mode)
		{
			if (disposed)
				throw new ObjectDisposedException("Glyph", "Cannot access a disposed object.");

			BBox box;
			FT.FT_Glyph_Get_CBox(Reference, mode, out box);

			return box;
		}

		/// <summary>
		/// Convert a given glyph object to a bitmap glyph object.
		/// </summary>
		/// <remarks><para>
		/// This function does nothing if the glyph format isn't scalable.
		/// </para><para>
		/// The glyph image is translated with the ‘origin’ vector before rendering.
		/// </para><para>
		/// The first parameter is a pointer to an <see cref="Glyph"/> handle, that will be replaced by this function
		/// (with newly allocated data). Typically, you would use (omitting error handling):
		/// </para><para>
		/// --sample code ommitted--
		/// </para></remarks>
		/// <param name="renderMode">An enumeration that describes how the data is rendered.</param>
		/// <param name="origin">
		/// A pointer to a vector used to translate the glyph image before rendering. Can be 0 (if no translation). The
		/// origin is expressed in 26.6 pixels.
		/// </param>
		/// <param name="destroy">
		/// A boolean that indicates that the original glyph image should be destroyed by this function. It is never
		/// destroyed in case of error.
		/// </param>
		public void ToBitmap(RenderMode renderMode, FTVector26Dot6 origin, bool destroy)
		{
			if (disposed)
				throw new ObjectDisposedException("Glyph", "Cannot access a disposed object.");

			IntPtr glyphRef = Reference;
			Error err = FT.FT_Glyph_To_Bitmap(ref glyphRef, renderMode, ref origin, destroy);

			Reference = glyphRef;

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		#region Glyph Stroker

		/// <summary>
		/// Stroke a given outline glyph object with a given stroker.
		/// </summary>
		/// <remarks>
		/// The source glyph is untouched in case of error.
		/// </remarks>
		/// <param name="stroker">A stroker handle.</param>
		/// <param name="destroy">A Boolean. If 1, the source glyph object is destroyed on success.</param>
		/// <returns>New glyph handle.</returns>
		public Glyph Stroke(Stroker stroker, bool destroy)
		{
			if (disposed)
				throw new ObjectDisposedException("Glyph", "Cannot access a disposed object.");

			if (stroker == null)
				throw new ArgumentNullException("stroker");

			IntPtr sourceRef = Reference;
			Error err = FT.FT_Glyph_Stroke(ref sourceRef, stroker.Reference, destroy);

			if (destroy && err == Error.Ok)
			{
				//if FT_Glyph_Stroke destroys the glyph, keep the C# side synchronized.
				disposed = true;
				reference = IntPtr.Zero;
			}

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			//check if the pointer didn't change.
			if (sourceRef == Reference)
				return this;
			else
				return new Glyph(sourceRef, Library);
		}

		/// <summary>
		/// Stroke a given outline glyph object with a given stroker, but only return either its inside or outside
		/// border.
		/// </summary>
		/// <remarks>
		/// The source glyph is untouched in case of error.
		/// </remarks>
		/// <param name="stroker">A stroker handle.</param>
		/// <param name="inside">A Boolean. If 1, return the inside border, otherwise the outside border.</param>
		/// <param name="destroy">A Boolean. If 1, the source glyph object is destroyed on success.</param>
		/// <returns>New glyph handle.</returns>
		public Glyph StrokeBorder(Stroker stroker, bool inside, bool destroy)
		{
			if (disposed)
				throw new ObjectDisposedException("Glyph", "Cannot access a disposed object.");

			if (stroker == null)
				throw new ArgumentNullException("stroker");

			IntPtr sourceRef = Reference;
			Error err = FT.FT_Glyph_StrokeBorder(ref sourceRef, stroker.Reference, inside, destroy);

			if (destroy && err == Error.Ok)
			{
				//if FT_Glyph_Stroke destroys the glyph, keep the C# side synchronized.
				disposed = true;
				reference = IntPtr.Zero;
			}

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			//check if the pointer didn't change.
			if (sourceRef == Reference)
				return this;
			else
				return new Glyph(sourceRef, Library);
		}

		#endregion

		/// <summary>
		/// Disposes the Glyph.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if (!disposed)
			{
				disposed = true;

				FT.FT_Done_Glyph(reference);

				// removes itself from the parent Library, with a check to prevent this from happening when Library is
				// being disposed (Library disposes all it's children with a foreach loop, this causes an
				// InvalidOperationException for modifying a collection during enumeration)
				if (!parentLibrary.IsDisposed)
					parentLibrary.RemoveChildGlyph(this);

				reference = IntPtr.Zero;
			}
		}

		#endregion
	}
}
