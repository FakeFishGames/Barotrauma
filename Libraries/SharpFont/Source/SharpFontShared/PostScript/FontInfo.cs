#region MIT License
/*Copyright (c) 2012-2013, 2016 Robert Rouhani <robert.rouhani@gmail.com>

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

using SharpFont.PostScript.Internal;

namespace SharpFont.PostScript
{
	/// <summary>
	/// A structure used to model a Type 1 or Type 2 FontInfo dictionary. Note that for Multiple Master fonts, each
	/// instance has its own FontInfo dictionary.
	/// </summary>
	public class FontInfo
	{
		#region Fields

		private FontInfoRec rec;

		#endregion

		#region Constructors

		internal FontInfo(FontInfoRec rec)
		{
			this.rec = rec;
		}

		#endregion

		#region Properties

		/// <summary>
		/// The version of the font.
		/// </summary>
		public string Version
		{
			get
			{
				return rec.version;
			}
		}

		/// <summary>
		/// The copyright notice for the font.
		/// </summary>
		public string Notice
		{
			get
			{
				return rec.notice;
			}
		}

		/// <summary>
		/// Gets the font's full name.
		/// </summary>
		public string FullName
		{
			get
			{
				return rec.full_name;
			}
		}

		/// <summary>
		/// Gets the font's family name.
		/// </summary>
		public string FamilyName
		{
			get
			{
				return rec.family_name;
			}
		}

		/// <summary>
		/// Gets the weight description of the font
		/// </summary>
		public string Weight
		{
			get
			{
				return rec.weight;
			}
		}

		/// <summary>
		/// Gets italic angle of the font.
		/// </summary>
		public int ItalicAngle
		{
			get
			{
				return (int)rec.italic_angle;
			}
		}

		/// <summary>
		/// Gets whether the font is fixed pitch.
		/// </summary>
		public bool IsFixedPitch
		{
			get
			{
				return rec.is_fixed_pitch == 1;
			}
		}

		/// <summary>
		/// Gets the position of the  underline.
		/// </summary>
		public short UnderlinePosition
		{
			get
			{
				return rec.underline_position;
			}
		}

		/// <summary>
		/// Gets the thickness of the underline stroke.
		/// </summary>
		[CLSCompliant(false)]
		public ushort UnderlineThickness
		{
			get
			{
				return rec.underline_thickness;
			}
		}

		#endregion
	}
}
