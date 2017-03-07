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

namespace SharpFont.Cache
{
	/// <summary>
	/// An opaque handle used to model a charmap cache. This cache is to hold character codes -> glyph indices
	/// mappings.
	/// </summary>
	public class CMapCache
	{
		#region Fields

		private IntPtr reference;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="CMapCache"/> class.
		/// </summary>
		/// <remarks>
		/// Like all other caches, this one will be destroyed with the cache manager.
		/// </remarks>
		/// <param name="manager">A handle to the cache manager.</param>
		public CMapCache(Manager manager)
		{
			IntPtr cacheRef;
			Error err = FT.FTC_CMapCache_New(manager.Reference, out cacheRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			Reference = cacheRef;
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
		/// Translate a character code into a glyph index, using the charmap cache.
		/// </summary>
		/// <param name="faceId">The source face ID.</param>
		/// <param name="cmapIndex">
		/// The index of the charmap in the source face. Any negative value means to use the cache <see cref="Face"/>'s
		/// default charmap.
		/// </param>
		/// <param name="charCode">The character code (in the corresponding charmap).</param>
		/// <returns>Glyph index. 0 means ‘no glyph’.</returns>
		[CLSCompliant(false)]
		public uint Lookup(IntPtr faceId, int cmapIndex, uint charCode)
		{
			return FT.FTC_CMapCache_Lookup(Reference, faceId, cmapIndex, charCode);
		}

		#endregion
	}
}
