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
    using Celt;
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using Concentus.Silk.Enums;
    using Concentus.Silk.Structs;
    using System;
    using System.Diagnostics;

    internal static class PitchAnalysisCore
    {
        private const int SCRATCH_SIZE = 22;
        private const int SF_LENGTH_4KHZ = (SilkConstants.PE_SUBFR_LENGTH_MS * 4);
        private const int SF_LENGTH_8KHZ = (SilkConstants.PE_SUBFR_LENGTH_MS * 8);
        private const int MIN_LAG_4KHZ = (SilkConstants.PE_MIN_LAG_MS * 4);
        private const int MIN_LAG_8KHZ = (SilkConstants.PE_MIN_LAG_MS * 8);
        private const int MAX_LAG_4KHZ = (SilkConstants.PE_MAX_LAG_MS * 4);
        private const int MAX_LAG_8KHZ = (SilkConstants.PE_MAX_LAG_MS * 8 - 1);
        private const int CSTRIDE_4KHZ = (MAX_LAG_4KHZ + 1 - MIN_LAG_4KHZ);
        private const int CSTRIDE_8KHZ = (MAX_LAG_8KHZ + 3 - (MIN_LAG_8KHZ - 2));
        private const int D_COMP_MIN = (MIN_LAG_8KHZ - 3);
        private const int D_COMP_MAX = (MAX_LAG_8KHZ + 4);
        private const int D_COMP_STRIDE = (D_COMP_MAX - D_COMP_MIN);

        // typedef int silk_pe_stage3_vals[SilkConstants.PE_NB_STAGE3_LAGS];
        // fixme can I linearize this?
        private class silk_pe_stage3_vals
        {
            public readonly int[] Values = new int[SilkConstants.PE_NB_STAGE3_LAGS];
        }

        /*************************************************************/
        /*      FIXED POINT CORE PITCH ANALYSIS FUNCTION             */
        /*************************************************************/
        internal static int silk_pitch_analysis_core(                  /* O    Voicing estimate: 0 voiced, 1 unvoiced                      */
            short[] frame,             /* I    Signal of length PE_FRAME_LENGTH_MS*Fs_kHz                  */
            int[] pitch_out,         /* O    4 pitch lag values                                          */
            BoxedValueShort lagIndex,          /* O    Lag Index                                                   */
            BoxedValueSbyte contourIndex,      /* O    Pitch contour Index                                         */
            BoxedValueInt LTPCorr_Q15,       /* I/O  Normalized correlation; input: value from previous frame    */
            int prevLag,            /* I    Last lag of previous frame; set to zero is unvoiced         */
            int search_thres1_Q16,  /* I    First stage threshold for lag candidates 0 - 1              */
            int search_thres2_Q13,  /* I    Final threshold for lag candidates 0 - 1                    */
            int Fs_kHz,             /* I    Sample frequency (kHz)                                      */
            int complexity,         /* I    Complexity setting, 0-2, where 2 is highest                 */
            int nb_subfr           /* I    number of 5 ms subframes                                    */
        )
        {
            short[] frame_8kHz;
            short[] frame_4kHz;
            int[] filt_state = new int[6];
            short[] input_frame_ptr;
            int i, k, d, j;
            short[] C;
            int[] xcorr32;
            short[] basis;
            int basis_ptr;
            short[] target;
            int target_ptr;
            int cross_corr, normalizer, energy, shift, energy_basis, energy_target;
            int Cmax, length_d_srch, length_d_comp;
            int[] d_srch = new int[SilkConstants.PE_D_SRCH_LENGTH];
            short[] d_comp;
            int sum, threshold, lag_counter;
            int CBimax, CBimax_new, CBimax_old, lag, start_lag, end_lag, lag_new;
            int CCmax, CCmax_b, CCmax_new_b, CCmax_new;
            int[] CC = new int[SilkConstants.PE_NB_CBKS_STAGE2_EXT];
            silk_pe_stage3_vals[] energies_st3;
            silk_pe_stage3_vals[] cross_corr_st3;
            int frame_length, frame_length_8kHz, frame_length_4kHz;
            int sf_length;
            int min_lag;
            int max_lag;
            int contour_bias_Q15, diff;
            int nb_cbk_search;
            int delta_lag_log2_sqr_Q7, lag_log2_Q7, prevLag_log2_Q7, prev_lag_bias_Q13;
            sbyte[][] Lag_CB_ptr;

            /* Check for valid sampling frequency */
            Inlines.OpusAssert(Fs_kHz == 8 || Fs_kHz == 12 || Fs_kHz == 16);

            /* Check for valid complexity setting */
            Inlines.OpusAssert(complexity >= SilkConstants.SILK_PE_MIN_COMPLEX);
            Inlines.OpusAssert(complexity <= SilkConstants.SILK_PE_MAX_COMPLEX);

            Inlines.OpusAssert(search_thres1_Q16 >= 0 && search_thres1_Q16 <= (1 << 16));
            Inlines.OpusAssert(search_thres2_Q13 >= 0 && search_thres2_Q13 <= (1 << 13));

            /* Set up frame lengths max / min lag for the sampling frequency */
            frame_length = (SilkConstants.PE_LTP_MEM_LENGTH_MS + nb_subfr * SilkConstants.PE_SUBFR_LENGTH_MS) * Fs_kHz;
            frame_length_4kHz = (SilkConstants.PE_LTP_MEM_LENGTH_MS + nb_subfr * SilkConstants.PE_SUBFR_LENGTH_MS) * 4;
            frame_length_8kHz = (SilkConstants.PE_LTP_MEM_LENGTH_MS + nb_subfr * SilkConstants.PE_SUBFR_LENGTH_MS) * 8;
            sf_length = SilkConstants.PE_SUBFR_LENGTH_MS * Fs_kHz;
            min_lag = SilkConstants.PE_MIN_LAG_MS * Fs_kHz;
            max_lag = SilkConstants.PE_MAX_LAG_MS * Fs_kHz - 1;

            /* Resample from input sampled at Fs_kHz to 8 kHz */
            frame_8kHz = new short[frame_length_8kHz];
            if (Fs_kHz == 16)
            {
                Arrays.MemSetInt(filt_state, 0, 2);
                Resampler.silk_resampler_down2(filt_state, frame_8kHz, frame, frame_length);
            }
            else if (Fs_kHz == 12)
            {
                Arrays.MemSetInt(filt_state, 0, 6);
                Resampler.silk_resampler_down2_3(filt_state, frame_8kHz, frame, frame_length);
            }
            else {
                Inlines.OpusAssert(Fs_kHz == 8);
                Array.Copy(frame, frame_8kHz, frame_length_8kHz);
            }

            /* Decimate again to 4 kHz */
            Arrays.MemSetInt(filt_state, 0, 2); /* Set state to zero */
            frame_4kHz = new short[frame_length_4kHz];
            Resampler.silk_resampler_down2(filt_state, frame_4kHz, frame_8kHz, frame_length_8kHz);

            /* Low-pass filter */
            for (i = frame_length_4kHz - 1; i > 0; i--)
            {
                frame_4kHz[i] = Inlines.silk_ADD_SAT16(frame_4kHz[i], frame_4kHz[i - 1]);
            }

            /*******************************************************************************
            ** Scale 4 kHz signal down to prevent correlations measures from overflowing
            ** find scaling as max scaling for each 8kHz(?) subframe
            *******************************************************************************/

            /* Inner product is calculated with different lengths, so scale for the worst case */
            SumSqrShift.silk_sum_sqr_shift(out energy, out shift, frame_4kHz, frame_length_4kHz);
            if (shift > 0)
            {
                shift = Inlines.silk_RSHIFT(shift, 1);
                for (i = 0; i < frame_length_4kHz; i++)
                {
                    frame_4kHz[i] = Inlines.silk_RSHIFT16(frame_4kHz[i], shift);
                }
            }

            /******************************************************************************
            * FIRST STAGE, operating in 4 khz
            ******************************************************************************/
            C = new short[nb_subfr * CSTRIDE_8KHZ];
            xcorr32 = new int[MAX_LAG_4KHZ - MIN_LAG_4KHZ + 1];
            Arrays.MemSetShort(C, 0, (nb_subfr >> 1) * CSTRIDE_4KHZ);
            target = frame_4kHz;
            target_ptr = Inlines.silk_LSHIFT(SF_LENGTH_4KHZ, 2);
            for (k = 0; k < nb_subfr >> 1; k++)
            {
                basis = target;
                basis_ptr = target_ptr - MIN_LAG_4KHZ;

                CeltPitchXCorr.pitch_xcorr(target, target_ptr, target, target_ptr - MAX_LAG_4KHZ, xcorr32, SF_LENGTH_8KHZ, MAX_LAG_4KHZ - MIN_LAG_4KHZ + 1);

                /* Calculate first vector products before loop */
                cross_corr = xcorr32[MAX_LAG_4KHZ - MIN_LAG_4KHZ];
                normalizer = Inlines.silk_inner_prod_self(target, target_ptr, SF_LENGTH_8KHZ);
                normalizer = Inlines.silk_ADD32(normalizer, Inlines.silk_inner_prod_self(basis, basis_ptr, SF_LENGTH_8KHZ));
                normalizer = Inlines.silk_ADD32(normalizer, Inlines.silk_SMULBB(SF_LENGTH_8KHZ, 4000));

                Inlines.MatrixSet(C, k, 0, CSTRIDE_4KHZ,
                    (short)Inlines.silk_DIV32_varQ(cross_corr, normalizer, 13 + 1));                      /* Q13 */

                /* From now on normalizer is computed recursively */
                for (d = MIN_LAG_4KHZ + 1; d <= MAX_LAG_4KHZ; d++)
                {
                    basis_ptr--;

                    cross_corr = xcorr32[MAX_LAG_4KHZ - d];

                    /* Add contribution of new sample and remove contribution from oldest sample */
                    normalizer = Inlines.silk_ADD32(normalizer,
                        Inlines.silk_SMULBB(basis[basis_ptr], basis[basis_ptr]) -
                        Inlines.silk_SMULBB(basis[basis_ptr + SF_LENGTH_8KHZ], basis[basis_ptr + SF_LENGTH_8KHZ]));

                    Inlines.MatrixSet(C, k, d - MIN_LAG_4KHZ, CSTRIDE_4KHZ,
                        (short)Inlines.silk_DIV32_varQ(cross_corr, normalizer, 13 + 1));                  /* Q13 */
                }
                /* Update target pointer */
                target_ptr += SF_LENGTH_8KHZ;
            }

            /* Combine two subframes into single correlation measure and apply short-lag bias */
            if (nb_subfr == SilkConstants.PE_MAX_NB_SUBFR)
            {
                for (i = MAX_LAG_4KHZ; i >= MIN_LAG_4KHZ; i--)
                {
                    sum = (int)Inlines.MatrixGet(C, 0, i - MIN_LAG_4KHZ, CSTRIDE_4KHZ)
                        + (int)Inlines.MatrixGet(C, 1, i - MIN_LAG_4KHZ, CSTRIDE_4KHZ);               /* Q14 */
                    sum = Inlines.silk_SMLAWB(sum, sum, Inlines.silk_LSHIFT(-i, 4));                                /* Q14 */
                    C[i - MIN_LAG_4KHZ] = (short)sum;                                            /* Q14 */
                }
            }
            else {
                /* Only short-lag bias */
                for (i = MAX_LAG_4KHZ; i >= MIN_LAG_4KHZ; i--)
                {
                    sum = Inlines.silk_LSHIFT((int)C[i - MIN_LAG_4KHZ], 1);                          /* Q14 */
                    sum = Inlines.silk_SMLAWB(sum, sum, Inlines.silk_LSHIFT(-i, 4));                                /* Q14 */
                    C[i - MIN_LAG_4KHZ] = (short)sum;                                            /* Q14 */
                }
            }

            /* Sort */
            length_d_srch = Inlines.silk_ADD_LSHIFT32(4, complexity, 1);
            Inlines.OpusAssert(3 * length_d_srch <= SilkConstants.PE_D_SRCH_LENGTH);
            Sort.silk_insertion_sort_decreasing_int16(C, d_srch, CSTRIDE_4KHZ, length_d_srch);

            /* Escape if correlation is very low already here */
            Cmax = (int)C[0];                                                    /* Q14 */
            if (Cmax < ((int)((0.2f) * ((long)1 << (14)) + 0.5))/*Inlines.SILK_CONST(0.2f, 14)*/)
            {
                Arrays.MemSetInt(pitch_out, 0, nb_subfr);
                LTPCorr_Q15.Val = 0;
                lagIndex.Val = 0;
                contourIndex.Val = 0;

                return 1;
            }

            threshold = Inlines.silk_SMULWB(search_thres1_Q16, Cmax);
            for (i = 0; i < length_d_srch; i++)
            {
                /* Convert to 8 kHz indices for the sorted correlation that exceeds the threshold */
                if (C[i] > threshold)
                {
                    d_srch[i] = Inlines.silk_LSHIFT(d_srch[i] + MIN_LAG_4KHZ, 1);
                }
                else {
                    length_d_srch = i;
                    break;
                }
            }
            Inlines.OpusAssert(length_d_srch > 0);

            d_comp = new short[D_COMP_STRIDE];
            for (i = D_COMP_MIN; i < D_COMP_MAX; i++)
            {
                d_comp[i - D_COMP_MIN] = 0;
            }
            for (i = 0; i < length_d_srch; i++)
            {
                d_comp[d_srch[i] - D_COMP_MIN] = 1;
            }

            /* Convolution */
            for (i = D_COMP_MAX - 1; i >= MIN_LAG_8KHZ; i--)
            {
                d_comp[i - D_COMP_MIN] += (short)(d_comp[i - 1 - D_COMP_MIN] + d_comp[i - 2 - D_COMP_MIN]);
            }

            length_d_srch = 0;
            for (i = MIN_LAG_8KHZ; i < MAX_LAG_8KHZ + 1; i++)
            {
                if (d_comp[i + 1 - D_COMP_MIN] > 0)
                {
                    d_srch[length_d_srch] = i;
                    length_d_srch++;
                }
            }

            /* Convolution */
            for (i = D_COMP_MAX - 1; i >= MIN_LAG_8KHZ; i--)
            {
                d_comp[i - D_COMP_MIN] += (short)(d_comp[i - 1 - D_COMP_MIN] + d_comp[i - 2 - D_COMP_MIN] + d_comp[i - 3 - D_COMP_MIN]);
            }

            length_d_comp = 0;
            for (i = MIN_LAG_8KHZ; i < D_COMP_MAX; i++)
            {
                if (d_comp[i - D_COMP_MIN] > 0)
                {
                    d_comp[length_d_comp] = (short)(i - 2);
                    length_d_comp++;
                }
            }

            /**********************************************************************************
            ** SECOND STAGE, operating at 8 kHz, on lag sections with high correlation
            *************************************************************************************/

            /******************************************************************************
            ** Scale signal down to avoid correlations measures from overflowing
            *******************************************************************************/
            /* find scaling as max scaling for each subframe */
            SumSqrShift.silk_sum_sqr_shift(out energy, out shift, frame_8kHz, frame_length_8kHz);
            if (shift > 0)
            {
                shift = Inlines.silk_RSHIFT(shift, 1);
                for (i = 0; i < frame_length_8kHz; i++)
                {
                    frame_8kHz[i] = Inlines.silk_RSHIFT16(frame_8kHz[i], shift);
                }
            }

            /*********************************************************************************
            * Find energy of each subframe projected onto its history, for a range of delays
            *********************************************************************************/
            Arrays.MemSetShort(C, 0, nb_subfr * CSTRIDE_8KHZ );

            target = frame_8kHz;
            target_ptr = SilkConstants.PE_LTP_MEM_LENGTH_MS * 8;
            for (k = 0; k < nb_subfr; k++)
            {

                energy_target = Inlines.silk_ADD32(Inlines.silk_inner_prod(target, target_ptr, target, target_ptr, SF_LENGTH_8KHZ), 1);
                for (j = 0; j < length_d_comp; j++)
                {
                    d = d_comp[j];
                    basis = target;
                    basis_ptr = target_ptr - d;

                    cross_corr = Inlines.silk_inner_prod(target, target_ptr, basis, basis_ptr, SF_LENGTH_8KHZ);
                    if (cross_corr > 0)
                    {
                        energy_basis = Inlines.silk_inner_prod_self(basis, basis_ptr, SF_LENGTH_8KHZ);
                        Inlines.MatrixSet(C, k, d - (MIN_LAG_8KHZ - 2), CSTRIDE_8KHZ,
                            (short)Inlines.silk_DIV32_varQ(cross_corr,
                                                         Inlines.silk_ADD32(energy_target,
                                                                     energy_basis),
                                                         13 + 1));                                      /* Q13 */
                    }
                    else {
                        Inlines.MatrixSet<short>(C, k, d - (MIN_LAG_8KHZ - 2), CSTRIDE_8KHZ, 0);
                    }
                }
                target_ptr += SF_LENGTH_8KHZ;
            }

            /* search over lag range and lags codebook */
            /* scale factor for lag codebook, as a function of center lag */

            CCmax = int.MinValue;
            CCmax_b = int.MinValue;

            CBimax = 0; /* To avoid returning undefined lag values */
            lag = -1;   /* To check if lag with strong enough correlation has been found */

            if (prevLag > 0)
            {
                if (Fs_kHz == 12)
                {
                    prevLag = Inlines.silk_DIV32_16(Inlines.silk_LSHIFT(prevLag, 1), 3);
                }
                else if (Fs_kHz == 16)
                {
                    prevLag = Inlines.silk_RSHIFT(prevLag, 1);
                }
                prevLag_log2_Q7 = Inlines.silk_lin2log((int)prevLag);
            }
            else {
                prevLag_log2_Q7 = 0;
            }
            Inlines.OpusAssert(search_thres2_Q13 == Inlines.silk_SAT16(search_thres2_Q13));
            /* Set up stage 2 codebook based on number of subframes */
            if (nb_subfr == SilkConstants.PE_MAX_NB_SUBFR)
            {
                Lag_CB_ptr = Tables.silk_CB_lags_stage2;
                if (Fs_kHz == 8 && complexity > SilkConstants.SILK_PE_MIN_COMPLEX)
                {
                    /* If input is 8 khz use a larger codebook here because it is last stage */
                    nb_cbk_search = SilkConstants.PE_NB_CBKS_STAGE2_EXT;
                }
                else {
                    nb_cbk_search = SilkConstants.PE_NB_CBKS_STAGE2;
                }
            }
            else {
                Lag_CB_ptr = Tables.silk_CB_lags_stage2_10_ms;
                nb_cbk_search = SilkConstants.PE_NB_CBKS_STAGE2_10MS;
            }

            for (k = 0; k < length_d_srch; k++)
            {
                d = d_srch[k];
                for (j = 0; j < nb_cbk_search; j++)
                {
                    CC[j] = 0;
                    for (i = 0; i < nb_subfr; i++)
                    {
                        int d_subfr;
                        /* Try all codebooks */
                        d_subfr = d + Lag_CB_ptr[i][j];
                        CC[j] = CC[j]
                           + (int)Inlines.MatrixGet(C, i,
                                                    d_subfr - (MIN_LAG_8KHZ - 2),
                                                    CSTRIDE_8KHZ);
                    }
                }
                /* Find best codebook */
                CCmax_new = int.MinValue;
                CBimax_new = 0;
                for (i = 0; i < nb_cbk_search; i++)
                {
                    if (CC[i] > CCmax_new)
                    {
                        CCmax_new = CC[i];
                        CBimax_new = i;
                    }
                }

                /* Bias towards shorter lags */
                lag_log2_Q7 = Inlines.silk_lin2log(d); /* Q7 */
                Inlines.OpusAssert(lag_log2_Q7 == Inlines.silk_SAT16(lag_log2_Q7));
                Inlines.OpusAssert(nb_subfr * ((int)((SilkConstants.PE_SHORTLAG_BIAS) * ((long)1 << (13)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.PE_SHORTLAG_BIAS, 13)*/ == Inlines.silk_SAT16(nb_subfr * ((int)((SilkConstants.PE_SHORTLAG_BIAS) * ((long)1 << (13)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.PE_SHORTLAG_BIAS, 13)*/));
                CCmax_new_b = CCmax_new - Inlines.silk_RSHIFT(Inlines.silk_SMULBB(nb_subfr * ((int)((SilkConstants.PE_SHORTLAG_BIAS) * ((long)1 << (13)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.PE_SHORTLAG_BIAS, 13)*/, lag_log2_Q7), 7); /* Q13 */

                /* Bias towards previous lag */
                Inlines.OpusAssert(nb_subfr * ((int)((SilkConstants.PE_PREVLAG_BIAS) * ((long)1 << (13)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.PE_PREVLAG_BIAS, 13)*/ == Inlines.silk_SAT16(nb_subfr * ((int)((SilkConstants.PE_PREVLAG_BIAS) * ((long)1 << (13)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.PE_PREVLAG_BIAS, 13)*/));
                if (prevLag > 0)
                {
                    delta_lag_log2_sqr_Q7 = lag_log2_Q7 - prevLag_log2_Q7;
                    Inlines.OpusAssert(delta_lag_log2_sqr_Q7 == Inlines.silk_SAT16(delta_lag_log2_sqr_Q7));
                    delta_lag_log2_sqr_Q7 = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(delta_lag_log2_sqr_Q7, delta_lag_log2_sqr_Q7), 7);
                    prev_lag_bias_Q13 = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(nb_subfr * ((int)((SilkConstants.PE_PREVLAG_BIAS) * ((long)1 << (13)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.PE_PREVLAG_BIAS, 13)*/, LTPCorr_Q15.Val), 15); /* Q13 */
                    prev_lag_bias_Q13 = Inlines.silk_DIV32(Inlines.silk_MUL(prev_lag_bias_Q13, delta_lag_log2_sqr_Q7), delta_lag_log2_sqr_Q7 + ((int)((0.5f) * ((long)1 << (7)) + 0.5))/*Inlines.SILK_CONST(0.5f, 7)*/);
                    CCmax_new_b -= prev_lag_bias_Q13; /* Q13 */
                }

                if (CCmax_new_b > CCmax_b &&  /* Find maximum biased correlation                  */
                    CCmax_new > Inlines.silk_SMULBB(nb_subfr, search_thres2_Q13) &&  /* Correlation needs to be high enough to be voiced */
                    Tables.silk_CB_lags_stage2[0][CBimax_new] <= MIN_LAG_8KHZ      /* Lag must be in range                             */
                 )
                {
                    CCmax_b = CCmax_new_b;
                    CCmax = CCmax_new;
                    lag = d;
                    CBimax = CBimax_new;
                }
            }

            if (lag == -1)
            {
                /* No suitable candidate found */
                Arrays.MemSetInt(pitch_out, 0, nb_subfr);
                LTPCorr_Q15.Val = 0;
                lagIndex.Val = 0;
                contourIndex.Val = 0;

                return 1;
            }

            /* Output normalized correlation */
            LTPCorr_Q15.Val = (int)Inlines.silk_LSHIFT(Inlines.silk_DIV32_16(CCmax, nb_subfr), 2);
            Inlines.OpusAssert(LTPCorr_Q15.Val >= 0);

            if (Fs_kHz > 8)
            {
                short[] scratch_mem;
                /***************************************************************************/
                /* Scale input signal down to avoid correlations measures from overflowing */
                /***************************************************************************/
                /* find scaling as max scaling for each subframe */
                SumSqrShift.silk_sum_sqr_shift(out energy, out shift, frame, frame_length);
                if (shift > 0)
                {
                    scratch_mem = new short[frame_length];
                    /* Move signal to scratch mem because the input signal should be unchanged */
                    shift = Inlines.silk_RSHIFT(shift, 1);
                    for (i = 0; i < frame_length; i++)
                    {
                        scratch_mem[i] = Inlines.silk_RSHIFT16(frame[i], shift);
                    }
                    input_frame_ptr = scratch_mem;
                }
                else {
                    input_frame_ptr = frame;
                }

                /* Search in original signal */
                CBimax_old = CBimax;
                /* Compensate for decimation */
                Inlines.OpusAssert(lag == Inlines.silk_SAT16(lag));
                if (Fs_kHz == 12)
                {
                    lag = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(lag, 3), 1);
                }
                else if (Fs_kHz == 16)
                {
                    lag = Inlines.silk_LSHIFT(lag, 1);
                }
                else {
                    lag = Inlines.silk_SMULBB(lag, 3);
                }

                lag = Inlines.silk_LIMIT_int(lag, min_lag, max_lag);
                start_lag = Inlines.silk_max_int(lag - 2, min_lag);
                end_lag = Inlines.silk_min_int(lag + 2, max_lag);
                lag_new = lag;                                    /* to avoid undefined lag */
                CBimax = 0;                                      /* to avoid undefined lag */

                CCmax = int.MinValue;
                /* pitch lags according to second stage */
                for (k = 0; k < nb_subfr; k++)
                {
                    pitch_out[k] = lag + 2 * Tables.silk_CB_lags_stage2[k][CBimax_old];
                }

                /* Set up codebook parameters according to complexity setting and frame length */
                if (nb_subfr == SilkConstants.PE_MAX_NB_SUBFR)
                {
                    nb_cbk_search = (int)Tables.silk_nb_cbk_searchs_stage3[complexity];
                    Lag_CB_ptr = Tables.silk_CB_lags_stage3;
                }
                else {
                    nb_cbk_search = SilkConstants.PE_NB_CBKS_STAGE3_10MS;
                    Lag_CB_ptr = Tables.silk_CB_lags_stage3_10_ms;
                }

                /* Calculate the correlations and energies needed in stage 3 */
                energies_st3 = new silk_pe_stage3_vals[nb_subfr * nb_cbk_search];
                cross_corr_st3 = new silk_pe_stage3_vals[nb_subfr * nb_cbk_search];
                for (int c = 0; c < nb_subfr * nb_cbk_search; c++)
                {
                    energies_st3[c] = new silk_pe_stage3_vals(); // fixme: these can be replaced with a linearized array probably, or at least a struct
                    cross_corr_st3[c] = new silk_pe_stage3_vals();
                }
                silk_P_Ana_calc_corr_st3(cross_corr_st3, input_frame_ptr, start_lag, sf_length, nb_subfr, complexity);
                silk_P_Ana_calc_energy_st3(energies_st3, input_frame_ptr, start_lag, sf_length, nb_subfr, complexity);

                lag_counter = 0;
                Inlines.OpusAssert(lag == Inlines.silk_SAT16(lag));
                contour_bias_Q15 = Inlines.silk_DIV32_16(((int)((SilkConstants.PE_FLATCONTOUR_BIAS) * ((long)1 << (15)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.PE_FLATCONTOUR_BIAS, 15)*/, lag);

                target = input_frame_ptr;
                target_ptr = SilkConstants.PE_LTP_MEM_LENGTH_MS * Fs_kHz;
                energy_target = Inlines.silk_ADD32(Inlines.silk_inner_prod_self(target, target_ptr, nb_subfr * sf_length), 1);
                for (d = start_lag; d <= end_lag; d++)
                {
                    for (j = 0; j < nb_cbk_search; j++)
                    {
                        cross_corr = 0;
                        energy = energy_target;
                        for (k = 0; k < nb_subfr; k++)
                        {
                            cross_corr = Inlines.silk_ADD32(cross_corr,
                                Inlines.MatrixGet(cross_corr_st3, k, j,
                                            nb_cbk_search).Values[lag_counter]);
                            energy = Inlines.silk_ADD32(energy,
                                Inlines.MatrixGet(energies_st3, k, j,
                                            nb_cbk_search).Values[lag_counter]);
                            Inlines.OpusAssert(energy >= 0);
                        }
                        if (cross_corr > 0)
                        {
                            CCmax_new = Inlines.silk_DIV32_varQ(cross_corr, energy, 13 + 1);          /* Q13 */
                                                                                                      /* Reduce depending on flatness of contour */
                            diff = short.MaxValue - Inlines.silk_MUL(contour_bias_Q15, j);            /* Q15 */
                            Inlines.OpusAssert(diff == Inlines.silk_SAT16(diff));
                            CCmax_new = Inlines.silk_SMULWB(CCmax_new, diff);                         /* Q14 */
                        }
                        else {
                            CCmax_new = 0;
                        }

                        if (CCmax_new > CCmax && (d + Tables.silk_CB_lags_stage3[0][j]) <= max_lag)
                        {
                            CCmax = CCmax_new;
                            lag_new = d;
                            CBimax = j;
                        }
                    }
                    lag_counter++;
                }

                for (k = 0; k < nb_subfr; k++)
                {
                    pitch_out[k] = lag_new + Lag_CB_ptr[k][CBimax];
                    pitch_out[k] = Inlines.silk_LIMIT(pitch_out[k], min_lag, SilkConstants.PE_MAX_LAG_MS * Fs_kHz);
                }
                lagIndex.Val = (short)(lag_new - min_lag);
                contourIndex.Val = (sbyte)CBimax;
            }
            else {        /* Fs_kHz == 8 */
                          /* Save Lags */
                for (k = 0; k < nb_subfr; k++)
                {
                    pitch_out[k] = lag + Lag_CB_ptr[k][CBimax];
                    pitch_out[k] = Inlines.silk_LIMIT(pitch_out[k], MIN_LAG_8KHZ, SilkConstants.PE_MAX_LAG_MS * 8);
                }
                lagIndex.Val = (short)(lag - MIN_LAG_8KHZ);
                contourIndex.Val = (sbyte)CBimax;
            }
            Inlines.OpusAssert(lagIndex.Val >= 0);
            /* return as voiced */

            return 0;
        }

        /***********************************************************************
         * Calculates the correlations used in stage 3 search. In order to cover
         * the whole lag codebook for all the searched offset lags (lag +- 2),
         * the following correlations are needed in each sub frame:
         *
         * sf1: lag range [-8,...,7] total 16 correlations
         * sf2: lag range [-4,...,4] total 9 correlations
         * sf3: lag range [-3,....4] total 8 correltions
         * sf4: lag range [-6,....8] total 15 correlations
         *
         * In total 48 correlations. The direct implementation computed in worst
         * case 4*12*5 = 240 correlations, but more likely around 120.
         ***********************************************************************/
        private static void silk_P_Ana_calc_corr_st3(
            silk_pe_stage3_vals[] cross_corr_st3,              /* O 3 DIM correlation array */
            short[] frame,                         /* I vector to correlate         */
            int start_lag,                       /* I lag offset to search around */
            int sf_length,                       /* I length of a 5 ms subframe   */
            int nb_subfr,                        /* I number of subframes         */
            int complexity                      /* I Complexity setting          */
        )
        {
            int target_ptr;
            int i, j, k, lag_counter, lag_low, lag_high;
            int nb_cbk_search, delta, idx;
            int[] scratch_mem;
            int[] xcorr32;
            sbyte[][] Lag_range_ptr;
            sbyte[][] Lag_CB_ptr;
            
            Inlines.OpusAssert(complexity >= SilkConstants.SILK_PE_MIN_COMPLEX);
            Inlines.OpusAssert(complexity <= SilkConstants.SILK_PE_MAX_COMPLEX);

            if (nb_subfr == SilkConstants.PE_MAX_NB_SUBFR)
            {
                Lag_range_ptr = Tables.silk_Lag_range_stage3[complexity];
                Lag_CB_ptr = Tables.silk_CB_lags_stage3;
                nb_cbk_search = Tables.silk_nb_cbk_searchs_stage3[complexity];
            }
            else {
                Inlines.OpusAssert(nb_subfr == SilkConstants.PE_MAX_NB_SUBFR >> 1);
                Lag_range_ptr = Tables.silk_Lag_range_stage3_10_ms;
                Lag_CB_ptr = Tables.silk_CB_lags_stage3_10_ms;
                nb_cbk_search = SilkConstants.PE_NB_CBKS_STAGE3_10MS;
            }
            scratch_mem = new int[SCRATCH_SIZE];
            xcorr32 = new int[SCRATCH_SIZE];

            target_ptr = Inlines.silk_LSHIFT(sf_length, 2); /* Pointer to middle of frame */
            for (k = 0; k < nb_subfr; k++)
            {
                lag_counter = 0;

                /* Calculate the correlations for each subframe */
                lag_low = Lag_range_ptr[k][0];
                lag_high = Lag_range_ptr[k][1];
                Inlines.OpusAssert(lag_high - lag_low + 1 <= SCRATCH_SIZE);
                CeltPitchXCorr.pitch_xcorr(frame, target_ptr, frame, target_ptr - start_lag - lag_high, xcorr32, sf_length, lag_high - lag_low + 1);
                for (j = lag_low; j <= lag_high; j++)
                {
                    Inlines.OpusAssert(lag_counter < SCRATCH_SIZE);
                    scratch_mem[lag_counter] = xcorr32[lag_high - j];
                    lag_counter++;
                }

                delta = Lag_range_ptr[k][0];
                for (i = 0; i < nb_cbk_search; i++)
                {
                    /* Fill out the 3 dim array that stores the correlations for */
                    /* each code_book vector for each start lag */
                    idx = Lag_CB_ptr[k][i] - delta;
                    for (j = 0; j < SilkConstants.PE_NB_STAGE3_LAGS; j++)
                    {
                        Inlines.OpusAssert(idx + j < SCRATCH_SIZE);
                        Inlines.OpusAssert(idx + j < lag_counter);
                        Inlines.MatrixGet(cross_corr_st3, k, i, nb_cbk_search).Values[j] =
                            scratch_mem[idx + j];
                    }
                }
                target_ptr += sf_length;
            }

        }

        /********************************************************************/
        /* Calculate the energies for first two subframes. The energies are */
        /* calculated recursively.                                          */
        /********************************************************************/
        static void silk_P_Ana_calc_energy_st3(
            silk_pe_stage3_vals[] energies_st3,                 /* O 3 DIM energy array */
            short[] frame,                          /* I vector to calc energy in    */
            int start_lag,                        /* I lag offset to search around */
            int sf_length,                        /* I length of one 5 ms subframe */
            int nb_subfr,                         /* I number of subframes         */
            int complexity                       /* I Complexity setting          */
        )
        {
            int target_ptr, basis_ptr;
            int energy;
            int k, i, j, lag_counter;
            int nb_cbk_search, delta, idx, lag_diff;
            int[] scratch_mem;
            sbyte[][] Lag_range_ptr;
            sbyte[][] Lag_CB_ptr;


            Inlines.OpusAssert(complexity >= SilkConstants.SILK_PE_MIN_COMPLEX);
            Inlines.OpusAssert(complexity <= SilkConstants.SILK_PE_MAX_COMPLEX);

            if (nb_subfr == SilkConstants.PE_MAX_NB_SUBFR)
            {
                Lag_range_ptr = Tables.silk_Lag_range_stage3[complexity];
                Lag_CB_ptr = Tables.silk_CB_lags_stage3;
                nb_cbk_search = Tables.silk_nb_cbk_searchs_stage3[complexity];
            }
            else {
                Inlines.OpusAssert(nb_subfr == SilkConstants.PE_MAX_NB_SUBFR >> 1);
                Lag_range_ptr = Tables.silk_Lag_range_stage3_10_ms;
                Lag_CB_ptr = Tables.silk_CB_lags_stage3_10_ms;
                nb_cbk_search = SilkConstants.PE_NB_CBKS_STAGE3_10MS;
            }
            scratch_mem = new int[SCRATCH_SIZE];

            target_ptr = Inlines.silk_LSHIFT(sf_length, 2);
            for (k = 0; k < nb_subfr; k++)
            {
                lag_counter = 0;

                /* Calculate the energy for first lag */
                basis_ptr = target_ptr - (start_lag + Lag_range_ptr[k][0]);
                energy = Inlines.silk_inner_prod_self(frame, basis_ptr, sf_length);
                Inlines.OpusAssert(energy >= 0);
                scratch_mem[lag_counter] = energy;
                lag_counter++;

                lag_diff = (Lag_range_ptr[k][1] - Lag_range_ptr[k][0] + 1);
                for (i = 1; i < lag_diff; i++)
                {
                    /* remove part outside new window */
                    energy -= Inlines.silk_SMULBB(frame[basis_ptr + sf_length - i], frame[basis_ptr + sf_length - i]);
                    Inlines.OpusAssert(energy >= 0);

                    /* add part that comes into window */
                    energy = Inlines.silk_ADD_SAT32(energy, Inlines.silk_SMULBB(frame[basis_ptr - i], frame[basis_ptr - i]));
                    Inlines.OpusAssert(energy >= 0);
                    Inlines.OpusAssert(lag_counter < SCRATCH_SIZE);
                    scratch_mem[lag_counter] = energy;
                    lag_counter++;
                }

                delta = Lag_range_ptr[k][0];
                for (i = 0; i < nb_cbk_search; i++)
                {
                    /* Fill out the 3 dim array that stores the correlations for    */
                    /* each code_book vector for each start lag                     */
                    idx = Lag_CB_ptr[k][i] - delta;
                    for (j = 0; j < SilkConstants.PE_NB_STAGE3_LAGS; j++)
                    {
                        Inlines.OpusAssert(idx + j < SCRATCH_SIZE);
                        Inlines.OpusAssert(idx + j < lag_counter);
                        Inlines.MatrixGet(energies_st3, k, i, nb_cbk_search).Values[j] = scratch_mem[idx + j];
                        Inlines.OpusAssert(Inlines.MatrixGet(energies_st3, k, i, nb_cbk_search).Values[j] >= 0);
                    }
                }
                target_ptr += sf_length;
            }
        }
    }
}
