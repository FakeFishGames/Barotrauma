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

    internal static class ResidualEnergy
    {
        /* Calculates residual energies of input subframes where all subframes have LPC_order   */
        /* of preceding samples                                                                 */
        internal static void silk_residual_energy(
            int[] nrgs,                   /* O    Residual energy per subframe  [MAX_NB_SUBFR]                                              */
            int[] nrgsQ,                  /* O    Q value per subframe   [MAX_NB_SUBFR]                                                     */
            short[] x,                                    /* I    Input signal                                                                */
            short[][] a_Q12,            /* I    AR coefs for each frame half [2][MAX_LPC_ORDER]                                                */
            int[] gains,                  /* I    Quantization gains [SilkConstants.MAX_NB_SUBFR]                                                         */
            int subfr_length,                           /* I    Subframe length                                                             */
            int nb_subfr,                               /* I    Number of subframes                                                         */
            int LPC_order                              /* I    LPC order                                                                   */
            )
        {
            int offset, i, j, lz1, lz2;
            int rshift, energy;
            int LPC_res_ptr;
            short[] LPC_res;
            int x_ptr;
            int tmp32;
            
            x_ptr = 0;
            offset = LPC_order + subfr_length;

            /* Filter input to create the LPC residual for each frame half, and measure subframe energies */
            LPC_res = new short[(SilkConstants.MAX_NB_SUBFR >> 1) * offset];
            Inlines.OpusAssert((nb_subfr >> 1) * (SilkConstants.MAX_NB_SUBFR >> 1) == nb_subfr);
            for (i = 0; i < nb_subfr >> 1; i++)
            {
                /* Calculate half frame LPC residual signal including preceding samples */
                Filters.silk_LPC_analysis_filter(LPC_res, 0, x, x_ptr, a_Q12[i], 0, (SilkConstants.MAX_NB_SUBFR >> 1) * offset, LPC_order);

                /* Point to first subframe of the just calculated LPC residual signal */
                LPC_res_ptr = LPC_order;
                for (j = 0; j < (SilkConstants.MAX_NB_SUBFR >> 1); j++)
                {
                    /* Measure subframe energy */
                    SumSqrShift.silk_sum_sqr_shift(out energy, out rshift, LPC_res, LPC_res_ptr, subfr_length);
                    nrgs[i * (SilkConstants.MAX_NB_SUBFR >> 1) + j] = energy;

                    /* Set Q values for the measured energy */
                    nrgsQ[i * (SilkConstants.MAX_NB_SUBFR >> 1) + j] = 0 - rshift;

                    /* Move to next subframe */
                    LPC_res_ptr += offset;
                }
                /* Move to next frame half */
                x_ptr += (SilkConstants.MAX_NB_SUBFR >> 1) * offset;
            }

            /* Apply the squared subframe gains */
            for (i = 0; i < nb_subfr; i++)
            {
                /* Fully upscale gains and energies */
                lz1 = Inlines.silk_CLZ32(nrgs[i]) - 1;
                lz2 = Inlines.silk_CLZ32(gains[i]) - 1;

                tmp32 = Inlines.silk_LSHIFT32(gains[i], lz2);

                /* Find squared gains */
                tmp32 = Inlines.silk_SMMUL(tmp32, tmp32); /* Q( 2 * lz2 - 32 )*/

                /* Scale energies */
                nrgs[i] = Inlines.silk_SMMUL(tmp32, Inlines.silk_LSHIFT32(nrgs[i], lz1)); /* Q( nrgsQ[ i ] + lz1 + 2 * lz2 - 32 - 32 )*/
                nrgsQ[i] += lz1 + 2 * lz2 - 32 - 32;
            }

        }

        /* Residual energy: nrg = wxx - 2 * wXx * c + c' * wXX * c */
        internal static int silk_residual_energy16_covar(
            short[] c,                                     /* I    Prediction vector                                                           */
            int c_ptr,
            int[] wXX,                                   /* I    Correlation matrix                                                          */
            int wXX_ptr,
            int[] wXx,                                   /* I    Correlation vector                                                          */
            int wxx,                                    /* I    Signal energy                                                               */
            int D,                                      /* I    Dimension                                                                   */
            int cQ                                      /* I    Q value for c vector 0 - 15                                                 */
        )
        {
            int i, j, lshifts, Qxtra;
            int c_max, w_max, tmp, tmp2, nrg;
            int[] cn = new int[D]; //SilkConstants.MAX_MATRIX_SIZE
            int pRow;

            /* Safety checks */
            Inlines.OpusAssert(D >= 0);
            Inlines.OpusAssert(D <= 16);
            Inlines.OpusAssert(cQ > 0);
            Inlines.OpusAssert(cQ < 16);

            lshifts = 16 - cQ;
            Qxtra = lshifts;

            c_max = 0;
            for (i = c_ptr; i < c_ptr + D; i++)
            {
                c_max = Inlines.silk_max_32(c_max, Inlines.silk_abs((int)c[i]));
            }
            Qxtra = Inlines.silk_min_int(Qxtra, Inlines.silk_CLZ32(c_max) - 17);

            w_max = Inlines.silk_max_32(wXX[wXX_ptr], wXX[wXX_ptr + (D * D) - 1]);
            Qxtra = Inlines.silk_min_int(Qxtra, Inlines.silk_CLZ32(Inlines.silk_MUL(D, Inlines.silk_RSHIFT(Inlines.silk_SMULWB(w_max, c_max), 4))) - 5);
            Qxtra = Inlines.silk_max_int(Qxtra, 0);
            for (i = 0; i < D; i++)
            {
                cn[i] = Inlines.silk_LSHIFT((int)c[c_ptr + i], Qxtra);
                Inlines.OpusAssert(Inlines.silk_abs(cn[i]) <= (short.MaxValue + 1)); /* Check that Inlines.silk_SMLAWB can be used */
            }
            lshifts -= Qxtra;

            /* Compute wxx - 2 * wXx * c */
            tmp = 0;
            for (i = 0; i < D; i++)
            {
                tmp = Inlines.silk_SMLAWB(tmp, wXx[i], cn[i]);
            }
            nrg = Inlines.silk_RSHIFT(wxx, 1 + lshifts) - tmp;                         /* Q: -lshifts - 1 */

            /* Add c' * wXX * c, assuming wXX is symmetric */
            tmp2 = 0;
            for (i = 0; i < D; i++)
            {
                tmp = 0;
                pRow = wXX_ptr + (i * D);
                for (j = i + 1; j < D; j++)
                {
                    tmp = Inlines.silk_SMLAWB(tmp, wXX[pRow + j], cn[j]);
                }
                tmp = Inlines.silk_SMLAWB(tmp, Inlines.silk_RSHIFT(wXX[pRow + i], 1), cn[i]);
                tmp2 = Inlines.silk_SMLAWB(tmp2, tmp, cn[i]);
            }
            nrg = Inlines.silk_ADD_LSHIFT32(nrg, tmp2, lshifts);                       /* Q: -lshifts - 1 */

            /* Keep one bit free always, because we add them for LSF interpolation */
            if (nrg < 1)
            {
                nrg = 1;
            }
            else if (nrg > Inlines.silk_RSHIFT(int.MaxValue, lshifts + 2))
            {
                nrg = int.MaxValue >> 1;
            }
            else {
                nrg = Inlines.silk_LSHIFT(nrg, lshifts + 1);                           /* Q0 */
            }
            return nrg;

        }
    }
}
