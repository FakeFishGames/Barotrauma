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

    internal static class EncodePulses
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="pulses_comb">(O)</param>
        /// <param name="pulses_in">(I)</param>
        /// <param name="max_pulses"> I    max value for sum of pulses</param>
        /// <param name="len">I    number of output values</param>
        /// <returns>return ok</returns>
        internal static int combine_and_check(
            int[] pulses_comb,
            int pulses_comb_ptr,
            int[] pulses_in,
            int pulses_in_ptr,
            int max_pulses,
            int len)
        {
            for (int k = 0; k < len; k++)
            {
                int k2p = 2 * k + pulses_in_ptr;
                int sum = pulses_in[k2p] + pulses_in[k2p + 1];
                if (sum > max_pulses)
                {
                    return 1;
                }
                pulses_comb[pulses_comb_ptr + k] = sum;
            }
            return 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pulses_comb">(O)</param>
        /// <param name="pulses_in">(I)</param>
        /// <param name="max_pulses"> I    max value for sum of pulses</param>
        /// <param name="len">I    number of output values</param>
        /// <returns>return ok</returns>
        internal static int combine_and_check(
            int[] pulses_comb,
            int[] pulses_in,
            int max_pulses,
            int len)
        {
            for (int k = 0; k < len; k++)
            {
                int sum = pulses_in[2 * k] + pulses_in[2 * k + 1];
                if (sum > max_pulses)
                {
                    return 1;
                }
                pulses_comb[k] = sum;
            }
            return 0;
        }

        /// <summary>
        /// Encode quantization indices of excitation
        /// </summary>
        /// <param name="psRangeEnc">I/O  compressor data structure</param>
        /// <param name="signalType">I    Signal type</param>
        /// <param name="quantOffsetType">I    quantOffsetType</param>
        /// <param name="pulses">I    quantization indices</param>
        /// <param name="frame_length">I    Frame length</param>
        internal static void silk_encode_pulses(
            EntropyCoder psRangeEnc,
            int signalType,
            int quantOffsetType,
            sbyte[] pulses,
            int frame_length)
        {
            int i, k, j, iter, bit, nLS, scale_down, RateLevelIndex = 0;
            int abs_q, minSumBits_Q5, sumBits_Q5;
            int[] abs_pulses;
            int[] sum_pulses;
            int[] nRshifts;
            int[] pulses_comb = new int[8];
            int abs_pulses_ptr;
            int pulses_ptr;
            byte[] nBits_ptr;

            Arrays.MemSetInt(pulses_comb, 0, 8);

            /****************************/
            /* Prepare for shell coding */
            /****************************/
            /* Calculate number of shell blocks */
            Inlines.OpusAssert(1 << SilkConstants.LOG2_SHELL_CODEC_FRAME_LENGTH == SilkConstants.SHELL_CODEC_FRAME_LENGTH);
            iter = Inlines.silk_RSHIFT(frame_length, SilkConstants.LOG2_SHELL_CODEC_FRAME_LENGTH);
            if (iter * SilkConstants.SHELL_CODEC_FRAME_LENGTH < frame_length)
            {
                Inlines.OpusAssert(frame_length == 12 * 10); /* Make sure only happens for 10 ms @ 12 kHz */
                iter++;
                Arrays.MemSetWithOffset<sbyte>(pulses, 0, frame_length, SilkConstants.SHELL_CODEC_FRAME_LENGTH);
            }

            /* Take the absolute value of the pulses */
            abs_pulses = new int[iter * SilkConstants.SHELL_CODEC_FRAME_LENGTH];
            Inlines.OpusAssert((SilkConstants.SHELL_CODEC_FRAME_LENGTH & 3) == 0);
            
            // unrolled loop
            for (i = 0; i < iter * SilkConstants.SHELL_CODEC_FRAME_LENGTH; i += 4)
            {
                abs_pulses[i + 0] = (int)Inlines.silk_abs(pulses[i + 0]);
                abs_pulses[i + 1] = (int)Inlines.silk_abs(pulses[i + 1]);
                abs_pulses[i + 2] = (int)Inlines.silk_abs(pulses[i + 2]);
                abs_pulses[i + 3] = (int)Inlines.silk_abs(pulses[i + 3]);
            }

            /* Calc sum pulses per shell code frame */
            sum_pulses = new int[iter];
            nRshifts = new int[iter];
            abs_pulses_ptr = 0;
            for (i = 0; i < iter; i++)
            {
                nRshifts[i] = 0;

                while (true)
                {
                    /* 1+1 . 2 */
                    scale_down = combine_and_check(pulses_comb, 0, abs_pulses, abs_pulses_ptr, Tables.silk_max_pulses_table[0], 8);
                    /* 2+2 . 4 */
                    scale_down += combine_and_check(pulses_comb, pulses_comb, Tables.silk_max_pulses_table[1], 4);
                    /* 4+4 . 8 */
                    scale_down += combine_and_check(pulses_comb, pulses_comb, Tables.silk_max_pulses_table[2], 2);
                    /* 8+8 . 16 */
                    scale_down += combine_and_check(sum_pulses, i, pulses_comb, 0, Tables.silk_max_pulses_table[3], 1);
                    
                    if (scale_down != 0)
                    {
                        /* We need to downscale the quantization signal */
                        nRshifts[i]++;
                        for (k = abs_pulses_ptr; k < abs_pulses_ptr + SilkConstants.SHELL_CODEC_FRAME_LENGTH; k++)
                        {
                            abs_pulses[k] = Inlines.silk_RSHIFT(abs_pulses[k], 1);
                        }
                    }
                    else
                    {
                        /* Jump out of while(1) loop and go to next shell coding frame */
                        break;
                    }
                }

                abs_pulses_ptr += SilkConstants.SHELL_CODEC_FRAME_LENGTH;
            }

            /**************/
            /* Rate level */
            /**************/
            /* find rate level that leads to fewest bits for coding of pulses per block info */
            minSumBits_Q5 = int.MaxValue;
            for (k = 0; k < SilkConstants.N_RATE_LEVELS - 1; k++)
            {
                nBits_ptr = Tables.silk_pulses_per_block_BITS_Q5[k];
                sumBits_Q5 = Tables.silk_rate_levels_BITS_Q5[signalType >> 1][k];
                for (i = 0; i < iter; i++)
                {
                    if (nRshifts[i] > 0)
                    {
                        sumBits_Q5 += nBits_ptr[SilkConstants.SILK_MAX_PULSES + 1];
                    }
                    else {
                        sumBits_Q5 += nBits_ptr[sum_pulses[i]];
                    }
                }
                if (sumBits_Q5 < minSumBits_Q5)
                {
                    minSumBits_Q5 = sumBits_Q5;
                    RateLevelIndex = k;
                }
            }

            psRangeEnc.enc_icdf( RateLevelIndex, Tables.silk_rate_levels_iCDF[signalType >> 1], 8);

            /***************************************************/
            /* Sum-Weighted-Pulses Encoding                    */
            /***************************************************/
            for (i = 0; i < iter; i++)
            {
                if (nRshifts[i] == 0)
                {
                    psRangeEnc.enc_icdf( sum_pulses[i], Tables.silk_pulses_per_block_iCDF[RateLevelIndex], 8);
                }
                else
                {
                    psRangeEnc.enc_icdf( SilkConstants.SILK_MAX_PULSES + 1, Tables.silk_pulses_per_block_iCDF[RateLevelIndex], 8);
                    for (k = 0; k < nRshifts[i] - 1; k++)
                    {
                        psRangeEnc.enc_icdf( SilkConstants.SILK_MAX_PULSES + 1, Tables.silk_pulses_per_block_iCDF[SilkConstants.N_RATE_LEVELS - 1], 8);
                    }

                    psRangeEnc.enc_icdf( sum_pulses[i], Tables.silk_pulses_per_block_iCDF[SilkConstants.N_RATE_LEVELS - 1], 8);
                }
            }

            /******************/
            /* Shell Encoding */
            /******************/
            for (i = 0; i < iter; i++)
            {
                if (sum_pulses[i] > 0)
                {
                    ShellCoder.silk_shell_encoder(psRangeEnc, abs_pulses, i * SilkConstants.SHELL_CODEC_FRAME_LENGTH);
                }
            }

            /****************/
            /* LSB Encoding */
            /****************/
            for (i = 0; i < iter; i++)
            {
                if (nRshifts[i] > 0)
                {
                    pulses_ptr = i * SilkConstants.SHELL_CODEC_FRAME_LENGTH;
                    nLS = nRshifts[i] - 1;
                    for (k = 0; k < SilkConstants.SHELL_CODEC_FRAME_LENGTH; k++)
                    {
                        abs_q = (sbyte)Inlines.silk_abs(pulses[pulses_ptr + k]);
                        for (j = nLS; j > 0; j--)
                        {
                            bit = Inlines.silk_RSHIFT(abs_q, j) & 1;
                            psRangeEnc.enc_icdf( bit, Tables.silk_lsb_iCDF, 8);
                        }
                        bit = abs_q & 1;
                        psRangeEnc.enc_icdf( bit, Tables.silk_lsb_iCDF, 8);
                    }
                }
            }

            /****************/
            /* Encode signs */
            /****************/
            CodeSigns.silk_encode_signs(psRangeEnc, pulses, frame_length, signalType, quantOffsetType, sum_pulses);
        }
    }
}
