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
	/// An <see cref="FTList"/> iterator function which is called during a list parse by <see cref="FTList.Iterate"/>.
	/// </summary>
	/// <param name="node">The current iteration list node.</param>
	/// <param name="user">
	/// A typeless pointer passed to <see cref="ListIterator"/>. Can be used to point to the iteration's state.
	/// </param>
	/// <returns>Error code.</returns>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate Error ListIterator(NativeReference<ListNode> node, IntPtr user);

	/// <summary>
	/// An <see cref="FTList"/> iterator function which is called during a list finalization by
	/// <see cref="FTList.Finalize"/> to destroy all elements in a given list.
	/// </summary>
	/// <param name="memory">The current system object.</param>
	/// <param name="data">The current object to destroy.</param>
	/// <param name="user">
	/// A typeless pointer passed to <see cref="FTList.Iterate"/>. It can be used to point to the iteration's state.
	/// </param>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void ListDestructor(NativeReference<Memory> memory, IntPtr data, IntPtr user);

	/// <summary>
	/// A structure used to hold a simple doubly-linked list. These are used in many parts of FreeType.
	/// </summary>
	public sealed class FTList
	{
		#region Fields

		private IntPtr reference;
		private ListRec rec;

		#endregion

		#region Constructors

		internal FTList(IntPtr reference)
		{
			Reference = reference;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the head (first element) of doubly-linked list.
		/// </summary>
		public ListNode Head
		{
			get
			{
				return new ListNode(rec.head);
			}
		}

		/// <summary>
		/// Gets the tail (last element) of doubly-linked list.
		/// </summary>
		public ListNode Tail
		{
			get
			{
				return new ListNode(rec.tail);
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
				rec = PInvokeHelper.PtrToStructure<ListRec>(reference);
			}
		}

		#endregion

		#region Methods

		/// <summary>
		/// Find the list node for a given listed object.
		/// </summary>
		/// <param name="data">The address of the listed object.</param>
		/// <returns>List node. NULL if it wasn't found.</returns>
		public ListNode Find(IntPtr data)
		{
			return new ListNode(FT.FT_List_Find(Reference, data));
		}

		/// <summary>
		/// Append an element to the end of a list.
		/// </summary>
		/// <param name="node">The node to append.</param>
		public void Add(ListNode node)
		{
			FT.FT_List_Add(Reference, node.Reference);
		}

		/// <summary>
		/// Insert an element at the head of a list.
		/// </summary>
		/// <param name="node">The node to insert.</param>
		public void Insert(ListNode node)
		{
			FT.FT_List_Insert(Reference, node.Reference);
		}

		/// <summary>
		/// Remove a node from a list. This function doesn't check whether the node is in the list!
		/// </summary>
		/// <param name="node">The node to remove.</param>
		public void Remove(ListNode node)
		{
			FT.FT_List_Remove(Reference, node.Reference);
		}

		/// <summary>
		/// Move a node to the head/top of a list. Used to maintain LRU lists.
		/// </summary>
		/// <param name="node">The node to move.</param>
		public void Up(ListNode node)
		{
			FT.FT_List_Up(Reference, node.Reference);
		}

		/// <summary>
		/// Parse a list and calls a given iterator function on each element. Note that parsing is stopped as soon as
		/// one of the iterator calls returns a non-zero value.
		/// </summary>
		/// <param name="iterator">An iterator function, called on each node of the list.</param>
		/// <param name="user">A user-supplied field which is passed as the second argument to the iterator.</param>
		public void Iterate(ListIterator iterator, IntPtr user)
		{
			Error err = FT.FT_List_Iterate(Reference, iterator, user);

			if (err != Error.Ok)
				throw new FreeTypeException(err);
		}

		/// <summary>
		/// Destroy all elements in the list as well as the list itself.
		/// </summary>
		/// <remarks>
		/// This function expects that all nodes added by <see cref="Add"/> or <see cref="Insert"/> have been
		/// dynamically allocated.
		/// </remarks>
		/// <param name="destroy">A list destructor that will be applied to each element of the list.</param>
		/// <param name="memory">The current memory object which handles deallocation.</param>
		/// <param name="user">A user-supplied field which is passed as the last argument to the destructor.</param>
		public void Finalize(ListDestructor destroy, Memory memory, IntPtr user)
		{
			FT.FT_List_Finalize(Reference, destroy, memory.Reference, user);
		}

		#endregion
	}
}
