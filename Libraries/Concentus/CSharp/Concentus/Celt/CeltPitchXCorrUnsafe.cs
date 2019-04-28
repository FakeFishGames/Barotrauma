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

#if UNSAFE

namespace Concentus.Celt
{
    using Concentus.Celt.Enums;
    using Concentus.Celt.Structs;
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using System.Diagnostics;
    using System.Threading;

    internal static class CeltPitchXCorr
    {
        internal static unsafe int pitch_xcorr(
            int[] _x,
            int[] _y,
            int[] xcorr,
            int len,
            int max_pitch)
        {
            int i;
            int maxcorr = 1;
            Inlines.OpusAssert(max_pitch > 0);
            fixed (int* py_base = _y, px = _x, px_base = _x)
            {
                for (i = 0; i < max_pitch - 3; i += 4)
                {
                    int sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                    int* py = py_base + i;
                    Kernels.xcorr_kernel(px, py, ref sum0, ref sum1, ref sum2, ref sum3, len);
                    xcorr[i] = sum0;
                    xcorr[i + 1] = sum1;
                    xcorr[i + 2] = sum2;
                    xcorr[i + 3] = sum3;
                    sum0 = Inlines.MAX32(sum0, sum1);
                    sum2 = Inlines.MAX32(sum2, sum3);
                    sum0 = Inlines.MAX32(sum0, sum2);
                    maxcorr = Inlines.MAX32(maxcorr, sum0);
                }
                /* In case max_pitch isn't a multiple of 4, do non-unrolled version. */
                for (; i < max_pitch; i++)
                {
                    int* py = py_base + i;
                    int inner_sum = Kernels.celt_inner_prod(px_base, py, len);
                    xcorr[i] = inner_sum;
                    maxcorr = Inlines.MAX32(maxcorr, inner_sum);
                }
            }

            return maxcorr;
        }

        internal static unsafe int pitch_xcorr(
            int* _x,
            int* _y,
            int[] xcorr,
            int len,
            int max_pitch)
        {
            int i;
            int maxcorr = 1;
            Inlines.OpusAssert(max_pitch > 0);
            for (i = 0; i < max_pitch - 3; i += 4)
            {
                int sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                int* py = _y + i;
                Kernels.xcorr_kernel(_x, py, ref sum0, ref sum1, ref sum2, ref sum3, len);
                xcorr[i] = sum0;
                xcorr[i + 1] = sum1;
                xcorr[i + 2] = sum2;
                xcorr[i + 3] = sum3;
                sum0 = Inlines.MAX32(sum0, sum1);
                sum2 = Inlines.MAX32(sum2, sum3);
                sum0 = Inlines.MAX32(sum0, sum2);
                maxcorr = Inlines.MAX32(maxcorr, sum0);
            }
            /* In case max_pitch isn't a multiple of 4, do non-unrolled version. */
            for (; i < max_pitch; i++)
            {
                int* py = _y + i;
                int inner_sum = Kernels.celt_inner_prod(_x, py, len);
                xcorr[i] = inner_sum;
                maxcorr = Inlines.MAX32(maxcorr, inner_sum);
            }

            return maxcorr;
        }

        internal static unsafe int pitch_xcorr(
            short[] _x,
            int _x_ptr,
            short[] _y,
            int _y_ptr,
            int[] xcorr,
            int len,
            int max_pitch)
        {
            int i;
            int maxcorr = 1;
            Inlines.OpusAssert(max_pitch > 0);
            fixed (int* pxcorr = xcorr)
            {
                fixed (short* px_base = _x, py_base = _y)
                {
                    short* px = px_base + _x_ptr;
                    for (i = 0; i < max_pitch - 3; i += 4)
                    {
                        int sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                        short* py = py_base + _y_ptr + i;
                        Kernels.xcorr_kernel(px, py, ref sum0, ref sum1, ref sum2, ref sum3, len);

                        int* pxcorr2 = pxcorr + i;
                        pxcorr2[0] = sum0;
                        pxcorr2[1] = sum1;
                        pxcorr2[2] = sum2;
                        pxcorr2[3] = sum3;
                        sum0 = Inlines.MAX32(sum0, sum1);
                        sum2 = Inlines.MAX32(sum2, sum3);
                        sum0 = Inlines.MAX32(sum0, sum2);
                        maxcorr = Inlines.MAX32(maxcorr, sum0);
                    }
                    /* In case max_pitch isn't a multiple of 4, do non-unrolled version. */
                    for (; i < max_pitch; i++)
                    {
                        short* py = py_base + _y_ptr + i;
                        int inner_sum = Kernels.celt_inner_prod(px, py, len);
                        xcorr[i] = inner_sum;
                        maxcorr = Inlines.MAX32(maxcorr, inner_sum);
                    }
                }
            }

            return maxcorr;
        }

        internal static unsafe int pitch_xcorr(
            short[] _x,
            short[] _y,
            int[] xcorr,
            int len,
            int max_pitch)
        {
            int i;
            int maxcorr = 1;
            Inlines.OpusAssert(max_pitch > 0);
            fixed (int* pxcorr_base = xcorr)
            {
                fixed (short* px = _x, py_base = _y)
                {
                    for (i = 0; i < max_pitch - 3; i += 4)
                    {
                        int sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                        short* py = py_base + i;
                        Kernels.xcorr_kernel(px, py, ref sum0, ref sum1, ref sum2, ref sum3, len);

                        int* pxcorr = pxcorr_base + i;
                        pxcorr[0] = sum0;
                        pxcorr[1] = sum1;
                        pxcorr[2] = sum2;
                        pxcorr[3] = sum3;
                        sum0 = Inlines.MAX32(sum0, sum1);
                        sum2 = Inlines.MAX32(sum2, sum3);
                        sum0 = Inlines.MAX32(sum0, sum2);
                        maxcorr = Inlines.MAX32(maxcorr, sum0);
                    }
                    /* In case max_pitch isn't a multiple of 4, do non-unrolled version. */
                    for (; i < max_pitch; i++)
                    {
                        short* py = py_base + i;
                        int inner_sum = Kernels.celt_inner_prod(px, py, len);
                        xcorr[i] = inner_sum;
                        maxcorr = Inlines.MAX32(maxcorr, inner_sum);
                    }
                    return maxcorr;
                }
            }
        }

        internal static unsafe int pitch_xcorr(
            short* px,
            short* py,
            int[] xcorr,
            int len,
            int max_pitch)
        {
            int i;
            int maxcorr = 1;
            Inlines.OpusAssert(max_pitch > 0);
            fixed (int* pxcorr_base = xcorr)
            {
                for (i = 0; i < max_pitch - 3; i += 4)
                {
                    int sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                    short* py2 = py + i;
                    Kernels.xcorr_kernel(px, py2, ref sum0, ref sum1, ref sum2, ref sum3, len);
                    int* pxcorr = pxcorr_base + i;
                    pxcorr[0] = sum0;
                    pxcorr[1] = sum1;
                    pxcorr[2] = sum2;
                    pxcorr[3] = sum3;
                    sum0 = Inlines.MAX32(sum0, sum1);
                    sum2 = Inlines.MAX32(sum2, sum3);
                    sum0 = Inlines.MAX32(sum0, sum2);
                    maxcorr = Inlines.MAX32(maxcorr, sum0);
                }
                /* In case max_pitch isn't a multiple of 4, do non-unrolled version. */
                for (; i < max_pitch; i++)
                {
                    short* py2 = py + i;
                    int inner_sum = Kernels.celt_inner_prod(px, py2, len);
                    xcorr[i] = inner_sum;
                    maxcorr = Inlines.MAX32(maxcorr, inner_sum);
                }
                return maxcorr;
            }
        }
    }
}

#endif