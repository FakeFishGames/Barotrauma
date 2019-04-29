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

    internal static class DecodePitch
    {
        internal static void silk_decode_pitch(
                short lagIndex,           /* I                                                                */
                sbyte contourIndex,       /* O                                                                */
                int[] pitch_lags,       /* O    4 pitch values                                              */
                int Fs_kHz,             /* I    sampling frequency (kHz)                                    */
                int nb_subfr            /* I    number of sub frames                                        */
            )
        {
            int lag, k, min_lag, max_lag;
            sbyte[][] Lag_CB_ptr;

            if (Fs_kHz == 8)
            {
                if (nb_subfr == SilkConstants.PE_MAX_NB_SUBFR)
                {
                    Lag_CB_ptr = Tables.silk_CB_lags_stage2;
                }
                else
                {
                    Inlines.OpusAssert(nb_subfr == SilkConstants.PE_MAX_NB_SUBFR >> 1);
                    Lag_CB_ptr = Tables.silk_CB_lags_stage2_10_ms;
                }
            }
            else
            {
                if (nb_subfr == SilkConstants.PE_MAX_NB_SUBFR)
                {
                    Lag_CB_ptr = Tables.silk_CB_lags_stage3;
                }
                else
                {
                    Inlines.OpusAssert(nb_subfr == SilkConstants.PE_MAX_NB_SUBFR >> 1);
                    Lag_CB_ptr = Tables.silk_CB_lags_stage3_10_ms;
                }
            }

            min_lag = Inlines.silk_SMULBB(SilkConstants.PE_MIN_LAG_MS, Fs_kHz);
            max_lag = Inlines.silk_SMULBB(SilkConstants.PE_MAX_LAG_MS, Fs_kHz);
            lag = min_lag + lagIndex;

            for (k = 0; k < nb_subfr; k++)
            {
                pitch_lags[k] = lag + Lag_CB_ptr[k][contourIndex];
                pitch_lags[k] = Inlines.silk_LIMIT(pitch_lags[k], min_lag, max_lag);
            }
        }
    }
}
