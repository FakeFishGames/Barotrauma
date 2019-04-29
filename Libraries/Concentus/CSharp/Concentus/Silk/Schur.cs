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

    internal static class Schur
    {
        /* Faster than schur64(), but much less accurate.                       */
        /* uses SMLAWB(), requiring armv5E and higher.                          */
        internal static int silk_schur(                              /* O    Returns residual energy                                     */
            short[] rc_Q15,            /* O    reflection coefficients [order] Q15                         */
            int[] c,                 /* I    correlations [order+1]                                      */
            int order               /* I    prediction order                                            */
        )
        {
            int k, n, lz;
            int[][] C = Arrays.InitTwoDimensionalArray<int>(SilkConstants.SILK_MAX_ORDER_LPC + 1, 2);
            int Ctmp1, Ctmp2, rc_tmp_Q15;

            Inlines.OpusAssert(order == 6 || order == 8 || order == 10 || order == 12 || order == 14 || order == 16);

            /* Get number of leading zeros */
            lz = Inlines.silk_CLZ32(c[0]);

            /* Copy correlations and adjust level to Q30 */
            if (lz < 2)
            {
                /* lz must be 1, so shift one to the right */
                for (k = 0; k < order + 1; k++)
                {
                    C[k][0] = C[k][1] = Inlines.silk_RSHIFT(c[k], 1);
                }
            }
            else if (lz > 2)
            {
                /* Shift to the left */
                lz -= 2;
                for (k = 0; k < order + 1; k++)
                {
                    C[k][0] = C[k][1] = Inlines.silk_LSHIFT(c[k], lz);
                }
            }
            else {
                /* No need to shift */
                for (k = 0; k < order + 1; k++)
                {
                    C[k][0] = C[k][1] = c[k];
                }
            }

            for (k = 0; k < order; k++)
            {
                /* Check that we won't be getting an unstable rc, otherwise stop here. */
                if (Inlines.silk_abs_int32(C[k + 1][0]) >= C[0][1])
                {
                    if (C[k + 1][0] > 0)
                    {
                        rc_Q15[k] = (short)(0 - ((int)((.99f) * ((long)1 << (15)) + 0.5))/*Inlines.SILK_CONST(.99f, 15)*/);
                    }
                    else {
                        rc_Q15[k] = (short)(((int)((.99f) * ((long)1 << (15)) + 0.5))/*Inlines.SILK_CONST(.99f, 15)*/);
                    }
                    k++;
                    break;
                }

                /* Get reflection coefficient */
                rc_tmp_Q15 = 0 - Inlines.silk_DIV32_16(C[k + 1][0], Inlines.silk_max_32(Inlines.silk_RSHIFT(C[0][1], 15), 1));

                /* Clip (shouldn't happen for properly conditioned inputs) */
                rc_tmp_Q15 = Inlines.silk_SAT16(rc_tmp_Q15);

                /* Store */
                rc_Q15[k] = (short)rc_tmp_Q15;

                /* Update correlations */
                for (n = 0; n < order - k; n++)
                {
                    Ctmp1 = C[n + k + 1][0];
                    Ctmp2 = C[n][1];
                    C[n + k + 1][0] = Inlines.silk_SMLAWB(Ctmp1, Inlines.silk_LSHIFT(Ctmp2, 1), rc_tmp_Q15);
                    C[n][1]         = Inlines.silk_SMLAWB(Ctmp2, Inlines.silk_LSHIFT(Ctmp1, 1), rc_tmp_Q15);
                }
            }

            for (; k < order; k++)
            {
                rc_Q15[k] = 0;
            }

            /* return residual energy */
            return Inlines.silk_max_32(1, C[0][1]);
        }

        /* Slower than schur(), but more accurate.                              */
        /* Uses SMULL(), available on armv4                                     */
        internal static int silk_schur64(                            /* O    returns residual energy                                     */
            int[] rc_Q16,           /* O    Reflection coefficients [order] Q16                         */
            int[] c,                /* I    Correlations [order+1]                                      */
            int order               /* I    Prediction order                                            */
        )
        {
            int k, n;
            int[][] C = Arrays.InitTwoDimensionalArray<int>(SilkConstants.SILK_MAX_ORDER_LPC + 1, 2);
            int Ctmp1_Q30, Ctmp2_Q30, rc_tmp_Q31;

            Inlines.OpusAssert(order == 6 || order == 8 || order == 10 || order == 12 || order == 14 || order == 16);

            /* Check for invalid input */
            if (c[0] <= 0)
            {
                Arrays.MemSetInt(rc_Q16, 0, order);
                return 0;
            }

            for (k = 0; k < order + 1; k++)
            {
                C[k][0] = C[k][1] = c[k];
            }

            for (k = 0; k < order; k++)
            {
                /* Check that we won't be getting an unstable rc, otherwise stop here. */
                if (Inlines.silk_abs_int32(C[k + 1][0]) >= C[0][1])
                {
                    if (C[k + 1][0] > 0)
                    {
                        rc_Q16[k] = -((int)((.99f) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(.99f, 16)*/;
                    }
                    else {
                        rc_Q16[k] = ((int)((.99f) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(.99f, 16)*/;
                    }
                    k++;
                    break;
                }

                /* Get reflection coefficient: divide two Q30 values and get result in Q31 */
                rc_tmp_Q31 = Inlines.silk_DIV32_varQ(-C[k + 1][0], C[0][1], 31);

                /* Save the output */
                rc_Q16[k] = Inlines.silk_RSHIFT_ROUND(rc_tmp_Q31, 15);

                /* Update correlations */
                for (n = 0; n < order - k; n++)
                {
                    Ctmp1_Q30 = C[n + k + 1][0];
                    Ctmp2_Q30 = C[n][1];

                    /* Multiply and add the highest int32 */
                    C[n + k + 1][0] = Ctmp1_Q30 + Inlines.silk_SMMUL(Inlines.silk_LSHIFT(Ctmp2_Q30, 1), rc_tmp_Q31);
                    C[n][1] = Ctmp2_Q30 + Inlines.silk_SMMUL(Inlines.silk_LSHIFT(Ctmp1_Q30, 1), rc_tmp_Q31);
                }
            }

            for (; k < order; k++)
            {
                rc_Q16[k] = 0;
            }

            return Inlines.silk_max_32(1, C[0][1]);
        }
    }
}
