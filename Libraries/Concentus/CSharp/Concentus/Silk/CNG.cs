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
    /// Comfort noise generation and estimation
    /// </summary>
    internal static class CNG
    {
        /// <summary>
        /// Generates excitation for CNG LPC synthesis
        /// </summary>
        /// <param name="exc_Q10">O    CNG excitation signal Q10</param>
        /// <param name="exc_buf_Q14">I    Random samples buffer Q10</param>
        /// <param name="Gain_Q16">I    Gain to apply</param>
        /// <param name="length">I    Length</param>
        /// <param name="rand_seed">I/O  Seed to random index generator</param>
        internal static void silk_CNG_exc(
            int[] exc_Q10,
            int exc_Q10_ptr,
            int[] exc_buf_Q14,
            int Gain_Q16,
            int length,
            ref int rand_seed)
        {
            int seed;
            int i, idx, exc_mask;

            exc_mask = SilkConstants.CNG_BUF_MASK_MAX;

            while (exc_mask > length)
            {
                exc_mask = Inlines.silk_RSHIFT(exc_mask, 1);
            }

            seed = rand_seed;
            for (i = exc_Q10_ptr; i < exc_Q10_ptr + length; i++)
            {
                seed = Inlines.silk_RAND(seed);
                idx = (int)(Inlines.silk_RSHIFT(seed, 24) & exc_mask);
                Inlines.OpusAssert(idx >= 0);
                Inlines.OpusAssert(idx <= SilkConstants.CNG_BUF_MASK_MAX);
                exc_Q10[i] = (short)Inlines.silk_SAT16(Inlines.silk_SMULWW(exc_buf_Q14[idx], Gain_Q16 >> 4));
            }

            rand_seed = seed;
        }

        /// <summary>
        /// Resets CNG state
        /// </summary>
        /// <param name="psDec">I/O  Decoder state</param>
        internal static void silk_CNG_Reset(SilkChannelDecoder psDec)
        {
            int i, NLSF_step_Q15, NLSF_acc_Q15;

            NLSF_step_Q15 = Inlines.silk_DIV32_16(short.MaxValue, (short)(psDec.LPC_order + 1));
            NLSF_acc_Q15 = 0;
            for (i = 0; i < psDec.LPC_order; i++)
            {
                NLSF_acc_Q15 += NLSF_step_Q15;
                psDec.sCNG.CNG_smth_NLSF_Q15[i] = (short)(NLSF_acc_Q15);
            }
            psDec.sCNG.CNG_smth_Gain_Q16 = 0;
            psDec.sCNG.rand_seed = 3176576;
        }

        /// <summary>
        /// Updates CNG estimate, and applies the CNG when packet was lost
        /// </summary>
        /// <param name="psDec">I/O  Decoder state</param>
        /// <param name="psDecCtrl">I/O  Decoder control</param>
        /// <param name="frame">I/O  Signal</param>
        /// <param name="length">I    Length of residual</param>
        internal static void silk_CNG(
            SilkChannelDecoder psDec,
            SilkDecoderControl psDecCtrl,
            short[] frame,
            int frame_ptr,
            int length)
        {
            int i, subfr;
            int sum_Q6, max_Gain_Q16, gain_Q16;
            short[] A_Q12 = new short[psDec.LPC_order];
            CNGState psCNG = psDec.sCNG;

            if (psDec.fs_kHz != psCNG.fs_kHz)
            {
                /* Reset state */
                silk_CNG_Reset(psDec);

                psCNG.fs_kHz = psDec.fs_kHz;
            }

            if (psDec.lossCnt == 0 && psDec.prevSignalType == SilkConstants.TYPE_NO_VOICE_ACTIVITY)
            {
                /* Update CNG parameters */

                /* Smoothing of LSF's  */
                for (i = 0; i < psDec.LPC_order; i++)
                {
                    psCNG.CNG_smth_NLSF_Q15[i] += (short)(Inlines.silk_SMULWB((int)psDec.prevNLSF_Q15[i] - (int)psCNG.CNG_smth_NLSF_Q15[i], SilkConstants.CNG_NLSF_SMTH_Q16));
                }

                /* Find the subframe with the highest gain */
                max_Gain_Q16 = 0;
                subfr = 0;
                for (i = 0; i < psDec.nb_subfr; i++)
                {
                    if (psDecCtrl.Gains_Q16[i] > max_Gain_Q16)
                    {
                        max_Gain_Q16 = psDecCtrl.Gains_Q16[i];
                        subfr = i;
                    }
                }

                /* Update CNG excitation buffer with excitation from this subframe */
                Arrays.MemMoveInt(psCNG.CNG_exc_buf_Q14, 0, psDec.subfr_length, (psDec.nb_subfr - 1) * psDec.subfr_length);

                /* Smooth gains */
                for (i = 0; i < psDec.nb_subfr; i++)
                {
                    psCNG.CNG_smth_Gain_Q16 += Inlines.silk_SMULWB(psDecCtrl.Gains_Q16[i] - psCNG.CNG_smth_Gain_Q16, SilkConstants.CNG_GAIN_SMTH_Q16);
                }
            }

            /* Add CNG when packet is lost or during DTX */
            if (psDec.lossCnt != 0)
            {
                int[] CNG_sig_Q10 = new int[length + SilkConstants.MAX_LPC_ORDER];

                /* Generate CNG excitation */
                gain_Q16 = Inlines.silk_SMULWW(psDec.sPLC.randScale_Q14, psDec.sPLC.prevGain_Q16[1]);
                if (gain_Q16 >= (1 << 21) || psCNG.CNG_smth_Gain_Q16 > (1 << 23))
                {
                    gain_Q16 = Inlines.silk_SMULTT(gain_Q16, gain_Q16);
                    gain_Q16 = Inlines.silk_SUB_LSHIFT32(Inlines.silk_SMULTT(psCNG.CNG_smth_Gain_Q16, psCNG.CNG_smth_Gain_Q16), gain_Q16, 5);
                    gain_Q16 = Inlines.silk_LSHIFT32(Inlines.silk_SQRT_APPROX(gain_Q16), 16);
                }
                else
                {
                    gain_Q16 = Inlines.silk_SMULWW(gain_Q16, gain_Q16);
                    gain_Q16 = Inlines.silk_SUB_LSHIFT32(Inlines.silk_SMULWW(psCNG.CNG_smth_Gain_Q16, psCNG.CNG_smth_Gain_Q16), gain_Q16, 5);
                    gain_Q16 = Inlines.silk_LSHIFT32(Inlines.silk_SQRT_APPROX(gain_Q16), 8);
                }
                silk_CNG_exc(CNG_sig_Q10, SilkConstants.MAX_LPC_ORDER, psCNG.CNG_exc_buf_Q14, gain_Q16, length, ref psCNG.rand_seed);

                /* Convert CNG NLSF to filter representation */
                NLSF.silk_NLSF2A(A_Q12, psCNG.CNG_smth_NLSF_Q15, psDec.LPC_order);

                /* Generate CNG signal, by synthesis filtering */
                Array.Copy(psCNG.CNG_synth_state, CNG_sig_Q10, SilkConstants.MAX_LPC_ORDER);

                for (i = 0; i < length; i++)
                {
                    int lpci = SilkConstants.MAX_LPC_ORDER + i;
                    Inlines.OpusAssert(psDec.LPC_order == 10 || psDec.LPC_order == 16);
                    /* Avoids introducing a bias because silk_SMLAWB() always rounds to -inf */
                    sum_Q6 = Inlines.silk_RSHIFT(psDec.LPC_order, 1);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 1], A_Q12[0]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 2], A_Q12[1]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 3], A_Q12[2]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 4], A_Q12[3]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 5], A_Q12[4]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 6], A_Q12[5]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 7], A_Q12[6]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 8], A_Q12[7]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 9], A_Q12[8]);
                    sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 10], A_Q12[9]);

                    if (psDec.LPC_order == 16)
                    {
                        sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 11], A_Q12[10]);
                        sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 12], A_Q12[11]);
                        sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 13], A_Q12[12]);
                        sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 14], A_Q12[13]);
                        sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 15], A_Q12[14]);
                        sum_Q6 = Inlines.silk_SMLAWB(sum_Q6, CNG_sig_Q10[lpci - 16], A_Q12[15]);
                    }

                    /* Update states */
                    CNG_sig_Q10[lpci] = Inlines.silk_ADD_LSHIFT(CNG_sig_Q10[lpci], sum_Q6, 4);

                    frame[frame_ptr + i] = Inlines.silk_ADD_SAT16(frame[frame_ptr + i], (short)(Inlines.silk_RSHIFT_ROUND(CNG_sig_Q10[lpci], 10)));
                }

                Array.Copy(CNG_sig_Q10, length, psCNG.CNG_synth_state, 0, SilkConstants.MAX_LPC_ORDER);
            }
            else
            {
                Arrays.MemSetInt(psCNG.CNG_synth_state, 0, psDec.LPC_order);
            }
        }
    }
}
