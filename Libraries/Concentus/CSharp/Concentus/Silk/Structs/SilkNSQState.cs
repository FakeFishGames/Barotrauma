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

namespace Concentus.Silk.Structs
{
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using Concentus.Silk.Enums;
    using System;
    /// <summary>
    /// Noise shaping quantization state
    /// </summary>
    internal class SilkNSQState
    {
        /// <summary>
        /// Buffer for quantized output signal
        /// </summary>
        internal readonly short[] xq = new short[2 * SilkConstants.MAX_FRAME_LENGTH]; //opt: make these arrays variable-sized since construction cost is significant here
        internal readonly int[] sLTP_shp_Q14 = new int[2 * SilkConstants.MAX_FRAME_LENGTH];
        internal readonly int[] sLPC_Q14 = new int[SilkConstants.MAX_SUB_FRAME_LENGTH + SilkConstants.NSQ_LPC_BUF_LENGTH];
        internal readonly int[] sAR2_Q14 = new int[SilkConstants.MAX_SHAPE_LPC_ORDER];
        internal int sLF_AR_shp_Q14 = 0;
        internal int lagPrev = 0;
        internal int sLTP_buf_idx = 0;
        internal int sLTP_shp_buf_idx = 0;
        internal int rand_seed = 0;
        internal int prev_gain_Q16 = 0;
        internal int rewhite_flag = 0;

        internal void Reset()
        {
            Arrays.MemSetShort(xq, 0, 2 * SilkConstants.MAX_FRAME_LENGTH);
            Arrays.MemSetInt(sLTP_shp_Q14, 0, 2 * SilkConstants.MAX_FRAME_LENGTH);
            Arrays.MemSetInt(sLPC_Q14, 0, SilkConstants.MAX_SUB_FRAME_LENGTH + SilkConstants.NSQ_LPC_BUF_LENGTH);
            Arrays.MemSetInt(sAR2_Q14, 0, SilkConstants.MAX_SHAPE_LPC_ORDER);
            sLF_AR_shp_Q14 = 0;
            lagPrev = 0;
            sLTP_buf_idx = 0;
            sLTP_shp_buf_idx = 0;
            rand_seed = 0;
            prev_gain_Q16 = 0;
            rewhite_flag = 0;
        }

        // Copies another nsq state to this one
        internal void Assign(SilkNSQState other)
        {
            this.sLF_AR_shp_Q14 = other.sLF_AR_shp_Q14;
            this.lagPrev = other.lagPrev;
            this.sLTP_buf_idx = other.sLTP_buf_idx;
            this.sLTP_shp_buf_idx = other.sLTP_shp_buf_idx;
            this.rand_seed = other.rand_seed;
            this.prev_gain_Q16 = other.prev_gain_Q16;
            this.rewhite_flag = other.rewhite_flag;
            Array.Copy(other.xq, this.xq, 2 * SilkConstants.MAX_FRAME_LENGTH);
            Array.Copy(other.sLTP_shp_Q14, this.sLTP_shp_Q14, 2 * SilkConstants.MAX_FRAME_LENGTH);
            Array.Copy(other.sLPC_Q14, this.sLPC_Q14, SilkConstants.MAX_SUB_FRAME_LENGTH + SilkConstants.NSQ_LPC_BUF_LENGTH);
            Array.Copy(other.sAR2_Q14, this.sAR2_Q14, SilkConstants.MAX_SHAPE_LPC_ORDER);
        }

        private class NSQ_del_dec_struct
        {
            internal readonly int[] sLPC_Q14 = new int[SilkConstants.MAX_SUB_FRAME_LENGTH + SilkConstants.NSQ_LPC_BUF_LENGTH];
            internal readonly int[] RandState = new int[SilkConstants.DECISION_DELAY];
            internal readonly int[] Q_Q10 = new int[SilkConstants.DECISION_DELAY];
            internal readonly int[] Xq_Q14 = new int[SilkConstants.DECISION_DELAY];
            internal readonly int[] Pred_Q15 = new int[SilkConstants.DECISION_DELAY];
            internal readonly int[] Shape_Q14 = new int[SilkConstants.DECISION_DELAY];
            internal int[] sAR2_Q14;
            internal int LF_AR_Q14 = 0;
            internal int Seed = 0;
            internal int SeedInit = 0;
            internal int RD_Q10 = 0;

            internal NSQ_del_dec_struct(int shapingOrder)
            {
                sAR2_Q14 = new int[shapingOrder];
            }

            internal void PartialCopyFrom(NSQ_del_dec_struct other, int q14Offset)
            {
                Buffer.BlockCopy(other.sLPC_Q14, q14Offset * sizeof(int), sLPC_Q14, q14Offset * sizeof(int), (SilkConstants.MAX_SUB_FRAME_LENGTH + SilkConstants.NSQ_LPC_BUF_LENGTH - q14Offset) * sizeof(int));
                Buffer.BlockCopy(other.RandState, 0, RandState, 0, SilkConstants.DECISION_DELAY * sizeof(int));
                Buffer.BlockCopy(other.Q_Q10, 0, Q_Q10, 0, SilkConstants.DECISION_DELAY * sizeof(int));
                Buffer.BlockCopy(other.Xq_Q14, 0, Xq_Q14, 0, SilkConstants.DECISION_DELAY * sizeof(int));
                Buffer.BlockCopy(other.Pred_Q15, 0, Pred_Q15, 0, SilkConstants.DECISION_DELAY * sizeof(int));
                Buffer.BlockCopy(other.Shape_Q14, 0, Shape_Q14, 0, SilkConstants.DECISION_DELAY * sizeof(int));
                Buffer.BlockCopy(other.sAR2_Q14, 0, sAR2_Q14, 0, sAR2_Q14.Length * sizeof(int));
                LF_AR_Q14 = other.LF_AR_Q14;
                Seed = other.Seed;
                SeedInit = other.SeedInit;
                RD_Q10 = other.RD_Q10;
            }

            internal void Assign(NSQ_del_dec_struct other)
            {
                this.PartialCopyFrom(other, 0);
            }
        }

        private struct NSQ_sample_struct
        {
            internal int Q_Q10;
            internal int RD_Q10;
            internal int xq_Q14;
            internal int LF_AR_Q14;
            internal int sLTP_shp_Q14;
            internal int LPC_exc_Q14;
            
            //internal void Assign(NSQ_sample_struct other)
            //{
            //    this.Q_Q10 = other.Q_Q10;
            //    this.RD_Q10 = other.RD_Q10;
            //    this.xq_Q14 = other.xq_Q14;
            //    this.LF_AR_Q14 = other.LF_AR_Q14;
            //    this.sLTP_shp_Q14 = other.sLTP_shp_Q14;
            //    this.LPC_exc_Q14 = other.LPC_exc_Q14;
            //}
        }

        internal void silk_NSQ
            (
                SilkChannelEncoder psEncC,                                    /* I/O  Encoder State                   */
                SideInfoIndices psIndices,                                 /* I/O  Quantization Indices            */
                int[] x_Q3,                                     /* I    Prefiltered input signal        */
                sbyte[] pulses,                                   /* O    Quantized pulse signal          */
                short[][] PredCoef_Q12,          /* I    Short term prediction coefs [2][SilkConstants.MAX_LPC_ORDER]    */
                short[] LTPCoef_Q14,    /* I    Long term prediction coefs [SilkConstants.LTP_ORDER * MAX_NB_SUBFR]     */
                short[] AR2_Q13, /* I Noise shaping coefs [MAX_NB_SUBFR * SilkConstants.MAX_SHAPE_LPC_ORDER]            */
                int[] HarmShapeGain_Q14,          /* I    Long term shaping coefs [MAX_NB_SUBFR]        */
                int[] Tilt_Q14,                   /* I    Spectral tilt [MAX_NB_SUBFR]                  */
                int[] LF_shp_Q14,                 /* I    Low frequency shaping coefs [MAX_NB_SUBFR]    */
                int[] Gains_Q16,                  /* I    Quantization step sizes [MAX_NB_SUBFR]        */
                int[] pitchL,                     /* I    Pitch lags [MAX_NB_SUBFR]                     */
                int Lambda_Q10,                                 /* I    Rate/distortion tradeoff        */
                int LTP_scale_Q14                               /* I    LTP state scaling               */
            )
        {
            int k, lag, start_idx, LSF_interpolation_flag;
            int A_Q12, B_Q14, AR_shp_Q13;
            int pxq;
            int[] sLTP_Q15;
            short[] sLTP;
            int HarmShapeFIRPacked_Q14;
            int offset_Q10;
            int[] x_sc_Q10;
            int pulses_ptr = 0;
            int x_Q3_ptr = 0;

            this.rand_seed = psIndices.Seed;

            /* Set unvoiced lag to the previous one, overwrite later for voiced */
            lag = this.lagPrev;

            Inlines.OpusAssert(this.prev_gain_Q16 != 0);

            offset_Q10 = Tables.silk_Quantization_Offsets_Q10[psIndices.signalType >> 1][psIndices.quantOffsetType];

            if (psIndices.NLSFInterpCoef_Q2 == 4)
            {
                LSF_interpolation_flag = 0;
            }
            else {
                LSF_interpolation_flag = 1;
            }

            sLTP_Q15 = new int[psEncC.ltp_mem_length + psEncC.frame_length];
            sLTP = new short[psEncC.ltp_mem_length + psEncC.frame_length];
            x_sc_Q10 = new int[psEncC.subfr_length];
            /* Set up pointers to start of sub frame */
            this.sLTP_shp_buf_idx = psEncC.ltp_mem_length;
            this.sLTP_buf_idx = psEncC.ltp_mem_length;
            pxq = psEncC.ltp_mem_length;
            for (k = 0; k < psEncC.nb_subfr; k++)
            {
                A_Q12 = (((k >> 1) | (1 - LSF_interpolation_flag)));
                B_Q14 = (k * SilkConstants.LTP_ORDER); // opt: does this indicate a partitioned array?
                AR_shp_Q13 = (k * SilkConstants.MAX_SHAPE_LPC_ORDER); // opt: same here

                /* Noise shape parameters */
                Inlines.OpusAssert(HarmShapeGain_Q14[k] >= 0);
                HarmShapeFIRPacked_Q14 = Inlines.silk_RSHIFT(HarmShapeGain_Q14[k], 2);
                HarmShapeFIRPacked_Q14 |= Inlines.silk_LSHIFT((int)Inlines.silk_RSHIFT(HarmShapeGain_Q14[k], 1), 16);

                this.rewhite_flag = 0;
                if (psIndices.signalType == SilkConstants.TYPE_VOICED)
                {
                    /* Voiced */
                    lag = pitchL[k];

                    /* Re-whitening */
                    if ((k & (3 - Inlines.silk_LSHIFT(LSF_interpolation_flag, 1))) == 0)
                    {
                        /* Rewhiten with new A coefs */
                        start_idx = psEncC.ltp_mem_length - lag - psEncC.predictLPCOrder - SilkConstants.LTP_ORDER / 2;
                        Inlines.OpusAssert(start_idx > 0);

                        Filters.silk_LPC_analysis_filter(sLTP, start_idx, this.xq, start_idx + k * psEncC.subfr_length,
                            PredCoef_Q12[A_Q12], 0, psEncC.ltp_mem_length - start_idx, psEncC.predictLPCOrder);

                        this.rewhite_flag = 1;
                        this.sLTP_buf_idx = psEncC.ltp_mem_length;
                    }
                }

                silk_nsq_scale_states(psEncC, x_Q3, x_Q3_ptr, x_sc_Q10, sLTP, sLTP_Q15, k, LTP_scale_Q14, Gains_Q16, pitchL, psIndices.signalType);

                silk_noise_shape_quantizer(
                    psIndices.signalType,
                    x_sc_Q10,
                    pulses,
                    pulses_ptr,
                    this.xq,
                    pxq,
                    sLTP_Q15,
                    PredCoef_Q12[A_Q12],
                    LTPCoef_Q14,
                    B_Q14,
                    AR2_Q13,
                    AR_shp_Q13,
                    lag,
                    HarmShapeFIRPacked_Q14,
                    Tilt_Q14[k],
                    LF_shp_Q14[k],
                    Gains_Q16[k],
                    Lambda_Q10,
                    offset_Q10,
                    psEncC.subfr_length,
                    psEncC.shapingLPCOrder,
                    psEncC.predictLPCOrder);

                x_Q3_ptr += psEncC.subfr_length;
                pulses_ptr += psEncC.subfr_length;
                pxq += psEncC.subfr_length;
            }

            /* Update lagPrev for next frame */
            this.lagPrev = pitchL[psEncC.nb_subfr - 1];

            /* Save quantized speech and noise shaping signals */
            Arrays.MemMoveShort(this.xq, psEncC.frame_length, 0, psEncC.ltp_mem_length);
            Arrays.MemMoveInt(this.sLTP_shp_Q14, psEncC.frame_length, 0, psEncC.ltp_mem_length);
        }

        /***********************************/
        /* silk_noise_shape_quantizer  */
        /***********************************/
        private void silk_noise_shape_quantizer(
                int signalType,             /* I    Signal type                     */
                int[] x_sc_Q10,             /* I [length]                                   */
                sbyte[] pulses,               /* O [length]                                    */
                int pulses_ptr,
                short[] xq,                   /* O [length]                                    */
                int xq_ptr,
                int[] sLTP_Q15,             /* I/O  LTP state                       */
                short[] a_Q12,                /* I    Short term prediction coefs     */
                short[] b_Q14,                /* I    Long term prediction coefs      */
                int b_Q14_ptr,
                short[] AR_shp_Q13,           /* I    Noise shaping AR coefs          */
                int AR_shp_Q13_ptr,
                int lag,                    /* I    Pitch lag                       */
                int HarmShapeFIRPacked_Q14, /* I                                    */
                int Tilt_Q14,               /* I    Spectral tilt                   */
                int LF_shp_Q14,             /* I                                    */
                int Gain_Q16,               /* I                                    */
                int Lambda_Q10,             /* I                                    */
                int offset_Q10,             /* I                                    */
                int length,                 /* I    Input length                    */
                int shapingLPCOrder,        /* I    Noise shaping AR filter order   */
                int predictLPCOrder         /* I    Prediction filter order         */
            )
        {
            int i, j;
            int LTP_pred_Q13, LPC_pred_Q10, n_AR_Q12, n_LTP_Q13;
            int n_LF_Q12, r_Q10, rr_Q10, q1_Q0, q1_Q10, q2_Q10, rd1_Q20, rd2_Q20;
            int exc_Q14, LPC_exc_Q14, xq_Q14, Gain_Q10;
            int tmp1, tmp2, sLF_AR_shp_Q14;
            int psLPC_Q14;
            int shp_lag_ptr;
            int pred_lag_ptr;

            shp_lag_ptr = this.sLTP_shp_buf_idx - lag + SilkConstants.HARM_SHAPE_FIR_TAPS / 2;
            pred_lag_ptr = this.sLTP_buf_idx - lag + SilkConstants.LTP_ORDER / 2;
            Gain_Q10 = Inlines.silk_RSHIFT(Gain_Q16, 6);

            /* Set up short term AR state */
            psLPC_Q14 = SilkConstants.NSQ_LPC_BUF_LENGTH - 1;

            for (i = 0; i < length; i++)
            {
                /* Generate dither */
                this.rand_seed = Inlines.silk_RAND(this.rand_seed);

                /* Short-term prediction */
                Inlines.OpusAssert(predictLPCOrder == 10 || predictLPCOrder == 16);
                /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                LPC_pred_Q10 = Inlines.silk_RSHIFT(predictLPCOrder, 1);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 0], a_Q12[0]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 1], a_Q12[1]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 2], a_Q12[2]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 3], a_Q12[3]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 4], a_Q12[4]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 5], a_Q12[5]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 6], a_Q12[6]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 7], a_Q12[7]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 8], a_Q12[8]);
                LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 9], a_Q12[9]);
                if (predictLPCOrder == 16)
                {
                    LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 10], a_Q12[10]);
                    LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 11], a_Q12[11]);
                    LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 12], a_Q12[12]);
                    LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 13], a_Q12[13]);
                    LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 14], a_Q12[14]);
                    LPC_pred_Q10 = Inlines.silk_SMLAWB(LPC_pred_Q10, this.sLPC_Q14[psLPC_Q14 - 15], a_Q12[15]);
                }

                /* Long-term prediction */
                if (signalType == SilkConstants.TYPE_VOICED)
                {
                    /* Unrolled loop */
                    /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                    LTP_pred_Q13 = 2;
                    LTP_pred_Q13 = Inlines.silk_SMLAWB(LTP_pred_Q13, sLTP_Q15[pred_lag_ptr], b_Q14[b_Q14_ptr]);
                    LTP_pred_Q13 = Inlines.silk_SMLAWB(LTP_pred_Q13, sLTP_Q15[pred_lag_ptr - 1], b_Q14[b_Q14_ptr + 1]);
                    LTP_pred_Q13 = Inlines.silk_SMLAWB(LTP_pred_Q13, sLTP_Q15[pred_lag_ptr - 2], b_Q14[b_Q14_ptr + 2]);
                    LTP_pred_Q13 = Inlines.silk_SMLAWB(LTP_pred_Q13, sLTP_Q15[pred_lag_ptr - 3], b_Q14[b_Q14_ptr + 3]);
                    LTP_pred_Q13 = Inlines.silk_SMLAWB(LTP_pred_Q13, sLTP_Q15[pred_lag_ptr - 4], b_Q14[b_Q14_ptr + 4]);
                    pred_lag_ptr += 1;
                }
                else {
                    LTP_pred_Q13 = 0;
                }

                /* Noise shape feedback */
                Inlines.OpusAssert((shapingLPCOrder & 1) == 0);   /* check that order is even */
                tmp2 = this.sLPC_Q14[psLPC_Q14];
                tmp1 = this.sAR2_Q14[0];
                this.sAR2_Q14[0] = tmp2;
                n_AR_Q12 = Inlines.silk_RSHIFT(shapingLPCOrder, 1);
                n_AR_Q12 = Inlines.silk_SMLAWB(n_AR_Q12, tmp2, AR_shp_Q13[AR_shp_Q13_ptr]);
                for (j = 2; j < shapingLPCOrder; j += 2)
                {
                    tmp2 = this.sAR2_Q14[j - 1];
                    this.sAR2_Q14[j - 1] = tmp1;
                    n_AR_Q12 = Inlines.silk_SMLAWB(n_AR_Q12, tmp1, AR_shp_Q13[AR_shp_Q13_ptr + j - 1]);
                    tmp1 = this.sAR2_Q14[j + 0];
                    this.sAR2_Q14[j + 0] = tmp2;
                    n_AR_Q12 = Inlines.silk_SMLAWB(n_AR_Q12, tmp2, AR_shp_Q13[AR_shp_Q13_ptr + j]);
                }
                this.sAR2_Q14[shapingLPCOrder - 1] = tmp1;
                n_AR_Q12 = Inlines.silk_SMLAWB(n_AR_Q12, tmp1, AR_shp_Q13[AR_shp_Q13_ptr + shapingLPCOrder - 1]);

                n_AR_Q12 = Inlines.silk_LSHIFT32(n_AR_Q12, 1);                                /* Q11 . Q12 */
                n_AR_Q12 = Inlines.silk_SMLAWB(n_AR_Q12, this.sLF_AR_shp_Q14, Tilt_Q14);

                n_LF_Q12 = Inlines.silk_SMULWB(this.sLTP_shp_Q14[this.sLTP_shp_buf_idx - 1], LF_shp_Q14);
                n_LF_Q12 = Inlines.silk_SMLAWT(n_LF_Q12, this.sLF_AR_shp_Q14, LF_shp_Q14);

                Inlines.OpusAssert(lag > 0 || signalType != SilkConstants.TYPE_VOICED);

                /* Combine prediction and noise shaping signals */
                tmp1 = Inlines.silk_SUB32(Inlines.silk_LSHIFT32(LPC_pred_Q10, 2), n_AR_Q12);        /* Q12 */
                tmp1 = Inlines.silk_SUB32(tmp1, n_LF_Q12);                                    /* Q12 */
                if (lag > 0)
                {
                    /* Symmetric, packed FIR coefficients */
                    n_LTP_Q13 = Inlines.silk_SMULWB(Inlines.silk_ADD32(this.sLTP_shp_Q14[shp_lag_ptr], this.sLTP_shp_Q14[shp_lag_ptr - 2]), HarmShapeFIRPacked_Q14);
                    n_LTP_Q13 = Inlines.silk_SMLAWT(n_LTP_Q13, this.sLTP_shp_Q14[shp_lag_ptr - 1], HarmShapeFIRPacked_Q14);
                    n_LTP_Q13 = Inlines.silk_LSHIFT(n_LTP_Q13, 1);
                    shp_lag_ptr += 1;

                    tmp2 = Inlines.silk_SUB32(LTP_pred_Q13, n_LTP_Q13);                       /* Q13 */
                    tmp1 = Inlines.silk_ADD_LSHIFT32(tmp2, tmp1, 1);                          /* Q13 */
                    tmp1 = Inlines.silk_RSHIFT_ROUND(tmp1, 3);                                /* Q10 */
                }
                else {
                    tmp1 = Inlines.silk_RSHIFT_ROUND(tmp1, 2);                                /* Q10 */
                }

                r_Q10 = Inlines.silk_SUB32(x_sc_Q10[i], tmp1);                              /* residual error Q10 */

                /* Flip sign depending on dither */
                if (this.rand_seed < 0)
                {
                    r_Q10 = -r_Q10;
                }
                r_Q10 = Inlines.silk_LIMIT_32(r_Q10, -(31 << 10), 30 << 10);

                /* Find two quantization level candidates and measure their rate-distortion */
                q1_Q10 = Inlines.silk_SUB32(r_Q10, offset_Q10);
                q1_Q0 = Inlines.silk_RSHIFT(q1_Q10, 10);
                if (q1_Q0 > 0)
                {
                    q1_Q10 = Inlines.silk_SUB32(Inlines.silk_LSHIFT(q1_Q0, 10), SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                    q1_Q10 = Inlines.silk_ADD32(q1_Q10, offset_Q10);
                    q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024);
                    rd1_Q20 = Inlines.silk_SMULBB(q1_Q10, Lambda_Q10);
                    rd2_Q20 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                }
                else if (q1_Q0 == 0)
                {
                    q1_Q10 = offset_Q10;
                    q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024 - SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                    rd1_Q20 = Inlines.silk_SMULBB(q1_Q10, Lambda_Q10);
                    rd2_Q20 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                }
                else if (q1_Q0 == -1)
                {
                    q2_Q10 = offset_Q10;
                    q1_Q10 = Inlines.silk_SUB32(q2_Q10, 1024 - SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                    rd1_Q20 = Inlines.silk_SMULBB(-q1_Q10, Lambda_Q10);
                    rd2_Q20 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                }
                else {            /* Q1_Q0 < -1 */
                    q1_Q10 = Inlines.silk_ADD32(Inlines.silk_LSHIFT(q1_Q0, 10), SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                    q1_Q10 = Inlines.silk_ADD32(q1_Q10, offset_Q10);
                    q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024);
                    rd1_Q20 = Inlines.silk_SMULBB(-q1_Q10, Lambda_Q10);
                    rd2_Q20 = Inlines.silk_SMULBB(-q2_Q10, Lambda_Q10);
                }
                rr_Q10 = Inlines.silk_SUB32(r_Q10, q1_Q10);
                rd1_Q20 = Inlines.silk_SMLABB(rd1_Q20, rr_Q10, rr_Q10);
                rr_Q10 = Inlines.silk_SUB32(r_Q10, q2_Q10);
                rd2_Q20 = Inlines.silk_SMLABB(rd2_Q20, rr_Q10, rr_Q10);

                if (rd2_Q20 < rd1_Q20)
                {
                    q1_Q10 = q2_Q10;
                }

                pulses[pulses_ptr + i] = (sbyte)Inlines.silk_RSHIFT_ROUND(q1_Q10, 10);

                /* Excitation */
                exc_Q14 = Inlines.silk_LSHIFT(q1_Q10, 4);
                if (this.rand_seed < 0)
                {
                    exc_Q14 = -exc_Q14;
                }

                /* Add predictions */
                LPC_exc_Q14 = Inlines.silk_ADD_LSHIFT32(exc_Q14, LTP_pred_Q13, 1);
                xq_Q14 = Inlines.silk_ADD_LSHIFT32(LPC_exc_Q14, LPC_pred_Q10, 4);

                /* Scale XQ back to normal level before saving */
                xq[xq_ptr + i] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWW(xq_Q14, Gain_Q10), 8));

                /* Update states */
                psLPC_Q14 += 1;
                this.sLPC_Q14[psLPC_Q14] = xq_Q14;
                sLF_AR_shp_Q14 = Inlines.silk_SUB_LSHIFT32(xq_Q14, n_AR_Q12, 2);
                this.sLF_AR_shp_Q14 = sLF_AR_shp_Q14;

                this.sLTP_shp_Q14[this.sLTP_shp_buf_idx] = Inlines.silk_SUB_LSHIFT32(sLF_AR_shp_Q14, n_LF_Q12, 2);
                sLTP_Q15[this.sLTP_buf_idx] = Inlines.silk_LSHIFT(LPC_exc_Q14, 1);
                this.sLTP_shp_buf_idx++;
                this.sLTP_buf_idx++;

                /* Make dither dependent on quantized signal */
                this.rand_seed = Inlines.silk_ADD32_ovflw(this.rand_seed, pulses[pulses_ptr + i]);
            }

            /* Update LPC synth buffer */
            Array.Copy(this.sLPC_Q14, length, this.sLPC_Q14, 0, SilkConstants.NSQ_LPC_BUF_LENGTH);
        }

        private void silk_nsq_scale_states(
                SilkChannelEncoder psEncC,           /* I    Encoder State                   */
                int[] x_Q3,                 /* I    input in Q3                     */
                int x_Q3_ptr,
                int[] x_sc_Q10,             /* O    input scaled with 1/Gain        */
                short[] sLTP,                 /* I    re-whitened LTP state in Q0     */
                int[] sLTP_Q15,             /* O    LTP state matching scaled input */
                int subfr,                  /* I    subframe number                 */
                int LTP_scale_Q14,          /* I                                    */
                int[] Gains_Q16, /* I [MAX_NB_SUBFR]                                */
                int[] pitchL, /* I    Pitch lag [MAX_NB_SUBFR]                      */
                int signal_type             /* I    Signal type                     */
            )
        {
            int i, lag;
            int gain_adj_Q16, inv_gain_Q31, inv_gain_Q23;

            lag = pitchL[subfr];
            inv_gain_Q31 = Inlines.silk_INVERSE32_varQ(Inlines.silk_max(Gains_Q16[subfr], 1), 47);
            Inlines.OpusAssert(inv_gain_Q31 != 0);

            /* Calculate gain adjustment factor */
            if (Gains_Q16[subfr] != this.prev_gain_Q16)
            {
                gain_adj_Q16 = Inlines.silk_DIV32_varQ(this.prev_gain_Q16, Gains_Q16[subfr], 16);
            }
            else {
                gain_adj_Q16 = (int)1 << 16;
            }

            /* Scale input */
            inv_gain_Q23 = Inlines.silk_RSHIFT_ROUND(inv_gain_Q31, 8);
            for (i = 0; i < psEncC.subfr_length; i++)
            {
                x_sc_Q10[i] = Inlines.silk_SMULWW(x_Q3[x_Q3_ptr + i], inv_gain_Q23);
            }

            /* Save inverse gain */
            this.prev_gain_Q16 = Gains_Q16[subfr];

            /* After rewhitening the LTP state is un-scaled, so scale with inv_gain_Q16 */
            if (this.rewhite_flag != 0)
            {
                if (subfr == 0)
                {
                    /* Do LTP downscaling */
                    inv_gain_Q31 = Inlines.silk_LSHIFT(Inlines.silk_SMULWB(inv_gain_Q31, LTP_scale_Q14), 2);
                }
                for (i = this.sLTP_buf_idx - lag - SilkConstants.LTP_ORDER / 2; i < this.sLTP_buf_idx; i++)
                {
                    Inlines.OpusAssert(i < SilkConstants.MAX_FRAME_LENGTH);
                    sLTP_Q15[i] = Inlines.silk_SMULWB(inv_gain_Q31, sLTP[i]);
                }
            }

            /* Adjust for changing gain */
            if (gain_adj_Q16 != (int)1 << 16)
            {
                /* Scale long-term shaping state */
                for (i = this.sLTP_shp_buf_idx - psEncC.ltp_mem_length; i < this.sLTP_shp_buf_idx; i++)
                {
                    this.sLTP_shp_Q14[i] = Inlines.silk_SMULWW(gain_adj_Q16, this.sLTP_shp_Q14[i]);
                }

                /* Scale long-term prediction state */
                if (signal_type == SilkConstants.TYPE_VOICED && this.rewhite_flag == 0)
                {
                    for (i = this.sLTP_buf_idx - lag - SilkConstants.LTP_ORDER / 2; i < this.sLTP_buf_idx; i++)
                    {
                        sLTP_Q15[i] = Inlines.silk_SMULWW(gain_adj_Q16, sLTP_Q15[i]);
                    }
                }

                this.sLF_AR_shp_Q14 = Inlines.silk_SMULWW(gain_adj_Q16, this.sLF_AR_shp_Q14);

                /* Scale short-term prediction and shaping states */
                for (i = 0; i < SilkConstants.NSQ_LPC_BUF_LENGTH; i++)
                {
                    this.sLPC_Q14[i] = Inlines.silk_SMULWW(gain_adj_Q16, this.sLPC_Q14[i]);
                }
                for (i = 0; i < SilkConstants.MAX_SHAPE_LPC_ORDER; i++)
                {
                    this.sAR2_Q14[i] = Inlines.silk_SMULWW(gain_adj_Q16, this.sAR2_Q14[i]);
                }
            }
        }

        internal void silk_NSQ_del_dec(
            SilkChannelEncoder psEncC,                                    /* I  Encoder State                   */
            SideInfoIndices psIndices,                                 /* I/O  Quantization Indices            */
            int[] x_Q3,                                     /* I    Prefiltered input signal        */
            sbyte[] pulses,                                   /* O    Quantized pulse signal          */
            short[][] PredCoef_Q12,          /* I    Short term prediction coefs [2 * MAX_LPC_ORDER]    */
            short[] LTPCoef_Q14,    /* I    Long term prediction coefs LTP_ORDER * MAX_NB_SUBFR]     */
            short[] AR2_Q13, /* I Noise shaping coefs  [MAX_NB_SUBFR * MAX_SHAPE_LPC_ORDER]           */
            int[] HarmShapeGain_Q14,          /* I    Long term shaping coefs [MAX_NB_SUBFR]        */
            int[] Tilt_Q14,                   /* I    Spectral tilt [MAX_NB_SUBFR]                  */
            int[] LF_shp_Q14,                 /* I    Low frequency shaping coefs [MAX_NB_SUBFR]    */
            int[] Gains_Q16,                  /* I    Quantization step sizes [MAX_NB_SUBFR]        */
            int[] pitchL,                     /* I    Pitch lags  [MAX_NB_SUBFR]                    */
            int Lambda_Q10,                                 /* I    Rate/distortion tradeoff        */
            int LTP_scale_Q14                               /* I    LTP state scaling               */
        )
        {
            int i, k, lag, start_idx, LSF_interpolation_flag, Winner_ind, subfr;
            int last_smple_idx, smpl_buf_idx, decisionDelay;
            int A_Q12;
            int pulses_ptr = 0;
            int pxq;
            int[] sLTP_Q15;
            short[] sLTP;
            int HarmShapeFIRPacked_Q14;
            int offset_Q10;
            int RDmin_Q10, Gain_Q10;
            int[] x_sc_Q10;
            int[] delayedGain_Q10;
            int x_Q3_ptr = 0;
            NSQ_del_dec_struct[] psDelDec;
            NSQ_del_dec_struct psDD;

            /* Set unvoiced lag to the previous one, overwrite later for voiced */
            lag = this.lagPrev;

            Inlines.OpusAssert(this.prev_gain_Q16 != 0);

            /* Initialize delayed decision states */
            psDelDec = new NSQ_del_dec_struct[psEncC.nStatesDelayedDecision];
            for (int c = 0; c < psEncC.nStatesDelayedDecision; c++)
            {
                psDelDec[c] = new NSQ_del_dec_struct(psEncC.shapingLPCOrder);
            }

            for (k = 0; k < psEncC.nStatesDelayedDecision; k++)
            {
                psDD = psDelDec[k];
                psDD.Seed = (k + psIndices.Seed) & 3;
                psDD.SeedInit = psDD.Seed;
                psDD.RD_Q10 = 0;
                psDD.LF_AR_Q14 = this.sLF_AR_shp_Q14;
                psDD.Shape_Q14[0] = this.sLTP_shp_Q14[psEncC.ltp_mem_length - 1];
                Array.Copy(this.sLPC_Q14, psDD.sLPC_Q14, SilkConstants.NSQ_LPC_BUF_LENGTH);
                Array.Copy(this.sAR2_Q14, psDD.sAR2_Q14, psEncC.shapingLPCOrder);
            }

            offset_Q10 = Tables.silk_Quantization_Offsets_Q10[psIndices.signalType >> 1][psIndices.quantOffsetType];
            smpl_buf_idx = 0; /* index of oldest samples */

            decisionDelay = Inlines.silk_min_int(SilkConstants.DECISION_DELAY, psEncC.subfr_length);

            /* For voiced frames limit the decision delay to lower than the pitch lag */
            if (psIndices.signalType == SilkConstants.TYPE_VOICED)
            {
                for (k = 0; k < psEncC.nb_subfr; k++)
                {
                    decisionDelay = Inlines.silk_min_int(decisionDelay, pitchL[k] - SilkConstants.LTP_ORDER / 2 - 1);
                }
            }
            else {
                if (lag > 0)
                {
                    decisionDelay = Inlines.silk_min_int(decisionDelay, lag - SilkConstants.LTP_ORDER / 2 - 1);
                }
            }

            if (psIndices.NLSFInterpCoef_Q2 == 4)
            {
                LSF_interpolation_flag = 0;
            }
            else {
                LSF_interpolation_flag = 1;
            }

            sLTP_Q15 = new int[psEncC.ltp_mem_length + psEncC.frame_length];
            sLTP = new short[psEncC.ltp_mem_length + psEncC.frame_length];
            x_sc_Q10 = new int[psEncC.subfr_length];
            delayedGain_Q10 = new int[SilkConstants.DECISION_DELAY];

            /* Set up pointers to start of sub frame */
            pxq = psEncC.ltp_mem_length;
            this.sLTP_shp_buf_idx = psEncC.ltp_mem_length;
            this.sLTP_buf_idx = psEncC.ltp_mem_length;
            subfr = 0;
            for (k = 0; k < psEncC.nb_subfr; k++)
            {
                A_Q12 = (((k >> 1) | (1 - LSF_interpolation_flag)));

                /* Noise shape parameters */
                Inlines.OpusAssert(HarmShapeGain_Q14[k] >= 0);
                HarmShapeFIRPacked_Q14 = Inlines.silk_RSHIFT(HarmShapeGain_Q14[k], 2);
                HarmShapeFIRPacked_Q14 |= Inlines.silk_LSHIFT((int)Inlines.silk_RSHIFT(HarmShapeGain_Q14[k], 1), 16);

                this.rewhite_flag = 0;
                if (psIndices.signalType == SilkConstants.TYPE_VOICED)
                {
                    /* Voiced */
                    lag = pitchL[k];

                    /* Re-whitening */
                    if ((k & (3 - Inlines.silk_LSHIFT(LSF_interpolation_flag, 1))) == 0)
                    {
                        if (k == 2)
                        {
                            /* RESET DELAYED DECISIONS */
                            /* Find winner */
                            RDmin_Q10 = psDelDec[0].RD_Q10;
                            Winner_ind = 0;
                            for (i = 1; i < psEncC.nStatesDelayedDecision; i++)
                            {
                                if (psDelDec[i].RD_Q10 < RDmin_Q10)
                                {
                                    RDmin_Q10 = psDelDec[i].RD_Q10;
                                    Winner_ind = i;
                                }
                            }
                            for (i = 0; i < psEncC.nStatesDelayedDecision; i++)
                            {
                                if (i != Winner_ind)
                                {
                                    psDelDec[i].RD_Q10 += (int.MaxValue >> 4);
                                    Inlines.OpusAssert(psDelDec[i].RD_Q10 >= 0);
                                }
                            }

                            /* Copy final part of signals from winner state to output and long-term filter states */
                            psDD = psDelDec[Winner_ind];
                            last_smple_idx = smpl_buf_idx + decisionDelay;
                            for (i = 0; i < decisionDelay; i++)
                            {
                                last_smple_idx = (last_smple_idx - 1) & SilkConstants.DECISION_DELAY_MASK;
                                pulses[pulses_ptr + i - decisionDelay] = (sbyte)Inlines.silk_RSHIFT_ROUND(psDD.Q_Q10[last_smple_idx], 10);
                                this.xq[pxq + i - decisionDelay] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(
                                                            Inlines.silk_SMULWW(psDD.Xq_Q14[last_smple_idx], Gains_Q16[1]), 14));
                                this.sLTP_shp_Q14[this.sLTP_shp_buf_idx - decisionDelay + i] = psDD.Shape_Q14[last_smple_idx];
                            }

                            subfr = 0;
                        }

                        /* Rewhiten with new A coefs */
                        start_idx = psEncC.ltp_mem_length - lag - psEncC.predictLPCOrder - SilkConstants.LTP_ORDER / 2;
                        Inlines.OpusAssert(start_idx > 0);

                        Filters.silk_LPC_analysis_filter(sLTP, start_idx, this.xq, start_idx + k * psEncC.subfr_length,
                            PredCoef_Q12[A_Q12], 0, psEncC.ltp_mem_length - start_idx, psEncC.predictLPCOrder);

                        this.sLTP_buf_idx = psEncC.ltp_mem_length;
                        this.rewhite_flag = 1;
                    }
                }

                silk_nsq_del_dec_scale_states(
                    psEncC,
                    psDelDec,
                    x_Q3,
                    x_Q3_ptr,
                    x_sc_Q10,
                    sLTP,
                    sLTP_Q15,
                    k,
                    psEncC.nStatesDelayedDecision,
                    LTP_scale_Q14,
                    Gains_Q16,
                    pitchL,
                    psIndices.signalType,
                    decisionDelay);

                BoxedValueInt smpl_buf_idx_boxed = new BoxedValueInt(smpl_buf_idx);
#if !UNSAFE
                silk_noise_shape_quantizer_del_dec(
                    psDelDec,
                    psIndices.signalType,
                    x_sc_Q10,
                    pulses,
                    pulses_ptr,
                    this.xq,
                    pxq,
                    sLTP_Q15,
                    delayedGain_Q10,
                    PredCoef_Q12[A_Q12],
                    LTPCoef_Q14,
                    k * SilkConstants.LTP_ORDER,
                    AR2_Q13,
                    k * SilkConstants.MAX_SHAPE_LPC_ORDER,
                    lag,
                    HarmShapeFIRPacked_Q14,
                    Tilt_Q14[k],
                    LF_shp_Q14[k],
                    Gains_Q16[k],
                    Lambda_Q10,
                    offset_Q10,
                    psEncC.subfr_length,
                    subfr++,
                    psEncC.shapingLPCOrder,
                    psEncC.predictLPCOrder,
                    psEncC.warping_Q16,
                    psEncC.nStatesDelayedDecision,
                    smpl_buf_idx_boxed,
                    decisionDelay);

#else
                unsafe
                {
                    fixed (short* pred_coef = PredCoef_Q12[A_Q12])
                    {
                        fixed (short* ltp_coef = LTPCoef_Q14)
                        {
                            fixed (int* sltp = sLTP_Q15)
                            {
                                short* b_q14 = ltp_coef + (k * SilkConstants.LTP_ORDER);
                                silk_noise_shape_quantizer_del_dec(
                                    psDelDec,
                                    psIndices.signalType,
                                    x_sc_Q10,
                                    pulses,
                                    pulses_ptr,
                                    this.xq,
                                    pxq,
                                    sltp,
                                    delayedGain_Q10,
                                    pred_coef,
                                    b_q14,
                                    AR2_Q13,
                                    k * SilkConstants.MAX_SHAPE_LPC_ORDER,
                                    lag,
                                    HarmShapeFIRPacked_Q14,
                                    Tilt_Q14[k],
                                    LF_shp_Q14[k],
                                    Gains_Q16[k],
                                    Lambda_Q10,
                                    offset_Q10,
                                    psEncC.subfr_length,
                                    subfr++,
                                    psEncC.shapingLPCOrder,
                                    psEncC.predictLPCOrder,
                                    psEncC.warping_Q16,
                                    psEncC.nStatesDelayedDecision,
                                    smpl_buf_idx_boxed,
                                    decisionDelay);
                            }
                        }
                    }
                }
#endif
                smpl_buf_idx = smpl_buf_idx_boxed.Val;

                x_Q3_ptr += psEncC.subfr_length;
                pulses_ptr += psEncC.subfr_length;
                pxq += psEncC.subfr_length;
            }

            /* Find winner */
            RDmin_Q10 = psDelDec[0].RD_Q10;
            Winner_ind = 0;
            for (k = 1; k < psEncC.nStatesDelayedDecision; k++)
            {
                if (psDelDec[k].RD_Q10 < RDmin_Q10)
                {
                    RDmin_Q10 = psDelDec[k].RD_Q10;
                    Winner_ind = k;
                }
            }

            /* Copy final part of signals from winner state to output and long-term filter states */
            psDD = psDelDec[Winner_ind];
            psIndices.Seed = (sbyte)(psDD.SeedInit);
            last_smple_idx = smpl_buf_idx + decisionDelay;
            Gain_Q10 = Inlines.silk_RSHIFT32(Gains_Q16[psEncC.nb_subfr - 1], 6);
            for (i = 0; i < decisionDelay; i++)
            {
                last_smple_idx = (last_smple_idx - 1) & SilkConstants.DECISION_DELAY_MASK;
                pulses[pulses_ptr + i - decisionDelay] = (sbyte)Inlines.silk_RSHIFT_ROUND(psDD.Q_Q10[last_smple_idx], 10);
                this.xq[pxq + i - decisionDelay] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(
                            Inlines.silk_SMULWW(psDD.Xq_Q14[last_smple_idx], Gain_Q10), 8));
                this.sLTP_shp_Q14[this.sLTP_shp_buf_idx - decisionDelay + i] = psDD.Shape_Q14[last_smple_idx];
            }
            Array.Copy(psDD.sLPC_Q14, psEncC.subfr_length, this.sLPC_Q14, 0, SilkConstants.NSQ_LPC_BUF_LENGTH);
            Array.Copy(psDD.sAR2_Q14, 0, this.sAR2_Q14, 0, psEncC.shapingLPCOrder);

            /* Update states */
            this.sLF_AR_shp_Q14 = psDD.LF_AR_Q14;
            this.lagPrev = pitchL[psEncC.nb_subfr - 1];

            /* Save quantized speech signal */
            Arrays.MemMoveShort(this.xq, psEncC.frame_length, 0, psEncC.ltp_mem_length);
            Arrays.MemMoveInt(this.sLTP_shp_Q14, psEncC.frame_length, 0, psEncC.ltp_mem_length);
        }

#if !UNSAFE

        /******************************************/
        /* Noise shape quantizer for one subframe */
        /******************************************/
        private void silk_noise_shape_quantizer_del_dec(
            NSQ_del_dec_struct[] psDelDec,             /* I/O  Delayed decision states             */
            int signalType,             /* I    Signal type                         */
            int[] x_Q10,                /* I                                        */
            sbyte[] pulses,               /* O                                        */
            int pulses_ptr,
            short[] xq,                   /* O                                        */
            int xq_ptr,
            int[] sLTP_Q15,             /* I/O  LTP filter state                    */
            int[] delayedGain_Q10,      /* I/O  Gain delay buffer                   */
            short[] a_Q12,                /* I    Short term prediction coefs         */
            short[] b_Q14,                /* I    Long term prediction coefs          */
            int b_Q14_ptr,
            short[] AR_shp_Q13,           /* I    Noise shaping coefs                 */
            int AR_shp_Q13_ptr,
            int lag,                    /* I    Pitch lag                           */
            int HarmShapeFIRPacked_Q14, /* I                                        */
            int Tilt_Q14,               /* I    Spectral tilt                       */
            int LF_shp_Q14,             /* I                                        */
            int Gain_Q16,               /* I                                        */
            int Lambda_Q10,             /* I                                        */
            int offset_Q10,             /* I                                        */
            int length,                 /* I    Input length                        */
            int subfr,                  /* I    Subframe number                     */
            int shapingLPCOrder,        /* I    Shaping LPC filter order            */
            int predictLPCOrder,        /* I    Prediction filter order             */
            int warping_Q16,            /* I                                        */
            int nStatesDelayedDecision, /* I    Number of states in decision tree   */
            BoxedValueInt smpl_buf_idx,          /* I    Index to newest samples in buffers  */
            int decisionDelay           /* I                                        */
        )
        {
            int i, j, k, Winner_ind, RDmin_ind, RDmax_ind, last_smple_idx;
            int Winner_rand_state;
            int LTP_pred_Q14, LPC_pred_Q14, n_AR_Q14, n_LTP_Q14;
            int n_LF_Q14, r_Q10, rr_Q10, rd1_Q10, rd2_Q10, RDmin_Q10, RDmax_Q10;
            int q1_Q0, q1_Q10, q2_Q10, exc_Q14, LPC_exc_Q14, xq_Q14, Gain_Q10;
            int tmp1, tmp2, sLF_AR_shp_Q14;
            int pred_lag_ptr, shp_lag_ptr, psLPC_Q14;
            NSQ_sample_struct[] sampleStates;
            NSQ_del_dec_struct psDD;
            int SS_left;
            int SS_right;

            Inlines.OpusAssert(nStatesDelayedDecision > 0);
            sampleStates = new NSQ_sample_struct[2 * nStatesDelayedDecision];
            // [porting note] structs must be initialized manually here
            for (int c = 0; c < 2 * nStatesDelayedDecision; c++)
            {
                sampleStates[c] = new NSQ_sample_struct();
            }

            shp_lag_ptr = this.sLTP_shp_buf_idx - lag + SilkConstants.HARM_SHAPE_FIR_TAPS / 2;
            pred_lag_ptr = this.sLTP_buf_idx - lag + SilkConstants.LTP_ORDER / 2;
            Gain_Q10 = Inlines.silk_RSHIFT(Gain_Q16, 6);

            for (i = 0; i < length; i++)
            {
                /* Perform common calculations used in all states */

                /* Long-term prediction */
                if (signalType == SilkConstants.TYPE_VOICED)
                {
                    /* Unrolled loop */
                    /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                    LTP_pred_Q14 = 2;
                    LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr], b_Q14[b_Q14_ptr + 0]);
                    LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 1], b_Q14[b_Q14_ptr + 1]);
                    LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 2], b_Q14[b_Q14_ptr + 2]);
                    LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 3], b_Q14[b_Q14_ptr + 3]);
                    LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 4], b_Q14[b_Q14_ptr + 4]);
                    LTP_pred_Q14 = Inlines.silk_LSHIFT(LTP_pred_Q14, 1);                          /* Q13 . Q14 */
                    pred_lag_ptr += 1;
                }
                else {
                    LTP_pred_Q14 = 0;
                }

                /* Long-term shaping */
                if (lag > 0)
                {
                    /* Symmetric, packed FIR coefficients */
                    n_LTP_Q14 = Inlines.silk_SMULWB(Inlines.silk_ADD32(this.sLTP_shp_Q14[shp_lag_ptr], this.sLTP_shp_Q14[shp_lag_ptr - 2]), HarmShapeFIRPacked_Q14);
                    n_LTP_Q14 = Inlines.silk_SMLAWT(n_LTP_Q14, this.sLTP_shp_Q14[shp_lag_ptr - 1], HarmShapeFIRPacked_Q14);
                    n_LTP_Q14 = Inlines.silk_SUB_LSHIFT32(LTP_pred_Q14, n_LTP_Q14, 2);            /* Q12 . Q14 */
                    shp_lag_ptr += 1;
                }
                else {
                    n_LTP_Q14 = 0;
                }

                for (k = 0; k < nStatesDelayedDecision; k++)
                {
                    /* Delayed decision state */
                    psDD = psDelDec[k];
                    int[] psDD_sAR2 = psDD.sAR2_Q14;

                    /* Sample state */
                    SS_left = 2 * k;
                    SS_right = SS_left + 1;

                    /* Generate dither */
                    psDD.Seed = Inlines.silk_RAND(psDD.Seed);

                    /* Pointer used in short term prediction and shaping */
                    psLPC_Q14 = SilkConstants.NSQ_LPC_BUF_LENGTH - 1 + i;
                    /* Short-term prediction */
                    Inlines.OpusAssert(predictLPCOrder == 10 || predictLPCOrder == 16);
                    /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                    LPC_pred_Q14 = Inlines.silk_RSHIFT(predictLPCOrder, 1);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14], a_Q12[0]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 1], a_Q12[1]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 2], a_Q12[2]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 3], a_Q12[3]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 4], a_Q12[4]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 5], a_Q12[5]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 6], a_Q12[6]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 7], a_Q12[7]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 8], a_Q12[8]);
                    LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 9], a_Q12[9]);
                    if (predictLPCOrder == 16)
                    {
                        LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 10], a_Q12[10]);
                        LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 11], a_Q12[11]);
                        LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 12], a_Q12[12]);
                        LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 13], a_Q12[13]);
                        LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 14], a_Q12[14]);
                        LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 15], a_Q12[15]);
                    }
                    LPC_pred_Q14 = Inlines.silk_LSHIFT(LPC_pred_Q14, 4);                              /* Q10 . Q14 */


                    /* Noise shape feedback */
                    Inlines.OpusAssert((shapingLPCOrder & 1) == 0);   /* check that order is even */
                                                                      /* Output of lowpass section */
                    tmp2 = Inlines.silk_SMLAWB(psDD.sLPC_Q14[psLPC_Q14], psDD_sAR2[0], warping_Q16);
                    /* Output of allpass section */
                    tmp1 = Inlines.silk_SMLAWB(psDD_sAR2[0], psDD_sAR2[1] - tmp2, warping_Q16);
                    psDD_sAR2[0] = tmp2;
                    n_AR_Q14 = Inlines.silk_RSHIFT(shapingLPCOrder, 1);
                    n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp2, AR_shp_Q13[AR_shp_Q13_ptr]);
                    /* Loop over allpass sections */
                    for (j = 2; j < shapingLPCOrder; j += 2)
                    {
                        /* Output of allpass section */
                        tmp2 = Inlines.silk_SMLAWB(psDD_sAR2[j - 1], psDD_sAR2[j + 0] - tmp1, warping_Q16);
                        psDD_sAR2[j - 1] = tmp1;
                        n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp1, AR_shp_Q13[AR_shp_Q13_ptr + j - 1]);
                        /* Output of allpass section */
                        tmp1 = Inlines.silk_SMLAWB(psDD_sAR2[j + 0], psDD_sAR2[j + 1] - tmp2, warping_Q16);
                        psDD_sAR2[j + 0] = tmp2;
                        n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp2, AR_shp_Q13[AR_shp_Q13_ptr + j]);
                    }
                    psDD_sAR2[shapingLPCOrder - 1] = tmp1;
                    n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp1, AR_shp_Q13[AR_shp_Q13_ptr + shapingLPCOrder - 1]);

                    n_AR_Q14 = Inlines.silk_LSHIFT(n_AR_Q14, 1);                                      /* Q11 . Q12 */
                    n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, psDD.LF_AR_Q14, Tilt_Q14);              /* Q12 */
                    n_AR_Q14 = Inlines.silk_LSHIFT(n_AR_Q14, 2);                                      /* Q12 . Q14 */

                    n_LF_Q14 = Inlines.silk_SMULWB(psDD.Shape_Q14[smpl_buf_idx.Val], LF_shp_Q14);     /* Q12 */
                    n_LF_Q14 = Inlines.silk_SMLAWT(n_LF_Q14, psDD.LF_AR_Q14, LF_shp_Q14);            /* Q12 */
                    n_LF_Q14 = Inlines.silk_LSHIFT(n_LF_Q14, 2);                                      /* Q12 . Q14 */

                    /* Input minus prediction plus noise feedback                       */
                    /* r = x[ i ] - LTP_pred - LPC_pred + n_AR + n_Tilt + n_LF + n_LTP  */
                    tmp1 = Inlines.silk_ADD32(n_AR_Q14, n_LF_Q14);                                    /* Q14 */
                    tmp2 = Inlines.silk_ADD32(n_LTP_Q14, LPC_pred_Q14);                               /* Q13 */
                    tmp1 = Inlines.silk_SUB32(tmp2, tmp1);                                            /* Q13 */
                    tmp1 = Inlines.silk_RSHIFT_ROUND(tmp1, 4);                                        /* Q10 */

                    r_Q10 = Inlines.silk_SUB32(x_Q10[i], tmp1);                                     /* residual error Q10 */

                    /* Flip sign depending on dither */
                    if (psDD.Seed < 0)
                    {
                        r_Q10 = -r_Q10;
                    }
                    r_Q10 = Inlines.silk_LIMIT_32(r_Q10, -(31 << 10), 30 << 10);

                    /* Find two quantization level candidates and measure their rate-distortion */
                    q1_Q10 = Inlines.silk_SUB32(r_Q10, offset_Q10);
                    q1_Q0 = Inlines.silk_RSHIFT(q1_Q10, 10);
                    if (q1_Q0 > 0)
                    {
                        q1_Q10 = Inlines.silk_SUB32(Inlines.silk_LSHIFT(q1_Q0, 10), SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                        q1_Q10 = Inlines.silk_ADD32(q1_Q10, offset_Q10);
                        q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024);
                        rd1_Q10 = Inlines.silk_SMULBB(q1_Q10, Lambda_Q10);
                        rd2_Q10 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                    }
                    else if (q1_Q0 == 0)
                    {
                        q1_Q10 = offset_Q10;
                        q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024 - SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                        rd1_Q10 = Inlines.silk_SMULBB(q1_Q10, Lambda_Q10);
                        rd2_Q10 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                    }
                    else if (q1_Q0 == -1)
                    {
                        q2_Q10 = offset_Q10;
                        q1_Q10 = Inlines.silk_SUB32(q2_Q10, 1024 - SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                        rd1_Q10 = Inlines.silk_SMULBB(-q1_Q10, Lambda_Q10);
                        rd2_Q10 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                    }
                    else {            /* q1_Q0 < -1 */
                        q1_Q10 = Inlines.silk_ADD32(Inlines.silk_LSHIFT(q1_Q0, 10), SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                        q1_Q10 = Inlines.silk_ADD32(q1_Q10, offset_Q10);
                        q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024);
                        rd1_Q10 = Inlines.silk_SMULBB(-q1_Q10, Lambda_Q10);
                        rd2_Q10 = Inlines.silk_SMULBB(-q2_Q10, Lambda_Q10);
                    }
                    rr_Q10 = Inlines.silk_SUB32(r_Q10, q1_Q10);
                    rd1_Q10 = Inlines.silk_RSHIFT(Inlines.silk_SMLABB(rd1_Q10, rr_Q10, rr_Q10), 10);
                    rr_Q10 = Inlines.silk_SUB32(r_Q10, q2_Q10);
                    rd2_Q10 = Inlines.silk_RSHIFT(Inlines.silk_SMLABB(rd2_Q10, rr_Q10, rr_Q10), 10);

                    if (rd1_Q10 < rd2_Q10)
                    {
                        sampleStates[SS_left].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd1_Q10);
                        sampleStates[SS_right].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd2_Q10);
                        sampleStates[SS_left].Q_Q10 = q1_Q10;
                        sampleStates[SS_right].Q_Q10 = q2_Q10;
                    }
                    else {
                        sampleStates[SS_left].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd2_Q10);
                        sampleStates[SS_right].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd1_Q10);
                        sampleStates[SS_left].Q_Q10 = q2_Q10;
                        sampleStates[SS_right].Q_Q10 = q1_Q10;
                    }

                    /* Update states for best quantization */

                    /* Quantized excitation */
                    exc_Q14 = Inlines.silk_LSHIFT32(sampleStates[SS_left].Q_Q10, 4);
                    if (psDD.Seed < 0)
                    {
                        exc_Q14 = -exc_Q14;
                    }

                    /* Add predictions */
                    LPC_exc_Q14 = Inlines.silk_ADD32(exc_Q14, LTP_pred_Q14);
                    xq_Q14 = Inlines.silk_ADD32(LPC_exc_Q14, LPC_pred_Q14);

                    /* Update states */
                    sLF_AR_shp_Q14 = Inlines.silk_SUB32(xq_Q14, n_AR_Q14);
                    sampleStates[SS_left].sLTP_shp_Q14 = Inlines.silk_SUB32(sLF_AR_shp_Q14, n_LF_Q14);
                    sampleStates[SS_left].LF_AR_Q14 = sLF_AR_shp_Q14;
                    sampleStates[SS_left].LPC_exc_Q14 = LPC_exc_Q14;
                    sampleStates[SS_left].xq_Q14 = xq_Q14;

                    /* Update states for second best quantization */

                    /* Quantized excitation */
                    exc_Q14 = Inlines.silk_LSHIFT32(sampleStates[SS_right].Q_Q10, 4);
                    if (psDD.Seed < 0)
                    {
                        exc_Q14 = -exc_Q14;
                    }


                    /* Add predictions */
                    LPC_exc_Q14 = Inlines.silk_ADD32(exc_Q14, LTP_pred_Q14);
                    xq_Q14 = Inlines.silk_ADD32(LPC_exc_Q14, LPC_pred_Q14);

                    /* Update states */
                    sLF_AR_shp_Q14 = Inlines.silk_SUB32(xq_Q14, n_AR_Q14);
                    sampleStates[SS_right].sLTP_shp_Q14 = Inlines.silk_SUB32(sLF_AR_shp_Q14, n_LF_Q14);
                    sampleStates[SS_right].LF_AR_Q14 = sLF_AR_shp_Q14;
                    sampleStates[SS_right].LPC_exc_Q14 = LPC_exc_Q14;
                    sampleStates[SS_right].xq_Q14 = xq_Q14;
                }

                smpl_buf_idx.Val = (smpl_buf_idx.Val - 1) & SilkConstants.DECISION_DELAY_MASK;                   /* Index to newest samples              */
                last_smple_idx = (smpl_buf_idx.Val + decisionDelay) & SilkConstants.DECISION_DELAY_MASK;       /* Index to decisionDelay old samples   */

                /* Find winner */
                RDmin_Q10 = sampleStates[0].RD_Q10;
                Winner_ind = 0;
                for (k = 1; k < nStatesDelayedDecision; k++)
                {
                    if (sampleStates[k * 2].RD_Q10 < RDmin_Q10)
                    {
                        RDmin_Q10 = sampleStates[k * 2].RD_Q10;
                        Winner_ind = k;
                    }
                }

                /* Increase RD values of expired states */
                Winner_rand_state = psDelDec[Winner_ind].RandState[last_smple_idx];
                for (k = 0; k < nStatesDelayedDecision; k++)
                {
                    if (psDelDec[k].RandState[last_smple_idx] != Winner_rand_state)
                    {
                        int k2 = k * 2;
                        sampleStates[k2].RD_Q10 = Inlines.silk_ADD32(sampleStates[k2].RD_Q10, int.MaxValue >> 4);
                        sampleStates[k2 + 1].RD_Q10 = Inlines.silk_ADD32(sampleStates[k2 + 1].RD_Q10, int.MaxValue >> 4);
                        Inlines.OpusAssert(sampleStates[k2].RD_Q10 >= 0);
                    }
                }

                /* Find worst in first set and best in second set */
                RDmax_Q10 = sampleStates[0].RD_Q10;
                RDmin_Q10 = sampleStates[1].RD_Q10;
                RDmax_ind = 0;
                RDmin_ind = 0;
                for (k = 1; k < nStatesDelayedDecision; k++)
                {
                    int k2 = k * 2;
                    /* find worst in first set */
                    if (sampleStates[k2].RD_Q10 > RDmax_Q10)
                    {
                        RDmax_Q10 = sampleStates[k2].RD_Q10;
                        RDmax_ind = k;
                    }
                    /* find best in second set */
                    if (sampleStates[k2 + 1].RD_Q10 < RDmin_Q10)
                    {
                        RDmin_Q10 = sampleStates[k2 + 1].RD_Q10;
                        RDmin_ind = k;
                    }
                }

                /* Replace a state if best from second set outperforms worst in first set */
                if (RDmin_Q10 < RDmax_Q10)
                {
                    psDelDec[RDmax_ind].PartialCopyFrom(psDelDec[RDmin_ind], i);
                    sampleStates[RDmax_ind * 2] = (sampleStates[RDmin_ind * 2 + 1]); // porting note: this uses struct copy-on-assign semantics
                }

                /* Write samples from winner to output and long-term filter states */
                psDD = psDelDec[Winner_ind];
                if (subfr > 0 || i >= decisionDelay)
                {
                    pulses[pulses_ptr + i - decisionDelay] = (sbyte)Inlines.silk_RSHIFT_ROUND(psDD.Q_Q10[last_smple_idx], 10);
                    xq[xq_ptr + i - decisionDelay] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(
                        Inlines.silk_SMULWW(psDD.Xq_Q14[last_smple_idx], delayedGain_Q10[last_smple_idx]), 8));
                    this.sLTP_shp_Q14[this.sLTP_shp_buf_idx - decisionDelay] = psDD.Shape_Q14[last_smple_idx];
                    sLTP_Q15[this.sLTP_buf_idx - decisionDelay] = psDD.Pred_Q15[last_smple_idx];
                }
                this.sLTP_shp_buf_idx++;
                this.sLTP_buf_idx++;

                /* Update states */
                for (k = 0; k < nStatesDelayedDecision; k++)
                {
                    psDD = psDelDec[k];
                    SS_left = k * 2;
                    psDD.LF_AR_Q14 = sampleStates[SS_left].LF_AR_Q14;
                    psDD.sLPC_Q14[SilkConstants.NSQ_LPC_BUF_LENGTH + i] = sampleStates[SS_left].xq_Q14;
                    psDD.Xq_Q14[smpl_buf_idx.Val] = sampleStates[SS_left].xq_Q14;
                    psDD.Q_Q10[smpl_buf_idx.Val] = sampleStates[SS_left].Q_Q10;
                    psDD.Pred_Q15[smpl_buf_idx.Val] = Inlines.silk_LSHIFT32(sampleStates[SS_left].LPC_exc_Q14, 1);
                    psDD.Shape_Q14[smpl_buf_idx.Val] = sampleStates[SS_left].sLTP_shp_Q14;
                    psDD.Seed = Inlines.silk_ADD32_ovflw(psDD.Seed, Inlines.silk_RSHIFT_ROUND(sampleStates[SS_left].Q_Q10, 10));
                    psDD.RandState[smpl_buf_idx.Val] = psDD.Seed;
                    psDD.RD_Q10 = sampleStates[SS_left].RD_Q10;
                }
                delayedGain_Q10[smpl_buf_idx.Val] = Gain_Q10;
            }

            /* Update LPC states */
            for (k = 0; k < nStatesDelayedDecision; k++)
            {
                psDD = psDelDec[k];
                Buffer.BlockCopy(psDD.sLPC_Q14, length * sizeof(int), psDD.sLPC_Q14, 0, SilkConstants.NSQ_LPC_BUF_LENGTH * sizeof(int));
            }
        }

#else

        /******************************************/
        /* Noise shape quantizer for one subframe */
        /******************************************/
        private unsafe void silk_noise_shape_quantizer_del_dec(
            NSQ_del_dec_struct[] psDelDec,             /* I/O  Delayed decision states             */
            int signalType,             /* I    Signal type                         */
            int[] x_Q10,                /* I                                        */
            sbyte[] pulses,               /* O                                        */
            int pulses_ptr,
            short[] xq,                   /* O                                        */
            int xq_ptr,
            int* sLTP_Q15,             /* I/O  LTP filter state                    */
            int[] delayedGain_Q10,      /* I/O  Gain delay buffer                   */
            short* a_Q12,                /* I    Short term prediction coefs         */
            short* b_Q14,                /* I    Long term prediction coefs          */
            short[] AR_shp_Q13,           /* I    Noise shaping coefs                 */
            int AR_shp_Q13_ptr,
            int lag,                    /* I    Pitch lag                           */
            int HarmShapeFIRPacked_Q14, /* I                                        */
            int Tilt_Q14,               /* I    Spectral tilt                       */
            int LF_shp_Q14,             /* I                                        */
            int Gain_Q16,               /* I                                        */
            int Lambda_Q10,             /* I                                        */
            int offset_Q10,             /* I                                        */
            int length,                 /* I    Input length                        */
            int subfr,                  /* I    Subframe number                     */
            int shapingLPCOrder,        /* I    Shaping LPC filter order            */
            int predictLPCOrder,        /* I    Prediction filter order             */
            int warping_Q16,            /* I                                        */
            int nStatesDelayedDecision, /* I    Number of states in decision tree   */
            BoxedValueInt smpl_buf_idx,          /* I    Index to newest samples in buffers  */
            int decisionDelay           /* I                                        */
        )
        {
            int i, j, k, Winner_ind, RDmin_ind, RDmax_ind, last_smple_idx;
            int Winner_rand_state;
            int LTP_pred_Q14, LPC_pred_Q14, n_AR_Q14, n_LTP_Q14;
            int n_LF_Q14, r_Q10, rr_Q10, rd1_Q10, rd2_Q10, RDmin_Q10, RDmax_Q10;
            int q1_Q0, q1_Q10, q2_Q10, exc_Q14, LPC_exc_Q14, xq_Q14, Gain_Q10;
            int tmp1, tmp2, sLF_AR_shp_Q14;
            int pred_lag_ptr, shp_lag_ptr, psLPC_Q14;
            NSQ_sample_struct[] sampleStates;
            NSQ_del_dec_struct psDD;
            int SS_left;
            int SS_right;

            Inlines.OpusAssert(nStatesDelayedDecision > 0);
            sampleStates = new NSQ_sample_struct[2 * nStatesDelayedDecision];
            // [porting note] structs must be initialized manually here
            for (int c = 0; c < 2 * nStatesDelayedDecision; c++)
            {
                sampleStates[c] = new NSQ_sample_struct();
            }

            shp_lag_ptr = this.sLTP_shp_buf_idx - lag + SilkConstants.HARM_SHAPE_FIR_TAPS / 2;
            pred_lag_ptr = this.sLTP_buf_idx - lag + SilkConstants.LTP_ORDER / 2;
            Gain_Q10 = Inlines.silk_RSHIFT(Gain_Q16, 6);

            fixed (int* psLTP_shp_Q14 = sLTP_shp_Q14)
            {
                for (i = 0; i < length; i++)
                {
                    /* Perform common calculations used in all states */

                    /* Long-term prediction */
                    if (signalType == SilkConstants.TYPE_VOICED)
                    {
                        /* Unrolled loop */
                        /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                        LTP_pred_Q14 = 2;
                        LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr], b_Q14[0]);
                        LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 1], b_Q14[1]);
                        LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 2], b_Q14[2]);
                        LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 3], b_Q14[3]);
                        LTP_pred_Q14 = Inlines.silk_SMLAWB(LTP_pred_Q14, sLTP_Q15[pred_lag_ptr - 4], b_Q14[4]);
                        LTP_pred_Q14 = Inlines.silk_LSHIFT(LTP_pred_Q14, 1);                          /* Q13 . Q14 */
                        pred_lag_ptr += 1;
                    }
                    else
                    {
                        LTP_pred_Q14 = 0;
                    }

                    /* Long-term shaping */
                    if (lag > 0)
                    {
                        /* Symmetric, packed FIR coefficients */
                        n_LTP_Q14 = Inlines.silk_SMULWB(Inlines.silk_ADD32(psLTP_shp_Q14[shp_lag_ptr], psLTP_shp_Q14[shp_lag_ptr - 2]), HarmShapeFIRPacked_Q14);
                        n_LTP_Q14 = Inlines.silk_SMLAWT(n_LTP_Q14, psLTP_shp_Q14[shp_lag_ptr - 1], HarmShapeFIRPacked_Q14);
                        n_LTP_Q14 = Inlines.silk_SUB_LSHIFT32(LTP_pred_Q14, n_LTP_Q14, 2);            /* Q12 . Q14 */
                        shp_lag_ptr += 1;
                    }
                    else
                    {
                        n_LTP_Q14 = 0;
                    }

                    for (k = 0; k < nStatesDelayedDecision; k++)
                    {
                        /* Delayed decision state */
                        psDD = psDelDec[k];
                        fixed (int* psDD_sAR2 = psDD.sAR2_Q14)
                        {
                            /* Sample state */
                            SS_left = 2 * k;
                            SS_right = SS_left + 1;

                            /* Generate dither */
                            psDD.Seed = Inlines.silk_RAND(psDD.Seed);

                            /* Pointer used in short term prediction and shaping */
                            psLPC_Q14 = SilkConstants.NSQ_LPC_BUF_LENGTH - 1 + i;
                            /* Short-term prediction */
                            Inlines.OpusAssert(predictLPCOrder == 10 || predictLPCOrder == 16);
                            /* Avoids introducing a bias because Inlines.silk_SMLAWB() always rounds to -inf */
                            LPC_pred_Q14 = Inlines.silk_RSHIFT(predictLPCOrder, 1);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14], a_Q12[0]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 1], a_Q12[1]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 2], a_Q12[2]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 3], a_Q12[3]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 4], a_Q12[4]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 5], a_Q12[5]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 6], a_Q12[6]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 7], a_Q12[7]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 8], a_Q12[8]);
                            LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 9], a_Q12[9]);
                            if (predictLPCOrder == 16)
                            {
                                LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 10], a_Q12[10]);
                                LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 11], a_Q12[11]);
                                LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 12], a_Q12[12]);
                                LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 13], a_Q12[13]);
                                LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 14], a_Q12[14]);
                                LPC_pred_Q14 = Inlines.silk_SMLAWB(LPC_pred_Q14, psDD.sLPC_Q14[psLPC_Q14 - 15], a_Q12[15]);
                            }
                            LPC_pred_Q14 = Inlines.silk_LSHIFT(LPC_pred_Q14, 4);                              /* Q10 . Q14 */


                            /* Noise shape feedback */
                            Inlines.OpusAssert((shapingLPCOrder & 1) == 0);   /* check that order is even */
                                                                              /* Output of lowpass section */
                            tmp2 = Inlines.silk_SMLAWB(psDD.sLPC_Q14[psLPC_Q14], psDD_sAR2[0], warping_Q16);
                            /* Output of allpass section */
                            tmp1 = Inlines.silk_SMLAWB(psDD_sAR2[0], psDD_sAR2[1] - tmp2, warping_Q16);
                            psDD_sAR2[0] = tmp2;
                            n_AR_Q14 = Inlines.silk_RSHIFT(shapingLPCOrder, 1);
                            n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp2, AR_shp_Q13[AR_shp_Q13_ptr]);
                            /* Loop over allpass sections */
                            for (j = 2; j < shapingLPCOrder; j += 2)
                            {
                                /* Output of allpass section */
                                tmp2 = Inlines.silk_SMLAWB(psDD_sAR2[j - 1], psDD_sAR2[j + 0] - tmp1, warping_Q16);
                                psDD_sAR2[j - 1] = tmp1;
                                n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp1, AR_shp_Q13[AR_shp_Q13_ptr + j - 1]);
                                /* Output of allpass section */
                                tmp1 = Inlines.silk_SMLAWB(psDD_sAR2[j + 0], psDD_sAR2[j + 1] - tmp2, warping_Q16);
                                psDD_sAR2[j + 0] = tmp2;
                                n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp2, AR_shp_Q13[AR_shp_Q13_ptr + j]);
                            }
                            psDD_sAR2[shapingLPCOrder - 1] = tmp1;
                            n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, tmp1, AR_shp_Q13[AR_shp_Q13_ptr + shapingLPCOrder - 1]);

                            n_AR_Q14 = Inlines.silk_LSHIFT(n_AR_Q14, 1);                                      /* Q11 . Q12 */
                            n_AR_Q14 = Inlines.silk_SMLAWB(n_AR_Q14, psDD.LF_AR_Q14, Tilt_Q14);              /* Q12 */
                            n_AR_Q14 = Inlines.silk_LSHIFT(n_AR_Q14, 2);                                      /* Q12 . Q14 */

                            n_LF_Q14 = Inlines.silk_SMULWB(psDD.Shape_Q14[smpl_buf_idx.Val], LF_shp_Q14);     /* Q12 */
                            n_LF_Q14 = Inlines.silk_SMLAWT(n_LF_Q14, psDD.LF_AR_Q14, LF_shp_Q14);            /* Q12 */
                            n_LF_Q14 = Inlines.silk_LSHIFT(n_LF_Q14, 2);                                      /* Q12 . Q14 */

                            /* Input minus prediction plus noise feedback                       */
                            /* r = x[ i ] - LTP_pred - LPC_pred + n_AR + n_Tilt + n_LF + n_LTP  */
                            tmp1 = Inlines.silk_ADD32(n_AR_Q14, n_LF_Q14);                                    /* Q14 */
                            tmp2 = Inlines.silk_ADD32(n_LTP_Q14, LPC_pred_Q14);                               /* Q13 */
                            tmp1 = Inlines.silk_SUB32(tmp2, tmp1);                                            /* Q13 */
                            tmp1 = Inlines.silk_RSHIFT_ROUND(tmp1, 4);                                        /* Q10 */

                            r_Q10 = Inlines.silk_SUB32(x_Q10[i], tmp1);                                     /* residual error Q10 */

                            /* Flip sign depending on dither */
                            if (psDD.Seed < 0)
                            {
                                r_Q10 = -r_Q10;
                            }
                            r_Q10 = Inlines.silk_LIMIT_32(r_Q10, -(31 << 10), 30 << 10);

                            /* Find two quantization level candidates and measure their rate-distortion */
                            q1_Q10 = Inlines.silk_SUB32(r_Q10, offset_Q10);
                            q1_Q0 = Inlines.silk_RSHIFT(q1_Q10, 10);
                            if (q1_Q0 > 0)
                            {
                                q1_Q10 = Inlines.silk_SUB32(Inlines.silk_LSHIFT(q1_Q0, 10), SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                                q1_Q10 = Inlines.silk_ADD32(q1_Q10, offset_Q10);
                                q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024);
                                rd1_Q10 = Inlines.silk_SMULBB(q1_Q10, Lambda_Q10);
                                rd2_Q10 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                            }
                            else if (q1_Q0 == 0)
                            {
                                q1_Q10 = offset_Q10;
                                q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024 - SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                                rd1_Q10 = Inlines.silk_SMULBB(q1_Q10, Lambda_Q10);
                                rd2_Q10 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                            }
                            else if (q1_Q0 == -1)
                            {
                                q2_Q10 = offset_Q10;
                                q1_Q10 = Inlines.silk_SUB32(q2_Q10, 1024 - SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                                rd1_Q10 = Inlines.silk_SMULBB(-q1_Q10, Lambda_Q10);
                                rd2_Q10 = Inlines.silk_SMULBB(q2_Q10, Lambda_Q10);
                            }
                            else
                            {            /* q1_Q0 < -1 */
                                q1_Q10 = Inlines.silk_ADD32(Inlines.silk_LSHIFT(q1_Q0, 10), SilkConstants.QUANT_LEVEL_ADJUST_Q10);
                                q1_Q10 = Inlines.silk_ADD32(q1_Q10, offset_Q10);
                                q2_Q10 = Inlines.silk_ADD32(q1_Q10, 1024);
                                rd1_Q10 = Inlines.silk_SMULBB(-q1_Q10, Lambda_Q10);
                                rd2_Q10 = Inlines.silk_SMULBB(-q2_Q10, Lambda_Q10);
                            }
                            rr_Q10 = Inlines.silk_SUB32(r_Q10, q1_Q10);
                            rd1_Q10 = Inlines.silk_RSHIFT(Inlines.silk_SMLABB(rd1_Q10, rr_Q10, rr_Q10), 10);
                            rr_Q10 = Inlines.silk_SUB32(r_Q10, q2_Q10);
                            rd2_Q10 = Inlines.silk_RSHIFT(Inlines.silk_SMLABB(rd2_Q10, rr_Q10, rr_Q10), 10);

                            if (rd1_Q10 < rd2_Q10)
                            {
                                sampleStates[SS_left].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd1_Q10);
                                sampleStates[SS_right].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd2_Q10);
                                sampleStates[SS_left].Q_Q10 = q1_Q10;
                                sampleStates[SS_right].Q_Q10 = q2_Q10;
                            }
                            else
                            {
                                sampleStates[SS_left].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd2_Q10);
                                sampleStates[SS_right].RD_Q10 = Inlines.silk_ADD32(psDD.RD_Q10, rd1_Q10);
                                sampleStates[SS_left].Q_Q10 = q2_Q10;
                                sampleStates[SS_right].Q_Q10 = q1_Q10;
                            }

                            /* Update states for best quantization */

                            /* Quantized excitation */
                            exc_Q14 = Inlines.silk_LSHIFT32(sampleStates[SS_left].Q_Q10, 4);
                            if (psDD.Seed < 0)
                            {
                                exc_Q14 = -exc_Q14;
                            }

                            /* Add predictions */
                            LPC_exc_Q14 = Inlines.silk_ADD32(exc_Q14, LTP_pred_Q14);
                            xq_Q14 = Inlines.silk_ADD32(LPC_exc_Q14, LPC_pred_Q14);

                            /* Update states */
                            sLF_AR_shp_Q14 = Inlines.silk_SUB32(xq_Q14, n_AR_Q14);
                            sampleStates[SS_left].sLTP_shp_Q14 = Inlines.silk_SUB32(sLF_AR_shp_Q14, n_LF_Q14);
                            sampleStates[SS_left].LF_AR_Q14 = sLF_AR_shp_Q14;
                            sampleStates[SS_left].LPC_exc_Q14 = LPC_exc_Q14;
                            sampleStates[SS_left].xq_Q14 = xq_Q14;

                            /* Update states for second best quantization */

                            /* Quantized excitation */
                            exc_Q14 = Inlines.silk_LSHIFT32(sampleStates[SS_right].Q_Q10, 4);
                            if (psDD.Seed < 0)
                            {
                                exc_Q14 = -exc_Q14;
                            }


                            /* Add predictions */
                            LPC_exc_Q14 = Inlines.silk_ADD32(exc_Q14, LTP_pred_Q14);
                            xq_Q14 = Inlines.silk_ADD32(LPC_exc_Q14, LPC_pred_Q14);

                            /* Update states */
                            sLF_AR_shp_Q14 = Inlines.silk_SUB32(xq_Q14, n_AR_Q14);
                            sampleStates[SS_right].sLTP_shp_Q14 = Inlines.silk_SUB32(sLF_AR_shp_Q14, n_LF_Q14);
                            sampleStates[SS_right].LF_AR_Q14 = sLF_AR_shp_Q14;
                            sampleStates[SS_right].LPC_exc_Q14 = LPC_exc_Q14;
                            sampleStates[SS_right].xq_Q14 = xq_Q14;
                        }
                    }

                    smpl_buf_idx.Val = (smpl_buf_idx.Val - 1) & SilkConstants.DECISION_DELAY_MASK;                   /* Index to newest samples              */
                    last_smple_idx = (smpl_buf_idx.Val + decisionDelay) & SilkConstants.DECISION_DELAY_MASK;       /* Index to decisionDelay old samples   */

                    /* Find winner */
                    RDmin_Q10 = sampleStates[0].RD_Q10;
                    Winner_ind = 0;
                    for (k = 1; k < nStatesDelayedDecision; k++)
                    {
                        if (sampleStates[k * 2].RD_Q10 < RDmin_Q10)
                        {
                            RDmin_Q10 = sampleStates[k * 2].RD_Q10;
                            Winner_ind = k;
                        }
                    }

                    /* Increase RD values of expired states */
                    Winner_rand_state = psDelDec[Winner_ind].RandState[last_smple_idx];
                    for (k = 0; k < nStatesDelayedDecision; k++)
                    {
                        if (psDelDec[k].RandState[last_smple_idx] != Winner_rand_state)
                        {
                            int k2 = k * 2;
                            sampleStates[k2].RD_Q10 = Inlines.silk_ADD32(sampleStates[k2].RD_Q10, int.MaxValue >> 4);
                            sampleStates[k2 + 1].RD_Q10 = Inlines.silk_ADD32(sampleStates[k2 + 1].RD_Q10, int.MaxValue >> 4);
                            Inlines.OpusAssert(sampleStates[k2].RD_Q10 >= 0);
                        }
                    }

                    /* Find worst in first set and best in second set */
                    RDmax_Q10 = sampleStates[0].RD_Q10;
                    RDmin_Q10 = sampleStates[1].RD_Q10;
                    RDmax_ind = 0;
                    RDmin_ind = 0;
                    for (k = 1; k < nStatesDelayedDecision; k++)
                    {
                        int k2 = k * 2;
                        /* find worst in first set */
                        if (sampleStates[k2].RD_Q10 > RDmax_Q10)
                        {
                            RDmax_Q10 = sampleStates[k2].RD_Q10;
                            RDmax_ind = k;
                        }
                        /* find best in second set */
                        if (sampleStates[k2 + 1].RD_Q10 < RDmin_Q10)
                        {
                            RDmin_Q10 = sampleStates[k2 + 1].RD_Q10;
                            RDmin_ind = k;
                        }
                    }

                    /* Replace a state if best from second set outperforms worst in first set */
                    if (RDmin_Q10 < RDmax_Q10)
                    {
                        psDelDec[RDmax_ind].PartialCopyFrom(psDelDec[RDmin_ind], i);
                        sampleStates[RDmax_ind * 2] = (sampleStates[RDmin_ind * 2 + 1]); // porting note: this uses struct copy-on-assign semantics
                    }

                    /* Write samples from winner to output and long-term filter states */
                    psDD = psDelDec[Winner_ind];
                    if (subfr > 0 || i >= decisionDelay)
                    {
                        pulses[pulses_ptr + i - decisionDelay] = (sbyte)Inlines.silk_RSHIFT_ROUND(psDD.Q_Q10[last_smple_idx], 10);
                        xq[xq_ptr + i - decisionDelay] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(
                            Inlines.silk_SMULWW(psDD.Xq_Q14[last_smple_idx], delayedGain_Q10[last_smple_idx]), 8));
                        psLTP_shp_Q14[this.sLTP_shp_buf_idx - decisionDelay] = psDD.Shape_Q14[last_smple_idx];
                        sLTP_Q15[this.sLTP_buf_idx - decisionDelay] = psDD.Pred_Q15[last_smple_idx];
                    }
                    this.sLTP_shp_buf_idx++;
                    this.sLTP_buf_idx++;

                    /* Update states */
                    for (k = 0; k < nStatesDelayedDecision; k++)
                    {
                        psDD = psDelDec[k];
                        SS_left = k * 2;
                        psDD.LF_AR_Q14 = sampleStates[SS_left].LF_AR_Q14;
                        psDD.sLPC_Q14[SilkConstants.NSQ_LPC_BUF_LENGTH + i] = sampleStates[SS_left].xq_Q14;
                        psDD.Xq_Q14[smpl_buf_idx.Val] = sampleStates[SS_left].xq_Q14;
                        psDD.Q_Q10[smpl_buf_idx.Val] = sampleStates[SS_left].Q_Q10;
                        psDD.Pred_Q15[smpl_buf_idx.Val] = Inlines.silk_LSHIFT32(sampleStates[SS_left].LPC_exc_Q14, 1);
                        psDD.Shape_Q14[smpl_buf_idx.Val] = sampleStates[SS_left].sLTP_shp_Q14;
                        psDD.Seed = Inlines.silk_ADD32_ovflw(psDD.Seed, Inlines.silk_RSHIFT_ROUND(sampleStates[SS_left].Q_Q10, 10));
                        psDD.RandState[smpl_buf_idx.Val] = psDD.Seed;
                        psDD.RD_Q10 = sampleStates[SS_left].RD_Q10;
                    }
                    delayedGain_Q10[smpl_buf_idx.Val] = Gain_Q10;
                }
            }

            /* Update LPC states */
            for (k = 0; k < nStatesDelayedDecision; k++)
            {
                psDD = psDelDec[k];
                Buffer.BlockCopy(psDD.sLPC_Q14, length * sizeof(int), psDD.sLPC_Q14, 0, SilkConstants.NSQ_LPC_BUF_LENGTH * sizeof(int));
            }
        }

#endif

        private void silk_nsq_del_dec_scale_states(
                SilkChannelEncoder psEncC,               /* I    Encoder State                       */
                NSQ_del_dec_struct[] psDelDec,                 /* I/O  Delayed decision states             */
                int[] x_Q3,                     /* I    Input in Q3                         */
                int x_Q3_ptr,
                int[] x_sc_Q10,                 /* O    Input scaled with 1/Gain in Q10     */
                short[] sLTP,                     /* I    Re-whitened LTP state in Q0         */
                int[] sLTP_Q15,                 /* O    LTP state matching scaled input     */
                int subfr,                      /* I    Subframe number                     */
                int nStatesDelayedDecision,     /* I    Number of del dec states            */
                int LTP_scale_Q14,              /* I    LTP state scaling                   */
                int[] Gains_Q16,  /* I [MAX_NB_SUBFR]                                       */
                int[] pitchL,     /* I    Pitch lag [MAX_NB_SUBFR]                          */
                int signal_type,                /* I    Signal type                         */
                int decisionDelay               /* I    Decision delay                      */
            )
        {
            int i, k, lag;
            int gain_adj_Q16, inv_gain_Q31, inv_gain_Q23;
            NSQ_del_dec_struct psDD;

            lag = pitchL[subfr];
            inv_gain_Q31 = Inlines.silk_INVERSE32_varQ(Inlines.silk_max(Gains_Q16[subfr], 1), 47);
            Inlines.OpusAssert(inv_gain_Q31 != 0);

            /* Calculate gain adjustment factor */
            if (Gains_Q16[subfr] != this.prev_gain_Q16)
            {
                gain_adj_Q16 = Inlines.silk_DIV32_varQ(this.prev_gain_Q16, Gains_Q16[subfr], 16);
            }
            else {
                gain_adj_Q16 = (int)1 << 16;
            }

            /* Scale input */
            inv_gain_Q23 = Inlines.silk_RSHIFT_ROUND(inv_gain_Q31, 8);
            for (i = 0; i < psEncC.subfr_length; i++)
            {
                x_sc_Q10[i] = Inlines.silk_SMULWW(x_Q3[x_Q3_ptr + i], inv_gain_Q23);
            }

            /* Save inverse gain */
            this.prev_gain_Q16 = Gains_Q16[subfr];

            /* After rewhitening the LTP state is un-scaled, so scale with inv_gain_Q16 */
            if (this.rewhite_flag != 0)
            {
                if (subfr == 0)
                {
                    /* Do LTP downscaling */
                    inv_gain_Q31 = Inlines.silk_LSHIFT(Inlines.silk_SMULWB(inv_gain_Q31, LTP_scale_Q14), 2);
                }
                for (i = this.sLTP_buf_idx - lag - SilkConstants.LTP_ORDER / 2; i < this.sLTP_buf_idx; i++)
                {
                    Inlines.OpusAssert(i < SilkConstants.MAX_FRAME_LENGTH);
                    sLTP_Q15[i] = Inlines.silk_SMULWB(inv_gain_Q31, sLTP[i]);
                }
            }

            /* Adjust for changing gain */
            if (gain_adj_Q16 != (int)1 << 16)
            {
                /* Scale long-term shaping state */
                for (i = this.sLTP_shp_buf_idx - psEncC.ltp_mem_length; i < this.sLTP_shp_buf_idx; i++)
                {
                    this.sLTP_shp_Q14[i] = Inlines.silk_SMULWW(gain_adj_Q16, this.sLTP_shp_Q14[i]);
                }

                /* Scale long-term prediction state */
                if (signal_type == SilkConstants.TYPE_VOICED && this.rewhite_flag == 0)
                {
                    for (i = this.sLTP_buf_idx - lag - SilkConstants.LTP_ORDER / 2; i < this.sLTP_buf_idx - decisionDelay; i++)
                    {
                        sLTP_Q15[i] = Inlines.silk_SMULWW(gain_adj_Q16, sLTP_Q15[i]);
                    }
                }

                for (k = 0; k < nStatesDelayedDecision; k++)
                {
                    psDD = psDelDec[k];

                    /* Scale scalar states */
                    psDD.LF_AR_Q14 = Inlines.silk_SMULWW(gain_adj_Q16, psDD.LF_AR_Q14);

                    /* Scale short-term prediction and shaping states */
                    for (i = 0; i < SilkConstants.NSQ_LPC_BUF_LENGTH; i++)
                    {
                        psDD.sLPC_Q14[i] = Inlines.silk_SMULWW(gain_adj_Q16, psDD.sLPC_Q14[i]);
                    }
                    for (i = 0; i < psEncC.shapingLPCOrder; i++)
                    {
                        psDD.sAR2_Q14[i] = Inlines.silk_SMULWW(gain_adj_Q16, psDD.sAR2_Q14[i]);
                    }
                    for (i = 0; i < SilkConstants.DECISION_DELAY; i++)
                    {
                        psDD.Pred_Q15[i] = Inlines.silk_SMULWW(gain_adj_Q16, psDD.Pred_Q15[i]);
                        psDD.Shape_Q14[i] = Inlines.silk_SMULWW(gain_adj_Q16, psDD.Shape_Q14[i]);
                    }
                }
            }
        }
    }
}
