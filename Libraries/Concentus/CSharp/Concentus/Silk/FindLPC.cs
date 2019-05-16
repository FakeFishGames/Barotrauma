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

    internal static class FindLPC
    {
        /* Finds LPC vector from correlations, and converts to NLSF */
        internal static void silk_find_LPC(
            SilkChannelEncoder psEncC,                                /* I/O  Encoder state                                                               */
            short[] NLSF_Q15,                             /* O    NLSFs                                                                       */
            short[] x,                                    /* I    Input signal                                                                */
            int minInvGain_Q30                          /* I    Inverse of max prediction gain                                              */
        )
        {
            int k, subfr_length;
            int[] a_Q16 = new int[SilkConstants.MAX_LPC_ORDER];
            int isInterpLower, shift;
            int res_nrg0, res_nrg1;
            int rshift0, rshift1;
            BoxedValueInt scratch_box1 = new BoxedValueInt();
            BoxedValueInt scratch_box2 = new BoxedValueInt();

            /* Used only for LSF interpolation */
            int[] a_tmp_Q16 = new int[SilkConstants.MAX_LPC_ORDER];
            int res_nrg_interp, res_nrg, res_tmp_nrg;
            int res_nrg_interp_Q, res_nrg_Q, res_tmp_nrg_Q;
            short[] a_tmp_Q12 = new short[SilkConstants.MAX_LPC_ORDER];
            short[] NLSF0_Q15 = new short[SilkConstants.MAX_LPC_ORDER];
            
            subfr_length = psEncC.subfr_length + psEncC.predictLPCOrder;

            /* Default: no interpolation */
            psEncC.indices.NLSFInterpCoef_Q2 = 4;

            /* Burg AR analysis for the full frame */
            BurgModified.silk_burg_modified(scratch_box1, scratch_box2, a_Q16, x, 0, minInvGain_Q30, subfr_length, psEncC.nb_subfr, psEncC.predictLPCOrder);
            res_nrg = scratch_box1.Val;
            res_nrg_Q = scratch_box2.Val;

            if (psEncC.useInterpolatedNLSFs != 0 && psEncC.first_frame_after_reset == 0 && psEncC.nb_subfr == SilkConstants.MAX_NB_SUBFR)
            {
                short[] LPC_res;

                /* Optimal solution for last 10 ms */
                BurgModified.silk_burg_modified(scratch_box1, scratch_box2, a_tmp_Q16, x, (2 * subfr_length), minInvGain_Q30, subfr_length, 2, psEncC.predictLPCOrder);
                res_tmp_nrg = scratch_box1.Val;
                res_tmp_nrg_Q = scratch_box2.Val;

                /* subtract residual energy here, as that's easier than adding it to the    */
                /* residual energy of the first 10 ms in each iteration of the search below */
                shift = res_tmp_nrg_Q - res_nrg_Q;
                if (shift >= 0)
                {
                    if (shift < 32)
                    {
                        res_nrg = res_nrg - Inlines.silk_RSHIFT(res_tmp_nrg, shift);
                    }
                }
                else {
                    Inlines.OpusAssert(shift > -32);
                    res_nrg = Inlines.silk_RSHIFT(res_nrg, -shift) - res_tmp_nrg;
                    res_nrg_Q = res_tmp_nrg_Q;
                }

                /* Convert to NLSFs */
                NLSF.silk_A2NLSF(NLSF_Q15, a_tmp_Q16, psEncC.predictLPCOrder);

               LPC_res = new short[2 * subfr_length];

                /* Search over interpolation indices to find the one with lowest residual energy */
                for (k = 3; k >= 0; k--)
                {
                    /* Interpolate NLSFs for first half */
                    Inlines.silk_interpolate(NLSF0_Q15, psEncC.prev_NLSFq_Q15, NLSF_Q15, k, psEncC.predictLPCOrder);

                    /* Convert to LPC for residual energy evaluation */
                    NLSF.silk_NLSF2A(a_tmp_Q12, NLSF0_Q15, psEncC.predictLPCOrder);

                    /* Calculate residual energy with NLSF interpolation */
                    Filters.silk_LPC_analysis_filter(LPC_res, 0, x, 0, a_tmp_Q12, 0, 2 * subfr_length, psEncC.predictLPCOrder);
                    
                    SumSqrShift.silk_sum_sqr_shift(out res_nrg0, out rshift0, LPC_res, psEncC.predictLPCOrder, subfr_length - psEncC.predictLPCOrder);
                    
                    SumSqrShift.silk_sum_sqr_shift(out res_nrg1, out rshift1, LPC_res, psEncC.predictLPCOrder + subfr_length, subfr_length - psEncC.predictLPCOrder);

                    /* Add subframe energies from first half frame */
                    shift = rshift0 - rshift1;
                    if (shift >= 0)
                    {
                        res_nrg1 = Inlines.silk_RSHIFT(res_nrg1, shift);
                        res_nrg_interp_Q = -rshift0;
                    }
                    else {
                        res_nrg0 = Inlines.silk_RSHIFT(res_nrg0, -shift);
                        res_nrg_interp_Q = -rshift1;
                    }
                    res_nrg_interp = Inlines.silk_ADD32(res_nrg0, res_nrg1);

                    /* Compare with first half energy without NLSF interpolation, or best interpolated value so far */
                    shift = res_nrg_interp_Q - res_nrg_Q;
                    if (shift >= 0)
                    {
                        if (Inlines.silk_RSHIFT(res_nrg_interp, shift) < res_nrg)
                        {
                            isInterpLower = (true ? 1 : 0);
                        }
                        else {
                            isInterpLower = (false ? 1 : 0);
                        }
                    }
                    else {
                        if (-shift < 32)
                        {
                            if (res_nrg_interp < Inlines.silk_RSHIFT(res_nrg, -shift))
                            {
                                isInterpLower = (true ? 1 : 0);
                            }
                            else {
                                isInterpLower = (false ? 1 : 0);
                            }
                        }
                        else {
                            isInterpLower = (false ? 1 : 0);
                        }
                    }

                    /* Determine whether current interpolated NLSFs are best so far */
                    if (isInterpLower == (true ? 1 : 0))
                    {
                        /* Interpolation has lower residual energy */
                        res_nrg = res_nrg_interp;
                        res_nrg_Q = res_nrg_interp_Q;
                        psEncC.indices.NLSFInterpCoef_Q2 = (sbyte)k;
                    }
                }
            }

            if (psEncC.indices.NLSFInterpCoef_Q2 == 4)
            {
                /* NLSF interpolation is currently inactive, calculate NLSFs from full frame AR coefficients */
                NLSF.silk_A2NLSF(NLSF_Q15, a_Q16, psEncC.predictLPCOrder);
            }

            Inlines.OpusAssert(psEncC.indices.NLSFInterpCoef_Q2 == 4 || (psEncC.useInterpolatedNLSFs != 0 && psEncC.first_frame_after_reset == 0 && psEncC.nb_subfr == SilkConstants.MAX_NB_SUBFR));

        }
    }
}
