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

    internal static class CeltLPC
    {
        internal static void celt_lpc(
            int[] _lpc, /* out: [0...p-1] LPC coefficients      */
            int[] ac,  /* in:  [0...p] autocorrelation values  */
            int p)
        {
            int i, j;
            int r;
            int error = ac[0];
            int[] lpc = new int[p];

            //Arrays.MemSetInt(lpc, 0, p); strictly, this is not necessary since the runtime zeroes memory for us

            if (ac[0] != 0)
            {
                for (i = 0; i < p; i++)
                {
                    /* Sum up this iteration's reflection coefficient */
                    int rr = 0;
                    for (j = 0; j < i; j++)
                        rr += Inlines.MULT32_32_Q31(lpc[j], ac[i - j]);
                    rr += Inlines.SHR32(ac[i + 1], 3);
                    r = 0 - Inlines.frac_div32(Inlines.SHL32(rr, 3), error);
                    /*  Update LPC coefficients and total error */
                    lpc[i] = Inlines.SHR32(r, 3);

                    for (j = 0; j < (i + 1) >> 1; j++)
                    {
                        int tmp1, tmp2;
                        tmp1 = lpc[j];
                        tmp2 = lpc[i - 1 - j];
                        lpc[j] = tmp1 + Inlines.MULT32_32_Q31(r, tmp2);
                        lpc[i - 1 - j] = tmp2 + Inlines.MULT32_32_Q31(r, tmp1);
                    }

                    error = error - Inlines.MULT32_32_Q31(Inlines.MULT32_32_Q31(r, r), error);

                    /* Bail out once we get 30 dB gain */
                    if (error < Inlines.SHR32(ac[0], 10))
                    {
                        break;
                    }
                }
            }

            for (i = 0; i < p; i++)
            {
                _lpc[i] = Inlines.ROUND16((lpc[i]), 16);
            }
        }

        internal static unsafe void celt_iir(
            int[] _x,
            int _x_ptr,
                 int[] den,
                 int[] _y,
                 int _y_ptr,
                 int N,
                 int ord,
                 int[] mem)
        {
            int i, j;
            int[] rden = new int[ord];
            int[] y = new int[N + ord];
            Inlines.OpusAssert((ord & 3) == 0);

            fixed (int* prden = rden, py_base = y)
            {
                for (i = 0; i < ord; i++)
                    rden[i] = den[ord - i - 1];
                for (i = 0; i < ord; i++)
                    y[i] = (0 - mem[ord - i - 1]);
                for (; i < N + ord; i++)
                    y[i] = 0;
                for (i = 0; i < N - 3; i += 4)
                {
                    int* py = py_base + i;
                    /* Unroll by 4 as if it were an FIR filter */
                    int sum0 = _x[_x_ptr + i];
                    int sum1 = _x[_x_ptr + i + 1];
                    int sum2 = _x[_x_ptr + i + 2];
                    int sum3 = _x[_x_ptr + i + 3];
                    Kernels.xcorr_kernel(prden, py, ref sum0, ref sum1, ref sum2, ref sum3, ord);

                    /* Patch up the result to compensate for the fact that this is an IIR */
                    y[i + ord] = (0 - Inlines.ROUND16((sum0), CeltConstants.SIG_SHIFT));
                    _y[_y_ptr + i] = sum0;
                    sum1 = Inlines.MAC16_16(sum1, y[i + ord], den[0]);
                    y[i + ord + 1] = (0 - Inlines.ROUND16((sum1), CeltConstants.SIG_SHIFT));
                    _y[_y_ptr + i + 1] = sum1;
                    sum2 = Inlines.MAC16_16(sum2, y[i + ord + 1], den[0]);
                    sum2 = Inlines.MAC16_16(sum2, y[i + ord], den[1]);
                    y[i + ord + 2] = (0 - Inlines.ROUND16((sum2), CeltConstants.SIG_SHIFT));
                    _y[_y_ptr + i + 2] = sum2;

                    sum3 = Inlines.MAC16_16(sum3, y[i + ord + 2], den[0]);
                    sum3 = Inlines.MAC16_16(sum3, y[i + ord + 1], den[1]);
                    sum3 = Inlines.MAC16_16(sum3, y[i + ord], den[2]);
                    y[i + ord + 3] = (0 - Inlines.ROUND16((sum3), CeltConstants.SIG_SHIFT));
                    _y[_y_ptr + i + 3] = sum3;
                }
                for (; i < N; i++)
                {
                    int sum = _x[_x_ptr + i];
                    for (j = 0; j < ord; j++)
                        sum -= Inlines.MULT16_16(rden[j], y[i + j]);
                    y[i + ord] = Inlines.ROUND16((sum), CeltConstants.SIG_SHIFT);
                    _y[_y_ptr + i] = sum;
                }
                for (i = 0; i < ord; i++)
                    mem[i] = (_y[_y_ptr + N - i - 1]);
            }
        }
    }
}

#endif