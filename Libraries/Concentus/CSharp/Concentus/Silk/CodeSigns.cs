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

    internal static class CodeSigns
    {
        private static int silk_enc_map(int a)
        {
            return (Inlines.silk_RSHIFT((a), 15) + 1);
        }

        private static int silk_dec_map(int a)
        {
            return (Inlines.silk_LSHIFT((a), 1) - 1);
        }
        
        /// <summary>
        /// Encodes signs of excitation
        /// </summary>
        /// <param name="psRangeEnc">I/O  Compressor data structure</param>
        /// <param name="pulses">I    pulse signal</param>
        /// <param name="length">I    length of input</param>
        /// <param name="signalType">I    Signal type</param>
        /// <param name="quantOffsetType">I    Quantization offset type</param>
        /// <param name="sum_pulses">I    Sum of absolute pulses per block [MAX_NB_SHELL_BLOCKS]</param>
        internal static void silk_encode_signs(
            EntropyCoder psRangeEnc,
            sbyte[] pulses,
            int length,
            int signalType,
            int quantOffsetType,
            int[] sum_pulses)
        {
            int i, j, p;
            byte[] icdf = new byte[2];
            int q_ptr;
            byte[] sign_icdf = Tables.silk_sign_iCDF;
            int icdf_ptr;

            icdf[1] = 0;
            q_ptr = 0;
            i = Inlines.silk_SMULBB(7, Inlines.silk_ADD_LSHIFT(quantOffsetType, signalType, 1));
            icdf_ptr = i;
            length = Inlines.silk_RSHIFT(length + (SilkConstants.SHELL_CODEC_FRAME_LENGTH / 2), SilkConstants.LOG2_SHELL_CODEC_FRAME_LENGTH);
            for (i = 0; i < length; i++)
            {
                p = sum_pulses[i];
                if (p > 0)
                {
                    icdf[0] = sign_icdf[icdf_ptr + Inlines.silk_min(p & 0x1F, 6)];
                    for (j = q_ptr; j < q_ptr + SilkConstants.SHELL_CODEC_FRAME_LENGTH; j++)
                    {
                        if (pulses[j] != 0)
                        {
                            psRangeEnc.enc_icdf( silk_enc_map(pulses[j]), icdf, 8);
                        }
                    }
                }

                q_ptr += SilkConstants.SHELL_CODEC_FRAME_LENGTH;
            }
        }

        /// <summary>
        /// Decodes signs of excitation
        /// </summary>
        /// <param name="psRangeDec">I/O  Compressor data structure</param>
        /// <param name="pulses">I/O  pulse signal</param>
        /// <param name="length">I    length of input</param>
        /// <param name="signalType">I    Signal type</param>
        /// <param name="quantOffsetType">I    Quantization offset type</param>
        /// <param name="sum_pulses">I    Sum of absolute pulses per block [MAX_NB_SHELL_BLOCKS]</param>
        internal static void silk_decode_signs(
            EntropyCoder psRangeDec,
            short[] pulses,
            int length,
            int signalType,
            int quantOffsetType,
            int[] sum_pulses)
        {
            int i, j, p;
            byte[] icdf = new byte[2];
            int q_ptr;
            byte[] icdf_table = Tables.silk_sign_iCDF;
            int icdf_ptr;

            icdf[1] = 0;
            q_ptr = 0;
            i = Inlines.silk_SMULBB(7, Inlines.silk_ADD_LSHIFT(quantOffsetType, signalType, 1));
            icdf_ptr = i;
            length = Inlines.silk_RSHIFT(length + SilkConstants.SHELL_CODEC_FRAME_LENGTH / 2, SilkConstants.LOG2_SHELL_CODEC_FRAME_LENGTH);

            for (i = 0; i < length; i++)
            {
                p = sum_pulses[i];

                if (p > 0)
                {
                    icdf[0] = icdf_table[icdf_ptr + Inlines.silk_min(p & 0x1F, 6)];
                    for (j = 0; j < SilkConstants.SHELL_CODEC_FRAME_LENGTH; j++)
                    {
                        if (pulses[q_ptr + j] > 0)
                        {
                            /* attach sign */
                            pulses[q_ptr + j] *= (short)(silk_dec_map(psRangeDec.dec_icdf(icdf, 8)));
                        }
                    }
                }

                q_ptr += SilkConstants.SHELL_CODEC_FRAME_LENGTH;
            }
        }
    }
}
