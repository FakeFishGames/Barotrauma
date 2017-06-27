#region MIT License
/*Copyright (c) 2012-2013, 2015-2016 Robert Rouhani <robert.rouhani@gmail.com>

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
	/// FreeType root size class structure. A size object models a face object at a given size.
	/// </summary>
	public sealed class FTSize : IDisposable
	{
		#region Fields

		private bool userAlloc;
		private bool disposed;
		private bool duplicate;

		private IntPtr reference;
		private SizeRec rec;

		private Face parentFace;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="FTSize"/> class.
		/// </summary>
		/// <param name="parent">The parent face.</param>
		public FTSize(Face parent)
		{
			IntPtr reference;
			Error err = FT.FT_New_Size(parent.Reference, out reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			Reference = reference;
			userAlloc = true;
		}

		internal FTSize(IntPtr reference, bool userAlloc, Face parentFace)
		{
			Reference = reference;

			this.userAlloc = userAlloc;

			if (parentFace != null)
			{
				this.parentFace = parentFace;
				parentFace.AddChildSize(this);
			}
			else
			{
				duplicate = true;
			}
		}

		/// <summary>
		/// Finalizes an instance of the FTSize class.
		/// </summary>
		~FTSize()
		{
			Dispose(false);
		}

		#endregion

		#region Events

		/// <summary>
		/// Occurs when the size is disposed.
		/// </summary>
		public event EventHandler Disposed;

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
		/// Gets a handle to the parent face object.
		/// </summary>
		public Face Face
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Face", "Cannot access a disposed object.");

				return parentFace;
			}
		}

		/// <summary>
		/// Gets or sets a typeless pointer, which is unused by the FreeType library or any of its drivers. It can be used by
		/// client applications to link their own data to each size object.
		/// </summary>
		[Obsolete("Use the Tag property and Disposed event instead.")]
		public Generic Generic
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Generic", "Cannot access a disposed object.");

				return new Generic(rec.generic);
			}

			set
			{
				if (disposed)
					throw new ObjectDisposedException("Generic", "Cannot access a disposed object.");

				value.WriteToUnmanagedMemory(PInvokeHelper.AbsoluteOffsetOf<SizeRec>(Reference, "generic"));
				Reference = reference; //update rec.
			}
		}

		/// <summary>
		/// Gets metrics for this size object. This field is read-only.
		/// </summary>
		public SizeMetrics Metrics
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Metrics", "Cannot access a disposed object.");

				return new SizeMetrics(rec.metrics);
			}
		}

		/// <summary>
		/// Gets or sets an object used to identify this instance of <see cref="FTSize"/>. This object will not be
		/// modified or accessed internally.
		/// </summary>
		/// <remarks>
		/// This is a replacement for FT_Generic in FreeType. If you are retrieving the same object multiple times
		/// from functions, this object will not appear in new copies.
		/// </remarks>
		public object Tag { get; set; }

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
				this.rec = PInvokeHelper.PtrToStructure<SizeRec>(reference);
			}
		}

		#endregion

		#region Public Methods

		/// <summary><para>
		/// Even though it is possible to create several size objects for a given face (see
		/// <see cref="SharpFont.Face.NewSize"/> for details), functions like <see cref="SharpFont.Face.LoadGlyph"/> or
		/// <see cref="SharpFont.Face.LoadChar"/> only use the one which has been activated last to determine the
		/// ‘current character pixel size’.
		/// </para><para>
		/// This function can be used to ‘activate’ a previously created size object.
		/// </para></summary>
		/// <remarks>
		/// If ‘face’ is the size's parent face object, this function changes the value of ‘face->size’ to the input
		/// size handle.
		/// </remarks>
		public void Activate()
		{
			if (disposed)
				throw new ObjectDisposedException("Activate", "Cannot access a disposed object.");

			Error err = FT.FT_Activate_Size(Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Diposes the FTSize.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion

		#region Private Methods

		private void Dispose(bool disposing)
		{
			if (!disposed)
			{
				disposed = true;

				//only dispose the user allocated sizes that are not duplicates.
				if (userAlloc && !duplicate)
				{
					FT.FT_Done_Size(reference);
				}

				// removes itself from the parent Face, with a check to prevent this from happening when Face is
				// being disposed (Face disposes all it's children with a foreach loop, this causes an
				// InvalidOperationException for modifying a collection during enumeration)
				if (parentFace != null && !parentFace.IsDisposed)
					parentFace.RemoveChildSize(this);

				reference = IntPtr.Zero;
				rec = new SizeRec();

				EventHandler handler = Disposed;
				if (handler != null)
					handler(this, EventArgs.Empty);
			}
		}

		#endregion
	}
}
