/* Copyright (c) 2006-2011 Skype Limited. All Rights Reserved
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

namespace Concentus.Silk
{
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using Concentus.Silk.Enums;
    using Concentus.Silk.Structs;
    using System.Diagnostics;

    internal static class LTPAnalysisFilter
    {
        internal static void silk_LTP_analysis_filter(
            short[] LTP_res,                               /* O    LTP residual signal of length SilkConstants.MAX_NB_SUBFR * ( pre_length + subfr_length )  */
            short[] x,                                     /* I    Pointer to input signal with at least max( pitchL ) preceding samples       */
            int x_ptr,
            short[] LTPCoef_Q14,/* I     LTP_ORDER LTP coefficients for each MAX_NB_SUBFR subframe  [SilkConstants.LTP_ORDER * SilkConstants.MAX_NB_SUBFR]                 */
            int[] pitchL,                 /* I    Pitch lag, one for each subframe [SilkConstants.MAX_NB_SUBFR]                                           */
            int[] invGains_Q16,           /* I    Inverse quantization gains, one for each subframe [SilkConstants.MAX_NB_SUBFR]                           */
            int subfr_length,                           /* I    Length of each subframe                                                     */
            int nb_subfr,                               /* I    Number of subframes                                                         */
            int pre_length                              /* I    Length of the preceding samples starting at &x[0] for each subframe         */
)
        {
            int x_ptr2, x_lag_ptr;
            short[] Btmp_Q14 = new short[SilkConstants.LTP_ORDER];
            int LTP_res_ptr;
            int k, i;
            int LTP_est;

            x_ptr2 = x_ptr;
            LTP_res_ptr = 0;
            for (k = 0; k < nb_subfr; k++)
            {
                x_lag_ptr = x_ptr2 - pitchL[k];

                Btmp_Q14[0] = LTPCoef_Q14[k * SilkConstants.LTP_ORDER];
                Btmp_Q14[1] = LTPCoef_Q14[k * SilkConstants.LTP_ORDER + 1];
                Btmp_Q14[2] = LTPCoef_Q14[k * SilkConstants.LTP_ORDER + 2];
                Btmp_Q14[3] = LTPCoef_Q14[k * SilkConstants.LTP_ORDER + 3];
                Btmp_Q14[4] = LTPCoef_Q14[k * SilkConstants.LTP_ORDER + 4];

                /* LTP analysis FIR filter */
                for (i = 0; i < subfr_length + pre_length; i++)
                {
                    int LTP_res_ptri = LTP_res_ptr + i;
                    LTP_res[LTP_res_ptri] = x[x_ptr2 + i];

                    /* Long-term prediction */
                    LTP_est = Inlines.silk_SMULBB(x[x_lag_ptr + SilkConstants.LTP_ORDER / 2], Btmp_Q14[0]);
                    LTP_est = Inlines.silk_SMLABB_ovflw(LTP_est, x[x_lag_ptr + 1], Btmp_Q14[1]);
                    LTP_est = Inlines.silk_SMLABB_ovflw(LTP_est, x[x_lag_ptr], Btmp_Q14[2]);
                    LTP_est = Inlines.silk_SMLABB_ovflw(LTP_est, x[x_lag_ptr - 1], Btmp_Q14[3]);
                    LTP_est = Inlines.silk_SMLABB_ovflw(LTP_est, x[x_lag_ptr - 2], Btmp_Q14[4]);

                    LTP_est = Inlines.silk_RSHIFT_ROUND(LTP_est, 14); /* round and . Q0*/

                    /* Subtract long-term prediction */
                    LTP_res[LTP_res_ptri] = (short)Inlines.silk_SAT16((int)x[x_ptr2 + i] - LTP_est);

                    /* Scale residual */
                    LTP_res[LTP_res_ptri] = (short)(Inlines.silk_SMULWB(invGains_Q16[k], LTP_res[LTP_res_ptri]));

                    x_lag_ptr++;
                }

                /* Update pointers */
                LTP_res_ptr += subfr_length + pre_length;
                x_ptr2 += subfr_length;
            }
        }
    }
}
