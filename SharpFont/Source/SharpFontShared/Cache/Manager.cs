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
using System.Collections.Generic;

namespace SharpFont.Cache
{
	/// <summary><para>
	/// This object corresponds to one instance of the cache-subsystem. It is used to cache one or more
	/// <see cref="Face"/> objects, along with corresponding <see cref="FTSize"/> objects.
	/// </para><para>
	/// The manager intentionally limits the total number of opened <see cref="Face"/> and <see cref="FTSize"/> objects
	/// to control memory usage. See the ‘max_faces’ and ‘max_sizes’ parameters of
	/// <see cref="Manager(Library, uint, uint, ulong, FaceRequester, IntPtr)"/>.
	/// </para><para>
	/// The manager is also used to cache ‘nodes’ of various types while limiting their total memory usage.
	/// </para><para>
	/// All limitations are enforced by keeping lists of managed objects in most-recently-used order, and flushing old
	/// nodes to make room for new ones.
	/// </para></summary>
	public sealed class Manager : IDisposable
	{
		#region Fields

		private IntPtr reference;
		private Library parentLibrary;

		private bool disposed;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the Manager class.
		/// </summary>
		/// <param name="library">The parent FreeType library handle to use.</param>
		/// <param name="maxFaces">
		/// Maximum number of opened <see cref="Face"/> objects managed by this cache instance. Use 0 for defaults.
		/// </param>
		/// <param name="maxSizes">
		/// Maximum number of opened <see cref="FTSize"/> objects managed by this cache instance. Use 0 for defaults.
		/// </param>
		/// <param name="maxBytes">
		/// Maximum number of bytes to use for cached data nodes. Use 0 for defaults. Note that this value does not
		/// account for managed <see cref="Face"/> and <see cref="FTSize"/> objects.
		/// </param>
		/// <param name="requester">
		/// An application-provided callback used to translate face IDs into real <see cref="Face"/> objects.
		/// </param>
		/// <param name="requestData">
		/// A generic pointer that is passed to the requester each time it is called (see <see cref="FaceRequester"/>).
		/// </param>
		[CLSCompliant(false)]
		public Manager(Library library, uint maxFaces, uint maxSizes, ulong maxBytes, FaceRequester requester, IntPtr requestData)
		{
			if (library == null)
				throw new ArgumentNullException("library");

			IntPtr mgrRef;
			Error err = FT.FTC_Manager_New(library.Reference, maxFaces, maxSizes, maxBytes, requester, requestData, out mgrRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			Reference = mgrRef;

			this.parentLibrary = library;
			library.AddChildManager(this);
		}

		/// <summary>
		/// Finalizes an instance of the Manager class.
		/// </summary>
		~Manager()
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
			}
		}

		#endregion

		#region Public Members

		/// <summary>
		/// Empty a given cache manager. This simply gets rid of all the currently cached <see cref="Face"/> and
		/// <see cref="FTSize"/> objects within the manager.
		/// </summary>
		public void Reset()
		{
			if (disposed)
				throw new ObjectDisposedException("Manager", "Cannot access a disposed object.");

			FT.FTC_Manager_Reset(Reference);
		}

		/// <summary>
		/// Retrieve the <see cref="Face"/> object that corresponds to a given face ID through a cache manager.
		/// </summary>
		/// <remarks><para>
		/// The returned <see cref="Face"/> object is always owned by the manager. You should never try to discard it
		/// yourself.
		/// </para><para>
		/// The <see cref="Face"/> object doesn't necessarily have a current size object (i.e., <see cref="Face.Size"/>
		/// can be 0). If you need a specific ‘font size’, use <see cref="Manager.LookupSize"/> instead.
		/// </para><para>
		/// Never change the face's transformation matrix (i.e., never call the <see cref="Face.SetTransform()"/>
		/// function) on a returned face! If you need to transform glyphs, do it yourself after glyph loading.
		/// </para><para>
		/// When you perform a lookup, out-of-memory errors are detected within the lookup and force incremental
		/// flushes of the cache until enough memory is released for the lookup to succeed.
		/// </para><para>
		/// If a lookup fails with <see cref="Error.OutOfMemory"/> the cache has already been completely flushed, and
		/// still no memory was available for the operation.
		/// </para></remarks>
		/// <param name="faceId">The ID of the face object.</param>
		/// <returns>A handle to the face object.</returns>
		public Face LookupFace(IntPtr faceId)
		{
			if (disposed)
				throw new ObjectDisposedException("Manager", "Cannot access a disposed object.");

			IntPtr faceRef;
			Error err = FT.FTC_Manager_LookupFace(Reference, faceId, out faceRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			//HACK fix this later.
			return new Face(faceRef, null);
		}

		/// <summary>
		/// Retrieve the <see cref="FTSize"/> object that corresponds to a given <see cref="Scaler"/> pointer through a
		/// cache manager.
		/// </summary>
		/// <remarks><para>
		/// The returned <see cref="FTSize"/> object is always owned by the/ manager. You should never try to discard
		/// it by yourself.
		/// </para><para>
		/// You can access the parent <see cref="Face"/> object simply as <see cref="FTSize.Face"/> if you need it.
		/// Note that this object is also owned by the manager.
		/// </para><para>
		/// When you perform a lookup, out-of-memory errors are detected within the lookup and force incremental
		/// flushes of the cache until enough memory is released for the lookup to succeed.
		/// </para><para>
		/// If a lookup fails with <see cref="Error.OutOfMemory"/> the cache has already been completely flushed, and
		/// still no memory is available for the operation.
		/// </para></remarks>
		/// <param name="scaler">A scaler handle.</param>
		/// <returns>A handle to the size object.</returns>
		public FTSize LookupSize(Scaler scaler)
		{
			if (disposed)
				throw new ObjectDisposedException("Manager", "Cannot access a disposed object.");

			IntPtr sizeRef;
			Error err = FT.FTC_Manager_LookupSize(Reference, scaler.Reference, out sizeRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			//HACK fix this later.
			return new FTSize(sizeRef, false, null);
		}

		/// <summary>
		/// A special function used to indicate to the cache manager that a given FTC_FaceID is no longer valid, either
		/// because its content changed, or because it was deallocated or uninstalled.
		/// </summary>
		/// <remarks><para>
		/// This function flushes all nodes from the cache corresponding to this ‘faceID’, with the
		/// exception of nodes with a non-null reference count.
		/// </para><para>
		/// Such nodes are however modified internally so as to never appear in later lookups with the same
		///  ‘faceID’ value, and to be immediately destroyed when released by all their users.
		/// </para></remarks>
		/// <param name="faceId">The FTC_FaceID to be removed.</param>
		public void RemoveFaceId(IntPtr faceId)
		{
			if (disposed)
				throw new ObjectDisposedException("Manager", "Cannot access a disposed object.");

			FT.FTC_Manager_RemoveFaceID(Reference, faceId);
		}

		#endregion

		#region IDisposable Members

		/// <summary>
		/// Disposes the Manager.
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

				FT.FTC_Manager_Done(reference);
				reference = IntPtr.Zero;

				// removes itself from the parent Library, with a check to prevent this from happening when Library is
				// being disposed (Library disposes all it's children with a foreach loop, this causes an
				// InvalidOperationException for modifying a collection during enumeration)
				if (!parentLibrary.IsDisposed)
					parentLibrary.RemoveChildManager(this);
			}
		}

		#endregion
	}
}
