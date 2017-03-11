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
	/// <summary><para>
	/// A function pointer type used to describe the signature of a ‘move to’ function during outline
	/// walking/decomposition.
	/// </para><para>
	/// A ‘move to’ is emitted to start a new contour in an outline.
	/// </para></summary>
	/// <param name="to">A pointer to the target point of the ‘move to’.</param>
	/// <param name="user">A typeless pointer which is passed from the caller of the decomposition function.</param>
	/// <returns>Error code. 0 means success.</returns>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int MoveToFunc(ref FTVector to, IntPtr user);

	/// <summary><para>
	/// A function pointer type used to describe the signature of a ‘line to’ function during outline
	/// walking/decomposition.
	/// </para><para>
	/// A ‘line to’ is emitted to indicate a segment in the outline.
	/// </para></summary>
	/// <param name="to">A pointer to the target point of the ‘line to’.</param>
	/// <param name="user">A typeless pointer which is passed from the caller of the decomposition function.</param>
	/// <returns>Error code. 0 means success.</returns>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int LineToFunc(ref FTVector to, IntPtr user);

	/// <summary><para>
	/// A function pointer type used to describe the signature of a ‘conic to’ function during outline walking or
	/// decomposition.
	/// </para><para>
	/// A ‘conic to’ is emitted to indicate a second-order Bézier arc in the outline.
	/// </para></summary>
	/// <param name="control">
	/// An intermediate control point between the last position and the new target in ‘to’.
	/// </param>
	/// <param name="to">A pointer to the target end point of the conic arc.</param>
	/// <param name="user">A typeless pointer which is passed from the caller of the decomposition function.</param>
	/// <returns>Error code. 0 means success.</returns>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int ConicToFunc(ref FTVector control, ref FTVector to, IntPtr user);

	/// <summary><para>
	/// A function pointer type used to describe the signature of a ‘cubic to’ function during outline walking or
	/// decomposition.
	/// </para><para>
	/// A ‘cubic to’ is emitted to indicate a third-order Bézier arc.
	/// </para></summary>
	/// <param name="control1">A pointer to the first Bézier control point.</param>
	/// <param name="control2">A pointer to the second Bézier control point.</param>
	/// <param name="to">A pointer to the target end point.</param>
	/// <param name="user">A typeless pointer which is passed from the caller of the decomposition function.</param>
	/// <returns>Error code. 0 means success.</returns>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate int CubicToFunc(ref FTVector control1, ref FTVector control2, ref FTVector to, IntPtr user);

	/// <summary>
	/// A structure to hold various function pointers used during outline decomposition in order to emit segments,
	/// conic, and cubic Béziers.
	/// </summary>
	/// <remarks>
	/// The point coordinates sent to the emitters are the transformed version of the original coordinates (this is
	/// important for high accuracy during scan-conversion). The transformation is simple:
	/// <code>
	/// x' = (x &lt;&lt; shift) - delta
	/// y' = (x &lt;&lt; shift) - delta
	/// </code>
	/// Set the values of ‘shift’ and ‘delta’ to 0 to get the original point coordinates.
	/// </remarks>
	public class OutlineFuncs : IDisposable
	{
		#region Fields

		private bool isDisposed;

		private MoveToFunc moveToFunc;
		private LineToFunc lineToFunc;
		private ConicToFunc conicToFunc;
		private CubicToFunc cubicToFunc;

		private GCHandle moveToPin;
		private GCHandle lineToPin;
		private GCHandle conicToPin;
		private GCHandle cubicToPin;

		private IntPtr moveToPtr;
		private IntPtr lineToPtr;
		private IntPtr conicToPtr;
		private IntPtr cubicToPtr;

		private int shift;
		private IntPtr delta;


		#endregion

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the OutlineFuncs class.
		/// </summary>
		public OutlineFuncs()
		{
		}

		/// <summary>
		/// Initializes a new instance of the OutlineFuncs class.
		/// </summary>
		/// <param name="moveTo">The move to delegate.</param>
		/// <param name="lineTo">The line to delegate.</param>
		/// <param name="conicTo">The conic to delegate.</param>
		/// <param name="cubicTo">The cubic to delegate.</param>
		/// <param name="shift">A value to shift by.</param>
		/// <param name="delta">A delta to transform by.</param>
		/// <exception cref="ArgumentNullException"><paramref name="moveTo"/>, <paramref name="lineTo"/>,
		/// <paramref name="conicTo"/>, or <paramref name="cubicTo"/> is <see langword="null" />.</exception>
		public OutlineFuncs(MoveToFunc moveTo, LineToFunc lineTo, ConicToFunc conicTo, CubicToFunc cubicTo, int shift, int delta)
		{
			if (moveTo == null)
			{
				throw new ArgumentNullException(nameof(moveTo));
			}
			if (lineTo == null)
			{
				throw new ArgumentNullException(nameof(lineTo));
			}
			if (conicTo == null)
			{
				throw new ArgumentNullException(nameof(conicTo));
			}
			if (cubicTo == null)
			{
				throw new ArgumentNullException(nameof(cubicTo));
			}

			moveToFunc = moveTo;
			lineToFunc = lineTo;
			conicToFunc = conicTo;
			cubicToFunc = cubicTo;

			moveToPtr = Marshal.GetFunctionPointerForDelegate(moveToFunc);
			lineToPtr = Marshal.GetFunctionPointerForDelegate(lineToFunc);
			conicToPtr = Marshal.GetFunctionPointerForDelegate(conicToFunc);
			cubicToPtr = Marshal.GetFunctionPointerForDelegate(cubicToFunc);

			this.shift = shift;
			this.delta = (IntPtr) delta;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the ‘move to’ emitter.
		/// </summary>
		public MoveToFunc MoveFunction
		{
			get
			{
				ThrowIfDisposed();
				return moveToFunc;
			}
			set
			{
				ThrowIfDisposed();
				if (moveToPin.IsAllocated)
				{
					moveToPin.Free();
				}
				moveToFunc = value;
				moveToPin = GCHandle.Alloc(moveToFunc);
				moveToPtr = Marshal.GetFunctionPointerForDelegate(moveToFunc);
			}
		}

		/// <summary>
		/// Gets or sets the segment emitter.
		/// </summary>
		public LineToFunc LineFunction
		{

			get
			{
				ThrowIfDisposed();
				return lineToFunc;
			}
			set
			{
				ThrowIfDisposed();
				if (lineToPin.IsAllocated)
				{
					lineToPin.Free();
				}
				lineToFunc = value;
				lineToPin = GCHandle.Alloc(lineToFunc);
				lineToPtr = Marshal.GetFunctionPointerForDelegate(lineToFunc);
			}
		}

		/// <summary>
		/// Gets or sets the second-order Bézier arc emitter.
		/// </summary>
		public ConicToFunc ConicFunction
		{

			get
			{
				ThrowIfDisposed();
				return conicToFunc;
			}
			set
			{
				ThrowIfDisposed();
				if (conicToPin.IsAllocated)
				{
					conicToPin.Free();
				}
				conicToFunc = value;
				conicToPin = GCHandle.Alloc(conicToFunc);
				conicToPtr = Marshal.GetFunctionPointerForDelegate(conicToFunc);
			}
		}

		/// <summary>
		/// Gets or sets the third-order Bézier arc emitter.
		/// </summary>
		public CubicToFunc CubicFunction
		{

			get
			{
				ThrowIfDisposed();
				return cubicToFunc;
			}
			set
			{
				ThrowIfDisposed();
				if (cubicToPin.IsAllocated)
				{
					cubicToPin.Free();
				}
				cubicToFunc = value;
				cubicToPin = GCHandle.Alloc(cubicToFunc);
				cubicToPtr = Marshal.GetFunctionPointerForDelegate(cubicToFunc);
			}
		}

		/// <summary>
		/// Gets or sets the shift that is applied to coordinates before they are sent to the emitter.
		/// </summary>
		public int Shift
		{
			get
			{
				ThrowIfDisposed();
				return shift;
			}

			set
			{
				ThrowIfDisposed();
				shift = value;
			}
		}

		/// <summary>
		/// Gets the delta that is applied to coordinates before they are sent to the emitter, but after the
		/// shift.
		/// </summary>
		public int Delta
		{
			get
			{
				ThrowIfDisposed();
				return (int) delta;
			}

			/*set
			{
				funcsInt.delta = (IntPtr)value;
			}*/
		}

		//TODO make a reference parameter instead?
		//HACK this copies the struct
		internal OutlineFuncsRec Record
		{
			get
			{
				ThrowIfDisposed();
				var r = new OutlineFuncsRec();
				r.moveTo = moveToPtr;
				r.lineTo = lineToPtr;
				r.conicTo = conicToPtr;
				r.cubicTo = cubicToPtr;
				return r;
			}
		}

		#endregion


		#region IDisposable

		/// <summary>
		/// Helper method to throw an exception if the object is disposed.
		/// </summary>
		/// <exception cref="ObjectDisposedException">If the object is already disposed</exception>
		private void ThrowIfDisposed()
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException("OutlineFuncs", "The outline funcs has already been disposed.");
			}
		}

		/// <summary>
		/// Finalizer which ensures that the pinned delegates are released.
		/// </summary>
		~OutlineFuncs()
		{
			Dispose(false);
		}

		/// <summary>
		///		Disposes this outline funcs, releasing any of the resources held by it.
		/// </summary>
		/// <param name="disposing"></param>
		protected virtual void Dispose(bool disposing)
		{
			if (isDisposed)
			{
				return;
			}

			if (disposing)
			{
				// If this class later needs any managed resources, they should be included here.
			}
			if (moveToPin.IsAllocated)
			{
				moveToPin.Free();
			}
			if (lineToPin.IsAllocated)
			{
				lineToPin.Free();
			}
			if (cubicToPin.IsAllocated)
			{
				cubicToPin.Free();
			}
			if (conicToPin.IsAllocated)
			{
				conicToPin.Free();
			}
			isDisposed = true;

		}

		/// <summary>
		///  Releases the pinned memory on this object.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		#endregion



	}
}
