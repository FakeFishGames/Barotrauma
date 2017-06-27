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

namespace SharpFont
{
	/// <summary>
	/// Opaque handler to a path stroker object.
	/// </summary>
	public class Stroker : IDisposable
	{
		#region Fields

		private IntPtr reference;
		private bool disposed;

		private Library parentLibrary;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="Stroker"/> class.
		/// </summary>
		/// <param name="library">FreeType library handle.</param>
		public Stroker(Library library)
		{
			IntPtr strokerRef;
			Error err = FT.FT_Stroker_New(library.Reference, out strokerRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			Reference = strokerRef;
			library.AddChildStroker(this);
			parentLibrary = library;
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="Stroker"/> class.
		/// </summary>
		~Stroker()
		{
			Dispose(false);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets a value indicating whether the <see cref="Stroker"/> has been disposed.
		/// </summary>
		public bool IsDisposed
		{
			get
			{
				return disposed;
			}
		}

		internal IntPtr Reference
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

				return reference;
			}

			set
			{
				if (disposed)
					throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

				reference = value;
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Reset a stroker object's attributes.
		/// </summary>
		/// <remarks>
		/// The radius is expressed in the same units as the outline coordinates.
		/// </remarks>
		/// <param name="radius">The border radius.</param>
		/// <param name="lineCap">The line cap style.</param>
		/// <param name="lineJoin">The line join style.</param>
		/// <param name="miterLimit">
		/// The miter limit for the <see cref="StrokerLineJoin.MiterFixed"/> and
		/// <see cref="StrokerLineJoin.MiterVariable"/> line join styles, expressed as 16.16 fixed point value.
		/// </param>
		public void Set(int radius, StrokerLineCap lineCap, StrokerLineJoin lineJoin, Fixed16Dot16 miterLimit)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			FT.FT_Stroker_Set(Reference, radius, lineCap, lineJoin, (IntPtr)miterLimit.Value);
		}

		/// <summary>
		/// Reset a stroker object without changing its attributes. You should call this function before beginning a
		/// new series of calls to <see cref="BeginSubPath"/> or <see cref="EndSubPath"/>.
		/// </summary>
		public void Rewind()
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			FT.FT_Stroker_Rewind(Reference);
		}

		/// <summary>
		/// A convenience function used to parse a whole outline with the stroker. The resulting outline(s) can be
		/// retrieved later by functions like <see cref="GetCounts"/> and <see cref="Export"/>.
		/// </summary>
		/// <remarks><para>
		/// If ‘opened’ is 0 (the default), the outline is treated as a closed path, and the stroker generates two
		/// distinct ‘border’ outlines.
		/// </para><para>
		/// If ‘opened’ is 1, the outline is processed as an open path, and the stroker generates a single ‘stroke’
		/// outline.
		/// </para><para>
		/// This function calls <see cref="Rewind"/> automatically.
		/// </para></remarks>
		/// <param name="outline">The source outline.</param>
		/// <param name="opened">
		/// A boolean. If 1, the outline is treated as an open path instead of a closed one.
		/// </param>
		public void ParseOutline(Outline outline, bool opened)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			if (outline == null)
				throw new ArgumentNullException("outline");

			Error err = FT.FT_Stroker_ParseOutline(Reference, outline.Reference, opened);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Start a new sub-path in the stroker.
		/// </summary>
		/// <remarks>
		/// This function is useful when you need to stroke a path that is not stored as an <see cref="Outline"/>
		/// object.
		/// </remarks>
		/// <param name="to">A pointer to the start vector.</param>
		/// <param name="open">A boolean. If 1, the sub-path is treated as an open one.</param>
		public void BeginSubPath(FTVector to, bool open)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			Error err = FT.FT_Stroker_BeginSubPath(Reference, ref to, open);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Close the current sub-path in the stroker.
		/// </summary>
		/// <remarks>
		/// You should call this function after <see cref="BeginSubPath"/>. If the subpath was not ‘opened’, this
		/// function ‘draws’ a single line segment to the start position when needed.
		/// </remarks>
		public void EndSubPath()
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			Error err = FT.FT_Stroker_EndSubPath(Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// ‘Draw’ a single line segment in the stroker's current sub-path, from the last position.
		/// </summary>
		/// <remarks>
		/// You should call this function between <see cref="BeginSubPath"/> and <see cref="EndSubPath"/>.
		/// </remarks>
		/// <param name="to">A pointer to the destination point.</param>
		public void LineTo(FTVector to)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			Error err = FT.FT_Stroker_LineTo(Reference, ref to);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// ‘Draw’ a single quadratic Bézier in the stroker's current sub-path, from the last position.
		/// </summary>
		/// <remarks>
		/// You should call this function between <see cref="BeginSubPath"/> and <see cref="EndSubPath"/>.
		/// </remarks>
		/// <param name="control">A pointer to a Bézier control point.</param>
		/// <param name="to">A pointer to the destination point.</param>
		public void ConicTo(FTVector control, FTVector to)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			Error err = FT.FT_Stroker_ConicTo(Reference, ref control, ref to);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// ‘Draw’ a single cubic Bézier in the stroker's current sub-path, from the last position.
		/// </summary>
		/// <remarks>
		/// You should call this function between <see cref="BeginSubPath"/> and <see cref="EndSubPath"/>.
		/// </remarks>
		/// <param name="control1">A pointer to the first Bézier control point.</param>
		/// <param name="control2">A pointer to second Bézier control point.</param>
		/// <param name="to">A pointer to the destination point.</param>
		public void CubicTo(FTVector control1, FTVector control2, FTVector to)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			Error err = FT.FT_Stroker_CubicTo(Reference, ref control1, ref control2, ref to);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Call this function once you have finished parsing your paths with the stroker. It returns the number of
		/// points and contours necessary to export one of the ‘border’ or ‘stroke’ outlines generated by the stroker.
		/// </summary>
		/// <remarks><para>
		/// When an outline, or a sub-path, is ‘closed’, the stroker generates two independent ‘border’ outlines, named
		/// ‘left’ and ‘right’.
		/// </para><para>
		/// When the outline, or a sub-path, is ‘opened’, the stroker merges the ‘border’ outlines with caps. The
		/// ‘left’ border receives all points, while the ‘right’ border becomes empty.
		/// </para><para>
		/// Use the function <see cref="GetCounts"/> instead if you want to retrieve the counts associated to both
		/// borders.
		/// </para></remarks>
		/// <param name="border">The border index.</param>
		/// <param name="pointsCount">The number of points.</param>
		/// <param name="contoursCount">The number of contours.</param>
		[CLSCompliant(false)]
		public void GetBorderCounts(StrokerBorder border, out uint pointsCount, out uint contoursCount)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			Error err = FT.FT_Stroker_GetBorderCounts(Reference, border, out pointsCount, out contoursCount);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary><para>
		/// Call this function after <see cref="GetBorderCounts"/> to export the corresponding border to your own
		/// <see cref="Outline"/> structure.
		/// </para><para>
		/// Note that this function appends the border points and contours to your outline, but does not try to resize
		/// its arrays.
		/// </para></summary>
		/// <remarks><para>
		/// Always call this function after <see cref="GetBorderCounts"/> to get sure that there is enough room in your
		/// <see cref="Outline"/> object to receive all new data.
		/// </para><para>
		/// When an outline, or a sub-path, is ‘closed’, the stroker generates two independent ‘border’ outlines, named
		/// ‘left’ and ‘right’.
		/// </para><para>
		/// When the outline, or a sub-path, is ‘opened’, the stroker merges the ‘border’ outlines with caps. The
		/// ‘left’ border receives all points, while the ‘right’ border becomes empty.
		/// </para><para>
		/// Use the function <see cref="Export"/> instead if you want to retrieve all borders at once.
		/// </para></remarks>
		/// <param name="border">The border index.</param>
		/// <param name="outline">The target outline handle.</param>
		public void ExportBorder(StrokerBorder border, Outline outline)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			if (outline == null)
				throw new ArgumentNullException("outline");

			FT.FT_Stroker_ExportBorder(Reference, border, outline.Reference);
		}

		/// <summary>
		/// Call this function once you have finished parsing your paths with the stroker. It returns the number of
		/// points and contours necessary to export all points/borders from the stroked outline/path.
		/// </summary>
		/// <param name="pointsCount">The number of points.</param>
		/// <param name="contoursCount">The number of contours.</param>
		[CLSCompliant(false)]
		public void GetCounts(out uint pointsCount, out uint contoursCount)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			Error err = FT.FT_Stroker_GetCounts(Reference, out pointsCount, out contoursCount);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary><para>
		/// Call this function after <see cref="GetBorderCounts"/> to export all borders to your own
		/// <see cref="Outline"/> structure.
		/// </para><para>
		/// Note that this function appends the border points and contours to your outline, but does not try to resize
		/// its arrays.
		/// </para></summary>
		/// <param name="outline">The target outline handle.</param>
		public void Export(Outline outline)
		{
			if (disposed)
				throw new ObjectDisposedException("Stroker", "Cannot access a disposed object.");

			if (outline == null)
				throw new ArgumentNullException("outline");

			FT.FT_Stroker_Export(Reference, outline.Reference);
		}

		/// <summary>
		/// Disposes an instance of the <see cref="Stroker"/> class.
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
				
				FT.FT_Stroker_Done(reference);

				// removes itself from the parent Library, with a check to prevent this from happening when Library is
				// being disposed (Library disposes all it's children with a foreach loop, this causes an
				// InvalidOperationException for modifying a collection during enumeration)
				if (!parentLibrary.IsDisposed)
					parentLibrary.RemoveChildStroker(this);
			}
		}

		#endregion
	}
}
