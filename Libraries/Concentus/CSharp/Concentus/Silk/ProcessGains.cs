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

    internal static class ProcessGains
    {
        /* Processing of gains */
        internal static void silk_process_gains(
            SilkChannelEncoder psEnc,                                 /* I/O  Encoder state                                                               */
            SilkEncoderControl psEncCtrl,                             /* I/O  Encoder control                                                             */
            int condCoding                              /* I    The type of conditional coding to use                                       */
        )
        {
            SilkShapeState psShapeSt = psEnc.sShape;
            int k;
            int s_Q16, InvMaxSqrVal_Q16, gain, gain_squared, ResNrg, ResNrgPart, quant_offset_Q10;

            /* Gain reduction when LTP coding gain is high */
            if (psEnc.indices.signalType == SilkConstants.TYPE_VOICED)
            {
                /*s = -0.5f * silk_sigmoid( 0.25f * ( psEncCtrl.LTPredCodGain - 12.0f ) ); */
                s_Q16 = 0 - Sigmoid.silk_sigm_Q15(Inlines.silk_RSHIFT_ROUND(psEncCtrl.LTPredCodGain_Q7 - ((int)((12.0f) * ((long)1 << (7)) + 0.5))/*Inlines.SILK_CONST(12.0f, 7)*/, 4));
                for (k = 0; k < psEnc.nb_subfr; k++)
                {
                    psEncCtrl.Gains_Q16[k] = Inlines.silk_SMLAWB(psEncCtrl.Gains_Q16[k], psEncCtrl.Gains_Q16[k], s_Q16);
                }
            }

            /* Limit the quantized signal */
            /* InvMaxSqrVal = pow( 2.0f, 0.33f * ( 21.0f - SNR_dB ) ) / subfr_length; */
            InvMaxSqrVal_Q16 = Inlines.silk_DIV32_16(Inlines.silk_log2lin(
                Inlines.silk_SMULWB(((int)((21 + 16 / 0.33f) * ((long)1 << (7)) + 0.5))/*Inlines.SILK_CONST(21 + 16 / 0.33f, 7)*/ - psEnc.SNR_dB_Q7, ((int)((0.33f) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(0.33f, 16)*/)), psEnc.subfr_length);

            for (k = 0; k < psEnc.nb_subfr; k++)
            {
                /* Soft limit on ratio residual energy and squared gains */
                ResNrg = psEncCtrl.ResNrg[k];
                ResNrgPart = Inlines.silk_SMULWW(ResNrg, InvMaxSqrVal_Q16);
                if (psEncCtrl.ResNrgQ[k] > 0)
                {
                    ResNrgPart = Inlines.silk_RSHIFT_ROUND(ResNrgPart, psEncCtrl.ResNrgQ[k]);
                }
                else {
                    if (ResNrgPart >= Inlines.silk_RSHIFT(int.MaxValue, -psEncCtrl.ResNrgQ[k]))
                    {
                        ResNrgPart = int.MaxValue;
                    }
                    else {
                        ResNrgPart = Inlines.silk_LSHIFT(ResNrgPart, -psEncCtrl.ResNrgQ[k]);
                    }
                }
                gain = psEncCtrl.Gains_Q16[k];
                gain_squared = Inlines.silk_ADD_SAT32(ResNrgPart, Inlines.silk_SMMUL(gain, gain));
                if (gain_squared < short.MaxValue)
                {
                    /* recalculate with higher precision */
                    gain_squared = Inlines.silk_SMLAWW(Inlines.silk_LSHIFT(ResNrgPart, 16), gain, gain);
                    Inlines.OpusAssert(gain_squared > 0);
                    gain = Inlines.silk_SQRT_APPROX(gain_squared);                    /* Q8   */
                    gain = Inlines.silk_min(gain, int.MaxValue >> 8);
                    psEncCtrl.Gains_Q16[k] = Inlines.silk_LSHIFT_SAT32(gain, 8);   /* Q16  */
                }
                else {
                    gain = Inlines.silk_SQRT_APPROX(gain_squared);                    /* Q0   */
                    gain = Inlines.silk_min(gain, int.MaxValue >> 16);
                    psEncCtrl.Gains_Q16[k] = Inlines.silk_LSHIFT_SAT32(gain, 16);  /* Q16  */
                }
                
            }

            /* Save unquantized gains and gain Index */
            Array.Copy(psEncCtrl.Gains_Q16, psEncCtrl.GainsUnq_Q16, psEnc.nb_subfr);
            psEncCtrl.lastGainIndexPrev = psShapeSt.LastGainIndex;

            /* Quantize gains */
            BoxedValueSbyte boxed_lastGainIndex = new BoxedValueSbyte(psShapeSt.LastGainIndex);
            GainQuantization.silk_gains_quant(psEnc.indices.GainsIndices, psEncCtrl.Gains_Q16,
                boxed_lastGainIndex, condCoding == SilkConstants.CODE_CONDITIONALLY ? 1 : 0, psEnc.nb_subfr);
            psShapeSt.LastGainIndex = boxed_lastGainIndex.Val;

            /* Set quantizer offset for voiced signals. Larger offset when LTP coding gain is low or tilt is high (ie low-pass) */
            if (psEnc.indices.signalType == SilkConstants.TYPE_VOICED)
            {
                if (psEncCtrl.LTPredCodGain_Q7 + Inlines.silk_RSHIFT(psEnc.input_tilt_Q15, 8) > ((int)((1.0f) * ((long)1 << (7)) + 0.5))/*Inlines.SILK_CONST(1.0f, 7)*/)
                {
                    psEnc.indices.quantOffsetType = 0;
                }
                else {
                    psEnc.indices.quantOffsetType = 1;
                }
            }

            /* Quantizer boundary adjustment */
            quant_offset_Q10 = Tables.silk_Quantization_Offsets_Q10[psEnc.indices.signalType >> 1][psEnc.indices.quantOffsetType];
            psEncCtrl.Lambda_Q10 = ((int)((TuningParameters.LAMBDA_OFFSET) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.LAMBDA_OFFSET, 10)*/
                                  + Inlines.silk_SMULBB(((int)((TuningParameters.LAMBDA_DELAYED_DECISIONS) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.LAMBDA_DELAYED_DECISIONS, 10)*/, psEnc.nStatesDelayedDecision)
                                  + Inlines.silk_SMULWB(((int)((TuningParameters.LAMBDA_SPEECH_ACT) * ((long)1 << (18)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.LAMBDA_SPEECH_ACT, 18)*/, psEnc.speech_activity_Q8)
                                  + Inlines.silk_SMULWB(((int)((TuningParameters.LAMBDA_INPUT_QUALITY) * ((long)1 << (12)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.LAMBDA_INPUT_QUALITY, 12)*/, psEncCtrl.input_quality_Q14)
                                  + Inlines.silk_SMULWB(((int)((TuningParameters.LAMBDA_CODING_QUALITY) * ((long)1 << (12)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.LAMBDA_CODING_QUALITY, 12)*/, psEncCtrl.coding_quality_Q14)
                                  + Inlines.silk_SMULWB(((int)((TuningParameters.LAMBDA_QUANT_OFFSET) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.LAMBDA_QUANT_OFFSET, 16)*/, quant_offset_Q10);

            Inlines.OpusAssert(psEncCtrl.Lambda_Q10 > 0);
            Inlines.OpusAssert(psEncCtrl.Lambda_Q10 < ((int)((2) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(2, 10)*/);
        }
    }
}
