using System;

namespace SharpFont
{
	/// <summary>
	/// Provide a consistent means for using pointers as references.
	/// </summary>
	public abstract class NativeObject
	{
		private IntPtr reference;

		/// <summary>
		/// Construct a new NativeObject and assign the reference.
		/// </summary>
		/// <param name="reference"></param>
		protected NativeObject(IntPtr reference)
		{
			this.reference = reference;
		}

		internal virtual IntPtr Reference
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
	}
}
