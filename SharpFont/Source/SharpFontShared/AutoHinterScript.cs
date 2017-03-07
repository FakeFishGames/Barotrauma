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

namespace SharpFont
{
	/// <summary>
	/// A list of constants used for the glyph-to-script-map property to specify the script submodule the auto-hinter
	/// should use for hinting a particular glyph.
	/// </summary>
	public enum AutoHinterScript
	{
		/// <summary>
		/// Don't auto-hint this glyph.
		/// </summary>
		None = 0,

		/// <summary>
		/// Apply the latin auto-hinter. For the auto-hinter, ‘latin’ is a very broad term, including Cyrillic and
		/// Greek also since characters from those scripts share the same design constraints.
		/// </summary>
		/// <remarks><para>
		/// By default, characters from the following Unicode ranges are assigned to this submodule.
		/// </para><para><code>
		/// U+0020 - U+007F  // Basic Latin (no control characters)
		/// U+00A0 - U+00FF  // Latin-1 Supplement (no control characters)
		/// U+0100 - U+017F  // Latin Extended-A
		/// U+0180 - U+024F  // Latin Extended-B
		/// U+0250 - U+02AF  // IPA Extensions
		/// U+02B0 - U+02FF  // Spacing Modifier Letters
		/// U+0300 - U+036F  // Combining Diacritical Marks
		/// U+0370 - U+03FF  // Greek and Coptic
		/// U+0400 - U+04FF  // Cyrillic
		/// U+0500 - U+052F  // Cyrillic Supplement
		/// U+1D00 - U+1D7F  // Phonetic Extensions
		/// U+1D80 - U+1DBF  // Phonetic Extensions Supplement
		/// U+1DC0 - U+1DFF  // Combining Diacritical Marks Supplement
		/// U+1E00 - U+1EFF  // Latin Extended Additional
		/// U+1F00 - U+1FFF  // Greek Extended
		/// U+2000 - U+206F  // General Punctuation
		/// U+2070 - U+209F  // Superscripts and Subscripts
		/// U+20A0 - U+20CF  // Currency Symbols
		/// U+2150 - U+218F  // Number Forms
		/// U+2460 - U+24FF  // Enclosed Alphanumerics
		/// U+2C60 - U+2C7F  // Latin Extended-C
		/// U+2DE0 - U+2DFF  // Cyrillic Extended-A
		/// U+2E00 - U+2E7F  // Supplemental Punctuation
		/// U+A640 - U+A69F  // Cyrillic Extended-B
		/// U+A720 - U+A7FF  // Latin Extended-D
		/// U+FB00 - U+FB06  // Alphab. Present. Forms (Latin Ligatures)
		/// U+1D400 - U+1D7FF // Mathematical Alphanumeric Symbols
		/// U+1F100 - U+1F1FF // Enclosed Alphanumeric Supplement
		/// </code></para></remarks>
		Latin = 1,

		/// <summary>
		/// Apply the CJK auto-hinter, covering Chinese, Japanese, Korean, old Vietnamese, and some other scripts.
		/// </summary>
		/// <remarks><para>
		/// By default, characters from the following Unicode ranges are assigned to this submodule.
		/// </para><para><code>
		/// U+1100 - U+11FF  // Hangul Jamo
		/// U+2E80 - U+2EFF  // CJK Radicals Supplement
		/// U+2F00 - U+2FDF  // Kangxi Radicals
		/// U+2FF0 - U+2FFF  // Ideographic Description Characters
		/// U+3000 - U+303F  // CJK Symbols and Punctuation
		/// U+3040 - U+309F  // Hiragana
		/// U+30A0 - U+30FF  // Katakana
		/// U+3100 - U+312F  // Bopomofo
		/// U+3130 - U+318F  // Hangul Compatibility Jamo
		/// U+3190 - U+319F  // Kanbun
		/// U+31A0 - U+31BF  // Bopomofo Extended
		/// U+31C0 - U+31EF  // CJK Strokes
		/// U+31F0 - U+31FF  // Katakana Phonetic Extensions
		/// U+3200 - U+32FF  // Enclosed CJK Letters and Months
		/// U+3300 - U+33FF  // CJK Compatibility
		/// U+3400 - U+4DBF  // CJK Unified Ideographs Extension A
		/// U+4DC0 - U+4DFF  // Yijing Hexagram Symbols
		/// U+4E00 - U+9FFF  // CJK Unified Ideographs
		/// U+A960 - U+A97F  // Hangul Jamo Extended-A
		/// U+AC00 - U+D7AF  // Hangul Syllables
		/// U+D7B0 - U+D7FF  // Hangul Jamo Extended-B
		/// U+F900 - U+FAFF  // CJK Compatibility Ideographs
		/// U+FE10 - U+FE1F  // Vertical forms
		/// U+FE30 - U+FE4F  // CJK Compatibility Forms
		/// U+FF00 - U+FFEF  // Halfwidth and Fullwidth Forms
		/// U+1B000 - U+1B0FF // Kana Supplement
		/// U+1D300 - U+1D35F // Tai Xuan Hing Symbols
		/// U+1F200 - U+1F2FF // Enclosed Ideographic Supplement
		/// U+20000 - U+2A6DF // CJK Unified Ideographs Extension B
		/// U+2A700 - U+2B73F // CJK Unified Ideographs Extension C
		/// U+2B740 - U+2B81F // CJK Unified Ideographs Extension D
		/// U+2F800 - U+2FA1F // CJK Compatibility Ideographs Supplement
		/// </code></para></remarks>
		Cjk = 2,

		/// <summary>
		/// Apply the indic auto-hinter, covering all major scripts from the Indian sub-continent and some other
		/// related scripts like Thai, Lao, or Tibetan.
		/// </summary>
		/// <remarks><para>
		/// By default, characters from the following Unicode ranges are assigned to this submodule.
		/// </para><para><code>
		/// U+0900 - U+0DFF  // Indic Range
		/// U+0F00 - U+0FFF  // Tibetan
		/// U+1900 - U+194F  // Limbu
		/// U+1B80 - U+1BBF  // Sundanese
		/// U+1C80 - U+1CDF  // Meetei Mayak
		/// U+A800 - U+A82F  // Syloti Nagri 
		/// U+11800 - U+118DF // Sharada
		/// </code></para><para>
		/// Note that currently Indic support is rudimentary only, missing blue zone support.
		/// </para></remarks>
		Indic = 3
	}
}
