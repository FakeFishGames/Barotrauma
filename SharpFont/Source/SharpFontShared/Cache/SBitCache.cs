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

namespace SharpFont.Cache
{
	/// <summary>
	/// A handle to a small bitmap cache. These are special cache objects used to store small glyph bitmaps (and
	/// anti-aliased pixmaps) in a much more efficient way than the traditional glyph image cache implemented by
	/// <see cref="ImageCache"/>.
	/// </summary>
	public class SBitCache
	{
		#region Fields

		private IntPtr reference;
		private Manager parentManager;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="SBitCache"/> class.
		/// </summary>
		/// <param name="manager">A handle to the source cache manager.</param>
		public SBitCache(Manager manager)
		{
			if (manager == null)
				throw new ArgumentNullException("manager");

			IntPtr cacheRef;
			Error err = FT.FTC_SBitCache_New(manager.Reference, out cacheRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			Reference = cacheRef;
			parentManager = manager;
		}

		#endregion

		#region Properties

		internal IntPtr Reference
		{
			get
			{
				return reference;
			}

			set
			{
				reference = value;
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Look up a given small glyph bitmap in a given sbit cache and ‘lock’ it to prevent its flushing from the
		/// cache until needed.
		/// </summary>
		/// <remarks><para>
		/// The small bitmap descriptor and its bit buffer are owned by the cache and should never be freed by the
		/// application. They might as well disappear from memory on the next cache lookup, so don't treat them as
		/// persistent data.
		/// </para><para>
		/// The descriptor's ‘buffer’ field is set to 0 to indicate a missing glyph bitmap.
		/// </para><para>
		/// If ‘node’ is not NULL, it receives the address of the cache node containing the bitmap, after
		/// increasing its reference count. This ensures that the node (as well as the image) will always be kept in
		/// the cache until you call <see cref="Node.Unref"/> to ‘release’ it.
		/// </para><para>
		/// If ‘node’ is NULL, the cache node is left unchanged, which means that the bitmap could be
		/// flushed out of the cache on the next call to one of the caching sub-system APIs. Don't assume that it is
		/// persistent!
		/// </para></remarks>
		/// <param name="type">A pointer to the glyph image type descriptor.</param>
		/// <param name="gIndex">The glyph index.</param>
		/// <param name="node">
		/// Used to return the address of of the corresponding cache node after incrementing its reference count (see
		/// note below).
		/// </param>
		/// <returns>A handle to a small bitmap descriptor.</returns>
		[CLSCompliant(false)]
		public SBit Lookup(ImageType type, uint gIndex, out Node node)
		{
			if (parentManager.IsDisposed)
				throw new ObjectDisposedException("Reference", "Cannot access a disposed object.");

			IntPtr sbitRef, nodeRef;
			Error err = FT.FTC_SBitCache_Lookup(Reference, type.Reference, gIndex, out sbitRef, out nodeRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			node = new Node(nodeRef);
			return new SBit(sbitRef);
		}

		/// <summary>
		/// A variant of <see cref="SBitCache.Lookup"/> that uses a <see cref="Scaler"/> to specify the face ID and its
		/// size.
		/// </summary>
		/// <remarks><para>
		/// The small bitmap descriptor and its bit buffer are owned by the cache and should never be freed by the
		/// application. They might as well disappear from memory on the next cache lookup, so don't treat them as
		/// persistent data.
		/// </para><para>
		/// The descriptor's ‘buffer’ field is set to 0 to indicate a missing glyph bitmap.
		/// </para><para>
		/// If ‘node’ is not NULL, it receives the address of the cache node containing the bitmap, after
		/// increasing its reference count. This ensures that the node (as well as the image) will always be kept in
		/// the cache until you call <see cref="Node.Unref"/> to ‘release’ it.
		/// </para><para>
		/// If ‘node’ is NULL, the cache node is left unchanged, which means that the bitmap could be
		/// flushed out of the cache on the next call to one of the caching sub-system APIs. Don't assume that it is
		/// persistent!
		/// </para></remarks>
		/// <param name="scaler">A pointer to the scaler descriptor.</param>
		/// <param name="loadFlags">The corresponding load flags.</param>
		/// <param name="gIndex">The glyph index.</param>
		/// <param name="node">
		/// Used to return the address of of the corresponding cache node after incrementing its reference count (see
		/// note below).
		/// </param>
		/// <returns>A handle to a small bitmap descriptor.</returns>
		[CLSCompliant(false)]
		public SBit LookupScaler(Scaler scaler, LoadFlags loadFlags, uint gIndex, out Node node)
		{
			if (parentManager.IsDisposed)
				throw new ObjectDisposedException("Reference", "Cannot access a disposed object.");

			IntPtr sbitRef, nodeRef;
			Error err = FT.FTC_SBitCache_LookupScaler(Reference, scaler.Reference, loadFlags, gIndex, out sbitRef, out nodeRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			node = new Node(nodeRef);
			return new SBit(sbitRef);
		}

		#endregion
	}
}
