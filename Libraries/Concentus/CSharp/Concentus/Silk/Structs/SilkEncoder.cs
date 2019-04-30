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

    /// <summary>
    /// Encoder Super Struct
    /// </summary>
    internal class SilkEncoder
    {
        internal readonly SilkChannelEncoder[] state_Fxx = new SilkChannelEncoder[SilkConstants.ENCODER_NUM_CHANNELS];
        internal readonly StereoEncodeState sStereo = new StereoEncodeState();
        internal int nBitsUsedLBRR = 0;
        internal int nBitsExceeded = 0;
        internal int nChannelsAPI = 0;
        internal int nChannelsInternal = 0;
        internal int nPrevChannelsInternal = 0;
        internal int timeSinceSwitchAllowed_ms = 0;
        internal int allowBandwidthSwitch = 0;
        internal int prev_decode_only_middle = 0;

        internal SilkEncoder()
        {
            for (int c = 0; c < SilkConstants.ENCODER_NUM_CHANNELS; c++)
            {
                state_Fxx[c] = new SilkChannelEncoder();
            }
        }

        internal void Reset()
        {
            for (int c = 0; c < SilkConstants.ENCODER_NUM_CHANNELS; c++)
            {
                state_Fxx[c].Reset();
            }

            sStereo.Reset();
            nBitsUsedLBRR = 0;
            nBitsExceeded = 0;
            nChannelsAPI = 0;
            nChannelsInternal = 0;
            nPrevChannelsInternal = 0;
            timeSinceSwitchAllowed_ms = 0;
            allowBandwidthSwitch = 0;
            prev_decode_only_middle = 0;
        }

        /// <summary>
        /// Initialize Silk Encoder state
        /// </summary>
        /// <param name="psEnc">I/O  Pointer to Silk FIX encoder state</param>
        /// <returns></returns>
        internal static int silk_init_encoder(SilkChannelEncoder psEnc)
        {
            int ret = 0;

            // Clear the entire encoder state
            psEnc.Reset();

            psEnc.variable_HP_smth1_Q15 = Inlines.silk_LSHIFT(Inlines.silk_lin2log(((int)((TuningParameters.VARIABLE_HP_MIN_CUTOFF_HZ) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(TuningParameters.VARIABLE_HP_MIN_CUTOFF_HZ, 16)*/) - (16 << 7), 8);
            psEnc.variable_HP_smth2_Q15 = psEnc.variable_HP_smth1_Q15;

            // Used to deactivate LSF interpolation, pitch prediction
            psEnc.first_frame_after_reset = 1;

            // Initialize Silk VAD
            ret += VoiceActivityDetection.silk_VAD_Init(psEnc.sVAD);

            return ret;
        }
    }
}