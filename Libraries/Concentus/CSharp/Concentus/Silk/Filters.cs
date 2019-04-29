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

    internal static class Filters
    {
        internal static void silk_warped_LPC_analysis_filter(
            int[] state,                    /* I/O  State [order + 1]                   */
            int[] res_Q2,                   /* O    Residual signal [length]            */
            short[] coef_Q13,                 /* I    Coefficients [order]                */
            int coef_Q13_ptr,
            short[] input,                    /* I    Input signal [length]               */
            int input_ptr,
            short lambda_Q16,                 /* I    Warping factor                      */
            int length,                     /* I    Length of input signal              */
            int order                       /* I    Filter order (even)                 */
        )
        {
            int n, i;
            int acc_Q11, tmp1, tmp2;

            /* Order must be even */
            Inlines.OpusAssert((order & 1) == 0);

            for (n = 0; n < length; n++)
            {
                /* Output of lowpass section */
                tmp2 = Inlines.silk_SMLAWB(state[0], state[1], lambda_Q16);
                state[0] = Inlines.silk_LSHIFT(input[input_ptr + n], 14);
                /* Output of allpass section */
                tmp1 = Inlines.silk_SMLAWB(state[1], state[2] - tmp2, lambda_Q16);
                state[1] = tmp2;
                acc_Q11 = Inlines.silk_RSHIFT(order, 1);
                acc_Q11 = Inlines.silk_SMLAWB(acc_Q11, tmp2, coef_Q13[coef_Q13_ptr]);
                /* Loop over allpass sections */
                for (i = 2; i < order; i += 2)
                {
                    /* Output of allpass section */
                    tmp2 = Inlines.silk_SMLAWB(state[i], state[i + 1] - tmp1, lambda_Q16);
                    state[i] = tmp1;
                    acc_Q11 = Inlines.silk_SMLAWB(acc_Q11, tmp1, coef_Q13[coef_Q13_ptr + i - 1]);
                    /* Output of allpass section */
                    tmp1 = Inlines.silk_SMLAWB(state[i + 1], state[i + 2] - tmp2, lambda_Q16);
                    state[i + 1] = tmp2;
                    acc_Q11 = Inlines.silk_SMLAWB(acc_Q11, tmp2, coef_Q13[coef_Q13_ptr + i]);
                }
                state[order] = tmp1;
                acc_Q11 = Inlines.silk_SMLAWB(acc_Q11, tmp1, coef_Q13[coef_Q13_ptr + order - 1]);
                res_Q2[n] = Inlines.silk_LSHIFT((int)input[input_ptr + n], 2) - Inlines.silk_RSHIFT_ROUND(acc_Q11, 9);
            }
        }

        internal static void silk_prefilter(
            SilkChannelEncoder psEnc,                                 /* I/O  Encoder state                                                               */
            SilkEncoderControl psEncCtrl,                             /* I    Encoder control                                                             */
            int[] xw_Q3,                                /* O    Weighted signal                                                             */
            short[] x,                                     /* I    Speech signal                                                               */
            int x_ptr
)
        {
            SilkPrefilterState P = psEnc.sPrefilt;
            int j, k, lag;
            int tmp_32;
            int AR1_shp_Q13;
            int px;
            int pxw_Q3;
            int HarmShapeGain_Q12, Tilt_Q14;
            int HarmShapeFIRPacked_Q12, LF_shp_Q14;
            int[] x_filt_Q12;
            int[] st_res_Q2;
            short[] B_Q10 = new short[2];

            /* Set up pointers */
            px = x_ptr;
            pxw_Q3 = 0;
            lag = P.lagPrev;
            x_filt_Q12 = new int[psEnc.subfr_length];
            st_res_Q2 = new int[psEnc.subfr_length];
            for (k = 0; k < psEnc.nb_subfr; k++)
            {
                /* Update Variables that change per sub frame */
                if (psEnc.indices.signalType == SilkConstants.TYPE_VOICED)
                {
                    lag = psEncCtrl.pitchL[k];
                }

                /* Noise shape parameters */
                HarmShapeGain_Q12 = Inlines.silk_SMULWB((int)psEncCtrl.HarmShapeGain_Q14[k], 16384 - psEncCtrl.HarmBoost_Q14[k]);
                Inlines.OpusAssert(HarmShapeGain_Q12 >= 0);
                HarmShapeFIRPacked_Q12 = Inlines.silk_RSHIFT(HarmShapeGain_Q12, 2);
                HarmShapeFIRPacked_Q12 |= Inlines.silk_LSHIFT((int)Inlines.silk_RSHIFT(HarmShapeGain_Q12, 1), 16);
                Tilt_Q14 = psEncCtrl.Tilt_Q14[k];
                LF_shp_Q14 = psEncCtrl.LF_shp_Q14[k];
                AR1_shp_Q13 = k * SilkConstants.MAX_SHAPE_LPC_ORDER;

                /* Short term FIR filtering*/
                silk_warped_LPC_analysis_filter(P.sAR_shp, st_res_Q2, psEncCtrl.AR1_Q13, AR1_shp_Q13, x, px,
                    (short)(psEnc.warping_Q16), psEnc.subfr_length, psEnc.shapingLPCOrder);

                /* Reduce (mainly) low frequencies during harmonic emphasis */
                B_Q10[0] = (short)(Inlines.silk_RSHIFT_ROUND(psEncCtrl.GainsPre_Q14[k], 4));
                tmp_32 = Inlines.silk_SMLABB(((int)((TuningParameters.INPUT_TILT) * ((long)1 << (26)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.INPUT_TILT, 26)*/, psEncCtrl.HarmBoost_Q14[k], HarmShapeGain_Q12);   /* Q26 */
                tmp_32 = Inlines.silk_SMLABB(tmp_32, psEncCtrl.coding_quality_Q14, ((int)((TuningParameters.HIGH_RATE_INPUT_TILT) * ((long)1 << (12)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.HIGH_RATE_INPUT_TILT, 12)*/);    /* Q26 */
                tmp_32 = Inlines.silk_SMULWB(tmp_32, -psEncCtrl.GainsPre_Q14[k]);                                                /* Q24 */
                tmp_32 = Inlines.silk_RSHIFT_ROUND(tmp_32, 14);                                                                     /* Q10 */
                B_Q10[1] = (short)(Inlines.silk_SAT16(tmp_32));
                x_filt_Q12[0] = Inlines.silk_MLA(Inlines.silk_MUL(st_res_Q2[0], B_Q10[0]), P.sHarmHP_Q2, B_Q10[1]);
                for (j = 1; j < psEnc.subfr_length; j++)
                {
                    x_filt_Q12[j] = Inlines.silk_MLA(Inlines.silk_MUL(st_res_Q2[j], B_Q10[0]), st_res_Q2[j - 1], B_Q10[1]);
                }
                P.sHarmHP_Q2 = st_res_Q2[psEnc.subfr_length - 1];

                silk_prefilt(P, x_filt_Q12, xw_Q3, pxw_Q3, HarmShapeFIRPacked_Q12, Tilt_Q14, LF_shp_Q14, lag, psEnc.subfr_length);

                px += psEnc.subfr_length;
                pxw_Q3 += psEnc.subfr_length;
            }

            P.lagPrev = psEncCtrl.pitchL[psEnc.nb_subfr - 1];

        }

        /* Prefilter for finding Quantizer input signal */
        static void silk_prefilt(
            SilkPrefilterState P,                         /* I/O  state                               */
            int[] st_res_Q12,               /* I    short term residual signal          */
            int[] xw_Q3,                    /* O    prefiltered signal                  */
            int xw_Q3_ptr,
            int HarmShapeFIRPacked_Q12,     /* I    Harmonic shaping coeficients        */
            int Tilt_Q14,                   /* I    Tilt shaping coeficient             */
            int LF_shp_Q14,                 /* I    Low-frequancy shaping coeficients   */
            int lag,                        /* I    Lag for harmonic shaping            */
            int length                      /* I    Length of signals                   */
        )
        {
            int i, idx, LTP_shp_buf_idx;
            int n_LTP_Q12, n_Tilt_Q10, n_LF_Q10;
            int sLF_MA_shp_Q12, sLF_AR_shp_Q12;
            short[] LTP_shp_buf;

            /* To speed up use temp variables instead of using the struct */
            LTP_shp_buf = P.sLTP_shp;
            LTP_shp_buf_idx = P.sLTP_shp_buf_idx;
            sLF_AR_shp_Q12 = P.sLF_AR_shp_Q12;
            sLF_MA_shp_Q12 = P.sLF_MA_shp_Q12;

            for (i = 0; i < length; i++)
            {
                if (lag > 0)
                {
                    /* unrolled loop */
                    Inlines.OpusAssert(SilkConstants.HARM_SHAPE_FIR_TAPS == 3);
                    idx = lag + LTP_shp_buf_idx;
                    n_LTP_Q12 = Inlines.silk_SMULBB(LTP_shp_buf[(idx - SilkConstants.HARM_SHAPE_FIR_TAPS / 2 - 1) & SilkConstants.LTP_MASK], HarmShapeFIRPacked_Q12);
                    n_LTP_Q12 = Inlines.silk_SMLABT(n_LTP_Q12, LTP_shp_buf[(idx - SilkConstants.HARM_SHAPE_FIR_TAPS / 2) & SilkConstants.LTP_MASK], HarmShapeFIRPacked_Q12);
                    n_LTP_Q12 = Inlines.silk_SMLABB(n_LTP_Q12, LTP_shp_buf[(idx - SilkConstants.HARM_SHAPE_FIR_TAPS / 2 + 1) & SilkConstants.LTP_MASK], HarmShapeFIRPacked_Q12);
                }
                else {
                    n_LTP_Q12 = 0;
                }

                n_Tilt_Q10 = Inlines.silk_SMULWB(sLF_AR_shp_Q12, Tilt_Q14);
                n_LF_Q10 = Inlines.silk_SMLAWB(Inlines.silk_SMULWT(sLF_AR_shp_Q12, LF_shp_Q14), sLF_MA_shp_Q12, LF_shp_Q14);

                sLF_AR_shp_Q12 = Inlines.silk_SUB32(st_res_Q12[i], Inlines.silk_LSHIFT(n_Tilt_Q10, 2));
                sLF_MA_shp_Q12 = Inlines.silk_SUB32(sLF_AR_shp_Q12, Inlines.silk_LSHIFT(n_LF_Q10, 2));

                LTP_shp_buf_idx = (LTP_shp_buf_idx - 1) & SilkConstants.LTP_MASK;
                LTP_shp_buf[LTP_shp_buf_idx] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(sLF_MA_shp_Q12, 12));

                xw_Q3[xw_Q3_ptr + i] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SUB32(sLF_MA_shp_Q12, n_LTP_Q12), 9);
            }

            /* Copy temp variable back to state */
            P.sLF_AR_shp_Q12 = sLF_AR_shp_Q12;
            P.sLF_MA_shp_Q12 = sLF_MA_shp_Q12;
            P.sLTP_shp_buf_idx = LTP_shp_buf_idx;
        }

#if !UNSAFE

        /// <summary>
        /// Second order ARMA filter, alternative implementation
        /// </summary>
        /// <param name="input">I     input signal</param>
        /// <param name="B_Q28">I     MA coefficients [3]</param>
        /// <param name="A_Q28">I     AR coefficients [2]</param>
        /// <param name="S">I/O   State vector [2]</param>
        /// <param name="output">O     output signal</param>
        /// <param name="len">I     signal length (must be even)</param>
        /// <param name="stride">I     Operate on interleaved signal if > 1</param>
        internal static void silk_biquad_alt(
            short[] input,
            int input_ptr,
            int[] B_Q28,
            int[] A_Q28,
            int[] S,
            short[] output,
            int output_ptr,
            int len,
            int stride)
        {
            /* DIRECT FORM II TRANSPOSED (uses 2 element state vector) */
            int k;
            int inval, A0_U_Q28, A0_L_Q28, A1_U_Q28, A1_L_Q28, out32_Q14;

            /* Negate A_Q28 values and split in two parts */
            A0_L_Q28 = (-A_Q28[0]) & 0x00003FFF;        /* lower part */
            A0_U_Q28 = Inlines.silk_RSHIFT(-A_Q28[0], 14);      /* upper part */
            A1_L_Q28 = (-A_Q28[1]) & 0x00003FFF;        /* lower part */
            A1_U_Q28 = Inlines.silk_RSHIFT(-A_Q28[1], 14);      /* upper part */

            for (k = 0; k < len; k++)
            {
                /* S[ 0 ], S[ 1 ]: Q12 */
                inval = input[input_ptr + k * stride];
                out32_Q14 = Inlines.silk_LSHIFT(Inlines.silk_SMLAWB(S[0], B_Q28[0], inval), 2);

                S[0] = S[1] + Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(out32_Q14, A0_L_Q28), 14);
                S[0] = Inlines.silk_SMLAWB(S[0], out32_Q14, A0_U_Q28);
                S[0] = Inlines.silk_SMLAWB(S[0], B_Q28[1], inval);

                S[1] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(out32_Q14, A1_L_Q28), 14);
                S[1] = Inlines.silk_SMLAWB(S[1], out32_Q14, A1_U_Q28);
                S[1] = Inlines.silk_SMLAWB(S[1], B_Q28[2], inval);

                /* Scale back to Q0 and saturate */
                output[output_ptr + k * stride] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT(out32_Q14 + (1 << 14) - 1, 14));
            }
        }

        internal static void silk_biquad_alt(
            short[] input,
            int input_ptr,
            int[] B_Q28,
            int[] A_Q28,
            int[] S,
            int S_ptr,
            short[] output,
            int output_ptr,
            int len,
            int stride)
        {
            /* DIRECT FORM II TRANSPOSED (uses 2 element state vector) */
            int k;
            int inval, A0_U_Q28, A0_L_Q28, A1_U_Q28, A1_L_Q28, out32_Q14;

            /* Negate A_Q28 values and split in two parts */
            A0_L_Q28 = (-A_Q28[0]) & 0x00003FFF;        /* lower part */
            A0_U_Q28 = Inlines.silk_RSHIFT(-A_Q28[0], 14);      /* upper part */
            A1_L_Q28 = (-A_Q28[1]) & 0x00003FFF;        /* lower part */
            A1_U_Q28 = Inlines.silk_RSHIFT(-A_Q28[1], 14);      /* upper part */

            for (k = 0; k < len; k++)
            {
                int s1 = S_ptr + 1;
                /* S[ 0 ], S[ 1 ]: Q12 */
                inval = input[input_ptr + k * stride];
                out32_Q14 = Inlines.silk_LSHIFT(Inlines.silk_SMLAWB(S[S_ptr], B_Q28[0], inval), 2);

                S[S_ptr] = S[s1] + Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(out32_Q14, A0_L_Q28), 14);
                S[S_ptr] = Inlines.silk_SMLAWB(S[S_ptr], out32_Q14, A0_U_Q28);
                S[S_ptr] = Inlines.silk_SMLAWB(S[S_ptr], B_Q28[1], inval);

                S[s1] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(out32_Q14, A1_L_Q28), 14);
                S[s1] = Inlines.silk_SMLAWB(S[s1], out32_Q14, A1_U_Q28);
                S[s1] = Inlines.silk_SMLAWB(S[s1], B_Q28[2], inval);

                /* Scale back to Q0 and saturate */
                output[output_ptr + k * stride] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT(out32_Q14 + (1 << 14) - 1, 14));
            }
        }

#else
        /// <summary>
        /// Second order ARMA filter, alternative implementation
        /// </summary>
        /// <param name="input">I     input signal</param>
        /// <param name="B_Q28">I     MA coefficients [3]</param>
        /// <param name="A_Q28">I     AR coefficients [2]</param>
        /// <param name="S">I/O   State vector [2]</param>
        /// <param name="output">O     output signal</param>
        /// <param name="len">I     signal length (must be even)</param>
        /// <param name="stride">I     Operate on interleaved signal if > 1</param>
        internal static unsafe void silk_biquad_alt(
            short[] input,
            int input_ptr,
            int[] B_Q28,
            int[] A_Q28,
            int[] S,
            int S_ptr,
            short[] output,
            int output_ptr,
            int len,
            int stride)
        {
            /* DIRECT FORM II TRANSPOSED (uses 2 element state vector) */
            int k;
            int inval, A0_U_Q28, A0_L_Q28, A1_U_Q28, A1_L_Q28, out32_Q14;

            /* Negate A_Q28 values and split in two parts */
            A0_L_Q28 = (-A_Q28[0]) & 0x00003FFF;        /* lower part */
            A0_U_Q28 = Inlines.silk_RSHIFT(-A_Q28[0], 14);      /* upper part */
            A1_L_Q28 = (-A_Q28[1]) & 0x00003FFF;        /* lower part */
            A1_U_Q28 = Inlines.silk_RSHIFT(-A_Q28[1], 14);      /* upper part */
            
            fixed (short* pinput_base = input, poutput_base = output)
            {
                fixed (int* pS_base = S, pBQ28 = B_Q28)
                {
                    int* pS = pS_base + S_ptr;
                    short* pinput = pinput_base + input_ptr;
                    short* poutput = poutput_base + output_ptr;
                    for (k = 0; k < len; k++)
                    {
                        /* S[ 0 ], S[ 1 ]: Q12 */
                        inval = pinput[k * stride];
                        out32_Q14 = Inlines.silk_LSHIFT(Inlines.silk_SMLAWB(pS[0], pBQ28[0], inval), 2);

                        pS[0] = pS[1] + Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(out32_Q14, A0_L_Q28), 14);
                        pS[0] = Inlines.silk_SMLAWB(pS[0], out32_Q14, A0_U_Q28);
                        pS[0] = Inlines.silk_SMLAWB(pS[0], pBQ28[1], inval);

                        pS[1] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(out32_Q14, A1_L_Q28), 14);
                        pS[1] = Inlines.silk_SMLAWB(pS[1], out32_Q14, A1_U_Q28);
                        pS[1] = Inlines.silk_SMLAWB(pS[1], pBQ28[2], inval);

                        /* Scale back to Q0 and saturate */
                        poutput[k * stride] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT(out32_Q14 + (1 << 14) - 1, 14));
                    }
                }
            }
        }

        internal static unsafe void silk_biquad_alt(
            short[] input,
            int input_ptr,
            int[] B_Q28,
            int[] A_Q28,
            int[] S,
            short[] output,
            int output_ptr,
            int len,
            int stride)
        {
            silk_biquad_alt(input, input_ptr, B_Q28, A_Q28, S, 0, output, output_ptr, len, stride);
        }
#endif

            /* Coefficients for 2-band filter bank based on first-order allpass filters */
        private readonly static short A_fb1_20 = 5394 << 1;
        private readonly static short A_fb1_21 = -24290; /* (opus_int16)(20623 << 1) */

        /// <summary>
        /// Split signal into two decimated bands using first-order allpass filters
        /// </summary>
        /// <param name="input">I    Input signal [N]</param>
        /// <param name="S">I/O  State vector [2]</param>
        /// <param name="outL">O    Low band [N/2]</param>
        /// <param name="outH">O    High band [N/2]</param>
        /// <param name="N">I    Number of input samples</param>
        internal static void silk_ana_filt_bank_1(
            short[] input,
            int input_ptr,
            int[] S,
            short[] outL,
            short[] outH,
            int outH_ptr,
            int N)
        {
            int k, N2 = Inlines.silk_RSHIFT(N, 1);
            int in32, X, Y, out_1, out_2;

            /* Internal variables and state are in Q10 format */
            for (k = 0; k < N2; k++)
            {
                /* Convert to Q10 */
                in32 = Inlines.silk_LSHIFT((int)input[input_ptr + 2 * k], 10);

                /* All-pass section for even input sample */
                Y = Inlines.silk_SUB32(in32, S[0]);
                X = Inlines.silk_SMLAWB(Y, Y, A_fb1_21);
                out_1 = Inlines.silk_ADD32(S[0], X);
                S[0] = Inlines.silk_ADD32(in32, X);

                /* Convert to Q10 */
                in32 = Inlines.silk_LSHIFT((int)input[input_ptr + 2 * k + 1], 10);

                /* All-pass section for odd input sample, and add to output of previous section */
                Y = Inlines.silk_SUB32(in32, S[1]);
                X = Inlines.silk_SMULWB(Y, A_fb1_20);
                out_2 = Inlines.silk_ADD32(S[1], X);
                S[1] = Inlines.silk_ADD32(in32, X);

                /* Add/subtract, convert back to int16 and store to output */
                outL[k] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_ADD32(out_2, out_1), 11));
                outH[outH_ptr + k] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_SUB32(out_2, out_1), 11));
            }
        }
        
        /// <summary>
        /// Chirp (bandwidth expand) LP AR filter
        /// </summary>
        /// <param name="ar">I/O  AR filter to be expanded (without leading 1)</param>
        /// <param name="d">I    Length of ar</param>
        /// <param name="chirp_Q16">I    Chirp factor in Q16</param>
        internal static void silk_bwexpander_32(int[] ar, int d, int chirp_Q16)
        {
            int i;
            int chirp_minus_one_Q16 = chirp_Q16 - 65536;

            for (i = 0; i < d - 1; i++)
            {
                ar[i] = Inlines.silk_SMULWW(chirp_Q16, ar[i]);
                chirp_Q16 += Inlines.silk_RSHIFT_ROUND(Inlines.silk_MUL(chirp_Q16, chirp_minus_one_Q16), 16);
            }

            ar[d - 1] = Inlines.silk_SMULWW(chirp_Q16, ar[d - 1]);
        }

        /// <summary>
        /// Elliptic/Cauer filters designed with 0.1 dB passband ripple,
        /// 80 dB minimum stopband attenuation, and
        /// [0.95 : 0.15 : 0.35] normalized cut off frequencies.
        /// Helper function, interpolates the filter taps
        /// </summary>
        /// <param name="B_Q28">order [TRANSITION_NB]</param>
        /// <param name="A_Q28">order [TRANSITION_NA]</param>
        /// <param name="ind"></param>
        /// <param name="fac_Q16"></param>
        internal static void silk_LP_interpolate_filter_taps(
            int[] B_Q28,
            int[] A_Q28,
            int ind,
            int fac_Q16)
        {
            int nb, na;

            if (ind < SilkConstants.TRANSITION_INT_NUM - 1)
            {
                if (fac_Q16 > 0)
                {
                    if (fac_Q16 < 32768)
                    {
                        /* fac_Q16 is in range of a 16-bit int */
                        /* Piece-wise linear interpolation of B and A */
                        for (nb = 0; nb < SilkConstants.TRANSITION_NB; nb++)
                        {
                            B_Q28[nb] = Inlines.silk_SMLAWB(
                                Tables.silk_Transition_LP_B_Q28[ind][nb],
                                Tables.silk_Transition_LP_B_Q28[ind + 1][nb] -
                                    Tables.silk_Transition_LP_B_Q28[ind][nb],
                                fac_Q16);
                        }

                        for (na = 0; na < SilkConstants.TRANSITION_NA; na++)
                        {
                            A_Q28[na] = Inlines.silk_SMLAWB(
                                Tables.silk_Transition_LP_A_Q28[ind][na],
                                Tables.silk_Transition_LP_A_Q28[ind + 1][na] -
                                    Tables.silk_Transition_LP_A_Q28[ind][na],
                                fac_Q16);
                        }
                    }
                    else
                    {
                        /* ( fac_Q16 - ( 1 << 16 ) ) is in range of a 16-bit int */
                        Inlines.OpusAssert(fac_Q16 - (1 << 16) == Inlines.silk_SAT16(fac_Q16 - (1 << 16)));

                        /* Piece-wise linear interpolation of B and A */

                        for (nb = 0; nb < SilkConstants.TRANSITION_NB; nb++)
                        {
                            B_Q28[nb] = Inlines.silk_SMLAWB(
                                Tables.silk_Transition_LP_B_Q28[ind + 1][nb],
                                Tables.silk_Transition_LP_B_Q28[ind + 1][nb] -
                                    Tables.silk_Transition_LP_B_Q28[ind][nb],
                                fac_Q16 - ((int)1 << 16));
                        }

                        for (na = 0; na < SilkConstants.TRANSITION_NA; na++)
                        {
                            A_Q28[na] = Inlines.silk_SMLAWB(
                                Tables.silk_Transition_LP_A_Q28[ind + 1][na],
                                Tables.silk_Transition_LP_A_Q28[ind + 1][na] -
                                    Tables.silk_Transition_LP_A_Q28[ind][na],
                                fac_Q16 - ((int)1 << 16));
                        }
                    }
                }
                else
                {
                    Array.Copy(Tables.silk_Transition_LP_B_Q28[ind], 0, B_Q28, 0, SilkConstants.TRANSITION_NB);
                    Array.Copy(Tables.silk_Transition_LP_A_Q28[ind], 0, A_Q28, 0, SilkConstants.TRANSITION_NA);
                }
            }
            else
            {
                Array.Copy(Tables.silk_Transition_LP_B_Q28[SilkConstants.TRANSITION_INT_NUM - 1], 0, B_Q28, 0, SilkConstants.TRANSITION_NB);
                Array.Copy(Tables.silk_Transition_LP_A_Q28[SilkConstants.TRANSITION_INT_NUM - 1], 0, A_Q28, 0, SilkConstants.TRANSITION_NA);
            }
        }
        
        /// <summary>
        /// LPC analysis filter
        /// NB! State is kept internally and the
        /// filter always starts with zero state
        /// first d output samples are set to zero
        /// </summary>
        /// <param name="output">O    Output signal</param>
        /// <param name="input">I    Input signal</param>
        /// <param name="B">I    MA prediction coefficients, Q12 [order]</param>
        /// <param name="len">I    Signal length</param>
        /// <param name="d">I    Filter order</param>
        internal static void silk_LPC_analysis_filter(
                    short[] output,
                    int output_ptr,
                    short[] input,
                    int input_ptr,
                    short[] B,
                    int B_ptr,
                    int len,
                    int d)
        {
            int j;

            short[] mem = new short[SilkConstants.SILK_MAX_ORDER_LPC];
            short[] num = new short[SilkConstants.SILK_MAX_ORDER_LPC];

            Inlines.OpusAssert(d >= 6);
            Inlines.OpusAssert((d & 1) == 0);
            Inlines.OpusAssert(d <= len);

            Inlines.OpusAssert(d <= SilkConstants.SILK_MAX_ORDER_LPC);
            for (j = 0; j < d; j++)
            {
                num[j] = (short)(0 - B[B_ptr + j]);
            }
            for (j = 0; j < d; j++)
            {
                mem[j] = input[input_ptr + d - j - 1];
            }
#if UNSAFE
            unsafe
            {
                fixed (short* pinput_base = input, poutput_base = output)
                {
                    short* pinput = pinput_base + input_ptr + d;
                    short* poutput = poutput_base + output_ptr + d;
                    Kernels.celt_fir(pinput, num, poutput, len - d, d, mem);
                }
            }
#else
            Kernels.celt_fir(input, input_ptr + d, num, output, output_ptr + d, len - d, d, mem);
#endif
            for (j = output_ptr; j < output_ptr + d; j++)
            {
                output[j] = 0;
            }
        }

        private const int QA = 24;
        private static readonly int A_LIMIT = ((int)((0.99975f) * ((long)1 << (QA)) + 0.5))/*Inlines.SILK_CONST(0.99975f, QA)*/;

        /// <summary>
        /// Compute inverse of LPC prediction gain, and
        /// test if LPC coefficients are stable (all poles within unit circle)
        /// </summary>
        /// <param name="A_QA">Prediction coefficients, order [2][SILK_MAX_ORDER_LPC]</param>
        /// <param name="order">Prediction order</param>
        /// <returns>inverse prediction gain in energy domain, Q30</returns>
        internal static int LPC_inverse_pred_gain_QA(
            int[][] A_QA,
            int order)
        {
            int k, n, mult2Q;
            int invGain_Q30, rc_Q31, rc_mult1_Q30, rc_mult2, tmp_QA;
            int[] Aold_QA, Anew_QA;

            Anew_QA = A_QA[order & 1];

            invGain_Q30 = (int)1 << 30;
            for (k = order - 1; k > 0; k--)
            {
                /* Check for stability */
                if ((Anew_QA[k] > A_LIMIT) || (Anew_QA[k] < -A_LIMIT))
                {
                    return 0;
                }

                /* Set RC equal to negated AR coef */
                rc_Q31 = 0 - Inlines.silk_LSHIFT(Anew_QA[k], 31 - QA);

                /* rc_mult1_Q30 range: [ 1 : 2^30 ] */
                rc_mult1_Q30 = ((int)1 << 30) - Inlines.silk_SMMUL(rc_Q31, rc_Q31);
                Inlines.OpusAssert(rc_mult1_Q30 > (1 << 15));                   /* reduce A_LIMIT if fails */
                Inlines.OpusAssert(rc_mult1_Q30 <= (1 << 30));

                /* rc_mult2 range: [ 2^30 : silk_int32_MAX ] */
                mult2Q = 32 - Inlines.silk_CLZ32(Inlines.silk_abs(rc_mult1_Q30));
                rc_mult2 = Inlines.silk_INVERSE32_varQ(rc_mult1_Q30, mult2Q + 30);

                /* Update inverse gain */
                /* invGain_Q30 range: [ 0 : 2^30 ] */
                invGain_Q30 = Inlines.silk_LSHIFT(Inlines.silk_SMMUL(invGain_Q30, rc_mult1_Q30), 2);
                Inlines.OpusAssert(invGain_Q30 >= 0);
                Inlines.OpusAssert(invGain_Q30 <= (1 << 30));

                /* Swap pointers */
                Aold_QA = Anew_QA;
                Anew_QA = A_QA[k & 1];

                /* Update AR coefficient */
                for (n = 0; n < k; n++)
                {
                    tmp_QA = Aold_QA[n] - Inlines.MUL32_FRAC_Q(Aold_QA[k - n - 1], rc_Q31, 31);
                    Anew_QA[n] = Inlines.MUL32_FRAC_Q(tmp_QA, rc_mult2, mult2Q);
                }
            }

            /* Check for stability */
            if ((Anew_QA[0] > A_LIMIT) || (Anew_QA[0] < -A_LIMIT))
            {
                return 0;
            }

            /* Set RC equal to negated AR coef */
            rc_Q31 = 0 - Inlines.silk_LSHIFT(Anew_QA[0], 31 - QA);

            /* Range: [ 1 : 2^30 ] */
            rc_mult1_Q30 = ((int)1 << 30) - Inlines.silk_SMMUL(rc_Q31, rc_Q31);

            /* Update inverse gain */
            /* Range: [ 0 : 2^30 ] */
            invGain_Q30 = Inlines.silk_LSHIFT(Inlines.silk_SMMUL(invGain_Q30, rc_mult1_Q30), 2);
            Inlines.OpusAssert(invGain_Q30 >= 0);
            Inlines.OpusAssert(invGain_Q30 <= 1 << 30);

            return invGain_Q30;
        }

        /// <summary>
        /// For input in Q12 domain
        /// </summary>
        /// <param name="A_Q12">Prediction coefficients, Q12 [order]</param>
        /// <param name="order">I   Prediction order</param>
        /// <returns>inverse prediction gain in energy domain, Q30</returns>
        internal static int silk_LPC_inverse_pred_gain(short[] A_Q12, int order)
        {
            int k;
            int[][] Atmp_QA = new int[2][];
            Atmp_QA[0] = new int[order];
            Atmp_QA[1] = new int[order];
            int[] Anew_QA;
            int DC_resp = 0;

            Anew_QA = Atmp_QA[order & 1];

            /* Increase Q domain of the AR coefficients */
            for (k = 0; k < order; k++)
            {
                DC_resp += (int)A_Q12[k];
                Anew_QA[k] = Inlines.silk_LSHIFT32((int)A_Q12[k], QA - 12);
            }

            /* If the DC is unstable, we don't even need to do the full calculations */
            if (DC_resp >= 4096)
            {
                return 0;
            }

            return LPC_inverse_pred_gain_QA(Atmp_QA, order);
        }
    }
}
