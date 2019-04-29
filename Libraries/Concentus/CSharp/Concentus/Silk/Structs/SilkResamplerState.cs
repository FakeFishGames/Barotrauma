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
    internal class SilkResamplerState
    {
        internal readonly int[] sIIR = new int[SilkConstants.SILK_RESAMPLER_MAX_IIR_ORDER]; /* this must be the first element of this struct FIXME why? */
        internal readonly int[] sFIR_i32 = new int[SilkConstants.SILK_RESAMPLER_MAX_FIR_ORDER]; // porting note: these two fields were originally a union, so that means only 1 will ever be used at a time.
        internal readonly short[] sFIR_i16 = new short[SilkConstants.SILK_RESAMPLER_MAX_FIR_ORDER];

        internal readonly short[] delayBuf = new short[48];
        internal int resampler_function = 0;
        internal int batchSize = 0;
        internal int invRatio_Q16 = 0;
        internal int FIR_Order = 0;
        internal int FIR_Fracs = 0;
        internal int Fs_in_kHz = 0;
        internal int Fs_out_kHz = 0;
        internal int inputDelay = 0;

        /// <summary>
        /// POINTER
        /// </summary>
        internal short[] Coefs = null;

        internal void Reset()
        {
            Arrays.MemSetInt(sIIR, 0, SilkConstants.SILK_RESAMPLER_MAX_IIR_ORDER);
            Arrays.MemSetInt(sFIR_i32, 0, SilkConstants.SILK_RESAMPLER_MAX_FIR_ORDER);
            Arrays.MemSetShort(sFIR_i16, 0, SilkConstants.SILK_RESAMPLER_MAX_FIR_ORDER);
            Arrays.MemSetShort(delayBuf, 0, 48);
            resampler_function = 0;
            batchSize = 0;
            invRatio_Q16 = 0;
            FIR_Order = 0;
            FIR_Fracs = 0;
            Fs_in_kHz = 0;
            Fs_out_kHz = 0;
            inputDelay = 0;
            Coefs = null;
        }

        internal void Assign(SilkResamplerState other)
        {
            resampler_function = other.resampler_function;
            batchSize = other.batchSize;
            invRatio_Q16 = other.invRatio_Q16;
            FIR_Order = other.FIR_Order;
            FIR_Fracs = other.FIR_Fracs;
            Fs_in_kHz = other.Fs_in_kHz;
            Fs_out_kHz = other.Fs_out_kHz;
            inputDelay = other.inputDelay;
            Coefs = other.Coefs;
            Array.Copy(other.sIIR, this.sIIR, SilkConstants.SILK_RESAMPLER_MAX_IIR_ORDER);
            Array.Copy(other.sFIR_i32, this.sFIR_i32, SilkConstants.SILK_RESAMPLER_MAX_FIR_ORDER);
            Array.Copy(other.sFIR_i16, this.sFIR_i16, SilkConstants.SILK_RESAMPLER_MAX_FIR_ORDER);
            Array.Copy(other.delayBuf, this.delayBuf, 48);
        }
    }
}
