#region MIT License
/*Copyright (c) 2012-2015 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// This structure is used to describe an outline to the scan-line converter.
	/// </summary>
	/// <remarks>
	/// The B/W rasterizer only checks bit 2 in the ‘tags’ array for the first point of each contour. The drop-out mode
	/// as given with <see cref="OutlineFlags.IgnoreDropouts"/>, <see cref="OutlineFlags.SmartDropouts"/>, and
	/// <see cref="OutlineFlags.IncludeStubs"/> in ‘flags’ is then overridden.
	/// </remarks>
	public sealed class Outline : IDisposable
	{
		#region Fields

		private bool disposed;
		private bool duplicate;

		private IntPtr reference;
		private OutlineRec rec;

		private Library parentLibrary;
		private Memory parentMemory;

		#endregion

		#region Constructor

		/// <summary>
		/// Initializes a new instance of the <see cref="Outline"/> class.
		/// </summary>
		/// <remarks>
		/// The reason why this function takes a ‘library’ parameter is simply to use the library's memory allocator.
		/// </remarks>
		/// <param name="library">
		/// A handle to the library object from where the outline is allocated. Note however that the new outline will
		/// not necessarily be freed, when destroying the library, by <see cref="Library.Finalize"/>.
		/// </param>
		/// <param name="pointsCount">The maximum number of points within the outline.</param>
		/// <param name="contoursCount">The maximum number of contours within the outline.</param>
		[CLSCompliant(false)]
		public Outline(Library library, uint pointsCount, int contoursCount)
		{
			IntPtr reference;
			Error err = FT.FT_Outline_New(library.Reference, pointsCount, contoursCount, out reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			parentLibrary = library;
			parentLibrary.AddChildOutline(this);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Outline"/> class.
		/// </summary>
		/// <param name="memory">A handle to the memory object from where the outline is allocated.</param>
		/// <param name="pointsCount">The maximum number of points within the outline.</param>
		/// <param name="contoursCount">The maximum number of contours within the outline.</param>
		[CLSCompliant(false)]
		public Outline(Memory memory, uint pointsCount, int contoursCount)
		{
			IntPtr reference;
			Error err = FT.FT_Outline_New_Internal(memory.Reference, pointsCount, contoursCount, out reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			parentMemory = memory; //TODO Should Memory be disposable as well?
		}

		internal Outline(IntPtr reference, OutlineRec outlineInt)
		{
			this.rec = outlineInt;
			this.reference = reference;

			duplicate = true;
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="Outline"/> class.
		/// </summary>
		~Outline()
		{
			Dispose(false);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets a value indicating whether the <see cref="Outline"/> has been disposed.
		/// </summary>
		public bool IsDisposed
		{
			get
			{
				return disposed;
			}
		}

		/// <summary>
		/// Gets the number of contours in the outline.
		/// </summary>
		public short ContoursCount
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("ContoursCount", "Cannot access a disposed object.");

				return rec.n_contours;
			}
		}

		/// <summary>
		/// Gets the number of points in the outline.
		/// </summary>
		public short PointsCount
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("PointsCount", "Cannot access a disposed object.");

				return rec.n_points;
			}
		}

		/// <summary>
		/// Gets a pointer to an array of ‘PointsCount’ <see cref="FTVector"/> elements, giving the outline's point
		/// coordinates.
		/// </summary>
		public FTVector[] Points
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Points", "Cannot access a disposed object.");

				int count = PointsCount;

				if (count == 0)
					return null;

				FTVector[] points = new FTVector[count];
				IntPtr array = rec.points;

				for (int i = 0; i < count; i++)
				{
					points[i] = new FTVector(new IntPtr(array.ToInt64() + (IntPtr.Size * i * 2)));
				}

				return points;
			}
		}

		/// <summary><para>
		/// Gets a pointer to an array of ‘PointsCount’ chars, giving each outline point's type.
		/// </para><para>
		/// If bit 0 is unset, the point is ‘off’ the curve, i.e., a Bézier control point, while it is ‘on’ if set.
		/// </para><para>
		/// Bit 1 is meaningful for ‘off’ points only. If set, it indicates a third-order Bézier arc control point; and
		/// a second-order control point if unset.
		/// </para><para>
		/// If bit 2 is set, bits 5-7 contain the drop-out mode (as defined in the OpenType specification; the value is
		/// the same as the argument to the SCANMODE instruction).
		/// </para><para>
		/// Bits 3 and 4 are reserved for internal purposes.
		/// </para></summary>
		public byte[] Tags
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Tags", "Cannot access a disposed object.");

				int count = PointsCount;

				if (count == 0)
					return null;

				byte[] tags = new byte[count];
				IntPtr array = rec.tags;

				for (int i = 0; i < count; i++)
				{
					tags[i] = Marshal.ReadByte(array, sizeof(byte) * i);
				}

				return tags;
			}
		}

		/// <summary>
		/// Gets an array of ‘ContoursCount’ shorts, giving the end point of each contour within the outline. For
		/// example, the first contour is defined by the points ‘0’ to ‘Contours[0]’, the second one is defined by the
		/// points ‘Contours[0]+1’ to ‘Contours[1]’, etc.
		/// </summary>
		public short[] Contours
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Contours", "Cannot access a disposed object.");

				int count = ContoursCount;

				if (count == 0)
					return null;

				short[] contours = new short[count];
				IntPtr array = rec.contours;

				for (int i = 0; i < count; i++)
				{
					contours[i] = Marshal.ReadInt16(array, sizeof(short) * i);
				}

				return contours;
			}
		}

		/// <summary>
		/// Gets a set of bit flags used to characterize the outline and give hints to the scan-converter and hinter on
		/// how to convert/grid-fit it.
		/// </summary>
		/// <see cref="OutlineFlags"/>
		public OutlineFlags Flags
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Flags", "Cannot access a disposed object.");

				return rec.flags;
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
				rec = PInvokeHelper.PtrToStructure<OutlineRec>(reference);
			}
		}

		#endregion

		#region Methods

		#region Outline Processing

		/// <summary>
		/// Copy an outline into another one. Both objects must have the same sizes (number of points &amp; number of
		/// contours) when this function is called.
		/// </summary>
		/// <param name="target">A handle to the target outline.</param>
		public void Copy(Outline target)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			if (target == null)
				throw new ArgumentNullException("target");

			IntPtr targetRef = target.Reference;
			Error err = FT.FT_Outline_Copy(reference, ref targetRef);
			target.Reference = reference;

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Apply a simple translation to the points of an outline.
		/// </summary>
		/// <param name="offsetX">The horizontal offset.</param>
		/// <param name="offsetY">The vertical offset.</param>
		public void Translate(int offsetX, int offsetY)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			FT.FT_Outline_Translate(reference, offsetX, offsetY);
		}

		/// <summary>
		/// Apply a simple 2x2 matrix to all of an outline's points. Useful for applying rotations, slanting, flipping,
		/// etc.
		/// </summary>
		/// <remarks>
		/// You can use <see cref="Translate"/> if you need to translate the outline's points.
		/// </remarks>
		/// <param name="matrix">A pointer to the transformation matrix.</param>
		public void Transform(FTMatrix matrix)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			FT.FT_Outline_Transform(reference, ref matrix);
		}

		/// <summary><para>
		/// Embolden an outline. The new outline will be at most 4 times ‘strength’ pixels wider and higher. You may
		/// think of the left and bottom borders as unchanged.
		/// </para><para>
		/// Negative ‘strength’ values to reduce the outline thickness are possible also.
		/// </para></summary>
		/// <remarks><para>
		/// The used algorithm to increase or decrease the thickness of the glyph doesn't change the number of points;
		/// this means that certain situations like acute angles or intersections are sometimes handled incorrectly.
		/// </para><para>
		/// If you need ‘better’ metrics values you should call <see cref="GetCBox"/> or <see cref="GetBBox"/>.
		/// </para></remarks>
		/// <example>
		/// FT_Load_Glyph( face, index, FT_LOAD_DEFAULT );
		/// if ( face-&gt;slot-&gt;format == FT_GLYPH_FORMAT_OUTLINE )
		/// 	FT_Outline_Embolden( &amp;face-&gt;slot-&gt;outline, strength );
		/// </example>
		/// <param name="strength">How strong the glyph is emboldened. Expressed in 26.6 pixel format.</param>
		public void Embolden(Fixed26Dot6 strength)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			Error err = FT.FT_Outline_Embolden(reference, (IntPtr)strength.Value);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Embolden an outline. The new outline will be ‘xstrength’ pixels wider and ‘ystrength’ pixels higher.
		/// Otherwise, it is similar to <see cref="Embolden"/>, which uses the same strength in both directions.
		/// </summary>
		/// <param name="strengthX">
		/// How strong the glyph is emboldened in the X direction. Expressed in 26.6 pixel format.
		/// </param>
		/// <param name="strengthY">
		/// How strong the glyph is emboldened in the Y direction. Expressed in 26.6 pixel format.
		/// </param>
		public void EmboldenXY(int strengthX, int strengthY)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			Error err = FT.FT_Outline_EmboldenXY(reference, strengthX, strengthY);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Reverse the drawing direction of an outline. This is used to ensure consistent fill conventions for
		/// mirrored glyphs.
		/// </summary>
		/// <remarks><para>
		/// This function toggles the bit flag <see cref="OutlineFlags.ReverseFill"/> in the outline's ‘flags’ field.
		/// </para><para>
		/// It shouldn't be used by a normal client application, unless it knows what it is doing.
		/// </para></remarks>
		public void Reverse()
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			FT.FT_Outline_Reverse(reference);
		}

		/// <summary>
		/// Check the contents of an outline descriptor.
		/// </summary>
		public void Check()
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			Error err = FT.FT_Outline_Check(reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Compute the exact bounding box of an outline. This is slower than computing the control box. However, it
		/// uses an advanced algorithm which returns very quickly when the two boxes coincide. Otherwise, the outline
		/// Bézier arcs are traversed to extract their extrema.
		/// </summary>
		/// <remarks>
		/// If the font is tricky and the glyph has been loaded with <see cref="LoadFlags.NoScale"/>, the resulting
		/// BBox is meaningless. To get reasonable values for the BBox it is necessary to load the glyph at a large
		/// ppem value (so that the hinting instructions can properly shift and scale the subglyphs), then extracting
		/// the BBox which can be eventually converted back to font units.
		/// </remarks>
		/// <returns>The outline's exact bounding box.</returns>
		public BBox GetBBox()
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			BBox bbox;
			Error err = FT.FT_Outline_Get_BBox(reference, out bbox);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return bbox;
		}

		/// <summary>
		/// Walk over an outline's structure to decompose it into individual segments and Bézier arcs. This function
		/// also emits ‘move to’ operations to indicate the start of new contours in the outline.
		/// </summary>
		/// <param name="funcInterface">
		/// A table of ‘emitters’, i.e., function pointers called during decomposition to indicate path operations.
		/// </param>
		/// <param name="user">
		/// A typeless pointer which is passed to each emitter during the decomposition. It can be used to store the
		/// state during the decomposition.
		/// </param>
		public void Decompose(OutlineFuncs funcInterface, IntPtr user)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			if (funcInterface == null)
				throw new ArgumentNullException("funcInterface");

			OutlineFuncsRec ofRec = funcInterface.Record;
			Error err = FT.FT_Outline_Decompose(reference, ref ofRec, user);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary><para>
		/// Return an outline's ‘control box’. The control box encloses all the outline's points, including Bézier
		/// control points. Though it coincides with the exact bounding box for most glyphs, it can be slightly larger
		/// in some situations (like when rotating an outline which contains Bézier outside arcs).
		/// </para><para>
		/// Computing the control box is very fast, while getting the bounding box can take much more time as it needs
		/// to walk over all segments and arcs in the outline. To get the latter, you can use the ‘ftbbox’ component
		/// which is dedicated to this single task.
		/// </para></summary>
		/// <remarks>See <see cref="Glyph.GetCBox"/> for a discussion of tricky fonts.</remarks>
		/// <returns>The outline's control box.</returns>
		public BBox GetCBox()
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			BBox cbox;
			FT.FT_Outline_Get_CBox(reference, out cbox);

			return cbox;
		}

		/// <summary>
		/// Render an outline within a bitmap. The outline's image is simply OR-ed to the target bitmap.
		/// </summary>
		/// <remarks><para>
		/// This function does NOT CREATE the bitmap, it only renders an outline image within the one you pass to it!
		/// Consequently, the various fields in ‘abitmap’ should be set accordingly.
		/// </para><para>
		/// It will use the raster corresponding to the default glyph format.
		/// </para><para>
		/// The value of the ‘num_grays’ field in ‘abitmap’ is ignored. If you select the gray-level rasterizer, and
		/// you want less than 256 gray levels, you have to use <see cref="Render(RasterParams)"/> directly.
		/// </para></remarks>
		/// <param name="bitmap">A pointer to the target bitmap descriptor.</param>
		public void GetBitmap(FTBitmap bitmap)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			Error err = FT.FT_Outline_Get_Bitmap(parentLibrary.Reference, reference, bitmap.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Render an outline within a bitmap. The outline's image is simply OR-ed to the target bitmap.
		/// </summary>
		/// <remarks><para>
		/// This function does NOT CREATE the bitmap, it only renders an outline image within the one you pass to it!
		/// Consequently, the various fields in ‘abitmap’ should be set accordingly.
		/// </para><para>
		/// It will use the raster corresponding to the default glyph format.
		/// </para><para>
		/// The value of the ‘num_grays’ field in ‘abitmap’ is ignored. If you select the gray-level rasterizer, and
		/// you want less than 256 gray levels, you have to use <see cref="Render(Library, RasterParams)"/> directly.
		/// </para></remarks>
		/// <param name="library">A handle to a FreeType library object.</param>
		/// <param name="bitmap">A pointer to the target bitmap descriptor.</param>
		public void GetBitmap(Library library, FTBitmap bitmap)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			if (library == null)
				throw new ArgumentNullException("library");

			if (bitmap == null)
				throw new ArgumentNullException("bitmap");

			Error err = FT.FT_Outline_Get_Bitmap(library.Reference, reference, bitmap.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Render an outline within a bitmap using the current scan-convert. This function uses an
		/// <see cref="RasterParams"/> structure as an argument, allowing advanced features like direct composition,
		/// translucency, etc.
		/// </summary>
		/// <remarks><para>
		/// You should know what you are doing and how <see cref="RasterParams"/> works to use this function.
		/// </para><para>
		/// The field ‘params.source’ will be set to ‘outline’ before the scan converter is called, which means that
		/// the value you give to it is actually ignored.
		/// </para><para>
		/// The gray-level rasterizer always uses 256 gray levels. If you want less gray levels, you have to provide
		/// your own span callback. See the <see cref="RasterFlags.Direct"/> value of the ‘flags’ field in the
		/// <see cref="RasterParams"/> structure for more details.
		/// </para></remarks>
		/// <param name="parameters">
		/// A pointer to an <see cref="RasterParams"/> structure used to describe the rendering operation.
		/// </param>
		public void Render(RasterParams parameters)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			if (parameters == null)
				throw new ArgumentNullException("parameters");

			Error err = FT.FT_Outline_Render(parentLibrary.Reference, reference, parameters.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Render an outline within a bitmap using the current scan-convert. This function uses an
		/// <see cref="RasterParams"/> structure as an argument, allowing advanced features like direct composition,
		/// translucency, etc.
		/// </summary>
		/// <remarks><para>
		/// You should know what you are doing and how <see cref="RasterParams"/> works to use this function.
		/// </para><para>
		/// The field ‘params.source’ will be set to ‘outline’ before the scan converter is called, which means that
		/// the value you give to it is actually ignored.
		/// </para><para>
		/// The gray-level rasterizer always uses 256 gray levels. If you want less gray levels, you have to provide
		/// your own span callback. See the <see cref="RasterFlags.Direct"/> value of the ‘flags’ field in the
		/// <see cref="RasterParams"/> structure for more details.
		/// </para></remarks>
		/// <param name="library">A handle to a FreeType library object.</param>
		/// <param name="parameters">
		/// A pointer to an <see cref="RasterParams"/> structure used to describe the rendering operation.
		/// </param>
		public void Render(Library library, RasterParams parameters)
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			if (library == null)
				throw new ArgumentNullException("library");

			if (parameters == null)
				throw new ArgumentNullException("parameters");

			Error err = FT.FT_Outline_Render(library.Reference, reference, parameters.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary><para>
		/// This function analyzes a glyph outline and tries to compute its fill orientation (see
		/// <see cref="Orientation"/>). This is done by computing the direction of each global horizontal and/or
		/// vertical extrema within the outline.
		/// </para><para>
		/// Note that this will return <see cref="Orientation.TrueType"/> for empty outlines.
		/// </para></summary>
		/// <returns>The orientation.</returns>
		public Orientation GetOrientation()
		{
			if (disposed)
				throw new ObjectDisposedException("Outline", "Cannot access a disposed object.");

			return FT.FT_Outline_Get_Orientation(reference);
		}

		#endregion

		#region Glyph Stroker

		/// <summary>
		/// Retrieve the <see cref="StrokerBorder"/> value corresponding to the ‘inside’ borders of a given outline.
		/// </summary>
		/// <returns>The border index. <see cref="StrokerBorder.Right"/> for empty or invalid outlines.</returns>
		public StrokerBorder GetInsideBorder()
		{
			return FT.FT_Outline_GetInsideBorder(Reference);
		}

		/// <summary>
		/// Retrieve the <see cref="StrokerBorder"/> value corresponding to the ‘outside’ borders of a given outline.
		/// </summary>
		/// <returns>The border index. <see cref="StrokerBorder.Left"/> for empty or invalid outlines.</returns>
		public StrokerBorder GetOutsideBorder()
		{
			return FT.FT_Outline_GetOutsideBorder(Reference);
		}

		#endregion

		/// <summary>
		/// Disposes an instance of the <see cref="Outline"/> class.
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

				if (!duplicate)
				{
					if (parentLibrary != null)
						FT.FT_Outline_Done(parentLibrary.Reference, reference);
					else
						FT.FT_Outline_Done_Internal(parentMemory.Reference, reference);

					// removes itself from the parent Library, with a check to prevent this from happening when Library is
					// being disposed (Library disposes all it's children with a foreach loop, this causes an
					// InvalidOperationException for modifying a collection during enumeration)
					if (!parentLibrary.IsDisposed)
						parentLibrary.RemoveChildOutline(this);
				}

				reference = IntPtr.Zero;
				rec = default(OutlineRec);
			}
		}

		#endregion
	}
}
