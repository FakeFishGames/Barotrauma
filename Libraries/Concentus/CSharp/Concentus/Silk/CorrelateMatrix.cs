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

    /**********************************************************************
 * Correlation Matrix Computations for LS estimate.
 **********************************************************************/
    internal static class CorrelateMatrix
    {
        /* Calculates correlation vector X'*t */
        internal static void silk_corrVector(
            short[] x,                                     /* I    x vector [L + order - 1] used to form data matrix X                         */
            int x_ptr,
            short[] t,                                     /* I    Target vector [L]                                                           */
            int t_ptr,
            int L,                                      /* I    Length of vectors                                                           */
            int order,                                  /* I    Max lag for correlation                                                     */
            int[] Xt,                                    /* O    Pointer to X'*t correlation vector [order]                                  */
            int rshifts                                /* I    Right shifts of correlations                                                */
        )
        {
            int lag, i;
            int ptr1;
            int ptr2;
            int inner_prod;

            ptr1 = x_ptr + order - 1; /* Points to first sample of column 0 of X: X[:,0] */
            ptr2 = t_ptr;
            /* Calculate X'*t */
            if (rshifts > 0)
            {
                /* Right shifting used */
                for (lag = 0; lag < order; lag++)
                {
                    inner_prod = 0;
                    for (i = 0; i < L; i++)
                    {
                        inner_prod += Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[ptr1 + i], t[ptr2 + i]), rshifts);
                    }
                    Xt[lag] = inner_prod; /* X[:,lag]'*t */
                    ptr1--; /* Go to next column of X */
                }
            }
            else {
                Inlines.OpusAssert(rshifts == 0);
                for (lag = 0; lag < order; lag++)
                {
                    Xt[lag] = Inlines.silk_inner_prod(x, ptr1, t, ptr2, L); /* X[:,lag]'*t */
                    ptr1--; /* Go to next column of X */
                }
            }
        }

        /* Calculates correlation matrix X'*X */
        internal static void silk_corrMatrix(
            short[] x,                                     /* I    x vector [L + order - 1] used to form data matrix X                         */
            int x_ptr,
            int L,                                      /* I    Length of vectors                                                           */
            int order,                                  /* I    Max lag for correlation                                                     */
            int head_room,                              /* I    Desired headroom                                                            */
            int[] XX,                                    /* O    Pointer to X'*X correlation matrix [ order x order ]                        */
            int XX_ptr,
            BoxedValueInt rshifts                               /* I/O  Right shifts of correlations                                                */
        )
        {
            int i, j, lag, head_room_rshifts;
            int energy, rshifts_local;
            int ptr1, ptr2;
            
            /* Calculate energy to find shift used to fit in 32 bits */
            SumSqrShift.silk_sum_sqr_shift(out energy, out rshifts_local, x, x_ptr, L + order - 1);
            /* Add shifts to get the desired head room */
            head_room_rshifts = Inlines.silk_max(head_room - Inlines.silk_CLZ32(energy), 0);

            energy = Inlines.silk_RSHIFT32(energy, head_room_rshifts);
            rshifts_local += head_room_rshifts;

            /* Calculate energy of first column (0) of X: X[:,0]'*X[:,0] */
            /* Remove contribution of first order - 1 samples */
            for (i = x_ptr; i < x_ptr + order - 1; i++)
            {
                energy -= Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[i], x[i]), rshifts_local);
            }
            if (rshifts_local < rshifts.Val)
            {
                /* Adjust energy */
                energy = Inlines.silk_RSHIFT32(energy, rshifts.Val - rshifts_local);
                rshifts_local = rshifts.Val;
            }

            /* Calculate energy of remaining columns of X: X[:,j]'*X[:,j] */
            /* Fill out the diagonal of the correlation matrix */
            Inlines.MatrixSet(XX, XX_ptr, 0, 0, order, energy);
            ptr1 = x_ptr + order - 1; /* First sample of column 0 of X */
            for (j = 1; j < order; j++)
            {
                energy = Inlines.silk_SUB32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[ptr1 + L - j], x[ptr1 + L - j]), rshifts_local));
                energy = Inlines.silk_ADD32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[ptr1 - j], x[ptr1 - j]), rshifts_local));
                Inlines.MatrixSet(XX, XX_ptr, j, j, order, energy);
            }

            ptr2 = x_ptr + order - 2; /* First sample of column 1 of X */
                                  /* Calculate the remaining elements of the correlation matrix */
            if (rshifts_local > 0)
            {
                /* Right shifting used */
                for (lag = 1; lag < order; lag++)
                {
                    /* Inner product of column 0 and column lag: X[:,0]'*X[:,lag] */
                    energy = 0;
                    for (i = 0; i < L; i++)
                    {
                        energy += Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[ptr1 + i], x[ptr2 + i]), rshifts_local);
                    }
                    /* Calculate remaining off diagonal: X[:,j]'*X[:,j + lag] */
                    Inlines.MatrixSet(XX, XX_ptr, lag, 0, order, energy);
                    Inlines.MatrixSet(XX, XX_ptr, 0, lag, order, energy);
                    for (j = 1; j < (order - lag); j++)
                    {
                        energy = Inlines.silk_SUB32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[ptr1 + L - j], x[ptr2 + L - j]), rshifts_local));
                        energy = Inlines.silk_ADD32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[ptr1 - j], x[ptr2 - j]), rshifts_local));
                        Inlines.MatrixSet(XX, XX_ptr, lag + j, j, order, energy);
                        Inlines.MatrixSet(XX, XX_ptr, j, lag + j, order, energy);
                    }
                    ptr2--; /* Update pointer to first sample of next column (lag) in X */
                }
            }
            else {
                for (lag = 1; lag < order; lag++)
                {
                    /* Inner product of column 0 and column lag: X[:,0]'*X[:,lag] */
                    energy = Inlines.silk_inner_prod(x, ptr1, x, ptr2, L);
                    Inlines.MatrixSet(XX, XX_ptr, lag, 0, order,energy);
                    Inlines.MatrixSet(XX, XX_ptr, 0, lag, order, energy);
                    /* Calculate remaining off diagonal: X[:,j]'*X[:,j + lag] */
                    for (j = 1; j < (order - lag); j++)
                    {
                        energy = Inlines.silk_SUB32(energy, Inlines.silk_SMULBB(x[ptr1 + L - j], x[ptr2 + L - j]));
                        energy = Inlines.silk_SMLABB(energy, x[ptr1 - j], x[ptr2 - j]);
                        Inlines.MatrixSet(XX, XX_ptr, lag + j, j, order, energy);
                        Inlines.MatrixSet(XX, XX_ptr, j, lag + j, order, energy);
                    }
                    ptr2--;/* Update pointer to first sample of next column (lag) in X */
                }
            }
            rshifts.Val = rshifts_local;
        }

    }
}
