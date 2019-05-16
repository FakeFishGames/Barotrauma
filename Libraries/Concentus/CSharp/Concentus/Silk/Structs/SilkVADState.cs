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
    /// VAD state
    /// </summary>
    internal class SilkVADState
    {
        /// <summary>
        /// Analysis filterbank state: 0-8 kHz
        /// </summary>
        internal readonly int[] AnaState = new int[2];

        /// <summary>
        /// Analysis filterbank state: 0-4 kHz
        /// </summary>
        internal readonly int[] AnaState1 = new int[2];

        /// <summary>
        /// Analysis filterbank state: 0-2 kHz
        /// </summary>
        internal readonly int[] AnaState2 = new int[2];

        /// <summary>
        /// Subframe energies
        /// </summary>
        internal readonly int[] XnrgSubfr = new int[SilkConstants.VAD_N_BANDS];

        /// <summary>
        /// Smoothed energy level in each band
        /// </summary>
        internal readonly int[] NrgRatioSmth_Q8 = new int[SilkConstants.VAD_N_BANDS];

        /// <summary>
        /// State of differentiator in the lowest band
        /// </summary>
        internal short HPstate = 0;

        /// <summary>
        /// Noise energy level in each band
        /// </summary>
        internal readonly int[] NL = new int[SilkConstants.VAD_N_BANDS];

        /// <summary>
        /// Inverse noise energy level in each band
        /// </summary>
        internal readonly int[] inv_NL = new int[SilkConstants.VAD_N_BANDS];

        /// <summary>
        /// Noise level estimator bias/offset
        /// </summary>
        internal readonly int[] NoiseLevelBias = new int[SilkConstants.VAD_N_BANDS];

        /// <summary>
        /// Frame counter used in the initial phase
        /// </summary>
        internal int counter = 0;

        internal void Reset()
        {
            Arrays.MemSetInt(AnaState, 0, 2);
            Arrays.MemSetInt(AnaState1, 0, 2);
            Arrays.MemSetInt(AnaState2, 0, 2);
            Arrays.MemSetInt(XnrgSubfr, 0, SilkConstants.VAD_N_BANDS);
            Arrays.MemSetInt(NrgRatioSmth_Q8, 0, SilkConstants.VAD_N_BANDS);
            HPstate = 0;
            Arrays.MemSetInt(NL, 0, SilkConstants.VAD_N_BANDS);
            Arrays.MemSetInt(inv_NL, 0, SilkConstants.VAD_N_BANDS);
            Arrays.MemSetInt(NoiseLevelBias, 0, SilkConstants.VAD_N_BANDS);
            counter = 0;
        }
    }
}
