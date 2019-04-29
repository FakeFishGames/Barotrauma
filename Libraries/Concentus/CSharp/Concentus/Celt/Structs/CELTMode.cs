/* Copyright (c) 2007-2008 CSIRO
   Copyright (c) 2007-2011 Xiph.Org Foundation
   Originally written by Jean-Marc Valin, Gregory Maxwell, Koen Vos,
   Timothy B. Terriberry, and the Opus open-source contributors
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

namespace Concentus.Celt.Structs
{
    using Concentus.Celt.Enums;
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using Concentus.Enums;
    using System;

    internal class CeltMode
    {
        internal int Fs = 0;
        internal int overlap = 0;

        internal int nbEBands = 0;
        internal int effEBands = 0;
        internal int[] preemph = { 0, 0, 0, 0 };

        /// <summary>
        /// Definition for each "pseudo-critical band"
        /// </summary>
        internal short[] eBands = null;

        internal int maxLM = 0;
        internal int nbShortMdcts = 0;
        internal int shortMdctSize = 0;

        /// <summary>
        /// Number of lines in allocVectors
        /// </summary>
        internal int nbAllocVectors = 0;

        /// <summary>
        /// Number of bits in each band for several rates
        /// </summary>
        internal byte[] allocVectors = null;
        internal short[] logN = null;

        internal int[] window = null;
        internal MDCTLookup mdct = new MDCTLookup();
        internal PulseCache cache = new PulseCache();

        private CeltMode()
        {
        }

        internal static readonly CeltMode mode48000_960_120 = new CeltMode
        {
            Fs = 48000,
            overlap = 120,
            nbEBands = 21,
            effEBands = 21,
            preemph = new int[] { 27853, 0, 4096, 8192 },
            eBands = Tables.eband5ms,
            maxLM = 3,
            nbShortMdcts = 8,
            shortMdctSize = 120,
            nbAllocVectors = 11,
            allocVectors = Tables.band_allocation,
            logN = Tables.logN400,
            window = Tables.window120,
            mdct = new MDCTLookup()
            {
                n = 1920,
                maxshift = 3,
                kfft = new FFTState[]
                {
                    Tables.fft_state48000_960_0,
                    Tables.fft_state48000_960_1,
                    Tables.fft_state48000_960_2,
                    Tables.fft_state48000_960_3,
                },
                trig = Tables.mdct_twiddles960
            },
            cache = new PulseCache()
            {
                size = 392,
                index = Tables.cache_index50,
                bits = Tables.cache_bits50,
                caps = Tables.cache_caps50,
            }
        };
    }
}
