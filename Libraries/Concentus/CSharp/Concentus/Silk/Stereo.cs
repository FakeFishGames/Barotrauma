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
    using System;
    using System.Diagnostics;

    internal static class Stereo
    {
        /// <summary>
        /// Decode mid/side predictors
        /// </summary>
        /// <param name="psRangeDec">I/O  Compressor data structure</param>
        /// <param name="pred_Q13">O Predictors</param>
        internal static void silk_stereo_decode_pred(
            EntropyCoder psRangeDec,
            int[] pred_Q13)
        {
            int n;
            int[][] ix = Arrays.InitTwoDimensionalArray<int>(2, 3);
            int low_Q13, step_Q13;

            // Entropy decoding
            n = psRangeDec.dec_icdf(Tables.silk_stereo_pred_joint_iCDF, 8);
            ix[0][2] = Inlines.silk_DIV32_16(n, 5);
            ix[1][2] = n - 5 * ix[0][2];
            for (n = 0; n < 2; n++)
            {
                ix[n][0] = psRangeDec.dec_icdf(Tables.silk_uniform3_iCDF, 8);
                ix[n][1] = psRangeDec.dec_icdf(Tables.silk_uniform5_iCDF, 8);
            }

            // Dequantize
            for (n = 0; n < 2; n++)
            {
                ix[n][0] += 3 * ix[n][2];
                low_Q13 = Tables.silk_stereo_pred_quant_Q13[ix[n][0]];
                step_Q13 = Inlines.silk_SMULWB(Tables.silk_stereo_pred_quant_Q13[ix[n][0] + 1] - low_Q13,
                    ((int)((0.5f / SilkConstants.STEREO_QUANT_SUB_STEPS) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(0.5f / SilkConstants.STEREO_QUANT_SUB_STEPS, 16)*/);
                pred_Q13[n] = Inlines.silk_SMLABB(low_Q13, step_Q13, 2 * ix[n][1] + 1);
            }

            /* Subtract second from first predictor (helps when actually applying these) */
            pred_Q13[0] -= pred_Q13[1];
        }

        /// <summary>
        /// Decode mid-only flag
        /// </summary>
        /// <param name="psRangeDec">I/O  Compressor data structure</param>
        /// <param name="decode_only_mid">O    Flag that only mid channel has been coded</param>
        internal static void silk_stereo_decode_mid_only(
            EntropyCoder psRangeDec,
            BoxedValueInt decode_only_mid
        )
        {
            /* Decode flag that only mid channel is coded */
            decode_only_mid.Val = psRangeDec.dec_icdf(Tables.silk_stereo_only_code_mid_iCDF, 8);
        }

        /// <summary>
        /// Entropy code the mid/side quantization indices
        /// </summary>
        /// <param name="psRangeEnc">I/O  Compressor data structure</param>
        /// <param name="ix">I    Quantization indices [ 2 ][ 3 ]</param>
        internal static void silk_stereo_encode_pred(EntropyCoder psRangeEnc, sbyte[][] ix)
        {
            int n;

            /* Entropy coding */
            n = 5 * ix[0][2] + ix[1][2];
            Inlines.OpusAssert(n < 25);
            psRangeEnc.enc_icdf( n, Tables.silk_stereo_pred_joint_iCDF, 8);
            for (n = 0; n < 2; n++)
            {
                Inlines.OpusAssert(ix[n][0] < 3);
                Inlines.OpusAssert(ix[n][1] < SilkConstants.STEREO_QUANT_SUB_STEPS);
                psRangeEnc.enc_icdf( ix[n][0], Tables.silk_uniform3_iCDF, 8);
                psRangeEnc.enc_icdf( ix[n][1], Tables.silk_uniform5_iCDF, 8);
            }
        }

        /// <summary>
        /// Entropy code the mid-only flag
        /// </summary>
        /// <param name="psRangeEnc">I/O  Compressor data structure</param>
        /// <param name="mid_only_flag"></param>
        internal static void silk_stereo_encode_mid_only(EntropyCoder psRangeEnc, sbyte mid_only_flag)
        {
            /* Encode flag that only mid channel is coded */
            psRangeEnc.enc_icdf( mid_only_flag, Tables.silk_stereo_only_code_mid_iCDF, 8);
        }

        /// <summary>
        /// Find least-squares prediction gain for one signal based on another and quantize it
        /// </summary>
        /// <param name="ratio_Q14">O    Ratio of residual and mid energies</param>
        /// <param name="x">I    Basis signal</param>
        /// <param name="y">I    Target signal</param>
        /// <param name="mid_res_amp_Q0">I/O  Smoothed mid, residual norms</param>
        /// <param name="length">I    Number of samples</param>
        /// <param name="smooth_coef_Q16">I    Smoothing coefficient</param>
        /// <returns>O    Returns predictor in Q13</returns>
        internal static int silk_stereo_find_predictor(
            BoxedValueInt ratio_Q14,
            short[] x,
            short[] y,
            int[] mid_res_amp_Q0,
            int mid_res_amp_Q0_ptr,
            int length,
            int smooth_coef_Q16)
        {
            int scale;
            int nrgx, nrgy, scale1, scale2;
            int corr, pred_Q13, pred2_Q10;

            /* Find predictor */
            SumSqrShift.silk_sum_sqr_shift(out nrgx, out scale1, x, length);
            SumSqrShift.silk_sum_sqr_shift(out nrgy, out scale2, y, length);
            scale = Inlines.silk_max_int(scale1, scale2);
            scale = scale + (scale & 1);          /* make even */
            nrgy = Inlines.silk_RSHIFT32(nrgy, scale - scale2);
            nrgx = Inlines.silk_RSHIFT32(nrgx, scale - scale1);
            nrgx = Inlines.silk_max_int(nrgx, 1);
            corr = Inlines.silk_inner_prod_aligned_scale(x, y, scale, length);
            pred_Q13 = Inlines.silk_DIV32_varQ(corr, nrgx, 13);
            pred_Q13 = Inlines.silk_LIMIT(pred_Q13, -(1 << 14), 1 << 14);
            pred2_Q10 = Inlines.silk_SMULWB(pred_Q13, pred_Q13);

            /* Faster update for signals with large prediction parameters */
            smooth_coef_Q16 = (int)Inlines.silk_max_int(smooth_coef_Q16, Inlines.silk_abs(pred2_Q10));

            /* Smoothed mid and residual norms */
            Inlines.OpusAssert(smooth_coef_Q16 < 32768);
            scale = Inlines.silk_RSHIFT(scale, 1);
            mid_res_amp_Q0[mid_res_amp_Q0_ptr] = Inlines.silk_SMLAWB(mid_res_amp_Q0[mid_res_amp_Q0_ptr],
                Inlines.silk_LSHIFT(Inlines.silk_SQRT_APPROX(nrgx), scale) - mid_res_amp_Q0[mid_res_amp_Q0_ptr], smooth_coef_Q16);
            /* Residual energy = nrgy - 2 * pred * corr + pred^2 * nrgx */
            nrgy = Inlines.silk_SUB_LSHIFT32(nrgy, Inlines.silk_SMULWB(corr, pred_Q13), 3 + 1);
            nrgy = Inlines.silk_ADD_LSHIFT32(nrgy, Inlines.silk_SMULWB(nrgx, pred2_Q10), 6);
            mid_res_amp_Q0[mid_res_amp_Q0_ptr + 1] = Inlines.silk_SMLAWB(mid_res_amp_Q0[mid_res_amp_Q0_ptr + 1],
                Inlines.silk_LSHIFT(Inlines.silk_SQRT_APPROX(nrgy), scale) - mid_res_amp_Q0[mid_res_amp_Q0_ptr + 1], smooth_coef_Q16);

            /* Ratio of smoothed residual and mid norms */
            ratio_Q14.Val = Inlines.silk_DIV32_varQ(mid_res_amp_Q0[mid_res_amp_Q0_ptr + 1], Inlines.silk_max(mid_res_amp_Q0[mid_res_amp_Q0_ptr], 1), 14);
            ratio_Q14.Val = Inlines.silk_LIMIT(ratio_Q14.Val, 0, 32767);

            return pred_Q13;
        }

        /// <summary>
        /// Convert Left/Right stereo signal to adaptive Mid/Side representation
        /// </summary>
        /// <param name="state">I/O  State</param>
        /// <param name="x1">I/O  Left input signal, becomes mid signal</param>
        /// <param name="x2">I/O  Right input signal, becomes side signal</param>
        /// <param name="ix">O    Quantization indices [ 2 ][ 3 ]</param>
        /// <param name="mid_only_flag">O    Flag: only mid signal coded</param>
        /// <param name="mid_side_rates_bps">O    Bitrates for mid and side signals</param>
        /// <param name="total_rate_bps">I    Total bitrate</param>
        /// <param name="prev_speech_act_Q8">I    Speech activity level in previous frame</param>
        /// <param name="toMono">I    Last frame before a stereo.mono transition</param>
        /// <param name="fs_kHz">I    Sample rate (kHz)</param>
        /// <param name="frame_length">I    Number of samples</param>
        internal static void silk_stereo_LR_to_MS(
            StereoEncodeState state,
            short[] x1,
            int x1_ptr,
            short[] x2,
            int x2_ptr,
            sbyte[][] ix,
            BoxedValueSbyte mid_only_flag,
            int[] mid_side_rates_bps,
            int total_rate_bps,
            int prev_speech_act_Q8,
            int toMono,
            int fs_kHz,
            int frame_length)
        {
            int n, is10msFrame, denom_Q16, delta0_Q13, delta1_Q13;
            int sum, diff, smooth_coef_Q16, pred0_Q13, pred1_Q13;
            int[] pred_Q13 = new int[2];
            int frac_Q16, frac_3_Q16, min_mid_rate_bps, width_Q14, w_Q24, deltaw_Q24;
            BoxedValueInt LP_ratio_Q14 = new BoxedValueInt();
            BoxedValueInt HP_ratio_Q14 = new BoxedValueInt();
            short[] side;
            short[] LP_mid;
            short[] HP_mid;
            short[] LP_side;
            short[] HP_side;
            int mid = x1_ptr - 2;

            side = new short[frame_length + 2];

            /* Convert to basic mid/side signals */
            for (n = 0; n < frame_length + 2; n++)
            {
                sum = x1[x1_ptr + n - 2] + (int)x2[x2_ptr + n - 2];
                diff = x1[x1_ptr + n - 2] - (int)x2[x2_ptr + n - 2];
                x1[mid + n] = (short)Inlines.silk_RSHIFT_ROUND(sum, 1);
                side[n] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(diff, 1));
            }

            /* Buffering */
            Array.Copy(state.sMid, 0, x1, mid, 2);
            Array.Copy(state.sSide, side, 2);
            Array.Copy(x1, mid + frame_length, state.sMid, 0, 2);
            Array.Copy(side, frame_length, state.sSide, 0, 2);

            /* LP and HP filter mid signal */
            LP_mid = new short[frame_length];
            HP_mid = new short[frame_length];
            for (n = 0; n < frame_length; n++)
            {
                sum = Inlines.silk_RSHIFT_ROUND(Inlines.silk_ADD_LSHIFT32(x1[mid + n] + x1[mid + n + 2], x1[mid + n + 1], 1), 2);
                LP_mid[n] = (short)(sum);
                HP_mid[n] = (short)(x1[mid + n + 1] - sum);
            }

            /* LP and HP filter side signal */
            LP_side = new short[frame_length];
            HP_side = new short[frame_length];
            for (n = 0; n < frame_length; n++)
            {
                sum = Inlines.silk_RSHIFT_ROUND(Inlines.silk_ADD_LSHIFT32(side[n] + side[n + 2], side[n + 1], 1), 2);
                LP_side[n] = (short)(sum);
                HP_side[n] = (short)(side[n + 1] - sum);
            }

            /* Find energies and predictors */
            is10msFrame = (frame_length == 10 * fs_kHz ? 1 : 0);
            smooth_coef_Q16 = is10msFrame != 0 ?
                ((int)((SilkConstants.STEREO_RATIO_SMOOTH_COEF / 2) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.STEREO_RATIO_SMOOTH_COEF / 2, 16)*/ :
                ((int)((SilkConstants.STEREO_RATIO_SMOOTH_COEF) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.STEREO_RATIO_SMOOTH_COEF, 16)*/;
            smooth_coef_Q16 = Inlines.silk_SMULWB(Inlines.silk_SMULBB(prev_speech_act_Q8, prev_speech_act_Q8), smooth_coef_Q16);

            pred_Q13[0] = silk_stereo_find_predictor(LP_ratio_Q14, LP_mid, LP_side, state.mid_side_amp_Q0, 0, frame_length, smooth_coef_Q16);
            pred_Q13[1] = silk_stereo_find_predictor(HP_ratio_Q14, HP_mid, HP_side, state.mid_side_amp_Q0, 2, frame_length, smooth_coef_Q16);
            
            /* Ratio of the norms of residual and mid signals */
            frac_Q16 = Inlines.silk_SMLABB(HP_ratio_Q14.Val, LP_ratio_Q14.Val, 3);
            frac_Q16 = Inlines.silk_min(frac_Q16, ((int)((1) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(1, 16)*/);

            /* Determine bitrate distribution between mid and side, and possibly reduce stereo width */
            total_rate_bps -= is10msFrame != 0 ? 1200 : 600;      /* Subtract approximate bitrate for coding stereo parameters */
            if (total_rate_bps < 1)
            {
                total_rate_bps = 1;
            }
            min_mid_rate_bps = Inlines.silk_SMLABB(2000, fs_kHz, 900);
            Inlines.OpusAssert(min_mid_rate_bps < 32767);
            /* Default bitrate distribution: 8 parts for Mid and (5+3*frac) parts for Side. so: mid_rate = ( 8 / ( 13 + 3 * frac ) ) * total_ rate */
            frac_3_Q16 = Inlines.silk_MUL(3, frac_Q16);
            mid_side_rates_bps[0] = Inlines.silk_DIV32_varQ(total_rate_bps, ((int)((8 + 5) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(8 + 5, 16)*/ + frac_3_Q16, 16 + 3);
            /* If Mid bitrate below minimum, reduce stereo width */
            if (mid_side_rates_bps[0] < min_mid_rate_bps)
            {
                mid_side_rates_bps[0] = min_mid_rate_bps;
                mid_side_rates_bps[1] = total_rate_bps - mid_side_rates_bps[0];
                /* width = 4 * ( 2 * side_rate - min_rate ) / ( ( 1 + 3 * frac ) * min_rate ) */
                width_Q14 = Inlines.silk_DIV32_varQ(Inlines.silk_LSHIFT(mid_side_rates_bps[1], 1) - min_mid_rate_bps,
                    Inlines.silk_SMULWB(((int)((1) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(1, 16)*/ + frac_3_Q16, min_mid_rate_bps), 14 + 2);
                width_Q14 = Inlines.silk_LIMIT(width_Q14, 0, ((int)((1) * ((long)1 << (14)) + 0.5))/*Inlines.SILK_CONST(1, 14)*/);
            }
            else {
                mid_side_rates_bps[1] = total_rate_bps - mid_side_rates_bps[0];
                width_Q14 = ((int)((1) * ((long)1 << (14)) + 0.5))/*Inlines.SILK_CONST(1, 14)*/;
            }

            /* Smoother */
            state.smth_width_Q14 = (short)Inlines.silk_SMLAWB(state.smth_width_Q14, width_Q14 - state.smth_width_Q14, smooth_coef_Q16);

            /* At very low bitrates or for inputs that are nearly amplitude panned, switch to panned-mono coding */
            mid_only_flag.Val = 0;
            if (toMono != 0)
            {
                /* Last frame before stereo.mono transition; collapse stereo width */
                width_Q14 = 0;
                pred_Q13[0] = 0;
                pred_Q13[1] = 0;
                silk_stereo_quant_pred(pred_Q13, ix);
            }
            else if (state.width_prev_Q14 == 0 &&
              (8 * total_rate_bps < 13 * min_mid_rate_bps || Inlines.silk_SMULWB(frac_Q16, state.smth_width_Q14) < ((int)((0.05f) * ((long)1 << (14)) + 0.5))/*Inlines.SILK_CONST(0.05f, 14)*/))
            {
                /* Code as panned-mono; previous frame already had zero width */
                /* Scale down and quantize predictors */
                pred_Q13[0] = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(state.smth_width_Q14, pred_Q13[0]), 14);
                pred_Q13[1] = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(state.smth_width_Q14, pred_Q13[1]), 14);
                silk_stereo_quant_pred(pred_Q13, ix);
                /* Collapse stereo width */
                width_Q14 = 0;
                pred_Q13[0] = 0;
                pred_Q13[1] = 0;
                mid_side_rates_bps[0] = total_rate_bps;
                mid_side_rates_bps[1] = 0;
                mid_only_flag.Val = 1;
            }
            else if (state.width_prev_Q14 != 0 &&
              (8 * total_rate_bps < 11 * min_mid_rate_bps || Inlines.silk_SMULWB(frac_Q16, state.smth_width_Q14) < ((int)((0.02f) * ((long)1 << (14)) + 0.5))/*Inlines.SILK_CONST(0.02f, 14)*/))
            {
                /* Transition to zero-width stereo */
                /* Scale down and quantize predictors */
                pred_Q13[0] = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(state.smth_width_Q14, pred_Q13[0]), 14);
                pred_Q13[1] = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(state.smth_width_Q14, pred_Q13[1]), 14);
                silk_stereo_quant_pred(pred_Q13, ix);
                /* Collapse stereo width */
                width_Q14 = 0;
                pred_Q13[0] = 0;
                pred_Q13[1] = 0;
            }
            else if (state.smth_width_Q14 > ((int)((0.95f) * ((long)1 << (14)) + 0.5))/*Inlines.SILK_CONST(0.95f, 14)*/)
            {
                /* Full-width stereo coding */
                silk_stereo_quant_pred(pred_Q13, ix);
                width_Q14 = ((int)((1) * ((long)1 << (14)) + 0.5))/*Inlines.SILK_CONST(1, 14)*/;
            }
            else
            {
                /* Reduced-width stereo coding; scale down and quantize predictors */
                pred_Q13[0] = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(state.smth_width_Q14, pred_Q13[0]), 14);
                pred_Q13[1] = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(state.smth_width_Q14, pred_Q13[1]), 14);
                silk_stereo_quant_pred(pred_Q13, ix);
                width_Q14 = state.smth_width_Q14;
            }

            /* Make sure to keep on encoding until the tapered output has been transmitted */
            if (mid_only_flag.Val == 1)
            {
                state.silent_side_len += (short)(frame_length - SilkConstants.STEREO_INTERP_LEN_MS * fs_kHz);
                if (state.silent_side_len < SilkConstants.LA_SHAPE_MS * fs_kHz)
                {
                    mid_only_flag.Val = 0;
                }
                else {
                    /* Limit to avoid wrapping around */
                    state.silent_side_len = 10000;
                }
            }
            else {
                state.silent_side_len = 0;
            }

            if (mid_only_flag.Val == 0 && mid_side_rates_bps[1] < 1)
            {
                mid_side_rates_bps[1] = 1;
                mid_side_rates_bps[0] = Inlines.silk_max_int(1, total_rate_bps - mid_side_rates_bps[1]);
            }

            /* Interpolate predictors and subtract prediction from side channel */
            pred0_Q13 = -state.pred_prev_Q13[0];
            pred1_Q13 = -state.pred_prev_Q13[1];
            w_Q24 = Inlines.silk_LSHIFT(state.width_prev_Q14, 10);
            denom_Q16 = Inlines.silk_DIV32_16((int)1 << 16, SilkConstants.STEREO_INTERP_LEN_MS * fs_kHz);
            delta0_Q13 = 0 - Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULBB(pred_Q13[0] - state.pred_prev_Q13[0], denom_Q16), 16);
            delta1_Q13 = 0 - Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULBB(pred_Q13[1] - state.pred_prev_Q13[1], denom_Q16), 16);
            deltaw_Q24 = Inlines.silk_LSHIFT(Inlines.silk_SMULWB(width_Q14 - state.width_prev_Q14, denom_Q16), 10);
            for (n = 0; n < SilkConstants.STEREO_INTERP_LEN_MS * fs_kHz; n++)
            {
                pred0_Q13 += delta0_Q13;
                pred1_Q13 += delta1_Q13;
                w_Q24 += deltaw_Q24;
                sum = Inlines.silk_LSHIFT(Inlines.silk_ADD_LSHIFT(x1[mid + n] + x1[mid + n + 2], x1[mid + n + 1], 1), 9);    /* Q11 */
                sum = Inlines.silk_SMLAWB(Inlines.silk_SMULWB(w_Q24, side[n + 1]), sum, pred0_Q13);               /* Q8  */
                sum = Inlines.silk_SMLAWB(sum, Inlines.silk_LSHIFT((int)x1[mid + n + 1], 11), pred1_Q13);       /* Q8  */
                x2[x2_ptr + n - 1] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(sum, 8));
            }

            pred0_Q13 = 0 - pred_Q13[0];
            pred1_Q13 = 0 - pred_Q13[1];
            w_Q24 = Inlines.silk_LSHIFT(width_Q14, 10);
            for (n = SilkConstants.STEREO_INTERP_LEN_MS * fs_kHz; n < frame_length; n++)
            {
                sum = Inlines.silk_LSHIFT(Inlines.silk_ADD_LSHIFT(x1[mid + n] + x1[mid + n + 2], x1[mid + n + 1], 1), 9);    /* Q11 */
                sum = Inlines.silk_SMLAWB(Inlines.silk_SMULWB(w_Q24, side[n + 1]), sum, pred0_Q13);               /* Q8  */
                sum = Inlines.silk_SMLAWB(sum, Inlines.silk_LSHIFT((int)x1[mid + n + 1], 11), pred1_Q13);       /* Q8  */
                x2[x2_ptr + n - 1] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(sum, 8));
            }
            state.pred_prev_Q13[0] = (short)pred_Q13[0];
            state.pred_prev_Q13[1] = (short)pred_Q13[1];
            state.width_prev_Q14 = (short)width_Q14;
        }

        /// <summary>
        /// Convert adaptive Mid/Side representation to Left/Right stereo signal
        /// </summary>
        /// <param name="state">I/O  State</param>
        /// <param name="x1">I/O  Left input signal, becomes mid signal</param>
        /// <param name="x2">I/O  Right input signal, becomes side signal</param>
        /// <param name="pred_Q13">I    Predictors</param>
        /// <param name="fs_kHz">I    Samples rate (kHz)</param>
        /// <param name="frame_length">I    Number of samples</param>
        internal static void silk_stereo_MS_to_LR(
            StereoDecodeState state,
            short[] x1,
            int x1_ptr,
            short[] x2,
            int x2_ptr,
            int[] pred_Q13,
            int fs_kHz,
            int frame_length)
        {
            int n, denom_Q16, delta0_Q13, delta1_Q13;
            int sum, diff, pred0_Q13, pred1_Q13;

            /* Buffering */
            Array.Copy(state.sMid, 0, x1, x1_ptr, 2);
            Array.Copy(state.sSide, 0, x2, x2_ptr, 2);
            Array.Copy(x1, x1_ptr + frame_length, state.sMid, 0, 2);
            Array.Copy(x2, x2_ptr + frame_length, state.sSide, 0, 2);

            /* Interpolate predictors and add prediction to side channel */
            pred0_Q13 = state.pred_prev_Q13[0];
            pred1_Q13 = state.pred_prev_Q13[1];
            denom_Q16 = Inlines.silk_DIV32_16((int)1 << 16, SilkConstants.STEREO_INTERP_LEN_MS * fs_kHz);
            delta0_Q13 = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULBB(pred_Q13[0] - state.pred_prev_Q13[0], denom_Q16), 16);
            delta1_Q13 = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULBB(pred_Q13[1] - state.pred_prev_Q13[1], denom_Q16), 16);
            for (n = 0; n < SilkConstants.STEREO_INTERP_LEN_MS * fs_kHz; n++)
            {
                pred0_Q13 += delta0_Q13;
                pred1_Q13 += delta1_Q13;
                sum = Inlines.silk_LSHIFT(Inlines.silk_ADD_LSHIFT(x1[x1_ptr + n] + x1[x1_ptr + n + 2], x1[x1_ptr + n + 1], 1), 9);       /* Q11 */
                sum = Inlines.silk_SMLAWB(Inlines.silk_LSHIFT((int)x2[x2_ptr + n + 1], 8), sum, pred0_Q13);         /* Q8  */
                sum = Inlines.silk_SMLAWB(sum, Inlines.silk_LSHIFT((int)x1[x1_ptr + n + 1], 11), pred1_Q13);        /* Q8  */
                x2[x2_ptr + n + 1] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(sum, 8));
            }
            pred0_Q13 = pred_Q13[0];
            pred1_Q13 = pred_Q13[1];
            for (n = SilkConstants.STEREO_INTERP_LEN_MS * fs_kHz; n < frame_length; n++)
            {
                sum = Inlines.silk_LSHIFT(Inlines.silk_ADD_LSHIFT(x1[x1_ptr + n] + x1[x1_ptr + n + 2], x1[x1_ptr + n + 1], 1), 9);       /* Q11 */
                sum = Inlines.silk_SMLAWB(Inlines.silk_LSHIFT((int)x2[x2_ptr + n + 1], 8), sum, pred0_Q13);         /* Q8  */
                sum = Inlines.silk_SMLAWB(sum, Inlines.silk_LSHIFT((int)x1[x1_ptr + n + 1], 11), pred1_Q13);        /* Q8  */
                x2[x2_ptr + n + 1] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(sum, 8));
            }
            state.pred_prev_Q13[0] = (short)(pred_Q13[0]);
            state.pred_prev_Q13[1] = (short)(pred_Q13[1]);

            /* Convert to left/right signals */
            for (n = 0; n < frame_length; n++)
            {
                sum = x1[x1_ptr + n + 1] + (int)x2[x2_ptr + n + 1];
                diff = x1[x1_ptr + n + 1] - (int)x2[x2_ptr + n + 1];
                x1[x1_ptr + n + 1] = (short)Inlines.silk_SAT16(sum);
                x2[x2_ptr + n + 1] = (short)Inlines.silk_SAT16(diff);
            }
        }

        /// <summary>
        /// Quantize mid/side predictors
        /// </summary>
        /// <param name="pred_Q13">I/O  Predictors (out: quantized)</param>
        /// <param name="ix">O    Quantization indices [ 2 ][ 3 ]</param>
        internal static void silk_stereo_quant_pred(
                            int[] pred_Q13,
                            sbyte[][] ix)
        {
            sbyte i, j; // [porting note] these were originally ints
            int n;
            int low_Q13, step_Q13, lvl_Q13, err_min_Q13, err_Q13, quant_pred_Q13 = 0;

            // FIXME: ix was formerly an out parameter that was newly allocated here
            // but now it relies on the caller to initialize it
            // clear ix
            Arrays.MemSetSbyte(ix[0], 0, 3);
            Arrays.MemSetSbyte(ix[1], 0, 3);

            /* Quantize */
            for (n = 0; n < 2; n++)
            {
                /* Brute-force search over quantization levels */
                err_min_Q13 = int.MaxValue;
                for (i = 0; i < SilkConstants.STEREO_QUANT_TAB_SIZE - 1; i++)
                {
                    low_Q13 = Tables.silk_stereo_pred_quant_Q13[i];
                    step_Q13 = Inlines.silk_SMULWB(Tables.silk_stereo_pred_quant_Q13[i + 1] - low_Q13,
                        ((int)((0.5f / SilkConstants.STEREO_QUANT_SUB_STEPS) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(0.5f / SilkConstants.STEREO_QUANT_SUB_STEPS, 16)*/);

                    for (j = 0; j < SilkConstants.STEREO_QUANT_SUB_STEPS; j++)
                    {
                        lvl_Q13 = Inlines.silk_SMLABB(low_Q13, step_Q13, 2 * j + 1);
                        err_Q13 = Inlines.silk_abs(pred_Q13[n] - lvl_Q13);
                        if (err_Q13 < err_min_Q13)
                        {
                            err_min_Q13 = err_Q13;
                            quant_pred_Q13 = lvl_Q13;
                            ix[n][0] = i;
                            ix[n][1] = j;
                        }
                        else
                        {
                            /* Error increasing, so we're past the optimum */
                            // FIXME: get this crap out of here
                            goto done;
                        }
                    }
                }

            done:
                ix[n][2] = (sbyte)(Inlines.silk_DIV32_16(ix[n][0], 3));
                ix[n][0] = (sbyte)(ix[n][0] - (sbyte)(ix[n][2] * 3));
                pred_Q13[n] = quant_pred_Q13;
            }

            /* Subtract second from first predictor (helps when actually applying these) */
            pred_Q13[0] -= pred_Q13[1];
        }
    }
}
