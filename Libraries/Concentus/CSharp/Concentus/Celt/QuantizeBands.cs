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

namespace Concentus.Celt
{
    using Concentus.Celt.Enums;
    using Concentus.Celt.Structs;
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using System;
    using System.Diagnostics;

    internal static class QuantizeBands
    {
        /* prediction coefficients: 0.9, 0.8, 0.65, 0.5 */
        private static readonly int[] pred_coef = new int[]{ 29440, 26112, 21248, 16384 };
        private static readonly int[] beta_coef = new int[] { 30147, 22282, 12124, 6554 };
        private static readonly int beta_intra = 4915;
        private static byte[] small_energy_icdf = { 2, 1, 0 };

        internal static int loss_distortion(int[][] eBands, int[][] oldEBands, int start, int end, int len, int C)
        {
            int c, i;
            int dist = 0;
            c = 0;
            do
            {
                for (i = start; i < end; i++)
                {
                    int d = Inlines.SUB16(Inlines.SHR16(eBands[c][i], 3), Inlines.SHR16(oldEBands[c][i], 3));
                    dist = Inlines.MAC16_16(dist, d, d);
                }
            } while (++c < C);

            return Inlines.MIN32(200, Inlines.SHR32(dist, 2 * CeltConstants.DB_SHIFT - 6));
        }

        internal static int quant_coarse_energy_impl(CeltMode m, int start, int end,
              int[][] eBands, int[][] oldEBands,
              int budget, int tell,
              byte[] prob_model, int[][] error, EntropyCoder enc,
              int C, int LM, int intra, int max_decay, int lfe)
        {
            int i, c;
            int badness = 0;
            int[] prev = { 0, 0 };
            int coef;
            int beta;

            if (tell + 3 <= budget)
            {
                enc.enc_bit_logp(intra, 3);
            }

            if (intra != 0)
            {
                coef = 0;
                beta = beta_intra;
            }
            else {
                beta = beta_coef[LM];
                coef = pred_coef[LM];
            }

            /* Encode at a fixed coarse resolution */
            for (i = start; i < end; i++)
            {
                c = 0;
                do
                {
                    int bits_left;
                    int qi, qi0;
                    int q;
                    int x;
                    int f, tmp;
                    int oldE;
                    int decay_bound;
                    x = eBands[c][i];
                    oldE = Inlines.MAX16(-((short)(0.5 + (9.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(9.0f, CeltConstants.DB_SHIFT)*/, oldEBands[c][i]);
                    f = Inlines.SHL32(Inlines.EXTEND32(x), 7) - Inlines.PSHR32(Inlines.MULT16_16(coef, oldE), 8) - prev[c];
                    /* Rounding to nearest integer here is really important! */
                    qi = (f + ((int)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT + 7))))/*Inlines.QCONST32(.5f, CeltConstants.DB_SHIFT + 7)*/) >> (CeltConstants.DB_SHIFT + 7);
                    decay_bound = Inlines.EXTRACT16(Inlines.MAX32(-((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/,
                          Inlines.SUB32((int)oldEBands[c][i], max_decay)));
                    /* Prevent the energy from going down too quickly (e.g. for bands
                       that have just one bin) */
                    if (qi < 0 && x < decay_bound)
                    {
                        qi += (int)Inlines.SHR16(Inlines.SUB16(decay_bound, x), CeltConstants.DB_SHIFT);
                        if (qi > 0)
                            qi = 0;
                    }
                    qi0 = qi;
                    /* If we don't have enough bits to encode all the energy, just assume
                        something safe. */
                    tell = enc.tell();
                    bits_left = budget - tell - 3 * C * (end - i);
                    if (i != start && bits_left < 30)
                    {
                        if (bits_left < 24)
                            qi = Inlines.IMIN(1, qi);
                        if (bits_left < 16)
                            qi = Inlines.IMAX(-1, qi);
                    }
                    if (lfe != 0 && i >= 2)
                        qi = Inlines.IMIN(qi, 0);
                    if (budget - tell >= 15)
                    {
                        int pi;
                        pi = 2 * Inlines.IMIN(i, 20);
                        Laplace.ec_laplace_encode(enc, ref qi, (((uint)prob_model[pi]) << 7), ((int)prob_model[pi + 1]) << 6);
                    }
                    else if (budget - tell >= 2)
                    {
                        qi = Inlines.IMAX(-1, Inlines.IMIN(qi, 1));
                        enc.enc_icdf(2 * qi ^ (0 - (qi < 0 ? 1 : 0)), small_energy_icdf, 2);
                    }
                    else if (budget - tell >= 1)
                    {
                        qi = Inlines.IMIN(0, qi);
                        enc.enc_bit_logp(-qi, 1);
                    }
                    else
                        qi = -1;
                    error[c][i] = (Inlines.PSHR32(f, 7) - Inlines.SHL16((qi), CeltConstants.DB_SHIFT));
                    badness += Inlines.abs(qi0 - qi);
                    q = (int)Inlines.SHL32(qi, CeltConstants.DB_SHIFT); // opus bug: useless extend32

                    tmp = Inlines.PSHR32(Inlines.MULT16_16(coef, oldE), 8) + prev[c] + Inlines.SHL32(q, 7);
                    tmp = Inlines.MAX32(-((int)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT + 7))))/*Inlines.QCONST32(28.0f, CeltConstants.DB_SHIFT + 7)*/, tmp);
                    oldEBands[c][i] = (Inlines.PSHR32(tmp, 7));
                    prev[c] = prev[c] + Inlines.SHL32(q, 7) - Inlines.MULT16_16(beta, Inlines.PSHR32(q, 8));
                } while (++c < C);
            }
            return lfe != 0 ? 0 : badness;
        }

        internal static void quant_coarse_energy(CeltMode m, int start, int end, int effEnd,
              int[][] eBands, int[][] oldEBands, uint budget,
              int[][] error, EntropyCoder enc, int C, int LM, int nbAvailableBytes,
              int force_intra, ref int delayedIntra, int two_pass, int loss_rate, int lfe)
        {
            int intra;
            int max_decay;
            int[][] oldEBands_intra;
            int[][] error_intra;
            EntropyCoder enc_start_state = new EntropyCoder(); // [porting note] stack variable
            uint tell;
            int badness1 = 0;
            int intra_bias;
            int new_distortion;


            intra = (force_intra != 0 || (two_pass == 0 && delayedIntra > 2 * C * (end - start) && nbAvailableBytes > (end - start) * C)) ? 1 : 0;
            intra_bias = (int)((budget * delayedIntra * loss_rate) / (C * 512));
            new_distortion = loss_distortion(eBands, oldEBands, start, effEnd, m.nbEBands, C);

            tell = (uint)enc.tell();
            if (tell + 3 > budget)
                two_pass = intra = 0;

            max_decay = ((short)(0.5 + (16.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(16.0f, CeltConstants.DB_SHIFT)*/;
            if (end - start > 10)
            {
                max_decay = (Inlines.MIN32(max_decay, Inlines.SHL32(nbAvailableBytes, CeltConstants.DB_SHIFT - 3))); // opus bug: useless extend32
            }
            if (lfe != 0)
            {
                max_decay = ((short)(0.5 + (3.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(3.0f, CeltConstants.DB_SHIFT)*/;
            }
            enc_start_state.Assign(enc);

            oldEBands_intra = Arrays.InitTwoDimensionalArray<int>(C, m.nbEBands);
            error_intra = Arrays.InitTwoDimensionalArray<int>(C, m.nbEBands);
            Array.Copy(oldEBands[0], 0, oldEBands_intra[0], 0, m.nbEBands);
            if (C == 2)
                Array.Copy(oldEBands[1], 0, oldEBands_intra[1], 0, m.nbEBands);

            if (two_pass != 0 || intra != 0)
            {
                badness1 = quant_coarse_energy_impl(m, start, end, eBands, oldEBands_intra, (int)budget,
                      (int)tell, Tables.e_prob_model[LM][1], error_intra, enc, C, LM, 1, max_decay, lfe);
            }

            if (intra == 0)
            {
                int intra_buf;
                EntropyCoder enc_intra_state = new EntropyCoder(); // [porting note] stack variable
                int tell_intra;
                uint nstart_bytes;
                uint nintra_bytes;
                uint save_bytes;
                int badness2;
                byte[] intra_bits = null;

                tell_intra = (int)enc.tell_frac();

                enc_intra_state.Assign(enc);

                nstart_bytes = enc_start_state.range_bytes();
                nintra_bytes = enc_intra_state.range_bytes();
                intra_buf = enc_intra_state.buf_ptr + (int)nstart_bytes;
                save_bytes = nintra_bytes - nstart_bytes;

                if (save_bytes != 0)
                {
                    intra_bits = new byte[(int)save_bytes];
                    /* Copy bits from intra bit-stream */
                    Array.Copy(enc_intra_state.buf, intra_buf, intra_bits, 0, (int)save_bytes);
                }

                enc.Assign(enc_start_state);

                badness2 = quant_coarse_energy_impl(m, start, end, eBands, oldEBands, (int)budget,
                      (int)tell, Tables.e_prob_model[LM][intra], error, enc, C, LM, 0, max_decay, lfe);

                if (two_pass != 0 && (badness1 < badness2 || (badness1 == badness2 && ((int)enc.tell_frac()) + intra_bias > tell_intra)))
                {
                    enc.Assign(enc_intra_state);
                    /* Copy intra bits to bit-stream */
                    if (intra_bits != null)
                    {
                        Array.Copy(intra_bits, 0, enc_intra_state.buf, intra_buf, (int)(nintra_bytes - nstart_bytes));
                    }
                    Array.Copy(oldEBands_intra[0], 0, oldEBands[0], 0, m.nbEBands);
                    Array.Copy(error_intra[0], 0, error[0], 0, m.nbEBands);
                    if (C == 2)
                    {
                        Array.Copy(oldEBands_intra[1], 0, oldEBands[1], 0, m.nbEBands);
                        Array.Copy(error_intra[1], 0, error[1], 0, m.nbEBands);
                    }
                    intra = 1;
                }
            }
            else
            {
                Array.Copy(oldEBands_intra[0], 0, oldEBands[0], 0, m.nbEBands);
                Array.Copy(error_intra[0], 0, error[0], 0, m.nbEBands);
                if (C == 2)
                {
                    Array.Copy(oldEBands_intra[1], 0, oldEBands[1], 0, m.nbEBands);
                    Array.Copy(error_intra[1], 0, error[1], 0, m.nbEBands);
                }
            }

            if (intra != 0)
            {
                delayedIntra = new_distortion;
            }
            else
            {
                delayedIntra = Inlines.ADD32(Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15(pred_coef[LM], pred_coef[LM]), delayedIntra),
                    new_distortion);
            }
        }

        internal static void quant_fine_energy(CeltMode m, int start, int end, int[][] oldEBands, int[][] error, int[] fine_quant, EntropyCoder enc, int C)
        {
            int i, c;

            /* Encode finer resolution */
            for (i = start; i < end; i++)
            {
                int frac = (1 << fine_quant[i]);
                if (fine_quant[i] <= 0)
                    continue;
                c = 0;
                do
                {
                    int q2;
                    int offset;
                    /* Has to be without rounding */
                    q2 = (error[c][i] + ((short)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.5f, CeltConstants.DB_SHIFT)*/) >> (CeltConstants.DB_SHIFT - fine_quant[i]);
                    if (q2 > frac - 1)
                        q2 = frac - 1;
                    if (q2 < 0)
                        q2 = 0;
                    enc.enc_bits((uint)q2, (uint)fine_quant[i]);
                    offset = Inlines.SUB16(
                        (Inlines.SHR32(
                            Inlines.SHL32(q2, CeltConstants.DB_SHIFT) + ((short)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.5f, CeltConstants.DB_SHIFT)*/,
                            fine_quant[i])),
                        ((short)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.5f, CeltConstants.DB_SHIFT)*/);
                    oldEBands[c][i] += offset;
                    error[c][i] -= offset;
                } while (++c < C);
            }
        }

        internal static void quant_energy_finalise(CeltMode m, int start, int end, int[][] oldEBands, int[][] error, int[] fine_quant, int[] fine_priority, int bits_left, EntropyCoder enc, int C)
        {
            int i, prio, c;

            /* Use up the remaining bits */
            for (prio = 0; prio < 2; prio++)
            {
                for (i = start; i < end && bits_left >= C; i++)
                {
                    if (fine_quant[i] >= CeltConstants.MAX_FINE_BITS || fine_priority[i] != prio)
                    {
                        continue;
                    }

                    c = 0;
                    do
                    {
                        int q2;
                        int offset;
                        q2 = error[c][i] < 0 ? 0 : 1;
                        enc.enc_bits((uint)q2, 1);
                        offset = Inlines.SHR16((Inlines.SHL16((q2), CeltConstants.DB_SHIFT) - ((short)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.5f, CeltConstants.DB_SHIFT)*/), fine_quant[i] + 1);
                        oldEBands[c][i] += offset;
                        bits_left--;
                    } while (++c < C);
                }
            }
        }

        internal static void unquant_coarse_energy(CeltMode m, int start, int end, int[] oldEBands, int intra, EntropyCoder dec, int C, int LM)
        {
            byte[] prob_model = Tables.e_prob_model[LM][intra];
            int i, c;
            int[] prev = { 0, 0 };
            int coef;
            int beta;
            int budget;
            int tell;

            if (intra != 0)
            {
                coef = 0;
                beta = beta_intra;
            }
            else {
                beta = beta_coef[LM];
                coef = pred_coef[LM];
            }

            budget = (int)dec.storage * 8;

            /* Decode at a fixed coarse resolution */
            for (i = start; i < end; i++)
            {
                c = 0;
                do
                {
                    int qi;
                    int q;
                    int tmp;
                    /* It would be better to express this invariant as a
                       test on C at function entry, but that isn't enough
                       to make the static analyzer happy. */
                    Inlines.OpusAssert(c < 2);
                    tell = dec.tell();
                    if (budget - tell >= 15)
                    {
                        int pi;
                        pi = 2 * Inlines.IMIN(i, 20);
                        qi = Laplace.ec_laplace_decode(dec,
                              (uint)prob_model[pi] << 7, prob_model[pi + 1] << 6);
                    }
                    else if (budget - tell >= 2)
                    {
                        qi = dec.dec_icdf(small_energy_icdf, 2);
                        qi = (qi >> 1) ^ -(qi & 1);
                    }
                    else if (budget - tell >= 1)
                    {
                        qi = 0 - dec.dec_bit_logp(1);
                    }
                    else
                    {
                        qi = -1;
                    }
                    q = (int)Inlines.SHL32(qi, CeltConstants.DB_SHIFT); // opus bug: useless extend32

                    oldEBands[i + c * m.nbEBands] = Inlines.MAX16((0 - ((short)(0.5 + (9.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(9.0f, CeltConstants.DB_SHIFT)*/), oldEBands[i + c * m.nbEBands]);
                    tmp = Inlines.PSHR32(Inlines.MULT16_16(coef, oldEBands[i + c * m.nbEBands]), 8) + prev[c] + Inlines.SHL32(q, 7);
                    tmp = Inlines.MAX32(-((int)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT + 7))))/*Inlines.QCONST32(28.0f, CeltConstants.DB_SHIFT + 7)*/, tmp);
                    oldEBands[i + c * m.nbEBands] = (Inlines.PSHR32(tmp, 7));
                    prev[c] = prev[c] + Inlines.SHL32(q, 7) - Inlines.MULT16_16(beta, Inlines.PSHR32(q, 8));
                } while (++c < C);
            }
        }

        internal static void unquant_fine_energy(CeltMode m, int start, int end, int[] oldEBands, int[] fine_quant, EntropyCoder dec, int C)
        {
            int i, c;
            /* Decode finer resolution */
            for (i = start; i < end; i++)
            {
                if (fine_quant[i] <= 0)
                    continue;
                c = 0;
                do
                {
                    int q2;
                    int offset;
                    q2 = (int)dec.dec_bits((uint)fine_quant[i]);
                    offset = Inlines.SUB16((Inlines.SHR32(
                        Inlines.SHL32(q2, CeltConstants.DB_SHIFT) + 
                        ((short)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.5f, CeltConstants.DB_SHIFT)*/, fine_quant[i])),
                        ((short)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.5f, CeltConstants.DB_SHIFT)*/); // opus bug: unnecessary extend32
                    oldEBands[i + c * m.nbEBands] += offset;
                } while (++c < C);
            }
        }

        internal static void unquant_energy_finalise(CeltMode m, int start, int end, int[] oldEBands, int[] fine_quant, int[] fine_priority, int bits_left, EntropyCoder dec, int C)
        {
            int i, prio, c;

            /* Use up the remaining bits */
            for (prio = 0; prio < 2; prio++)
            {
                for (i = start; i < end && bits_left >= C; i++)
                {
                    if (fine_quant[i] >= CeltConstants.MAX_FINE_BITS || fine_priority[i] != prio)
                        continue;
                    c = 0;
                    do
                    {
                        int q2;
                        int offset;
                        q2 = (int)dec.dec_bits(1);
                        offset = Inlines.SHR16((Inlines.SHL16((q2), CeltConstants.DB_SHIFT) - ((short)(0.5 + (.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.5f, CeltConstants.DB_SHIFT)*/), fine_quant[i] + 1);
                        oldEBands[i + c * m.nbEBands] += offset;
                        bits_left--;
                    } while (++c < C);
                }
            }
        }

        /// <summary>
        /// non-pointer case
        /// </summary>
        /// <param name="m"></param>
        /// <param name="effEnd"></param>
        /// <param name="end"></param>
        /// <param name="bandE"></param>
        /// <param name="bandLogE"></param>
        /// <param name="C"></param>
        internal static void amp2Log2(CeltMode m, int effEnd, int end,
              int[][] bandE, int[][] bandLogE, int C)
        {
            int c, i;
            c = 0;
            do
            {
                for (i = 0; i < effEnd; i++)
                {
                    bandLogE[c][i] =
                       (Inlines.celt_log2(Inlines.SHL32(bandE[c][i], 2))
                       - Inlines.SHL16((int)Tables.eMeans[i], 6));
                }
                for (i = effEnd; i < end; i++)
                {
                    bandLogE[c][i] = (0 - ((short)(0.5 + (14.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(14.0f, CeltConstants.DB_SHIFT)*/);
                }
            } while (++c < C);
        }

        /// <summary>
        /// only needed in one place
        /// </summary>
        /// <param name="m"></param>
        /// <param name="effEnd"></param>
        /// <param name="end"></param>
        /// <param name="bandE"></param>
        /// <param name="bandLogE"></param>
        /// <param name="C"></param>
        internal static void amp2Log2(CeltMode m, int effEnd, int end,
              int[] bandE, int[] bandLogE, int bandLogE_ptr, int C)
        {
            int c, i;
            c = 0;
            do
            {
                for (i = 0; i < effEnd; i++)
                {
                    bandLogE[bandLogE_ptr + (c * m.nbEBands) + i] =
                       (Inlines.celt_log2(Inlines.SHL32(bandE[i + c * m.nbEBands], 2))
                       - Inlines.SHL16((int)Tables.eMeans[i], 6));
                }
                for (i = effEnd; i < end; i++)
                {
                    bandLogE[bandLogE_ptr + (c * m.nbEBands) + i] = (0 - ((short)(0.5 + (14.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(14.0f, CeltConstants.DB_SHIFT)*/);
                }
            } while (++c < C);
        }
    }
}
