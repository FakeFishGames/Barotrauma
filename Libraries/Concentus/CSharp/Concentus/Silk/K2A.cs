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

    internal static class K2A
    {
        /* Step up function, converts reflection coefficients to prediction coefficients */
        internal static void silk_k2a(
            int[] A_Q24,             /* O    Prediction coefficients [order] Q24                         */
            short[] rc_Q15,            /* I    Reflection coefficients [order] Q15                         */
            int order               /* I    Prediction order                                            */
        )
        {
            int k, n;
            int[] Atmp = new int[SilkConstants.SILK_MAX_ORDER_LPC];

            for (k = 0; k < order; k++)
            {
                for (n = 0; n < k; n++)
                {
                    Atmp[n] = A_Q24[n];
                }
                for (n = 0; n < k; n++)
                {
                    A_Q24[n] = Inlines.silk_SMLAWB(A_Q24[n], Inlines.silk_LSHIFT(Atmp[k - n - 1], 1), rc_Q15[k]);
                }
                A_Q24[k] = 0 - Inlines.silk_LSHIFT((int)rc_Q15[k], 9);
            }
        }

        /* Step up function, converts reflection coefficients to prediction coefficients */
        internal static void silk_k2a_Q16(
            int[] A_Q24,             /* O    Prediction coefficients [order] Q24                         */
            int[] rc_Q16,            /* I    Reflection coefficients [order] Q16                         */
            int order               /* I    Prediction order                                            */
        )
        {
            int k, n;
            int[] Atmp = new int[SilkConstants.SILK_MAX_ORDER_LPC];

            for (k = 0; k < order; k++)
            {
                for (n = 0; n < k; n++)
                {
                    Atmp[n] = A_Q24[n];
                }
                for (n = 0; n < k; n++)
                {
                    A_Q24[n] = Inlines.silk_SMLAWW(A_Q24[n], Atmp[k - n - 1], rc_Q16[k]);
                }
                A_Q24[k] = 0 - Inlines.silk_LSHIFT(rc_Q16[k], 8);
            }
        }

    }
}
