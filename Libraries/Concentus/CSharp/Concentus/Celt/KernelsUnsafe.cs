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

    internal static class Kernels
    {
        internal static unsafe void celt_fir(
             short* x,
             short[] num,
             short* y,
             int N,
             int ord,
             short[] mem
             )
        {
            int i, j;
            short[] rnum = new short[ord];
            short[] local_x = new short[N + ord];
            fixed (short* prnum = rnum, plocal_x = local_x)
            {
                for (i = 0; i < ord; i++)
                {
                    prnum[i] = num[ord - i - 1];
                }

                for (i = 0; i < ord; i++)
                {
                    plocal_x[i] = mem[ord - i - 1];
                }

                for (i = 0; i < N; i++)
                {
                    plocal_x[i + ord] = x[i];
                }

                for (i = 0; i < ord; i++)
                {
                    mem[i] = x[N - i - 1];
                }

                short* py = y;
                for (i = 0; i < N - 3; i += 4)
                {
                    int sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                    short* local_x2 = plocal_x + i;
                    xcorr_kernel(prnum, local_x2, ref sum0, ref sum1, ref sum2, ref sum3, ord);
                    py[0] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(x[i]), Inlines.PSHR32(sum0, CeltConstants.SIG_SHIFT))));
                    py[1] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(x[i + 1]), Inlines.PSHR32(sum1, CeltConstants.SIG_SHIFT))));
                    py[2] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(x[i + 2]), Inlines.PSHR32(sum2, CeltConstants.SIG_SHIFT))));
                    py[3] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(x[i + 3]), Inlines.PSHR32(sum3, CeltConstants.SIG_SHIFT))));
                    py += 4;
                }
                
                for (; i < N; i++)
                {
                    int sum = 0;

                    for (j = 0; j < ord; j++)
                    {
                        sum = Inlines.MAC16_16(sum, prnum[j], plocal_x[i + j]);
                    }

                    *py = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(x[i]), Inlines.PSHR32(sum, CeltConstants.SIG_SHIFT))));
                    py++;
                }
            }
        }

        internal static unsafe void celt_fir(
             int* px,
             int* pnum,
             int* py,
             int N,
             int ord,
             int[] mem
             )
        {
            int i, j;
            int[] rnum = new int[ord];
            int[] local_x = new int[N + ord];
            fixed (int* prnum = rnum, plocal_x_base = local_x)
            {
                for (i = 0; i < ord; i++)
                {
                    rnum[i] = pnum[ord - i - 1];
                }

                for (i = 0; i < ord; i++)
                {
                    local_x[i] = mem[ord - i - 1];
                }

                for (i = 0; i < N; i++)
                {
                    local_x[i + ord] = px[i];
                }

                for (i = 0; i < ord; i++)
                {
                    mem[i] = px[N - i - 1];
                }
                
                int* px2 = px;
                for (i = 0; i < N - 3; i += 4)
                {
                    int sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                    int* plocal_x = plocal_x_base + i;
                    xcorr_kernel(prnum, plocal_x, ref sum0, ref sum1, ref sum2, ref sum3, ord);
                    py[0] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(px2[0]), Inlines.PSHR32(sum0, CeltConstants.SIG_SHIFT))));
                    py[1] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(px2[1]), Inlines.PSHR32(sum1, CeltConstants.SIG_SHIFT))));
                    py[2] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(px2[2]), Inlines.PSHR32(sum2, CeltConstants.SIG_SHIFT))));
                    py[3] = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(px2[3]), Inlines.PSHR32(sum3, CeltConstants.SIG_SHIFT))));
                    py += 4;
                    px2 += 4;
                }

                for (; i < N; i++)
                {
                    int sum = 0;

                    for (j = 0; j < ord; j++)
                    {
                        sum = Inlines.MAC16_16(sum, rnum[j], local_x[i + j]);
                    }

                    *py = Inlines.SATURATE16((Inlines.ADD32(Inlines.EXTEND32(px[i]), Inlines.PSHR32(sum, CeltConstants.SIG_SHIFT))));
                    py++;
                }
            }
        }

        /// <summary>
        /// OPT: This is the kernel you really want to optimize. It gets used a lot by the prefilter and by the PLC.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="py"></param>
        /// <param name="sum0"></param>
        /// <param name="len"></param>
        internal unsafe static void xcorr_kernel(short* x, short* py, ref int sum0, ref int sum1, ref int sum2, ref int sum3, int len)
        {
            int j;
            short y_0, y_1, y_2, y_3;
            Inlines.OpusAssert(len >= 3);
            y_3 = 0; /* gcc doesn't realize that y_3 can't be used uninitialized */
            y_0 = *py++;
            y_1 = *py++;
            y_2 = *py++;
            for (j = 0; j < len - 3; j += 4)
            {
                short tmp;
                tmp = *x++;
                y_3 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_0);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_1);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_2);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_3);
                tmp = *x++;
                y_0 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_1);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_2);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_3);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_0);
                tmp = *x++;
                y_1 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_2);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_3);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_0);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_1);
                tmp = *x++;
                y_2 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_3);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_0);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_1);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_2);
            }
            if (j++ < len)
            {
                short tmp;
                tmp = *x++;
                y_3 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_0);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_1);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_2);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_3);
            }
            if (j++ < len)
            {
                short tmp;
                tmp = *x++;
                y_0 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_1);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_2);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_3);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_0);
            }
            if (j < len)
            {
                short tmp;
                tmp = *x++;
                y_1 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_2);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_3);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_0);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_1);
            }
        }

        internal unsafe static void xcorr_kernel(int* px, int* py, ref int sum0, ref int sum1, ref int sum2, ref int sum3, int len)
        {
            int j;
            int y_0, y_1, y_2, y_3;
            Inlines.OpusAssert(len >= 3);
            y_3 = 0; /* gcc doesn't realize that y_3 can't be used uninitialized */
            y_0 = *py++;
            y_1 = *py++;
            y_2 = *py++;
            for (j = 0; j < len - 3; j += 4)
            {
                int tmp;
                tmp = *px++;
                y_3 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_0);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_1);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_2);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_3);
                tmp = *px++;
                y_0 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_1);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_2);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_3);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_0);
                tmp = *px++;
                y_1 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_2);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_3);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_0);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_1);
                tmp = *px++;
                y_2 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_3);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_0);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_1);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_2);
            }
            if (j++ < len)
            {
                int tmp;
                tmp = *px++;
                y_3 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_0);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_1);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_2);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_3);
            }
            if (j++ < len)
            {
                int tmp;
                tmp = *px++;
                y_0 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_1);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_2);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_3);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_0);
            }
            if (j < len)
            {
                int tmp;
                tmp = *px++;
                y_1 = *py++;
                sum0 = Inlines.MAC16_16(sum0, tmp, y_2);
                sum1 = Inlines.MAC16_16(sum1, tmp, y_3);
                sum2 = Inlines.MAC16_16(sum2, tmp, y_0);
                sum3 = Inlines.MAC16_16(sum3, tmp, y_1);
            }
        }

        internal static unsafe int celt_inner_prod(short* x, short* y, int N)
        {
            int i;
            int xy = 0;
            for (i = 0; i < N; i++)
                xy = Inlines.MAC16_16(xy, x[i], y[i]);
            return xy;
        }

        internal static unsafe int celt_inner_prod(int* x, int* y, int N)
        {
            int i;
            int xy = 0;
            for (i = 0; i < N; i++)
                xy = Inlines.MAC16_16(xy, x[i], y[i]);
            return xy;
        }

        internal static unsafe void dual_inner_prod(int* x, int* y01, int* y02, int N, out int xy1, out int xy2)
        {
            int i;
            int xy01 = 0;
            int xy02 = 0;
            for (i = 0; i < N; i++)
            {
                xy01 = Inlines.MAC16_16(xy01, x[i], y01[i]);
                xy02 = Inlines.MAC16_16(xy02, x[i], y02[i]);
            }
            xy1 = xy01;
            xy2 = xy02;
        }
    }
}

#endif