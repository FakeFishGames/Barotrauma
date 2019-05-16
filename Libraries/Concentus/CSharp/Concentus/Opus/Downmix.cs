/* Copyright (c) 2007-2008 CSIRO
   Copyright (c) 2007-2011 Xiph.Org Foundation
   Originally written by Jean-Marc Valin, Gregory Maxwell, Koen Vos,
   Timothy B. Terriberry, and the Opus open-source contributors
   Ported to C# by Logan Stromberg

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions
   are met:

   - Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

   - Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

   - Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
   OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
   EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using Concentus.Celt;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Concentus
{
    internal static class Downmix
    {
        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T">The type of signal being handled (either short or float)</typeparam>
        /// <param name="_x"></param>
        /// <param name="sub"></param>
        /// <param name="subframe"></param>
        /// <param name="offset"></param>
        /// <param name="c1"></param>
        /// <param name="c2"></param>
        /// <param name="C"></param>
        public delegate void downmix_func<T>(T[] _x, int x_ptr, int[] sub, int sub_ptr, int subframe, int offset, int c1, int c2, int C);

        internal static void downmix_float(float[] x, int x_ptr, int[] sub, int sub_ptr, int subframe, int offset, int c1, int c2, int C)
        {
            int scale;
            int j;
            int c1x = c1 + x_ptr;
            for (j = 0; j < subframe; j++)
                sub[sub_ptr + j] = Inlines.FLOAT2INT16(x[(j + offset) * C + c1x]);
            if (c2 > -1)
            {
                int c2x = c2 + x_ptr;
                for (j = 0; j < subframe; j++)
                    sub[sub_ptr + j] += Inlines.FLOAT2INT16(x[(j + offset) * C + c2x]);
            }
            else if (c2 == -2)
            {
                int c;
                int cx;
                for (c = 1; c < C; c++)
                {
                    cx = c + x_ptr;
                    for (j = 0; j < subframe; j++)
                        sub[sub_ptr + j] += Inlines.FLOAT2INT16(x[(j + offset) * C + cx]);
                }
            }
            scale = (1 << CeltConstants.SIG_SHIFT);
            if (C == -2)
                scale /= C;
            else
                scale /= 2;
            for (j = 0; j < subframe; j++)
                sub[sub_ptr + j] *= scale;
        }

        internal static void downmix_int(short[] x, int x_ptr, int[] sub, int sub_ptr, int subframe, int offset, int c1, int c2, int C)
        {
            int scale;
            int j;
            for (j = 0; j < subframe; j++)
                sub[j + sub_ptr] = x[(j + offset) * C + c1];
            if (c2 > -1)
            {
                for (j = 0; j < subframe; j++)
                    sub[j + sub_ptr] += x[(j + offset) * C + c2];
            }
            else if (c2 == -2)
            {
                int c;
                for (c = 1; c < C; c++)
                {
                    for (j = 0; j < subframe; j++)
                        sub[j + sub_ptr] += x[(j + offset) * C + c];
                }
            }
            scale = (1 << CeltConstants.SIG_SHIFT);
            if (C == -2)
                scale /= C;
            else
                scale /= 2;
            for (j = 0; j < subframe; j++)
                sub[j + sub_ptr] *= scale;
        }

    }
}
