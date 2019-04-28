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

    internal static class DecodePulses
    {
        /*********************************************/
        /* Decode quantization indices of excitation */
        /*********************************************/
        internal static void silk_decode_pulses(
            EntropyCoder psRangeDec,                    /* I/O  Compressor data structure                   */
            short[] pulses,                       /* O    Excitation signal                           */
            int signalType,                     /* I    Sigtype                                     */
            int quantOffsetType,                /* I    quantOffsetType                             */
            int frame_length                    /* I    Frame length                                */
            )
        {
            int i, j, k, iter, abs_q, nLS, RateLevelIndex;
            int[] sum_pulses = new int[SilkConstants.MAX_NB_SHELL_BLOCKS];
            int[] nLshifts = new int[SilkConstants.MAX_NB_SHELL_BLOCKS];
            int pulses_ptr;

            /*********************/
            /* Decode rate level */
            /*********************/
            RateLevelIndex = psRangeDec.dec_icdf(Tables.silk_rate_levels_iCDF[signalType >> 1], 8);

            /* Calculate number of shell blocks */
            Inlines.OpusAssert(1 << SilkConstants.LOG2_SHELL_CODEC_FRAME_LENGTH == SilkConstants.SHELL_CODEC_FRAME_LENGTH);
            iter = Inlines.silk_RSHIFT(frame_length, SilkConstants.LOG2_SHELL_CODEC_FRAME_LENGTH);
            if (iter * SilkConstants.SHELL_CODEC_FRAME_LENGTH < frame_length)
            {
                Inlines.OpusAssert(frame_length == 12 * 10); /* Make sure only happens for 10 ms @ 12 kHz */
                iter++;
            }

            /***************************************************/
            /* Sum-Weighted-Pulses Decoding                    */
            /***************************************************/
            for (i = 0; i < iter; i++)
            {
                nLshifts[i] = 0;
                sum_pulses[i] = psRangeDec.dec_icdf(Tables.silk_pulses_per_block_iCDF[RateLevelIndex], 8);

                /* LSB indication */
                while (sum_pulses[i] == SilkConstants.SILK_MAX_PULSES + 1)
                {
                    nLshifts[i]++;
                    /* When we've already got 10 LSBs, we shift the table to not allow (SILK_MAX_PULSES + 1) */
                    sum_pulses[i] = psRangeDec.dec_icdf(
                          Tables.silk_pulses_per_block_iCDF[SilkConstants.N_RATE_LEVELS - 1], (nLshifts[i] == 10 ? 1 : 0), 8);
                }
            }

            /***************************************************/
            /* Shell decoding                                  */
            /***************************************************/
            for (i = 0; i < iter; i++)
            {
                if (sum_pulses[i] > 0)
                {
                    ShellCoder.silk_shell_decoder(pulses, Inlines.silk_SMULBB(i, SilkConstants.SHELL_CODEC_FRAME_LENGTH), psRangeDec, sum_pulses[i]);
                }
                else
                {
                    Arrays.MemSetWithOffset<short>(pulses, 0, Inlines.silk_SMULBB(i, SilkConstants.SHELL_CODEC_FRAME_LENGTH), SilkConstants.SHELL_CODEC_FRAME_LENGTH);
                }
            }

            /***************************************************/
            /* LSB Decoding                                    */
            /***************************************************/
            for (i = 0; i < iter; i++)
            {
                if (nLshifts[i] > 0)
                {
                    nLS = nLshifts[i];
                    pulses_ptr = Inlines.silk_SMULBB(i, SilkConstants.SHELL_CODEC_FRAME_LENGTH);
                    for (k = 0; k < SilkConstants.SHELL_CODEC_FRAME_LENGTH; k++)
                    {
                        abs_q = pulses[pulses_ptr + k];
                        for (j = 0; j < nLS; j++)
                        {
                            abs_q = Inlines.silk_LSHIFT(abs_q, 1);
                            abs_q += psRangeDec.dec_icdf(Tables.silk_lsb_iCDF, 8);
                        }
                        pulses[pulses_ptr + k] = (short)(abs_q);
                    }
                    /* Mark the number of pulses non-zero for sign decoding. */
                    sum_pulses[i] |= nLS << 5;
                }
            }

            /****************************************/
            /* Decode and add signs to pulse signal */
            /****************************************/
            CodeSigns.silk_decode_signs(psRangeDec, pulses, frame_length, signalType, quantOffsetType, sum_pulses);
        }
    }
}
