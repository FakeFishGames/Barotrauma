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

namespace SharpFont.Cache
{
	/// <summary>
	/// A callback function provided by client applications. It is used by the cache manager to translate a given
	/// FTC_FaceID into a new valid <see cref="Face"/> object, on demand.
	/// </summary>
	/// <remarks><para>
	/// The third parameter ‘req_data’ is the same as the one passed by the client when
	/// <see cref="Manager(Library, uint, uint, ulong, FaceRequester, IntPtr)"/> is called.
	/// </para><para>
	/// The face requester should not perform funny things on the returned face object, like creating a new
	/// <see cref="FTSize"/> for it, or setting a transformation through <see cref="Face.SetTransform()"/>!
	/// </para></remarks>
	/// <param name="faceId">The face ID to resolve.</param>
	/// <param name="library">A handle to a FreeType library object.</param>
	/// <param name="requestData">Application-provided request data (see note below).</param>
	/// <param name="aface">A new <see cref="Face"/> handle.</param>
	/// <returns>FreeType error code. 0 means success.</returns>
	public delegate Error FaceRequester(IntPtr faceId, IntPtr library, IntPtr requestData, out IntPtr aface);
}
