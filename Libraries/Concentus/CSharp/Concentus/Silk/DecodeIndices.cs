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

    internal static class DecodeIndices
    {
        /* Decode side-information parameters from payload */
        internal static void silk_decode_indices(
            SilkChannelDecoder psDec,                         /* I/O  State                                       */
            EntropyCoder psRangeDec,                    /* I/O  Compressor data structure                   */
            int FrameIndex,                     /* I    Frame number                                */
            int decode_LBRR,                    /* I    Flag indicating LBRR data is being decoded  */
            int condCoding                      /* I    The type of conditional coding to use       */
        )
        {
            int i, k, Ix;
            int decode_absolute_lagIndex, delta_lagIndex;
            short[] ec_ix = new short[psDec.LPC_order];
            byte[] pred_Q8 = new byte[psDec.LPC_order];

            /*******************************************/
            /* Decode signal type and quantizer offset */
            /*******************************************/
            if (decode_LBRR != 0 || psDec.VAD_flags[FrameIndex] != 0)
            {
                Ix = psRangeDec.dec_icdf(Tables.silk_type_offset_VAD_iCDF, 8) + 2;
            }
            else {
                Ix = psRangeDec.dec_icdf(Tables.silk_type_offset_no_VAD_iCDF, 8);
            }
            psDec.indices.signalType = (sbyte)Inlines.silk_RSHIFT(Ix, 1);
            psDec.indices.quantOffsetType = (sbyte)(Ix & 1);

            /****************/
            /* Decode gains */
            /****************/
            /* First subframe */
            if (condCoding == SilkConstants.CODE_CONDITIONALLY)
            {
                /* Conditional coding */
                psDec.indices.GainsIndices[0] = (sbyte)psRangeDec.dec_icdf(Tables.silk_delta_gain_iCDF, 8);
            }
            else {
                /* Independent coding, in two stages: MSB bits followed by 3 LSBs */
                psDec.indices.GainsIndices[0] = (sbyte)Inlines.silk_LSHIFT(psRangeDec.dec_icdf(Tables.silk_gain_iCDF[psDec.indices.signalType], 8), 3);
                psDec.indices.GainsIndices[0] += (sbyte)psRangeDec.dec_icdf(Tables.silk_uniform8_iCDF, 8);
            }

            /* Remaining subframes */
            for (i = 1; i < psDec.nb_subfr; i++)
            {
                psDec.indices.GainsIndices[i] = (sbyte)psRangeDec.dec_icdf(Tables.silk_delta_gain_iCDF, 8);
            }

            /**********************/
            /* Decode LSF Indices */
            /**********************/
            psDec.indices.NLSFIndices[0] = (sbyte)psRangeDec.dec_icdf(psDec.psNLSF_CB.CB1_iCDF, (psDec.indices.signalType >> 1) * psDec.psNLSF_CB.nVectors, 8);
            NLSF.silk_NLSF_unpack(ec_ix, pred_Q8, psDec.psNLSF_CB, psDec.indices.NLSFIndices[0]);
            Inlines.OpusAssert(psDec.psNLSF_CB.order == psDec.LPC_order);
            for (i = 0; i < psDec.psNLSF_CB.order; i++)
            {
                Ix = psRangeDec.dec_icdf(psDec.psNLSF_CB.ec_iCDF, (ec_ix[i]), 8);
                if (Ix == 0)
                {
                    Ix -= psRangeDec.dec_icdf(Tables.silk_NLSF_EXT_iCDF, 8);
                }
                else if (Ix == 2 * SilkConstants.NLSF_QUANT_MAX_AMPLITUDE)
                {
                    Ix += psRangeDec.dec_icdf(Tables.silk_NLSF_EXT_iCDF, 8);
                }
                psDec.indices.NLSFIndices[i + 1] = (sbyte)(Ix - SilkConstants.NLSF_QUANT_MAX_AMPLITUDE);
            }

            /* Decode LSF interpolation factor */
            if (psDec.nb_subfr == SilkConstants.MAX_NB_SUBFR)
            {
                psDec.indices.NLSFInterpCoef_Q2 = (sbyte)psRangeDec.dec_icdf(Tables.silk_NLSF_interpolation_factor_iCDF, 8);
            }
            else {
                psDec.indices.NLSFInterpCoef_Q2 = 4;
            }

            if (psDec.indices.signalType == SilkConstants.TYPE_VOICED)
            {
                /*********************/
                /* Decode pitch lags */
                /*********************/
                /* Get lag index */
                decode_absolute_lagIndex = 1;
                if (condCoding == SilkConstants.CODE_CONDITIONALLY && psDec.ec_prevSignalType == SilkConstants.TYPE_VOICED)
                {
                    /* Decode Delta index */
                    delta_lagIndex = (short)psRangeDec.dec_icdf(Tables.silk_pitch_delta_iCDF, 8);
                    if (delta_lagIndex > 0)
                    {
                        delta_lagIndex = delta_lagIndex - 9;
                        psDec.indices.lagIndex = (short)(psDec.ec_prevLagIndex + delta_lagIndex);
                        decode_absolute_lagIndex = 0;
                    }
                }
                if (decode_absolute_lagIndex != 0)
                {
                    /* Absolute decoding */
                    psDec.indices.lagIndex = (short)(psRangeDec.dec_icdf(Tables.silk_pitch_lag_iCDF, 8) * Inlines.silk_RSHIFT(psDec.fs_kHz, 1));
                    psDec.indices.lagIndex += (short)psRangeDec.dec_icdf(psDec.pitch_lag_low_bits_iCDF, 8);
                }
                psDec.ec_prevLagIndex = psDec.indices.lagIndex;

                /* Get countour index */
                psDec.indices.contourIndex = (sbyte)psRangeDec.dec_icdf(psDec.pitch_contour_iCDF, 8);

                /********************/
                /* Decode LTP gains */
                /********************/
                /* Decode PERIndex value */
                psDec.indices.PERIndex = (sbyte)psRangeDec.dec_icdf(Tables.silk_LTP_per_index_iCDF, 8);

                for (k = 0; k < psDec.nb_subfr; k++)
                {
                    psDec.indices.LTPIndex[k] = (sbyte)psRangeDec.dec_icdf(Tables.silk_LTP_gain_iCDF_ptrs[psDec.indices.PERIndex], 8);
                }

                /**********************/
                /* Decode LTP scaling */
                /**********************/
                if (condCoding == SilkConstants.CODE_INDEPENDENTLY)
                {
                    psDec.indices.LTP_scaleIndex = (sbyte)psRangeDec.dec_icdf(Tables.silk_LTPscale_iCDF, 8);
                }
                else {
                    psDec.indices.LTP_scaleIndex = 0;
                }
            }
            psDec.ec_prevSignalType = psDec.indices.signalType;

            /***************/
            /* Decode seed */
            /***************/
            psDec.indices.Seed = (sbyte)psRangeDec.dec_icdf(Tables.silk_uniform4_iCDF, 8);
        }
    }
}
