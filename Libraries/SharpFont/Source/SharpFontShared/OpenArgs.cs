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
	/// A structure used to indicate how to open a new font file or stream. A pointer to such a structure can be used
	/// as a parameter for the functions <see cref="Library.OpenFace"/> and <see cref="Face.AttachStream"/>.
	/// </summary>
	/// <remarks>
	/// The stream type is determined by the contents of <see cref="Flags"/> which are tested in the following order by
	/// <see cref="Library.OpenFace"/>:
	/// <list type="bullet">
	/// <item><description>
	/// If the <see cref="OpenFlags.Memory"/> bit is set, assume that this is a memory file of <see cref="MemorySize"/>
	/// bytes, located at <see cref="MemoryBase"/>. The data are are not copied, and the client is responsible for
	/// releasing and destroying them after the corresponding call to <see cref="Face.Dispose()"/>.
	/// </description></item>
	/// <item><description>
	/// Otherwise, if the <see cref="OpenFlags.Stream"/> bit is set, assume that a custom input stream
	/// <see cref="Stream"/> is used.
	/// </description></item>
	/// <item><description>
	/// Otherwise, if the <see cref="OpenFlags.PathName"/> bit is set, assume that this is a normal file and use
	/// <see cref="PathName"/> to open it.
	/// </description></item>
	/// <item><description>
	/// If the <see cref="OpenFlags.Driver"/> bit is set, <see cref="Library.OpenFace"/> only tries to open the file
	/// with the driver whose handler is in <see cref="Driver"/>.
	/// </description></item>
	/// <item><description>
	/// If the <see cref="OpenFlags.Params"/> bit is set, the parameters given by <see cref="ParamsCount"/> and
	/// <see cref="Params"/> is used. They are ignored otherwise.
	/// </description></item>
	/// </list>
	/// Ideally, both the <see cref="PathName"/> and <see cref="Params"/> fields should be tagged as ‘const’; this is
	/// missing for API backwards compatibility. In other words, applications should treat them as read-only.
	/// </remarks>
	public sealed class OpenArgs
	{
		#region Fields

		private IntPtr reference;
		private OpenArgsRec rec;

		#endregion

		#region Constructors

		internal OpenArgs(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets a set of bit flags indicating how to use the structure.
		/// </summary>
		public OpenFlags Flags
		{
			get
			{
				return rec.flags;
			}
		}

		/// <summary>
		/// Gets the first byte of the file in memory.
		/// </summary>
		public IntPtr MemoryBase
		{
			get
			{
				return rec.memory_base;
			}
		}

		/// <summary>
		/// Gets the size in bytes of the file in memory.
		/// </summary>
		public int MemorySize
		{
			get
			{
				return (int)rec.memory_size;
			}
		}

		/// <summary>
		/// Gets a pointer to an 8-bit file pathname.
		/// </summary>
		public string PathName
		{
			get
			{
				return rec.pathname;
			}
		}

		/// <summary>
		/// Gets a handle to a source stream object.
		/// </summary>
		public FTStream Stream
		{
			get
			{
				return new FTStream(rec.stream);
			}
		}

		/// <summary>
		/// Gets the font driver to use to open the face. If set to 0, FreeType tries to load the face with each one of
		/// the drivers in its list.
		/// </summary>
		/// <remarks>This field is exclusively used by <see cref="Library.OpenFace"/>.</remarks>
		public Module Driver
		{
			get
			{
				return new Module(rec.driver);
			}
		}

		/// <summary>
		/// Gets the number of extra parameters.
		/// </summary>
		public int ParamsCount
		{
			get
			{
				return rec.num_params;
			}
		}

		/// <summary>
		/// Gets the extra parameters passed to the font driver when opening a new face.
		/// </summary>
		public Parameter[] Params
		{
			get
			{
				int count = ParamsCount;

				if (count == 0)
					return null;

				Parameter[] parameters = new Parameter[count];
				IntPtr array = rec.@params;

				for (int i = 0; i < count; i++)
				{
					parameters[i] = new Parameter(new IntPtr(array.ToInt64() + ParameterRec.SizeInBytes * i));
				}

				return parameters;
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
				rec = PInvokeHelper.PtrToStructure<OpenArgsRec>(reference);
			}
		}

		#endregion
	}
}
