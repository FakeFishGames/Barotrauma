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

using Concentus.Celt.Enums;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;


namespace Concentus.Celt
{
    internal static class Laplace
    {
        /* The minimum probability of an energy delta (out of 32768). */
        private const int LAPLACE_LOG_MINP = 0;
        private const uint LAPLACE_MINP = (1 << LAPLACE_LOG_MINP);
        /* The minimum number of guaranteed representable energy deltas (in one
            direction). */
        private const int LAPLACE_NMIN = 16;

        /* When called, decay is positive and at most 11456. */
        internal static uint ec_laplace_get_freq1(uint fs0, int decay)
        {
            uint ft;
            ft = 32768 - LAPLACE_MINP * (2 * LAPLACE_NMIN) - fs0;
            return (uint)((ft * (int)(16384 - decay)) >> 15);
        }

        internal static void ec_laplace_encode(EntropyCoder enc, ref int value, uint fs, int decay)
        {
            uint fl;
            int val = value;
            fl = 0;
            if (val != 0)
            {
                int s;
                int i;
                s = 0 - (val < 0 ? 1 : 0);
                val = (val + s) ^ s;
                fl = fs;
                fs = ec_laplace_get_freq1(fs, decay);

                /* Search the decaying part of the PDF.*/
                for (i = 1; fs > 0 && i < val; i++)
                {
                    fs *= 2;
                    fl += fs + 2 * LAPLACE_MINP;
                    fs = (uint)((fs * (int)decay) >> 15);
                }

                /* Everything beyond that has probability LAPLACE_MINP. */
                if (fs == 0)
                {
                    int di;
                    int ndi_max;
                    ndi_max = (int)(32768 - fl + LAPLACE_MINP - 1) >> LAPLACE_LOG_MINP;
                    ndi_max = (ndi_max - s) >> 1;
                    di = Inlines.IMIN(val - i, ndi_max - 1);
                    fl += (uint)(2 * di + 1 + s) * LAPLACE_MINP;
                    fs = Inlines.IMIN(LAPLACE_MINP, 32768 - fl);
                    value = (i + di + s) ^ s;
                }
                else
                {
                    fs += LAPLACE_MINP;
                    fl += (uint)(fs & ~s);
                }
                Inlines.OpusAssert(fl + fs <= 32768);
                Inlines.OpusAssert(fs > 0);
            }

            enc.encode_bin(fl, fl + fs, 15);
        }

        internal static int ec_laplace_decode(EntropyCoder dec, uint fs, int decay)
        {
            int val = 0;
            uint fl;
            uint fm;
            fm = dec.decode_bin(15);
            fl = 0;

            if (fm >= fs)
            {
                val++;
                fl = fs;
                fs = ec_laplace_get_freq1(fs, decay) + LAPLACE_MINP;
                /* Search the decaying part of the PDF.*/
                while (fs > LAPLACE_MINP && fm >= fl + 2 * fs)
                {
                    fs *= 2;
                    fl += fs;
                    fs = (uint)(((fs - 2 * LAPLACE_MINP) * (int)decay) >> 15);
                    fs += LAPLACE_MINP;
                    val++;
                }
                /* Everything beyond that has probability LAPLACE_MINP. */
                if (fs <= LAPLACE_MINP)
                {
                    int di;
                    di = (int)(fm - fl) >> (LAPLACE_LOG_MINP + 1);
                    val += di;
                    fl += (uint)(2 * di * LAPLACE_MINP);
                }
                if (fm < fl + fs)
                    val = -val;
                else
                    fl += fs;
            }

            Inlines.OpusAssert(fl < 32768);
            Inlines.OpusAssert(fs > 0);
            Inlines.OpusAssert(fl <= fm);
            Inlines.OpusAssert(fm < Inlines.IMIN(fl + fs, 32768));

            dec.dec_update(fl, Inlines.IMIN(fl + fs, 32768), 32768);
            return val;
        }

    }
}
