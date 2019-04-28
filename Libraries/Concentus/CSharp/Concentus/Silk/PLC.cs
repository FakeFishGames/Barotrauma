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

    /// <summary>
    /// Routines for managing packet loss concealment
    /// </summary>
    internal static class PLC
    {
        private const int NB_ATT = 2;
        private static readonly short[] HARM_ATT_Q15 = { 32440, 31130 }; /* 0.99, 0.95 */
        private static readonly short[] PLC_RAND_ATTENUATE_V_Q15 = { 31130, 26214 }; /* 0.95, 0.8 */
        private static readonly short[] PLC_RAND_ATTENUATE_UV_Q15 = { 32440, 29491 }; /* 0.99, 0.9 */

        internal static void silk_PLC_Reset(
            SilkChannelDecoder psDec              /* I/O Decoder state        */
        )
        {
            psDec.sPLC.pitchL_Q8 = Inlines.silk_LSHIFT(psDec.frame_length, 8 - 1);
            psDec.sPLC.prevGain_Q16[0] = ((int)((1) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(1, 16)*/;
            psDec.sPLC.prevGain_Q16[1] = ((int)((1) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(1, 16)*/;
            psDec.sPLC.subfr_length = 20;
            psDec.sPLC.nb_subfr = 2;
        }

        internal static void silk_PLC(
            SilkChannelDecoder psDec,             /* I/O Decoder state        */
            SilkDecoderControl psDecCtrl,         /* I/O Decoder control      */
            short[] frame,            /* I/O  signal              */
            int frame_ptr,
            int lost               /* I Loss flag              */
        )
        {
            /* PLC control function */
            if (psDec.fs_kHz != psDec.sPLC.fs_kHz)
            {
                silk_PLC_Reset(psDec);
                psDec.sPLC.fs_kHz = psDec.fs_kHz;
            }

            if (lost != 0)
            {
                /****************************/
                /* Generate Signal          */
                /****************************/
                silk_PLC_conceal(psDec, psDecCtrl, frame, frame_ptr);

                psDec.lossCnt++;
            }
            else {
                /****************************/
                /* Update state             */
                /****************************/
                silk_PLC_update(psDec, psDecCtrl);
            }
        }

        /**************************************************/
        /* Update state of PLC                            */
        /**************************************************/
        internal static void silk_PLC_update(
            SilkChannelDecoder psDec,             /* I/O Decoder state        */
            SilkDecoderControl psDecCtrl          /* I/O Decoder control      */
        )
        {
            int LTP_Gain_Q14, temp_LTP_Gain_Q14;
            int i, j;
            PLCStruct psPLC = psDec.sPLC; // [porting note] pointer on the stack

            /* Update parameters used in case of packet loss */
            psDec.prevSignalType = psDec.indices.signalType;
            LTP_Gain_Q14 = 0;
            if (psDec.indices.signalType == SilkConstants.TYPE_VOICED)
            {
                /* Find the parameters for the last subframe which contains a pitch pulse */
                for (j = 0; j * psDec.subfr_length < psDecCtrl.pitchL[psDec.nb_subfr - 1]; j++)
                {
                    if (j == psDec.nb_subfr)
                    {
                        break;
                    }
                    temp_LTP_Gain_Q14 = 0;
                    for (i = 0; i < SilkConstants.LTP_ORDER; i++)
                    {
                        temp_LTP_Gain_Q14 += psDecCtrl.LTPCoef_Q14[(psDec.nb_subfr - 1 - j) * SilkConstants.LTP_ORDER + i];
                    }
                    if (temp_LTP_Gain_Q14 > LTP_Gain_Q14)
                    {
                        LTP_Gain_Q14 = temp_LTP_Gain_Q14;

                        Array.Copy(psDecCtrl.LTPCoef_Q14, Inlines.silk_SMULBB(psDec.nb_subfr - 1 - j, SilkConstants.LTP_ORDER), psPLC.LTPCoef_Q14, 0, SilkConstants.LTP_ORDER);

                        psPLC.pitchL_Q8 = Inlines.silk_LSHIFT(psDecCtrl.pitchL[psDec.nb_subfr - 1 - j], 8);
                    }
                }

                Arrays.MemSetShort(psPLC.LTPCoef_Q14, 0, SilkConstants.LTP_ORDER);
                psPLC.LTPCoef_Q14[SilkConstants.LTP_ORDER / 2] = (short)(LTP_Gain_Q14);

                /* Limit LT coefs */
                if (LTP_Gain_Q14 < SilkConstants.V_PITCH_GAIN_START_MIN_Q14)
                {
                    int scale_Q10;
                    int tmp;

                    tmp = Inlines.silk_LSHIFT(SilkConstants.V_PITCH_GAIN_START_MIN_Q14, 10);
                    scale_Q10 = Inlines.silk_DIV32(tmp, Inlines.silk_max(LTP_Gain_Q14, 1));
                    for (i = 0; i < SilkConstants.LTP_ORDER; i++)
                    {
                        psPLC.LTPCoef_Q14[i] = (short)(Inlines.silk_RSHIFT(Inlines.silk_SMULBB(psPLC.LTPCoef_Q14[i], scale_Q10), 10));
                    }
                }
                else if (LTP_Gain_Q14 > SilkConstants.V_PITCH_GAIN_START_MAX_Q14)
                {
                    int scale_Q14;
                    int tmp;

                    tmp = Inlines.silk_LSHIFT(SilkConstants.V_PITCH_GAIN_START_MAX_Q14, 14);
                    scale_Q14 = Inlines.silk_DIV32(tmp, Inlines.silk_max(LTP_Gain_Q14, 1));
                    for (i = 0; i < SilkConstants.LTP_ORDER; i++)
                    {
                        psPLC.LTPCoef_Q14[i] = (short)(Inlines.silk_RSHIFT(Inlines.silk_SMULBB(psPLC.LTPCoef_Q14[i], scale_Q14), 14));
                    }
                }
            }
            else {
                psPLC.pitchL_Q8 = Inlines.silk_LSHIFT(Inlines.silk_SMULBB(psDec.fs_kHz, 18), 8);
                Arrays.MemSetShort(psPLC.LTPCoef_Q14, 0, SilkConstants.LTP_ORDER);
            }

            /* Save LPC coeficients */
            Array.Copy(psDecCtrl.PredCoef_Q12[1], psPLC.prevLPC_Q12, psDec.LPC_order);
            psPLC.prevLTP_scale_Q14 = (short)(psDecCtrl.LTP_scale_Q14);

            /* Save last two gains */
            Array.Copy(psDecCtrl.Gains_Q16, psDec.nb_subfr - 2, psPLC.prevGain_Q16, 0, 2);

            psPLC.subfr_length = psDec.subfr_length;
            psPLC.nb_subfr = psDec.nb_subfr;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="energy1">O</param>
        /// <param name="shift1">O</param>
        /// <param name="energy2">O</param>
        /// <param name="shift2">O</param>
        /// <param name="exc_Q14">I</param>
        /// <param name="prevGain_Q10">I</param>
        /// <param name="subfr_length">I</param>
        /// <param name="nb_subfr">I</param>
        internal static void silk_PLC_energy(
            out int energy1,
            out int shift1,
            out int energy2,
            out int shift2,
            int[] exc_Q14,
            int[] prevGain_Q10,
            int subfr_length,
            int nb_subfr)
        {
            int i, k;
            int exc_buf_ptr = 0;
            short[] exc_buf = new short[2 * subfr_length];

            /* Find random noise component */
            /* Scale previous excitation signal */
            for (k = 0; k < 2; k++)
            {
                for (i = 0; i < subfr_length; i++)
                {
                    exc_buf[exc_buf_ptr + i] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT(
                        Inlines.silk_SMULWW(exc_Q14[i + (k + nb_subfr - 2) * subfr_length], prevGain_Q10[k]), 8));
                }
                exc_buf_ptr += subfr_length;
            }

            /* Find the subframe with lowest energy of the last two and use that as random noise generator */
            SumSqrShift.silk_sum_sqr_shift(out energy1, out shift1, exc_buf, subfr_length);
            SumSqrShift.silk_sum_sqr_shift(out energy2, out shift2, exc_buf, subfr_length, subfr_length);
        }

        internal static void silk_PLC_conceal(
            SilkChannelDecoder psDec,             /* I/O Decoder state        */
            SilkDecoderControl psDecCtrl,         /* I/O Decoder control      */
            short[] frame,            /* O LPC residual signal    */
            int frame_ptr
        )
        {
            int i, j, k;
            int lag, idx, sLTP_buf_idx;
            int rand_seed, harm_Gain_Q15, rand_Gain_Q15, inv_gain_Q30;
            int energy1, energy2, shift1, shift2;
            int rand_ptr;
            int pred_lag_ptr;
            int LPC_pred_Q10, LTP_pred_Q12;
            short rand_scale_Q14;
            short[] B_Q14;
            int sLPC_Q14_ptr;
            short[] sLTP = new short[psDec.ltp_mem_length];
            int[] sLTP_Q14 = new int[psDec.ltp_mem_length + psDec.frame_length];
            PLCStruct psPLC = psDec.sPLC;
            int[] prevGain_Q10 = new int[2];

            prevGain_Q10[0] = Inlines.silk_RSHIFT(psPLC.prevGain_Q16[0], 6);
            prevGain_Q10[1] = Inlines.silk_RSHIFT(psPLC.prevGain_Q16[1], 6);

            if (psDec.first_frame_after_reset != 0)
            {
                Arrays.MemSetShort(psPLC.prevLPC_Q12, 0, SilkConstants.MAX_LPC_ORDER);
            }

            silk_PLC_energy(out energy1, out shift1, out energy2, out shift2, psDec.exc_Q14, prevGain_Q10, psDec.subfr_length, psDec.nb_subfr);

            if (Inlines.silk_RSHIFT(energy1, shift2) < Inlines.silk_RSHIFT(energy2, shift1))
            {
                /* First sub-frame has lowest energy */
                rand_ptr = Inlines.silk_max_int(0, (psPLC.nb_subfr - 1) * psPLC.subfr_length - SilkConstants.RAND_BUF_SIZE);
            }
            else {
                /* Second sub-frame has lowest energy */
                rand_ptr = Inlines.silk_max_int(0, psPLC.nb_subfr * psPLC.subfr_length - SilkConstants.RAND_BUF_SIZE);
            }

            /* Set up Gain to random noise component */
            B_Q14 = psPLC.LTPCoef_Q14;
            rand_scale_Q14 = psPLC.randScale_Q14;

            /* Set up attenuation gains */
            harm_Gain_Q15 = HARM_ATT_Q15[Inlines.silk_min_int(NB_ATT - 1, psDec.lossCnt)];
            if (psDec.prevSignalType == SilkConstants.TYPE_VOICED)
            {
                rand_Gain_Q15 = PLC_RAND_ATTENUATE_V_Q15[Inlines.silk_min_int(NB_ATT - 1, psDec.lossCnt)];
            }
            else {
                rand_Gain_Q15 = PLC_RAND_ATTENUATE_UV_Q15[Inlines.silk_min_int(NB_ATT - 1, psDec.lossCnt)];
            }

            /* LPC concealment. Apply BWE to previous LPC */
            BWExpander.silk_bwexpander(psPLC.prevLPC_Q12, psDec.LPC_order, ((int)((SilkConstants.BWE_COEF) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.BWE_COEF, 16)*/);
            
            /* First Lost frame */
            if (psDec.lossCnt == 0)
            {
                rand_scale_Q14 = 1 << 14;

                /* Reduce random noise Gain for voiced frames */
                if (psDec.prevSignalType == SilkConstants.TYPE_VOICED)
                {
                    for (i = 0; i < SilkConstants.LTP_ORDER; i++)
                    {
                        rand_scale_Q14 -= B_Q14[i];
                    }
                    rand_scale_Q14 = Inlines.silk_max_16(3277, rand_scale_Q14); /* 0.2 */
                    rand_scale_Q14 = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(rand_scale_Q14, psPLC.prevLTP_scale_Q14), 14);
                }
                else
                {
                    /* Reduce random noise for unvoiced frames with high LPC gain */
                    int invGain_Q30, down_scale_Q30;

                    invGain_Q30 = LPCInversePredGain.silk_LPC_inverse_pred_gain(psPLC.prevLPC_Q12, psDec.LPC_order);

                    down_scale_Q30 = Inlines.silk_min_32(Inlines.silk_RSHIFT((int)1 << 30, SilkConstants.LOG2_INV_LPC_GAIN_HIGH_THRES), invGain_Q30);
                    down_scale_Q30 = Inlines.silk_max_32(Inlines.silk_RSHIFT((int)1 << 30, SilkConstants.LOG2_INV_LPC_GAIN_LOW_THRES), down_scale_Q30);
                    down_scale_Q30 = Inlines.silk_LSHIFT(down_scale_Q30, SilkConstants.LOG2_INV_LPC_GAIN_HIGH_THRES);

                    rand_Gain_Q15 = Inlines.silk_RSHIFT(Inlines.silk_SMULWB(down_scale_Q30, rand_Gain_Q15), 14);
                }
            }

            rand_seed = psPLC.rand_seed;
            lag = Inlines.silk_RSHIFT_ROUND(psPLC.pitchL_Q8, 8);
            sLTP_buf_idx = psDec.ltp_mem_length;

            /* Rewhiten LTP state */
            idx = psDec.ltp_mem_length - lag - psDec.LPC_order - SilkConstants.LTP_ORDER / 2;
            Inlines.OpusAssert(idx > 0);
            Filters.silk_LPC_analysis_filter(sLTP, idx, psDec.outBuf, idx, psPLC.prevLPC_Q12, 0, psDec.ltp_mem_length - idx, psDec.LPC_order);
            /* Scale LTP state */
            inv_gain_Q30 = Inlines.silk_INVERSE32_varQ(psPLC.prevGain_Q16[1], 46);
            inv_gain_Q30 = Inlines.silk_min(inv_gain_Q30, int.MaxValue >> 1);
            for (i = idx + psDec.LPC_order; i < psDec.ltp_mem_length; i++)
            {
                sLTP_Q14[i] = Inlines.silk_SMULWB(inv_gain_Q30, sLTP[i]);
            }

            /***************************/
            /* LTP synthesis filtering */
            /***************************/
            for (k = 0; k < psDec.nb_subfr; k++)
            {
                /* Set up pointer */
                pred_lag_ptr = sLTP_buf_idx - lag + SilkConstants.LTP_ORDER / 2;
                for (i = 0; i < psDec.subfr_length; i++)
                {
                    /* Unrolled loop */
                    /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                    LTP_pred_Q12 = 2;
                    LTP_pred_Q12 = Inlines.silk_SMLAWB(LTP_pred_Q12, sLTP_Q14[pred_lag_ptr], B_Q14[0]);
                    LTP_pred_Q12 = Inlines.silk_SMLAWB(LTP_pred_Q12, sLTP_Q14[pred_lag_ptr - 1], B_Q14[1]);
                    LTP_pred_Q12 = Inlines.silk_SMLAWB(LTP_pred_Q12, sLTP_Q14[pred_lag_ptr - 2], B_Q14[2]);
                    LTP_pred_Q12 = Inlines.silk_SMLAWB(LTP_pred_Q12, sLTP_Q14[pred_lag_ptr - 3], B_Q14[3]);
                    LTP_pred_Q12 = Inlines.silk_SMLAWB(LTP_pred_Q12, sLTP_Q14[pred_lag_ptr - 4], B_Q14[4]);
                    pred_lag_ptr++;

                    /* Generate LPC excitation */
                    rand_seed = Inlines.silk_RAND(rand_seed);
                    idx = Inlines.silk_RSHIFT(rand_seed, 25) & SilkConstants.RAND_BUF_MASK;
                    sLTP_Q14[sLTP_buf_idx] = Inlines.silk_LSHIFT32(Inlines.silk_SMLAWB(LTP_pred_Q12, psDec.exc_Q14[rand_ptr + idx], rand_scale_Q14), 2);
                    sLTP_buf_idx++;
                }

                /* Gradually reduce LTP gain */
                for (j = 0; j < SilkConstants.LTP_ORDER; j++)
                {
                    B_Q14[j] = (short)(Inlines.silk_RSHIFT(Inlines.silk_SMULBB(harm_Gain_Q15, B_Q14[j]), 15));
                }
                /* Gradually reduce excitation gain */
                rand_scale_Q14 = (short)(Inlines.silk_RSHIFT(Inlines.silk_SMULBB(rand_scale_Q14, rand_Gain_Q15), 15));

                /* Slowly increase pitch lag */
                psPLC.pitchL_Q8 = Inlines.silk_SMLAWB(psPLC.pitchL_Q8, psPLC.pitchL_Q8, SilkConstants.PITCH_DRIFT_FAC_Q16);
                psPLC.pitchL_Q8 = Inlines.silk_min_32(psPLC.pitchL_Q8, Inlines.silk_LSHIFT(Inlines.silk_SMULBB(SilkConstants.MAX_PITCH_LAG_MS, psDec.fs_kHz), 8));
                lag = Inlines.silk_RSHIFT_ROUND(psPLC.pitchL_Q8, 8);
            }

            /***************************/
            /* LPC synthesis filtering */
            /***************************/
            sLPC_Q14_ptr = psDec.ltp_mem_length - SilkConstants.MAX_LPC_ORDER;

            /* Copy LPC state */
            Array.Copy(psDec.sLPC_Q14_buf, 0, sLTP_Q14, sLPC_Q14_ptr, SilkConstants.MAX_LPC_ORDER);

            Inlines.OpusAssert(psDec.LPC_order >= 10); /* check that unrolling works */
            for (i = 0; i < psDec.frame_length; i++)
            {
                /* partly unrolled */
                int sLPCmaxi = sLPC_Q14_ptr + SilkConstants.MAX_LPC_ORDER + i;
                /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                LPC_pred_Q10 = Inlines.silk_RSHIFT(psDec.LPC_order, 1);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 1], psPLC.prevLPC_Q12[0]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 2], psPLC.prevLPC_Q12[1]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 3], psPLC.prevLPC_Q12[2]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 4], psPLC.prevLPC_Q12[3]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 5], psPLC.prevLPC_Q12[4]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 6], psPLC.prevLPC_Q12[5]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 7], psPLC.prevLPC_Q12[6]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 8], psPLC.prevLPC_Q12[7]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 9], psPLC.prevLPC_Q12[8]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - 10], psPLC.prevLPC_Q12[9]);
                for (j = 10; j < psDec.LPC_order; j++)
                {
                    LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, sLTP_Q14[sLPCmaxi - j - 1], psPLC.prevLPC_Q12[j]);
                }

                /* Add prediction to LPC excitation */
                sLTP_Q14[sLPCmaxi] = Inlines.silk_ADD_LSHIFT32(sLTP_Q14[sLPCmaxi], LPC_pred_Q10, 4);

                /* Scale with Gain */
                frame[frame_ptr + i] = (short)Inlines.silk_SAT16(Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWW(sLTP_Q14[sLPCmaxi], prevGain_Q10[1]), 8)));
            }

            /* Save LPC state */
            Array.Copy(sLTP_Q14, sLPC_Q14_ptr + psDec.frame_length, psDec.sLPC_Q14_buf, 0, SilkConstants.MAX_LPC_ORDER);

            /**************************************/
            /* Update states                      */
            /**************************************/
            psPLC.rand_seed = rand_seed;
            psPLC.randScale_Q14 = rand_scale_Q14;
            for (i = 0; i < SilkConstants.MAX_NB_SUBFR; i++)
            {
                psDecCtrl.pitchL[i] = lag;
            }
        }

        /* Glues concealed frames with new good received frames */
        internal static void silk_PLC_glue_frames(
            SilkChannelDecoder psDec,             /* I/O decoder state        */
            short[] frame,            /* I/O signal               */
            int frame_ptr,
            int length              /* I length of signal       */
        )
        {
            int i;
            int energy_shift, energy;
            PLCStruct psPLC = psDec.sPLC;

            if (psDec.lossCnt != 0)
            {
                /* Calculate energy in concealed residual */
                SumSqrShift.silk_sum_sqr_shift(out psPLC.conc_energy, out psPLC.conc_energy_shift, frame, frame_ptr, length);

                psPLC.last_frame_lost = 1;
            }
            else
            {
                if (psDec.sPLC.last_frame_lost != 0)
                {
                    /* Calculate residual in decoded signal if last frame was lost */
                    SumSqrShift.silk_sum_sqr_shift(out energy, out energy_shift, frame, frame_ptr, length);

                    /* Normalize energies */
                    if (energy_shift > psPLC.conc_energy_shift)
                    {
                        psPLC.conc_energy = Inlines.silk_RSHIFT(psPLC.conc_energy, energy_shift - psPLC.conc_energy_shift);
                    }
                    else if (energy_shift < psPLC.conc_energy_shift)
                    {
                        energy = Inlines.silk_RSHIFT(energy, psPLC.conc_energy_shift - energy_shift);
                    }

                    /* Fade in the energy difference */
                    if (energy > psPLC.conc_energy)
                    {
                        int frac_Q24, LZ;
                        int gain_Q16, slope_Q16;

                        LZ = Inlines.silk_CLZ32(psPLC.conc_energy);
                        LZ = LZ - 1;
                        psPLC.conc_energy = Inlines.silk_LSHIFT(psPLC.conc_energy, LZ);
                        energy = Inlines.silk_RSHIFT(energy, Inlines.silk_max_32(24 - LZ, 0));

                        frac_Q24 = Inlines.silk_DIV32(psPLC.conc_energy, Inlines.silk_max(energy, 1));

                        gain_Q16 = Inlines.silk_LSHIFT(Inlines.silk_SQRT_APPROX(frac_Q24), 4);
                        slope_Q16 = Inlines.silk_DIV32_16(((int)1 << 16) - gain_Q16, length);
                        /* Make slope 4x steeper to avoid missing onsets after DTX */
                        slope_Q16 = Inlines.silk_LSHIFT(slope_Q16, 2);

                        for (i = frame_ptr; i < frame_ptr + length; i++)
                        {
                            frame[i] = (short)(Inlines.silk_SMULWB(gain_Q16, frame[i]));
                            gain_Q16 += slope_Q16;
                            if (gain_Q16 > (int)1 << 16)
                            {
                                break;
                            }
                        }
                    }
                }
                psPLC.last_frame_lost = 0;
            }
        }
    }
}
