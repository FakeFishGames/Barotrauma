#region MIT License
/*Copyright (c) 2012-2014, 2016 Robert Rouhani <robert.rouhani@gmail.com>

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
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SharpFont.Cache;
using SharpFont.Internal;
using SharpFont.TrueType;

namespace SharpFont
{
	/// <summary><para>
	/// A handle to a FreeType library instance. Each ‘library’ is completely independent from the others; it is the
	/// ‘root’ of a set of objects like fonts, faces, sizes, etc.
	/// </para><para>
	/// It also embeds a memory manager (see <see cref="Memory"/>), as well as a scan-line converter object (see
	/// <see cref="Raster"/>).
	/// </para><para>
	/// For multi-threading applications each thread should have its own <see cref="Library"/> object.
	/// </para></summary>
	public sealed class Library : IDisposable
	{
		#region Fields

		private IntPtr reference;

		private bool customMemory;
		private bool disposed;

		private List<Face> childFaces;
		private List<Glyph> childGlyphs;
		private List<Outline> childOutlines;
		private List<Stroker> childStrokers;
		private List<Manager> childManagers;

		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="Library"/> class.
		/// </summary>
		/// <remarks>
		/// SharpFont assumes that you have the correct version of FreeType for your operating system and processor
		/// architecture. If you get a <see cref="BadImageFormatException"/> here on Windows, there's a good chance
		/// that you're trying to run your program as a 64-bit process and have a 32-bit version of FreeType or vice
		/// versa. See the SharpFont.Examples project for how to handle this situation.
		/// </remarks>
		public Library()
			: this(false)
		{
			IntPtr libraryRef;
			Error err = FT.FT_Init_FreeType(out libraryRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			Reference = libraryRef;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Library"/> class.
		/// </summary>
		/// <param name="memory">A custom FreeType memory manager.</param>
		public Library(Memory memory)
			: this(false)
		{
			IntPtr libraryRef;
			Error err = FT.FT_New_Library(memory.Reference, out libraryRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			Reference = libraryRef;
			customMemory = true;
		}

		private Library(bool duplicate)
		{
			childFaces = new List<Face>();
			childGlyphs = new List<Glyph>();
			childOutlines = new List<Outline>();
			childStrokers = new List<Stroker>();
			childManagers = new List<Manager>();
		}

		/// <summary>
		/// Finalizes an instance of the <see cref="Library"/> class.
		/// </summary>
		~Library()
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
		/// Gets the version of the FreeType library being used.
		/// </summary>
		public Version Version
		{
			get
			{
				if (disposed)
					throw new ObjectDisposedException("Version", "Cannot access a disposed object.");

				int major, minor, patch;
				FT.FT_Library_Version(Reference, out major, out minor, out patch);
				return new Version(major, minor, patch);
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

		#region Methods

		#region Base Interface

		/// <summary>
		/// This function calls <see cref="OpenFace"/> to open a font by its pathname.
		/// </summary>
		/// <param name="path">A path to the font file.</param>
		/// <param name="faceIndex">The index of the face within the font. The first face has index 0.</param>
		/// <returns>
		/// A handle to a new face object. If ‘faceIndex’ is greater than or equal to zero, it must be non-NULL.
		/// </returns>
		/// <see cref="OpenFace"/>
		public Face NewFace(string path, int faceIndex)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			return new Face(this, path, faceIndex);
		}

		/// <summary>
		/// This function calls <see cref="OpenFace"/> to open a font which has been loaded into memory.
		/// </summary>
		/// <remarks>
		/// You must not deallocate the memory before calling <see cref="Face.Dispose()"/>.
		/// </remarks>
		/// <param name="file">A pointer to the beginning of the font data.</param>
		/// <param name="faceIndex">The index of the face within the font. The first face has index 0.</param>
		/// <returns>
		/// A handle to a new face object. If ‘faceIndex’ is greater than or equal to zero, it must be non-NULL.
		/// </returns>
		/// <see cref="OpenFace"/>
		public Face NewMemoryFace(byte[] file, int faceIndex)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			return new Face(this, file, faceIndex);
		}

		/// <summary>
		/// This function calls <see cref="OpenFace"/> to open a font which has been loaded into memory.
		/// </summary>
		/// <param name="bufferPtr">A pointer to the beginning of the font data.</param>
		/// <param name="length">Length of the buffer</param>
		/// <param name="faceIndex">The index of the face within the font. The first face has index 0.</param>
		/// <returns>
		/// A handle to a new face object. If ‘faceIndex’ is greater than or equal to zero, it must be non-NULL.
		/// </returns>
		public Face NewMemoryFace(IntPtr bufferPtr, int length, int faceIndex)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			return new Face(this, bufferPtr, length, faceIndex);
		}

		/// <summary>
		/// Create a <see cref="Face"/> object from a given resource described by <see cref="OpenArgs"/>.
		/// </summary>
		/// <remarks><para>
		/// Unlike FreeType 1.x, this function automatically creates a glyph slot for the face object which can be
		/// accessed directly through <see cref="Face.Glyph"/>.
		/// </para><para>
		/// OpenFace can be used to quickly check whether the font format of a given font resource is supported by
		/// FreeType. If the ‘faceIndex’ field is negative, the function's return value is 0 if the font format is
		/// recognized, or non-zero otherwise; the function returns a more or less empty face handle in ‘*aface’ (if
		/// ‘aface’ isn't NULL). The only useful field in this special case is <see cref="Face.FaceCount"/> which gives
		/// the number of faces within the font file. After examination, the returned <see cref="Face"/> structure
		/// should be deallocated with a call to <see cref="Face.Dispose()"/>.
		/// </para><para>
		/// Each new face object created with this function also owns a default <see cref="FTSize"/> object, accessible
		/// as <see cref="Face.Size"/>.
		/// </para><para>
		/// See the discussion of reference counters in the description of FT_Reference_Face.
		/// </para></remarks>
		/// <param name="args">
		/// A pointer to an <see cref="OpenArgs"/> structure which must be filled by the caller.
		/// </param>
		/// <param name="faceIndex">The index of the face within the font. The first face has index 0.</param>
		/// <returns>
		/// A handle to a new face object. If ‘faceIndex’ is greater than or equal to zero, it must be non-NULL.
		/// </returns>
		public Face OpenFace(OpenArgs args, int faceIndex)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			IntPtr faceRef;

			Error err = FT.FT_Open_Face(Reference, args.Reference, faceIndex, out faceRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return new Face(faceRef, this);
		}

		#endregion

#if !SHARPFONT_PLATFORM_IOS
		#region Mac Specific Interface

		/// <summary>
		/// Create a new face object from a FOND resource.
		/// </summary>
		/// <remarks>
		/// This function can be used to create <see cref="Face"/> objects from fonts that are installed in the system
		/// as follows.
		/// <code>
		/// fond = GetResource( 'FOND', fontName );
		/// error = FT_New_Face_From_FOND( library, fond, 0, &amp;face );
		/// </code>
		/// </remarks>
		/// <param name="fond">A FOND resource.</param>
		/// <param name="faceIndex">Only supported for the -1 ‘sanity check’ special case.</param>
		/// <returns>A handle to a new face object.</returns>
		public Face NewFaceFromFond(IntPtr fond, int faceIndex)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			IntPtr faceRef;

			Error err = FT.FT_New_Face_From_FOND(Reference, fond, faceIndex, out faceRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return new Face(faceRef, this);
		}

		/// <summary>
		/// Create a new face object from a given resource and typeface index using an FSSpec to the font file.
		/// </summary>
		/// <remarks>
		/// <see cref="NewFaceFromFSSpec"/> is identical to <see cref="NewFace"/> except it accepts an FSSpec instead
		/// of a path.
		/// </remarks>
		/// <param name="spec">FSSpec to the font file.</param>
		/// <param name="faceIndex">The index of the face within the resource. The first face has index 0.</param>
		/// <returns>A handle to a new face object.</returns>
		public Face NewFaceFromFSSpec(IntPtr spec, int faceIndex)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			IntPtr faceRef;

			Error err = FT.FT_New_Face_From_FSSpec(Reference, spec, faceIndex, out faceRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return new Face(faceRef, this);
		}

		/// <summary>
		/// Create a new face object from a given resource and typeface index using an FSRef to the font file.
		/// </summary>
		/// <remarks>
		/// <see cref="NewFaceFromFSRef"/> is identical to <see cref="NewFace"/> except it accepts an FSRef instead of
		/// a path.
		/// </remarks>
		/// <param name="ref">FSRef to the font file.</param>
		/// <param name="faceIndex">The index of the face within the resource. The first face has index 0.</param>
		/// <returns>A handle to a new face object.</returns>
		public Face NewFaceFromFSRef(IntPtr @ref, int faceIndex)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			IntPtr faceRef;

			Error err = FT.FT_New_Face_From_FSRef(Reference, @ref, faceIndex, out faceRef);

			if (err != Error.Ok)
				throw new FreeTypeException(err);

			return new Face(faceRef, this);
		}

		#endregion
#endif

		#region Module Management

		/// <summary>
		/// Add a new module to a given library instance.
		/// </summary>
		/// <remarks>
		/// An error will be returned if a module already exists by that name, or if the module requires a version of
		/// FreeType that is too great.
		/// </remarks>
		/// <param name="clazz">A pointer to class descriptor for the module.</param>
		public void AddModule(ModuleClass clazz)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			Error err = FT.FT_Add_Module(Reference, clazz.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Find a module by its name.
		/// </summary>
		/// <remarks>
		/// FreeType's internal modules aren't documented very well, and you should look up the source code for
		/// details.
		/// </remarks>
		/// <param name="moduleName">The module's name (as an ASCII string).</param>
		/// <returns>A module handle. 0 if none was found.</returns>
		public Module GetModule(string moduleName)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			return new Module(FT.FT_Get_Module(Reference, moduleName));
		}

		/// <summary>
		/// Remove a given module from a library instance.
		/// </summary>
		/// <remarks>
		/// The module object is destroyed by the function in case of success.
		/// </remarks>
		/// <param name="module">A handle to a module object.</param>
		public void RemoveModule(Module module)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			if (module == null)
				throw new ArgumentNullException("module");

			Error err = FT.FT_Remove_Module(Reference, module.Reference);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Set a property for a given module.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName"><para>The property name. Properties are described in the ‘Synopsis’ subsection
		/// of the module's documentation.
		/// </para><para>
		/// Note that only a few modules have properties.</para></param>
		/// <param name="value">A generic pointer to a variable or structure which gives the new value of the property.
		/// The exact definition of ‘value’ is dependent on the property; see the ‘Synopsis’ subsection of the module's
		/// documentation.</param>
		public void PropertySet(string moduleName, string propertyName, IntPtr value)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			Error err = FT.FT_Property_Set(Reference, moduleName, propertyName, value);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Set a property for a given module.
		/// </summary>
		/// <typeparam name="T">The type of property to set.</typeparam>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName"><para>The property name. Properties are described in the ‘Synopsis’ subsection
		/// of the module's documentation.
		/// </para><para>
		/// Note that only a few modules have properties.</para></param>
		/// <param name="value">A generic pointer to a variable or structure which gives the new value of the property.
		/// The exact definition of ‘value’ is dependent on the property; see the ‘Synopsis’ subsection of the module's
		/// documentation.</param>
		public void PropertySet<T>(string moduleName, string propertyName, ref T value)
			where T : struct
		{
			GCHandle gch = GCHandle.Alloc(value, GCHandleType.Pinned);
			PropertySet(moduleName, propertyName, gch.AddrOfPinnedObject());
			gch.Free();
		}

		/// <summary>
		/// Set a property for a given module.
		/// </summary>
		/// <typeparam name="T">The type of property to set.</typeparam>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName"><para>The property name. Properties are described in the ‘Synopsis’ subsection
		/// of the module's documentation.
		/// </para><para>
		/// Note that only a few modules have properties.</para></param>
		/// <param name="value">A generic pointer to a variable or structure which gives the new value of the property.
		/// The exact definition of ‘value’ is dependent on the property; see the ‘Synopsis’ subsection of the module's
		/// documentation.</param>
		public void PropertySet<T>(string moduleName, string propertyName, T value)
			where T : struct
		{
			PropertySet(moduleName, propertyName, ref value);
		}

		/// <summary>
		/// Set a property for a given module.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName"><para>The property name. Properties are described in the ‘Synopsis’ subsection
		/// of the module's documentation.
		/// </para><para>
		/// Note that only a few modules have properties.</para></param>
		/// <param name="value">A generic pointer to a variable or structure which gives the new value of the property.
		/// The exact definition of ‘value’ is dependent on the property; see the ‘Synopsis’ subsection of the module's
		/// documentation.</param>
		public void PropertySet(string moduleName, string propertyName, GlyphToScriptMapProperty value)
		{
			var rec = value.Rec;
			PropertySet(moduleName, propertyName, ref rec);
		}

		/// <summary>
		/// Set a property for a given module.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName"><para>The property name. Properties are described in the ‘Synopsis’ subsection
		/// of the module's documentation.
		/// </para><para>
		/// Note that only a few modules have properties.</para></param>
		/// <param name="value">A generic pointer to a variable or structure which gives the new value of the property.
		/// The exact definition of ‘value’ is dependent on the property; see the ‘Synopsis’ subsection of the module's
		/// documentation.</param>
		public void PropertySet(string moduleName, string propertyName, IncreaseXHeightProperty value)
		{
			var rec = value.Rec;
			PropertySet(moduleName, propertyName, ref rec);
		}

		/// <summary>
		/// Get a module's property value.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName">The property name. Properties are described in the ‘Synopsis’ subsection of the
		/// module's documentation.</param>
		/// <param name="value">A generic pointer to a variable or structure which gives the value of the property. The
		/// exact definition of ‘value’ is dependent on the property; see the ‘Synopsis’ subsection of the module's
		/// documentation.</param>
		public void PropertyGet(string moduleName, string propertyName, IntPtr value)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			Error err = FT.FT_Property_Get(Reference, moduleName, propertyName, value);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Get a module's property value.
		/// </summary>
		/// <typeparam name="T">The type of property to get.</typeparam>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName">The property name. Properties are described in the ‘Synopsis’ subsection of the
		/// module's documentation.</param>
		/// <param name="value">The value read from the module.</param>
		public void PropertyGet<T>(string moduleName, string propertyName, out T value)
			where T : struct
		{
			value = default(T);

			GCHandle gch = GCHandle.Alloc(value, GCHandleType.Pinned);
			PropertyGet(moduleName, propertyName, gch.AddrOfPinnedObject());
			value = PInvokeHelper.PtrToStructure<T>(gch.AddrOfPinnedObject());
			gch.Free();
		}

		/// <summary>
		/// Get a module's property value.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName">The property name. Properties are described in the ‘Synopsis’ subsection of the
		/// module's documentation.</param>
		/// <param name="value">The value read from the module.</param>
		[Obsolete("Use PropertyGetGlyphToScriptMap instead")]
		public void PropertyGet(string moduleName, string propertyName, out GlyphToScriptMapProperty value)
		{
			value = PropertyGetGlyphToScriptMap(moduleName, propertyName);
		}

		/// <summary>
		/// Get a module's property value.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName">The property name. Properties are described in the ‘Synopsis’ subsection of the
		/// module's documentation.</param>
		/// <param name="value">The value read from the module.</param>
		[Obsolete("Use PropertyGetIncreaseXHeight instead")]
		public void PropertyGet(string moduleName, string propertyName, out IncreaseXHeightProperty value)
		{
			value = PropertyGetIncreaseXHeight(moduleName, propertyName);
		}

		/// <summary>
		/// Gets a module's property value of the type <see cref="GlyphToScriptMapProperty"/>.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName">The property name. Properties are described in the ‘Synopsis’ subsection of the
		/// module's documentation.</param>
		/// <returns>The value read from the module.</returns>
		public GlyphToScriptMapProperty PropertyGetGlyphToScriptMap(string moduleName, string propertyName)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			GlyphToScriptMapPropertyRec rec;
			PropertyGet(moduleName, propertyName, out rec);

			Face face = childFaces.Find(f => f.Reference == rec.face);
			return new GlyphToScriptMapProperty(rec, face);
		}

		/// <summary>
		/// Gets a module's property value of the type <see cref="IncreaseXHeightProperty"/>.
		/// </summary>
		/// <param name="moduleName">The module name.</param>
		/// <param name="propertyName">The property name. Properties are described in the ‘Synopsis’ subsection of the
		/// module's documentation.</param>
		/// <returns>The value read from the module.</returns>
		public IncreaseXHeightProperty PropertyGetIncreaseXHeight(string moduleName, string propertyName)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			IncreaseXHeightPropertyRec rec;
			PropertyGet(moduleName, propertyName, out rec);

			Face face = childFaces.Find(f => f.Reference == rec.face);
			return new IncreaseXHeightProperty(rec, face);
		}

		/// <summary>
		/// Set a debug hook function for debugging the interpreter of a font format.
		/// </summary>
		/// <remarks><para>
		/// Currently, four debug hook slots are available, but only two (for the TrueType and the Type 1 interpreter)
		/// are defined.
		/// </para><para>
		/// Since the internal headers of FreeType are no longer installed, the symbol ‘FT_DEBUG_HOOK_TRUETYPE’ isn't
		/// available publicly. This is a bug and will be fixed in a forthcoming release.
		/// </para></remarks>
		/// <param name="hookIndex">The index of the debug hook. You should use the values defined in ‘ftobjs.h’, e.g.,
		/// ‘FT_DEBUG_HOOK_TRUETYPE’.</param>
		/// <param name="debugHook">The function used to debug the interpreter.</param>
		[CLSCompliant(false)]
		public void SetDebugHook(uint hookIndex, IntPtr debugHook)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			FT.FT_Set_Debug_Hook(Reference, hookIndex, debugHook);
		}

		/// <summary>
		/// Add the set of default drivers to a given library object. This is only useful when you create a library
		/// object with <see cref="Library(Memory)"/> (usually to plug a custom memory manager).
		/// </summary>
		public void AddDefaultModules()
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			FT.FT_Add_Default_Modules(Reference);
		}

		/// <summary>
		/// Retrieve the current renderer for a given glyph format.
		/// </summary>
		/// <remarks><para>
		/// An error will be returned if a module already exists by that name, or if the module requires a version of
		/// FreeType that is too great.
		/// </para><para>
		/// To add a new renderer, simply use <see cref="AddModule"/>. To retrieve a renderer by its name, use
		/// <see cref="GetModule"/>.
		/// </para></remarks>
		/// <param name="format">The glyph format.</param>
		/// <returns>A renderer handle. 0 if none found.</returns>
		[CLSCompliant(false)]
		public Renderer GetRenderer(GlyphFormat format)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			return new Renderer(FT.FT_Get_Renderer(Reference, format));
		}

		/// <summary>
		/// Set the current renderer to use, and set additional mode.
		/// </summary>
		/// <remarks><para>
		/// In case of success, the renderer will be used to convert glyph images in the renderer's known format into
		/// bitmaps.
		/// </para><para>
		/// This doesn't change the current renderer for other formats.
		/// </para><para>
		/// Currently, only the B/W renderer, if compiled with FT_RASTER_OPTION_ANTI_ALIASING (providing a 5-levels
		/// anti-aliasing mode; this option must be set directly in ‘ftraster.c’ and is undefined by default) accepts a
		/// single tag ‘pal5’ to set its gray palette as a character string with 5 elements. Consequently, the third
		/// and fourth argument are zero normally.
		/// </para></remarks>
		/// <param name="renderer">A handle to the renderer object.</param>
		/// <param name="numParams">The number of additional parameters.</param>
		/// <param name="parameters">Additional parameters.</param>
		[CLSCompliant(false)]
		public unsafe void SetRenderer(Renderer renderer, uint numParams, Parameter[] parameters)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			if (renderer == null)
				throw new ArgumentNullException("renderer");

			if (parameters == null)
				throw new ArgumentNullException("parameters");

			ParameterRec[] paramRecs = parameters.Select(x => x.Record).ToArray();
			fixed (void* ptr = paramRecs)
			{
				Error err = FT.FT_Set_Renderer(Reference, renderer.Reference, numParams, (IntPtr)ptr);

				if (err != Error.Ok)
					throw new FreeTypeException(err);
			}
		}

		#endregion

		#region LCD Filtering

		/// <summary>
		/// This function is used to apply color filtering to LCD decimated bitmaps, like the ones used when calling
		/// <see cref="GlyphSlot.RenderGlyph"/> with <see cref="RenderMode.Lcd"/> or
		/// <see cref="RenderMode.VerticalLcd"/>.
		/// </summary>
		/// <remarks><para>
		/// This feature is always disabled by default. Clients must make an explicit call to this function with a
		/// ‘filter’ value other than <see cref="LcdFilter.None"/> in order to enable it.
		/// </para><para>
		/// Due to <b>PATENTS</b> covering subpixel rendering, this function doesn't do anything except returning
		/// <see cref="Error.UnimplementedFeature"/> if the configuration macro FT_CONFIG_OPTION_SUBPIXEL_RENDERING is
		/// not defined in your build of the library, which should correspond to all default builds of FreeType.
		/// </para><para>
		/// The filter affects glyph bitmaps rendered through <see cref="GlyphSlot.RenderGlyph"/>,
		/// <see cref="Outline.GetBitmap(FTBitmap)"/>, <see cref="Face.LoadGlyph"/>, and <see cref="Face.LoadChar"/>.
		/// </para><para>
		/// It does not affect the output of <see cref="Outline.Render(RasterParams)"/> and
		/// <see cref="Outline.GetBitmap(FTBitmap)"/>.
		/// </para><para>
		/// If this feature is activated, the dimensions of LCD glyph bitmaps are either larger or taller than the
		/// dimensions of the corresponding outline with regards to the pixel grid. For example, for
		/// <see cref="RenderMode.Lcd"/>, the filter adds up to 3 pixels to the left, and up to 3 pixels to the right.
		/// </para><para>
		/// The bitmap offset values are adjusted correctly, so clients shouldn't need to modify their layout and glyph
		/// positioning code when enabling the filter.
		/// </para></remarks>
		/// <param name="filter"><para>
		/// The filter type.
		/// </para><para>
		/// You can use <see cref="LcdFilter.None"/> here to disable this feature, or <see cref="LcdFilter.Default"/>
		/// to use a default filter that should work well on most LCD screens.
		/// </para></param>
		public void SetLcdFilter(LcdFilter filter)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			Error err = FT.FT_Library_SetLcdFilter(Reference, filter);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Use this function to override the filter weights selected by <see cref="SetLcdFilter"/>. By default,
		/// FreeType uses the quintuple (0x00, 0x55, 0x56, 0x55, 0x00) for <see cref="LcdFilter.Light"/>, and (0x10,
		/// 0x40, 0x70, 0x40, 0x10) for <see cref="LcdFilter.Default"/> and <see cref="LcdFilter.Legacy"/>.
		/// </summary>
		/// <remarks><para>
		/// Due to <b>PATENTS</b> covering subpixel rendering, this function doesn't do anything except returning
		/// <see cref="Error.UnimplementedFeature"/> if the configuration macro FT_CONFIG_OPTION_SUBPIXEL_RENDERING is
		/// not defined in your build of the library, which should correspond to all default builds of FreeType.
		/// </para><para>
		/// This function must be called after <see cref="SetLcdFilter"/> to have any effect.
		/// </para></remarks>
		/// <param name="weights">
		/// A pointer to an array; the function copies the first five bytes and uses them to specify the filter
		/// weights.
		/// </param>
		public void SetLcdFilterWeights(byte[] weights)
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			if (weights == null)
				throw new ArgumentNullException("weights");

			Error err = FT.FT_Library_SetLcdFilterWeights(Reference, weights);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		#endregion

		#region The TrueType Engine

		/// <summary>
		/// Return an <see cref="EngineType"/> value to indicate which level of the TrueType virtual machine a given
		/// library instance supports.
		/// </summary>
		/// <returns>A value indicating which level is supported.</returns>
		public EngineType GetTrueTypeEngineType()
		{
			if (disposed)
				throw new ObjectDisposedException("Library", "Cannot access a disposed object.");

			return FT.FT_Get_TrueType_Engine_Type(Reference);
		}

		#endregion

		/// <summary>
		/// Disposes the Library.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		internal void AddChildFace(Face child)
		{
			childFaces.Add(child);
		}

		internal void RemoveChildFace(Face child)
		{
			childFaces.Remove(child);
		}

		internal void AddChildGlyph(Glyph child)
		{
			childGlyphs.Add(child);
		}

		internal void RemoveChildGlyph(Glyph child)
		{
			childGlyphs.Remove(child);
		}

		internal void AddChildOutline(Outline child)
		{
			childOutlines.Add(child);
		}

		internal void RemoveChildOutline(Outline child)
		{
			childOutlines.Remove(child);
		}

		internal void AddChildStroker(Stroker child)
		{
			childStrokers.Add(child);
		}

		internal void RemoveChildStroker(Stroker child)
		{
			childStrokers.Remove(child);
		}

		internal void AddChildManager(Manager child)
		{
			childManagers.Add(child);
		}

		internal void RemoveChildManager(Manager child)
		{
			childManagers.Remove(child);
		}

		private void Dispose(bool disposing)
		{
			if (!disposed)
			{
				disposed = true;

				//dipose all the children before disposing the library.
				foreach (Face f in childFaces)
					f.Dispose();

				foreach (Glyph g in childGlyphs)
					g.Dispose();

				foreach (Outline o in childOutlines)
					o.Dispose();

				foreach (Stroker s in childStrokers)
					s.Dispose();

				foreach (Manager m in childManagers)
					m.Dispose();

				childFaces.Clear();
				childGlyphs.Clear();
				childOutlines.Clear();
				childStrokers.Clear();
				childManagers.Clear();

				Error err = customMemory ? FT.FT_Done_Library(reference) : FT.FT_Done_FreeType(reference);
				reference = IntPtr.Zero;
			}
		}

		#endregion
	}
}
