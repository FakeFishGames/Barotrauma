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

    internal static class Bands
    {
        internal static int hysteresis_decision(
            int val,
            int[] thresholds,
            int[] hysteresis,
            int N,
            int prev)
        {
            int i;
            for (i = 0; i < N; i++)
            {
                if (val < thresholds[i])
                    break;
            }

            if (i > prev && val < thresholds[prev] + hysteresis[prev])
            {
                i = prev;
            }

            if (i < prev && val > thresholds[prev - 1] - hysteresis[prev - 1])
            {
                i = prev;
            }

            return i;
        }

        internal static uint celt_lcg_rand(uint seed)
        {
            return unchecked(1664525 * seed + 1013904223);
        }

        /* This is a cos() approximation designed to be bit-exact on any platform. Bit exactness
           with this approximation is important because it has an impact on the bit allocation */
        internal static int bitexact_cos(int x)
        {
            int tmp;
            int x2;
            tmp = (4096 + ((int)(x) * (x))) >> 13;
            Inlines.OpusAssert(tmp <= 32767);
            x2 = (tmp);
            x2 = ((32767 - x2) + Inlines.FRAC_MUL16(x2, (-7651 + Inlines.FRAC_MUL16(x2, (8277 + Inlines.FRAC_MUL16(-626, x2))))));
            Inlines.OpusAssert(x2 <= 32766);
            return (1 + x2);
        }

        internal static int bitexact_log2tan(int isin, int icos)
        {
            int lc = Inlines.EC_ILOG((uint)icos);
            int ls = Inlines.EC_ILOG((uint)isin);
            icos <<= 15 - lc;
            isin <<= 15 - ls;
            return (ls - lc) * (1 << 11)
                  + Inlines.FRAC_MUL16(isin, Inlines.FRAC_MUL16(isin, -2597) + 7932)
                  - Inlines.FRAC_MUL16(icos, Inlines.FRAC_MUL16(icos, -2597) + 7932);
        }

        /* Compute the amplitude (sqrt energy) in each of the bands */
        internal static void compute_band_energies(CeltMode m, int[][] X, int[][] bandE, int end, int C, int LM)
        {
            int i, c, N;
            short[] eBands = m.eBands;
            N = m.shortMdctSize << LM;
            c = 0;

            do
            {
                for (i = 0; i < end; i++)
                {
                    int j;
                    int maxval = 0;
                    int sum = 0;
                    maxval = Inlines.celt_maxabs32(X[c], (eBands[i] << LM), (eBands[i + 1] - eBands[i]) << LM);
                    if (maxval > 0)
                    {
                        int shift = Inlines.celt_ilog2(maxval) - 14 + (((m.logN[i] >> EntropyCoder.BITRES) + LM + 1) >> 1);
                        j = eBands[i] << LM;
                        if (shift > 0)
                        {
                            do
                            {
                                sum = Inlines.MAC16_16(sum, Inlines.EXTRACT16(Inlines.SHR32(X[c][j], shift)),
                                       Inlines.EXTRACT16(Inlines.SHR32(X[c][j], shift)));
                            } while (++j < eBands[i + 1] << LM);
                        }
                        else {
                            do
                            {
                                sum = Inlines.MAC16_16(sum, Inlines.EXTRACT16(Inlines.SHL32(X[c][j], -shift)),
                                       Inlines.EXTRACT16(Inlines.SHL32(X[c][j], -shift)));
                            } while (++j < eBands[i + 1] << LM);
                        }
                        /* We're adding one here to ensure the normalized band isn't larger than unity norm */
                        bandE[c][i] = CeltConstants.EPSILON + Inlines.VSHR32(Inlines.celt_sqrt(sum), -shift);
                    }
                    else {
                        bandE[c][i] = CeltConstants.EPSILON;
                    }
                    /*printf ("%f ", bandE[i+c*m->nbEBands]);*/
                }
            } while (++c < C);
        }

        /* Normalise each band such that the energy is one. */
        internal static void normalise_bands(CeltMode m, int[][] freq, int[][] X, int[][] bandE, int end, int C, int M)
        {
            int i, c;
            short[] eBands = m.eBands;
            c = 0;
            do
            {
                i = 0;
                do
                {
                    int g;
                    int j, shift;
                    int E;
                    shift = Inlines.celt_zlog2(bandE[c][i]) - 13;
                    E = Inlines.VSHR32(bandE[c][i], shift);
                    g = Inlines.EXTRACT16(Inlines.celt_rcp(Inlines.SHL32(E, 3)));
                    j = M * eBands[i]; do
                    {
                        X[c][j] = Inlines.MULT16_16_Q15(Inlines.VSHR32(freq[c][j], shift - 1), g);
                    } while (++j < M * eBands[i + 1]);
                } while (++i < end);
            } while (++c < C);
        }

        /* De-normalise the energy to produce the synthesis from the unit-energy bands */
        internal static void denormalise_bands(CeltMode m, int[] X,
              int[] freq, int freq_ptr, int[] bandLogE, int bandLogE_ptr, int start,
              int end, int M, int downsample, int silence)
        {
            int i, N;
            int bound;
            int f;
            int x;
            short[] eBands = m.eBands;
            N = M * m.shortMdctSize;
            bound = M * eBands[end];
            if (downsample != 1)
                bound = Inlines.IMIN(bound, N / downsample);
            if (silence != 0)
            {
                bound = 0;
                start = end = 0;
            }
            f = freq_ptr;
            x = M * eBands[start];

            for (i = 0; i < M * eBands[start]; i++)
            {
                freq[f++] = 0;
            }

            for (i = start; i < end; i++)
            {
                int j, band_end;
                int g;
                int lg;
                int shift;

                j = M * eBands[i];
                band_end = M * eBands[i + 1];
                lg = Inlines.ADD16(bandLogE[bandLogE_ptr + i], Inlines.SHL16(Tables.eMeans[i], 6));

                /* Handle the integer part of the log energy */
                shift = 16 - (lg >> CeltConstants.DB_SHIFT);
                if (shift > 31)
                {
                    shift = 0;
                    g = 0;
                }
                else {
                    /* Handle the fractional part. */
                    g = Inlines.celt_exp2_frac(lg & ((1 << CeltConstants.DB_SHIFT) - 1));
                }
                /* Handle extreme gains with negative shift. */
                if (shift < 0)
                {
                    /* For shift < -2 we'd be likely to overflow, so we're capping
                          the gain here. This shouldn't happen unless the bitstream is
                          already corrupted. */
                    if (shift < -2)
                    {
                        g = 32767;
                        shift = -2;
                    }
                    do
                    {
                        freq[f] = Inlines.SHR32(Inlines.MULT16_16(X[x], g), -shift);
                    } while (++j < band_end);
                }
                else
                {
                    do
                    {
                        freq[f++] = Inlines.SHR32(Inlines.MULT16_16(X[x++], g), shift);
                    } while (++j < band_end);
                }
            }

            Inlines.OpusAssert(start <= end);
            Arrays.MemSetWithOffset<int>(freq, 0, freq_ptr + bound, N - bound);
        }

        /* This prevents energy collapse for transients with multiple short MDCTs */
        internal static void anti_collapse(CeltMode m, int[][] X_, byte[] collapse_masks, int LM, int C, int size,
              int start, int end, int[] logE, int[] prev1logE,
              int[] prev2logE, int[] pulses, uint seed)
        {
            int c, i, j, k;
            for (i = start; i < end; i++)
            {
                int N0;
                int thresh, sqrt_1;
                int depth;
                int shift;
                int thresh32;

                N0 = m.eBands[i + 1] - m.eBands[i];
                /* depth in 1/8 bits */
                Inlines.OpusAssert(pulses[i] >= 0);
                depth = Inlines.celt_udiv(1 + pulses[i], (m.eBands[i + 1] - m.eBands[i])) >> LM;

                thresh32 = Inlines.SHR32(Inlines.celt_exp2((0 - Inlines.SHL16((depth), 10 - EntropyCoder.BITRES))), 1);
                thresh = (Inlines.MULT16_32_Q15(((short)(0.5 + (0.5f) * (((int)1) << (15))))/*Inlines.QCONST16(0.5f, 15)*/, Inlines.MIN32(32767, thresh32)));
                {
                    int t;
                    t = N0 << LM;
                    shift = Inlines.celt_ilog2(t) >> 1;
                    t = Inlines.SHL32(t, (7 - shift) << 1);
                    sqrt_1 = Inlines.celt_rsqrt_norm(t);
                }

                c = 0; do
                {
                    int X;
                    int prev1;
                    int prev2;
                    int Ediff;
                    int r;
                    int renormalize = 0;
                    prev1 = prev1logE[c * m.nbEBands + i];
                    prev2 = prev2logE[c * m.nbEBands + i];
                    if (C == 1)
                    {
                        prev1 = Inlines.MAX16(prev1, prev1logE[m.nbEBands + i]);
                        prev2 = Inlines.MAX16(prev2, prev2logE[m.nbEBands + i]);
                    }
                    Ediff = Inlines.EXTEND32(logE[c * m.nbEBands + i]) - Inlines.EXTEND32(Inlines.MIN16(prev1, prev2));
                    Ediff = Inlines.MAX32(0, Ediff);

                    if (Ediff < 16384)
                    {
                        int r32 = Inlines.SHR32(Inlines.celt_exp2((short)(0 - Inlines.EXTRACT16(Ediff))), 1);
                        r = (2 * Inlines.MIN16(16383, (r32)));
                    }
                    else {
                        r = 0;
                    }
                    if (LM == 3)
                        r = Inlines.MULT16_16_Q14(23170, Inlines.MIN32(23169, r)); // opus bug: was MIN32
                    r = Inlines.SHR16(Inlines.MIN16(thresh, r), 1);
                    r = (Inlines.SHR32(Inlines.MULT16_16_Q15(sqrt_1, r), shift));

                    X = m.eBands[i] << LM;
                    for (k = 0; k < 1 << LM; k++)
                    {
                        /* Detect collapse */
                        if ((collapse_masks[i * C + c] & 1 << k) == 0)
                        {
                            /* Fill with noise */
                            int Xk = X + k;
                            for (j = 0; j < N0; j++)
                            {
                                seed = celt_lcg_rand(seed);
                                X_[c][Xk + (j << LM)] = ((seed & 0x8000) != 0 ? r : 0 - r);
                            }
                            renormalize = 1;
                        }
                    }
                    /* We just added some energy, so we need to renormalise */
                    if (renormalize != 0)
                    {
                        VQ.renormalise_vector(X_[c], X, N0 << LM, CeltConstants.Q15ONE);
                    }
                } while (++c < C);
            }
        }

        internal static void intensity_stereo(CeltMode m, int[] X, int X_ptr, int[] Y, int Y_ptr, int[][] bandE, int bandID, int N)
        {
            int i = bandID;
            int j;
            int a1, a2;
            int left, right;
            int norm;
            int shift = Inlines.celt_zlog2(Inlines.MAX32(bandE[0][i], bandE[1][i])) - 13;
            left = Inlines.VSHR32(bandE[0][i], shift);
            right = Inlines.VSHR32(bandE[1][i], shift);
            norm = CeltConstants.EPSILON + Inlines.celt_sqrt(CeltConstants.EPSILON + Inlines.MULT16_16(left, left) + Inlines.MULT16_16(right, right));
            a1 = Inlines.DIV32_16(Inlines.SHL32(left, 14), norm);
            a2 = Inlines.DIV32_16(Inlines.SHL32(right, 14), norm);
            for (j = 0; j < N; j++)
            {
                int r, l;
                l = X[X_ptr + j];
                r = Y[Y_ptr + j];
                X[X_ptr + j] = Inlines.EXTRACT16(Inlines.SHR32(Inlines.MAC16_16(Inlines.MULT16_16(a1, l), a2, r), 14));
                /* Side is not encoded, no need to calculate */
            }
        }

        static void stereo_split(int[] X, int X_ptr, int[] Y, int Y_ptr, int N)
        {
            int j;
            for (j = 0; j < N; j++)
            {
                int r, l;
                l = Inlines.MULT16_16(((short)(0.5 + (.70710678f) * (((int)1) << (15))))/*Inlines.QCONST16(.70710678f, 15)*/, X[X_ptr + j]);
                r = Inlines.MULT16_16(((short)(0.5 + (.70710678f) * (((int)1) << (15))))/*Inlines.QCONST16(.70710678f, 15)*/, Y[Y_ptr + j]);
                X[X_ptr + j] = Inlines.EXTRACT16(Inlines.SHR32(Inlines.ADD32(l, r), 15));
                Y[Y_ptr + j] = Inlines.EXTRACT16(Inlines.SHR32(Inlines.SUB32(r, l), 15));
            }
        }

        static void stereo_merge(int[] X, int X_ptr, int[] Y, int Y_ptr, int mid, int N)
        {
            int j;
            int xp, side;
            int El, Er;
            int mid2;
            int kl, kr;
            int t, lgain, rgain;

            /* Compute the norm of X+Y and X-Y as |X|^2 + |Y|^2 +/- sum(xy) */
#if UNSAFE
            unsafe
            {
                fixed (int* py_base = Y, px_base = X)
                {
                    int* py = py_base + Y_ptr;
                    int* px = px_base + X_ptr;
                    Kernels.dual_inner_prod(py, px, py, N, out xp, out side);
                }
            }
#else
            Kernels.dual_inner_prod(Y, Y_ptr, X, X_ptr, Y, Y_ptr, N, out xp, out side);
#endif
            /* Compensating for the mid normalization */
            xp = Inlines.MULT16_32_Q15(mid, xp);
            /* mid and side are in Q15, not Q14 like X and Y */
            mid2 = Inlines.SHR16(mid, 1);
            El = Inlines.MULT16_16(mid2, mid2) + side - (2 * xp);
            Er = Inlines.MULT16_16(mid2, mid2) + side + (2 * xp);
            if (Er < ((int)(0.5 + (6e-4f) * (((int)1) << (28))))/*Inlines.QCONST32(6e-4f, 28)*/ || El < ((int)(0.5 + (6e-4f) * (((int)1) << (28))))/*Inlines.QCONST32(6e-4f, 28)*/)
            {
                Array.Copy(X, X_ptr, Y, Y_ptr, N);
                return;
            }

            kl = Inlines.celt_ilog2(El) >> 1;
            kr = Inlines.celt_ilog2(Er) >> 1;
            t = Inlines.VSHR32(El, (kl - 7) << 1);
            lgain = Inlines.celt_rsqrt_norm(t);
            t = Inlines.VSHR32(Er, (kr - 7) << 1);
            rgain = Inlines.celt_rsqrt_norm(t);

            if (kl < 7)
                kl = 7;
            if (kr < 7)
                kr = 7;

            for (j = 0; j < N; j++)
            {
                int r, l;
                /* Apply mid scaling (side is already scaled) */
                l = Inlines.MULT16_16_P15(mid, X[X_ptr + j]);
                r = Y[Y_ptr + j];
                X[X_ptr + j] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MULT16_16(lgain, Inlines.SUB16(l, r)), kl + 1));
                Y[Y_ptr + j] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MULT16_16(rgain, Inlines.ADD16(l, r)), kr + 1));
            }
        }

        /* Decide whether we should spread the pulses in the current frame */
        internal static int spreading_decision(CeltMode m, int[][] X, ref int average,
              int last_decision, ref int hf_average, ref int tapset_decision, int update_hf,
              int end, int C, int M)
        {
            int i, c;
            int sum = 0, nbBands = 0;
            short[] eBands = m.eBands;
            int decision;
            int hf_sum = 0;

            Inlines.OpusAssert(end > 0);

            if (M * (eBands[end] - eBands[end - 1]) <= 8)
            {
                return Spread.SPREAD_NONE;
            }

            c = 0;

            do
            {
                for (i = 0; i < end; i++)
                {
                    int j, N, tmp = 0;
                    int[] tcount = { 0, 0, 0 };
                    int[] x = X[c];
                    int x_ptr = M * eBands[i];
                    N = M * (eBands[i + 1] - eBands[i]);
                    if (N <= 8)
                        continue;
                    /* Compute rough CDF of |x[j]| */
                    for (j = x_ptr; j < N + x_ptr; j++)
                    {
                        int x2N; /* Q13 */

                        x2N = Inlines.MULT16_16(Inlines.MULT16_16_Q15(x[j], x[j]), N);
                        if (x2N < ((short)(0.5 + (0.25f) * (((int)1) << (13))))/*Inlines.QCONST16(0.25f, 13)*/)
                            tcount[0]++;
                        if (x2N < ((short)(0.5 + (0.0625f) * (((int)1) << (13))))/*Inlines.QCONST16(0.0625f, 13)*/)
                            tcount[1]++;
                        if (x2N < ((short)(0.5 + (0.015625f) * (((int)1) << (13))))/*Inlines.QCONST16(0.015625f, 13)*/)
                            tcount[2]++;
                    }

                    /* Only include four last bands (8 kHz and up) */
                    if (i > m.nbEBands - 4)
                    {
                        hf_sum += Inlines.celt_udiv(32 * (tcount[1] + tcount[0]), N);
                    }

                    tmp = (2 * tcount[2] >= N ? 1 : 0) + (2 * tcount[1] >= N ? 1 : 0) + (2 * tcount[0] >= N ? 1 : 0);
                    sum += tmp * 256;
                    nbBands++;
                }
            } while (++c < C);

            if (update_hf != 0)
            {
                if (hf_sum != 0)
                {
                    hf_sum = Inlines.celt_udiv(hf_sum, C * (4 - m.nbEBands + end));
                }

                hf_average = (hf_average + hf_sum) >> 1;
                hf_sum = hf_average;

                if (tapset_decision == 2)
                {
                    hf_sum += 4;
                }
                else if (tapset_decision == 0)
                {
                    hf_sum -= 4;
                }
                if (hf_sum > 22)
                {
                    tapset_decision = 2;
                }
                else if (hf_sum > 18)
                {
                    tapset_decision = 1;
                }
                else
                {
                    tapset_decision = 0;
                }
            }

            Inlines.OpusAssert(nbBands > 0); /* end has to be non-zero */
            Inlines.OpusAssert(sum >= 0);
            sum = Inlines.celt_udiv(sum, nbBands);

            /* Recursive averaging */
            sum = (sum + average) >> 1;
            average = sum;

            /* Hysteresis */
            sum = (3 * sum + (((3 - last_decision) << 7) + 64) + 2) >> 2;
            if (sum < 80)
            {
                decision = Spread.SPREAD_AGGRESSIVE;
            }
            else if (sum < 256)
            {
                decision = Spread.SPREAD_NORMAL;
            }
            else if (sum < 384)
            {
                decision = Spread.SPREAD_LIGHT;
            }
            else {
                decision = Spread.SPREAD_NONE;
            }
#if FUZZING
            decision = new Random().Next() & 0x3;
            tapset_decision.Val = new Random().Next() % 3;
#endif
            return decision;
        }

        internal static void deinterleave_hadamard(int[] X, int X_ptr, int N0, int stride, int hadamard)
        {
            int i, j;
            int N;
            N = N0 * stride;
            int[] tmp = new int[N];

            Inlines.OpusAssert(stride > 0);
            if (hadamard != 0)
            {
                int ordery = (stride - 2);

                for (i = 0; i < stride; i++)
                {
                    for (j = 0; j < N0; j++)
                    {
                        tmp[Tables.ordery_table[ordery + i] * N0 + j] = X[j * stride + i + X_ptr];
                    }
                }
            }
            else
            {
                for (i = 0; i < stride; i++)
                {
                    for (j = 0; j < N0; j++)
                    {
                        tmp[i * N0 + j] = X[j * stride + i + X_ptr];
                    }
                }
            }

            Array.Copy(tmp, 0, X, X_ptr, N);
        }

        internal static void interleave_hadamard(int[] X, int X_ptr, int N0, int stride, int hadamard)
        {
            int i, j;
            int N;
            N = N0 * stride;
            int[] tmp = new int[N];

            if (hadamard != 0)
            {
                int ordery = (stride - 2);
                for (i = 0; i < stride; i++)
                {
                    for (j = 0; j < N0; j++)
                    {
                        tmp[j * stride + i] = X[Tables.ordery_table[ordery + i] * N0 + j + X_ptr];
                    }
                }
            }
            else
            {
                for (i = 0; i < stride; i++)
                {
                    for (j = 0; j < N0; j++)
                    {
                        tmp[j * stride + i] = X[i * N0 + j + X_ptr];
                    }
                }
            }

            Array.Copy(tmp, 0, X, X_ptr, N);
        }

        internal static void haar1(int[] X, int X_ptr, int N0, int stride)
        {
            int i, j;
            N0 >>= 1;
            for (i = 0; i < stride; i++)
            {
                for (j = 0; j < N0; j++)
                {
                    int tmpidx = X_ptr + i + (stride * 2 * j);
                    int tmp1, tmp2;
                    tmp1 = Inlines.MULT16_16(((short)(0.5 + (.70710678f) * (((int)1) << (15))))/*Inlines.QCONST16(.70710678f, 15)*/, X[tmpidx]);
                    tmp2 = Inlines.MULT16_16(((short)(0.5 + (.70710678f) * (((int)1) << (15))))/*Inlines.QCONST16(.70710678f, 15)*/, X[tmpidx + stride]);
                    X[tmpidx] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.ADD32(tmp1, tmp2), 15));
                    X[tmpidx + stride] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.SUB32(tmp1, tmp2), 15));
                }
            }
        }

        internal static void haar1ZeroOffset(int[] X, int N0, int stride)
        {
            int i, j;
            N0 >>= 1;
            for (i = 0; i < stride; i++)
            {
                for (j = 0; j < N0; j++)
                {
                    int tmpidx = i + (stride * 2 * j);
                    int tmp1, tmp2;
                    tmp1 = Inlines.MULT16_16(((short)(0.5 + (.70710678f) * (((int)1) << (15))))/*Inlines.QCONST16(.70710678f, 15)*/, X[tmpidx]);
                    tmp2 = Inlines.MULT16_16(((short)(0.5 + (.70710678f) * (((int)1) << (15))))/*Inlines.QCONST16(.70710678f, 15)*/, X[tmpidx + stride]);
                    X[tmpidx] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.ADD32(tmp1, tmp2), 15));
                    X[tmpidx + stride] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.SUB32(tmp1, tmp2), 15));
                }
            }
        }

        internal static int compute_qn(int N, int b, int offset, int pulse_cap, int stereo)
        {
            short[] exp2_table8 =
               {16384, 17866, 19483, 21247, 23170, 25267, 27554, 30048};
            int qn, qb;
            int N2 = 2 * N - 1;
            if (stereo != 0 && N == 2)
            {
                N2--;
            }

            /* The upper limit ensures that in a stereo split with itheta==16384, we'll
                always have enough bits left over to code at least one pulse in the
                side; otherwise it would collapse, since it doesn't get folded. */

            qb = Inlines.celt_sudiv(b + N2 * offset, N2);
            qb = Inlines.IMIN(b - pulse_cap - (4 << EntropyCoder.BITRES), qb);

            qb = Inlines.IMIN(8 << EntropyCoder.BITRES, qb);

            if (qb < (1 << EntropyCoder.BITRES >> 1))
            {
                qn = 1;
            }
            else {
                qn = exp2_table8[qb & 0x7] >> (14 - (qb >> EntropyCoder.BITRES));
                qn = (qn + 1) >> 1 << 1;
            }
            Inlines.OpusAssert(qn <= 256);
            return qn;
        }

        public class band_ctx
        {
            public int encode;
            public CeltMode m;
            public int i;
            public int intensity;
            public int spread;
            public int tf_change;
            public EntropyCoder ec;
            public int remaining_bits;
            public int[][] bandE;
            public uint seed;
        };

        public class split_ctx
        {
            public int inv;
            public int imid;
            public int iside;
            public int delta;
            public int itheta;
            public int qalloc;
        };

        internal static void compute_theta(band_ctx ctx, split_ctx sctx,
               int[] X, int X_ptr, int[] Y, int Y_ptr, int N, ref int b, int B, int B0,
              int LM,
              int stereo, ref int fill)
        {
            int qn;
            int itheta = 0;
            int delta;
            int imid, iside;
            int qalloc;
            int pulse_cap;
            int offset;
            int tell;
            int inv = 0;
            int encode;
            CeltMode m;
            int i;
            int intensity;
            EntropyCoder ec; // porting note: pointer
            int[][] bandE;

            encode = ctx.encode;
            m = ctx.m;
            i = ctx.i;
            intensity = ctx.intensity;
            ec = ctx.ec;
            bandE = ctx.bandE;

            /* Decide on the resolution to give to the split parameter theta */
            pulse_cap = m.logN[i] + LM * (1 << EntropyCoder.BITRES);
            offset = (pulse_cap >> 1) - (stereo != 0 && N == 2 ? CeltConstants.QTHETA_OFFSET_TWOPHASE : CeltConstants.QTHETA_OFFSET);
            qn = compute_qn(N, b, offset, pulse_cap, stereo);
            if (stereo != 0 && i >= intensity)
            {
                qn = 1;
            }

            if (encode != 0)
            {
                /* theta is the atan() of the ratio between the (normalized)
                   side and mid. With just that parameter, we can re-scale both
                   mid and side because we know that 1) they have unit norm and
                   2) they are orthogonal. */
                itheta = VQ.stereo_itheta(X, X_ptr, Y, Y_ptr, stereo, N);
            }

            tell = (int)ec.tell_frac();

            if (qn != 1)
            {
                if (encode != 0)
                {
                    itheta = (itheta * qn + 8192) >> 14;
                }

                /* Entropy coding of the angle. We use a uniform pdf for the
                   time split, a step for stereo, and a triangular one for the rest. */
                if (stereo != 0 && N > 2)
                {
                    int p0 = 3;
                    int x = itheta;
                    int x0 = qn / 2;
                    uint ft = (uint)(p0 * (x0 + 1) + x0);
                    /* Use a probability of p0 up to itheta=8192 and then use 1 after */
                    if (encode != 0)
                    {
                        ec.encode(
                            (uint)(x <= x0 ?
                                (p0 * x) :
                                ((x - 1 - x0) + (x0 + 1) * p0)),
                            (uint)(x <= x0 ?
                                (p0 * (x + 1)) :
                                ((x - x0) + (x0 + 1) * p0)),
                            ft);
                    }
                    else
                    {
                        int fs;
                        fs = (int)ec.decode(ft);
                        if (fs < (x0 + 1) * p0)
                        {
                            x = fs / p0;
                        }
                        else
                        {
                            x = x0 + 1 + (fs - (x0 + 1) * p0);
                        }

                        ec.dec_update(
                            (uint)(x <= x0 ?
                                p0 * x :
                                (x - 1 - x0) + (x0 + 1) * p0),
                            (uint)(x <= x0 ?
                                p0 * (x + 1) :
                                (x - x0) + (x0 + 1) * p0),
                            ft);
                        itheta = x;
                    }
                }
                else if (B0 > 1 || stereo != 0)
                {
                    /* Uniform pdf */
                    if (encode != 0)
                    {
                        ec.enc_uint((uint)itheta, (uint)qn + 1);
                    }
                    else
                    {
                        itheta = (int)ec.dec_uint((uint)qn + 1);
                    }
                }
                else
                {
                    int fs = 1, ft;
                    ft = ((qn >> 1) + 1) * ((qn >> 1) + 1);
                    if (encode != 0)
                    {
                        int fl;

                        fs = itheta <= (qn >> 1) ? itheta + 1 : qn + 1 - itheta;
                        fl = itheta <= (qn >> 1) ? itheta * (itheta + 1) >> 1 :
                         ft - ((qn + 1 - itheta) * (qn + 2 - itheta) >> 1);

                        ec.encode((uint)fl, (uint)(fl + fs), (uint)ft);
                    }
                    else
                    {
                        /* Triangular pdf */
                        int fl = 0;
                        int fm;
                        fm = (int)ec.decode((uint)ft);

                        if (fm < ((qn >> 1) * ((qn >> 1) + 1) >> 1))
                        {
                            itheta = (int)(Inlines.isqrt32(8 * (uint)fm + 1) - 1) >> 1;
                            fs = itheta + 1;
                            fl = itheta * (itheta + 1) >> 1;
                        }
                        else
                        {
                            itheta = (int)(2 * (qn + 1) - Inlines.isqrt32(8 * (uint)(ft - fm - 1) + 1)) >> 1;
                            fs = qn + 1 - itheta;
                            fl = ft - ((qn + 1 - itheta) * (qn + 2 - itheta) >> 1);
                        }

                        ec.dec_update((uint)fl, (uint)(fl + fs), (uint)ft);
                    }
                }
                Inlines.OpusAssert(itheta >= 0);
                itheta = Inlines.celt_udiv(itheta * 16384, qn);
                if (encode != 0 && stereo != 0)
                {
                    if (itheta == 0)
                    {
                        intensity_stereo(m, X, X_ptr, Y, Y_ptr, bandE, i, N);
                    }
                    else
                    {
                        stereo_split(X, X_ptr, Y, Y_ptr, N);
                    }
                }
            }
            else if (stereo != 0)
            {
                if (encode != 0)
                {
                    inv = itheta > 8192 ? 1 : 0;
                    if (inv != 0)
                    {
                        int j;
                        for (j = 0; j < N; j++)
                            Y[Y_ptr + j] = (0 - Y[Y_ptr + j]);
                    }
                    intensity_stereo(m, X, X_ptr, Y, Y_ptr, bandE, i, N);
                }
                if (b > 2 << EntropyCoder.BITRES && ctx.remaining_bits > 2 << EntropyCoder.BITRES)
                {
                    if (encode != 0)
                    {
                        ec.enc_bit_logp(inv, 2);
                    }
                    else
                    {
                        inv = ec.dec_bit_logp(2);
                    }
                }
                else
                    inv = 0;
                itheta = 0;
            }
            qalloc = (int)ec.tell_frac() - tell;
            b -= qalloc;

            if (itheta == 0)
            {
                imid = 32767;
                iside = 0;
                fill &= (1 << B) - 1;
                delta = -16384;
            }
            else if (itheta == 16384)
            {
                imid = 0;
                iside = 32767;
                fill &= ((1 << B) - 1) << B;
                delta = 16384;
            }
            else {
                imid = bitexact_cos((short)itheta);
                iside = bitexact_cos((short)(16384 - itheta));
                /* This is the mid vs side allocation that minimizes squared error
                   in that band. */
                delta = Inlines.FRAC_MUL16((N - 1) << 7, bitexact_log2tan(iside, imid));
            }

            sctx.inv = inv;
            sctx.imid = imid;
            sctx.iside = iside;
            sctx.delta = delta;
            sctx.itheta = itheta;
            sctx.qalloc = qalloc;
        }

        internal static uint quant_band_n1(band_ctx ctx, int[] X, int X_ptr, int[] Y, int Y_ptr, int b,
                 int[] lowband_out, int lowband_out_ptr)
        {
            int resynth = ctx.encode == 0 ? 1 : 0;
            int c;
            int stereo;
            int[] x = X;
            int x_ptr = X_ptr;
            int encode;
            EntropyCoder ec; // porting note: pointer

            encode = ctx.encode;
            ec = ctx.ec;

            stereo = (Y != null) ? 1 : 0;
            c = 0;
            do
            {
                int sign = 0;
                if (ctx.remaining_bits >= 1 << EntropyCoder.BITRES)
                {
                    if (encode != 0)
                    {
                        sign = x[x_ptr] < 0 ? 1 : 0;
                        ec.enc_bits((uint)sign, 1);
                    }
                    else
                    {
                        sign = (int)ec.dec_bits(1);
                    }
                    ctx.remaining_bits -= 1 << EntropyCoder.BITRES;
                    b -= 1 << EntropyCoder.BITRES;
                }
                if (resynth != 0)
                    x[x_ptr] = sign != 0 ? 0 - CeltConstants.NORM_SCALING : CeltConstants.NORM_SCALING;
                x = Y;
                x_ptr = Y_ptr;
            } while (++c < 1 + stereo);
            if (lowband_out != null)
            {
                lowband_out[lowband_out_ptr] = Inlines.SHR16(X[X_ptr], 4);
            }

            return 1;
        }

        /* This function is responsible for encoding and decoding a mono partition.
           It can split the band in two and transmit the energy difference with
           the two half-bands. It can be called recursively so bands can end up being
           split in 8 parts. */
        internal static uint quant_partition(band_ctx ctx, int[] X, int X_ptr,
      int N, int b, int B, int[] lowband, int lowband_ptr,
      int LM,
      int gain, int fill)
        {
            int cache_ptr;
            int q;
            int curr_bits;
            int imid = 0, iside = 0;
            int B0 = B;
            int mid = 0, side = 0;
            uint cm = 0;
            int resynth = (ctx.encode == 0) ? 1 : 0;
            int Y = 0;
            int encode;
            CeltMode m; //porting note: pointer
            int i;
            int spread;
            EntropyCoder ec; //porting note: pointer

            encode = ctx.encode;
            m = ctx.m;
            i = ctx.i;
            spread = ctx.spread;
            ec = ctx.ec;
            byte[] cache = m.cache.bits;
            /* If we need 1.5 more bits than we can produce, split the band in two. */
            cache_ptr = m.cache.index[(LM + 1) * m.nbEBands + i];
            if (LM != -1 && b > cache[cache_ptr + cache[cache_ptr]] + 12 && N > 2)
            {
                int mbits, sbits, delta;
                int itheta;
                int qalloc;
                split_ctx sctx = new split_ctx();
                int next_lowband2 = 0;
                int rebalance;

                N >>= 1;
                Y = X_ptr + N;
                LM -= 1;
                if (B == 1)
                {
                    fill = (fill & 1) | (fill << 1);
                }

                B = (B + 1) >> 1;
                
                compute_theta(ctx, sctx, X, X_ptr, X, Y, N, ref b, B, B0, LM, 0, ref fill);

                imid = sctx.imid;
                iside = sctx.iside;
                delta = sctx.delta;
                itheta = sctx.itheta;
                qalloc = sctx.qalloc;
                mid = (imid);
                side = (iside);

                /* Give more bits to low-energy MDCTs than they would otherwise deserve */
                if (B0 > 1 && ((itheta & 0x3fff) != 0))
                {
                    if (itheta > 8192)
                        /* Rough approximation for pre-echo masking */
                        delta -= delta >> (4 - LM);
                    else
                        /* Corresponds to a forward-masking slope of 1.5 dB per 10 ms */
                        delta = Inlines.IMIN(0, delta + (N << EntropyCoder.BITRES >> (5 - LM)));
                }
                mbits = Inlines.IMAX(0, Inlines.IMIN(b, (b - delta) / 2));
                sbits = b - mbits;
                ctx.remaining_bits -= qalloc;

                if (lowband != null)
                {
                    next_lowband2 = (lowband_ptr + N); /* >32-bit split case */
                }

                rebalance = ctx.remaining_bits;
                if (mbits >= sbits)
                {
                    cm = quant_partition(ctx, X, X_ptr, N, mbits, B,
                          lowband, lowband_ptr, LM,
                          Inlines.MULT16_16_P15(gain, mid), fill);
                    rebalance = mbits - (rebalance - ctx.remaining_bits);
                    if (rebalance > 3 << EntropyCoder.BITRES && itheta != 0)
                        sbits += rebalance - (3 << EntropyCoder.BITRES);
                    cm |= quant_partition(ctx, X, Y, N, sbits, B,
                          lowband, next_lowband2, LM,
                          Inlines.MULT16_16_P15(gain, side), fill >> B) << (B0 >> 1);
                }
                else {
                    cm = quant_partition(ctx, X, Y, N, sbits, B,
                          lowband, next_lowband2, LM,
                          Inlines.MULT16_16_P15(gain, side), fill >> B) << (B0 >> 1);
                    rebalance = sbits - (rebalance - ctx.remaining_bits);
                    if (rebalance > 3 << EntropyCoder.BITRES && itheta != 16384)
                        mbits += rebalance - (3 << EntropyCoder.BITRES);
                    cm |= quant_partition(ctx, X, X_ptr, N, mbits, B,
                          lowband, lowband_ptr, LM,
                          Inlines.MULT16_16_P15(gain, mid), fill);
                }
            }
            else {
                /* This is the basic no-split case */
                q = Rate.bits2pulses(m, i, LM, b);
                curr_bits = Rate.pulses2bits(m, i, LM, q);
                ctx.remaining_bits -= curr_bits;

                /* Ensures we can never bust the budget */
                while (ctx.remaining_bits < 0 && q > 0)
                {
                    ctx.remaining_bits += curr_bits;
                    q--;
                    curr_bits = Rate.pulses2bits(m, i, LM, q);
                    ctx.remaining_bits -= curr_bits;
                }

                if (q != 0)
                {
                    int K = Rate.get_pulses(q);

                    /* Finally do the actual quantization */
                    if (encode != 0)
                    {
                        cm = VQ.alg_quant(X, X_ptr, N, K, spread, B, ec);
                    }
                    else {
                        cm = VQ.alg_unquant(X, X_ptr, N, K, spread, B, ec, gain);
                    }
                }
                else
                {
                    /* If there's no pulse, fill the band anyway */
                    int j;

                    if (resynth != 0)
                    {
                        uint cm_mask;
                        /* B can be as large as 16, so this shift might overflow an int on a
                           16-bit platform; use a long to get defined behavior.*/
                        cm_mask = (uint)(1UL << B) - 1;
                        fill &= (int)cm_mask;

                        if (fill == 0)
                        {
                            Arrays.MemSetWithOffset<int>(X, 0, X_ptr, N);
                        }
                        else
                        {
                            if (lowband == null)
                            {
                                /* Noise */
                                for (j = 0; j < N; j++)
                                {
                                    ctx.seed = celt_lcg_rand(ctx.seed);
                                    X[X_ptr + j] = unchecked(unchecked((int)ctx.seed) >> 20);
                                }
                                cm = cm_mask;
                            }
                            else
                            {
                                /* Folded spectrum */
                                for (j = 0; j < N; j++)
                                {
                                    int tmp;
                                    ctx.seed = celt_lcg_rand(ctx.seed);
                                    /* About 48 dB below the "normal" folding level */
                                    tmp = ((short)(0.5 + (1.0f / 256) * (((int)1) << (10))))/*Inlines.QCONST16(1.0f / 256, 10)*/;
                                    tmp = (((ctx.seed) & 0x8000) != 0 ? tmp : 0 - tmp);
                                    X[X_ptr + j] = (lowband[lowband_ptr + j] + tmp);
                                }
                                cm = (uint)fill;
                            }

                            VQ.renormalise_vector(X, X_ptr, N, gain);
                        }
                    }
                }
            }

            return cm;
        }

        private static readonly byte[] bit_interleave_table = { 0, 1, 1, 1, 2, 3, 3, 3, 2, 3, 3, 3, 2, 3, 3, 3 };

        private static readonly byte[] bit_deinterleave_table ={
                           0x00,0x03,0x0C,0x0F,0x30,0x33,0x3C,0x3F,
                           0xC0,0xC3,0xCC,0xCF,0xF0,0xF3,0xFC,0xFF
                     };

        /* This function is responsible for encoding and decoding a band for the mono case. */
        internal static uint quant_band(band_ctx ctx, int[] X, int X_ptr,
              int N, int b, int B, int[] lowband, int lowband_ptr,
              int LM, int[] lowband_out, int lowband_out_ptr,
              int gain, int[] lowband_scratch, int lowband_scratch_ptr, int fill)
        {
            int N0 = N;
            int N_B = N;
            int N_B0;
            int B0 = B;
            int time_divide = 0;
            int recombine = 0;
            int longBlocks;
            uint cm = 0;
            int resynth = ctx.encode == 0 ? 1 : 0;
            int k;
            int encode;
            int tf_change;

            encode = ctx.encode;
            tf_change = ctx.tf_change;

            longBlocks = B0 == 1 ? 1 : 0;

            N_B = Inlines.celt_udiv(N_B, B);

            /* Special case for one sample */
            if (N == 1)
            {
                return quant_band_n1(ctx, X, X_ptr, null, 0, b, lowband_out, lowband_out_ptr);
            }

            if (tf_change > 0)
                recombine = tf_change;
            /* Band recombining to increase frequency resolution */

            if (lowband_scratch != null && lowband != null && (recombine != 0 || ((N_B & 1) == 0 && tf_change < 0) || B0 > 1))
            {
                Array.Copy(lowband, lowband_ptr, lowband_scratch, lowband_scratch_ptr, N);
                lowband = lowband_scratch;
                lowband_ptr = lowband_scratch_ptr;
            }

            for (k = 0; k < recombine; k++)
            {
                if (encode != 0)
                    haar1(X, X_ptr, N >> k, 1 << k);
                if (lowband != null)
                    haar1(lowband, lowband_ptr, N >> k, 1 << k);
                fill = bit_interleave_table[fill & 0xF] | bit_interleave_table[fill >> 4] << 2;
            }
            B >>= recombine;
            N_B <<= recombine;

            /* Increasing the time resolution */
            while ((N_B & 1) == 0 && tf_change < 0)
            {
                if (encode != 0)
                    haar1(X, X_ptr, N_B, B);
                if (lowband != null)
                    haar1(lowband, lowband_ptr, N_B, B);
                fill |= fill << B;
                B <<= 1;
                N_B >>= 1;
                time_divide++;
                tf_change++;
            }
            B0 = B;
            N_B0 = N_B;

            /* Reorganize the samples in time order instead of frequency order */
            if (B0 > 1)
            {
                if (encode != 0)
                    deinterleave_hadamard(X, X_ptr, N_B >> recombine, B0 << recombine, longBlocks);
                if (lowband != null)
                    deinterleave_hadamard(lowband, lowband_ptr, N_B >> recombine, B0 << recombine, longBlocks);
            }

            cm = quant_partition(ctx, X, X_ptr, N, b, B, lowband, lowband_ptr, LM, gain, fill);

            /* This code is used by the decoder and by the resynthesis-enabled encoder */
            if (resynth != 0)
            {
                /* Undo the sample reorganization going from time order to frequency order */
                if (B0 > 1)
                    interleave_hadamard(X, X_ptr, N_B >> recombine, B0 << recombine, longBlocks);

                /* Undo time-freq changes that we did earlier */
                N_B = N_B0;
                B = B0;
                for (k = 0; k < time_divide; k++)
                {
                    B >>= 1;
                    N_B <<= 1;
                    cm |= cm >> B;
                    haar1(X, X_ptr, N_B, B);
                }

                for (k = 0; k < recombine; k++)
                {
                    cm = bit_deinterleave_table[cm];
                    haar1(X, X_ptr, N0 >> k, 1 << k);
                }
                B <<= recombine;

                /* Scale output for later folding */
                if (lowband_out != null)
                {
                    int j;
                    int n;
                    n = (Inlines.celt_sqrt(Inlines.SHL32(N0, 22)));
                    for (j = 0; j < N0; j++)
                        lowband_out[lowband_out_ptr + j] = Inlines.MULT16_16_Q15(n, X[X_ptr + j]);
                }

                cm = cm & (uint)((1 << B) - 1);
            }
            return cm;
        }

        /* This function is responsible for encoding and decoding a band for the stereo case. */
        internal static uint quant_band_stereo(band_ctx ctx, int[] X, int X_ptr, int[] Y, int Y_ptr,
              int N, int b, int B, int[] lowband, int lowband_ptr,
              int LM, int[] lowband_out, int lowband_out_ptr,
              int[] lowband_scratch, int lowband_scratch_ptr, int fill)
        {
            int imid = 0, iside = 0;
            int inv = 0;
            int mid = 0, side = 0;
            uint cm = 0;
            int resynth = ctx.encode == 0 ? 1 : 0;
            int mbits, sbits, delta;
            int itheta;
            int qalloc;
            split_ctx sctx = new split_ctx(); // porting note: stack var
            int orig_fill;
            int encode;
            EntropyCoder ec; //porting note: pointer

            encode = ctx.encode;
            ec = ctx.ec;

            /* Special case for one sample */
            if (N == 1)
            {
                return quant_band_n1(ctx, X, X_ptr, Y, Y_ptr, b, lowband_out, lowband_out_ptr);
            }

            orig_fill = fill;
            
            compute_theta(ctx, sctx, X, X_ptr, Y, Y_ptr, N, ref b, B, B, LM, 1, ref fill);

            inv = sctx.inv;
            imid = sctx.imid;
            iside = sctx.iside;
            delta = sctx.delta;
            itheta = sctx.itheta;
            qalloc = sctx.qalloc;
            mid = (imid);
            side = (iside);

            /* This is a special case for N=2 that only works for stereo and takes
               advantage of the fact that mid and side are orthogonal to encode
               the side with just one bit. */
            if (N == 2)
            {
                int c;
                int sign = 0;
                int[] x2, y2;
                int x2_ptr, y2_ptr;
                mbits = b;
                sbits = 0;
                /* Only need one bit for the side. */
                if (itheta != 0 && itheta != 16384)
                    sbits = 1 << EntropyCoder.BITRES;
                mbits -= sbits;
                c = itheta > 8192 ? 1 : 0;
                ctx.remaining_bits -= qalloc + sbits;
                if (c != 0)
                {
                    x2 = Y;
                    x2_ptr = Y_ptr;
                    y2 = X;
                    y2_ptr = X_ptr;
                }
                else
                {
                    x2 = X;
                    x2_ptr = X_ptr;
                    y2 = Y;
                    y2_ptr = Y_ptr;
                }

                if (sbits != 0)
                {
                    if (encode != 0)
                    {
                        /* Here we only need to encode a sign for the side. */
                        sign = (x2[x2_ptr] * y2[Y_ptr + 1] - x2[x2_ptr + 1] * y2[Y_ptr] < 0) ? 1 : 0;
                        ec.enc_bits((uint)sign, 1);
                    }
                    else
                    {
                        sign = (int)ec.dec_bits(1);
                    }
                }
                sign = 1 - 2 * sign;
                /* We use orig_fill here because we want to fold the side, but if
                   itheta==16384, we'll have cleared the low bits of fill. */
                cm = quant_band(ctx, x2, x2_ptr, N, mbits, B, lowband, lowband_ptr,
                      LM, lowband_out, lowband_out_ptr, CeltConstants.Q15ONE, lowband_scratch, lowband_scratch_ptr, orig_fill);

                /* We don't split N=2 bands, so cm is either 1 or 0 (for a fold-collapse),
                   and there's no need to worry about mixing with the other channel. */
                y2[Y_ptr] = ((0 - sign) * x2[x2_ptr + 1]);
                y2[Y_ptr + 1] = (sign * x2[x2_ptr]);
                if (resynth != 0)
                {
                    int tmp;
                    X[X_ptr] = Inlines.MULT16_16_Q15(mid, X[X_ptr]);
                    X[X_ptr + 1] = Inlines.MULT16_16_Q15(mid, X[X_ptr + 1]);
                    Y[Y_ptr] = Inlines.MULT16_16_Q15(side, Y[Y_ptr]);
                    Y[Y_ptr + 1] = Inlines.MULT16_16_Q15(side, Y[Y_ptr + 1]);
                    tmp = X[X_ptr];
                    X[X_ptr] = Inlines.SUB16(tmp, Y[Y_ptr]);
                    Y[Y_ptr] = Inlines.ADD16(tmp, Y[Y_ptr]);
                    tmp = X[X_ptr + 1];
                    X[X_ptr + 1] = Inlines.SUB16(tmp, Y[Y_ptr + 1]);
                    Y[Y_ptr + 1] = Inlines.ADD16(tmp, Y[Y_ptr + 1]);
                }
            }
            else
            {
                /* "Normal" split code */
                int rebalance;

                mbits = Inlines.IMAX(0, Inlines.IMIN(b, (b - delta) / 2));
                sbits = b - mbits;
                ctx.remaining_bits -= qalloc;

                rebalance = ctx.remaining_bits;
                if (mbits >= sbits)
                {
                    /* In stereo mode, we do not apply a scaling to the mid because we need the normalized
                       mid for folding later. */
                    cm = quant_band(ctx, X, X_ptr, N, mbits, B,
                          lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr,
                          CeltConstants.Q15ONE, lowband_scratch, lowband_scratch_ptr, fill);
                    rebalance = mbits - (rebalance - ctx.remaining_bits);
                    if (rebalance > 3 << EntropyCoder.BITRES && itheta != 0)
                        sbits += rebalance - (3 << EntropyCoder.BITRES);

                    /* For a stereo split, the high bits of fill are always zero, so no
                       folding will be done to the side. */
                    cm |= quant_band(ctx, Y, Y_ptr, N, sbits, B,
                          null, 0, LM, null, 0,
                          side, null, 0, fill >> B);
                }
                else
                {
                    /* For a stereo split, the high bits of fill are always zero, so no
                       folding will be done to the side. */
                    cm = quant_band(ctx, Y, Y_ptr, N, sbits, B,
                          null, 0, LM, null, 0,
                          side, null, 0, fill >> B);
                    rebalance = sbits - (rebalance - ctx.remaining_bits);
                    if (rebalance > 3 << EntropyCoder.BITRES && itheta != 16384)
                        mbits += rebalance - (3 << EntropyCoder.BITRES);
                    /* In stereo mode, we do not apply a scaling to the mid because we need the normalized
                       mid for folding later. */
                    cm |= quant_band(ctx, X, X_ptr, N, mbits, B,
                          lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr,
                          CeltConstants.Q15ONE, lowband_scratch, lowband_scratch_ptr, fill);
                }
            }


            /* This code is used by the decoder and by the resynthesis-enabled encoder */
            if (resynth != 0)
            {
                if (N != 2)
                {
                    stereo_merge(X, X_ptr, Y, Y_ptr, mid, N);
                }
                if (inv != 0)
                {
                    int j;
                    for (j = Y_ptr; j < N + Y_ptr; j++)
                        Y[j] = (short)(0 - Y[j]);
                }
            }

            return cm;
        }


        internal static void quant_all_bands(int encode, CeltMode m, int start, int end,
              int[] X_, int[] Y_, byte[] collapse_masks,
              int[][] bandE, int[] pulses, int shortBlocks, int spread,
              int dual_stereo, int intensity, int[] tf_res, int total_bits,
              int balance, EntropyCoder ec, int LM, int codedBands,
              ref uint seed)
        {
            int i;
            int remaining_bits;
            short[] eBands = m.eBands;
            int[] norm;
            int norm2;
            int[] lowband_scratch;
            int lowband_scratch_ptr;
            int B;
            int M;
            int lowband_offset;
            int update_lowband = 1;
            int C = Y_ != null ? 2 : 1;
            int norm_offset;
            int resynth = encode == 0 ? 1 : 0;
            band_ctx ctx = new band_ctx(); // porting note: stack var

            M = 1 << LM;
            B = (shortBlocks != 0) ? M : 1;
            norm_offset = M * eBands[start];

            /* No need to allocate norm for the last band because we don't need an
               output in that band. */
            norm = new int[(C * (M * eBands[m.nbEBands - 1] - norm_offset))];
            norm2 = M * eBands[m.nbEBands - 1] - norm_offset;

            /* We can use the last band as scratch space because we don't need that
               scratch space for the last band. */
            lowband_scratch = X_;
            lowband_scratch_ptr = M * eBands[m.nbEBands - 1];

            lowband_offset = 0;
            ctx.bandE = bandE;
            ctx.ec = ec;
            ctx.encode = encode;
            ctx.intensity = intensity;
            ctx.m = m;
            ctx.seed = seed;
            ctx.spread = spread;
            for (i = start; i < end; i++)
            {
                int tell;
                int b;
                int N;
                int curr_balance;
                int effective_lowband = -1;
                int[] X, Y;
                int X_ptr, Y_ptr;
                Y_ptr = 0;
                int tf_change = 0;
                uint x_cm;
                uint y_cm;
                int last;

                ctx.i = i;
                last = (i == end - 1) ? 1 : 0;
                
                X = X_;
                X_ptr = (M * eBands[i]);
                if (Y_ != null)
                {
                    Y = Y_;
                    Y_ptr = (M * eBands[i]);
                }
                else
                {
                    Y = null;
                }
                N = M * eBands[i + 1] - M * eBands[i];
                tell = (int)ec.tell_frac();

                /* Compute how many bits we want to allocate to this band */
                if (i != start)
                    balance -= tell;
                remaining_bits = total_bits - tell - 1;
                ctx.remaining_bits = remaining_bits;
                if (i <= codedBands - 1)
                {
                    curr_balance = Inlines.celt_sudiv(balance, Inlines.IMIN(3, codedBands - i));
                    b = Inlines.IMAX(0, Inlines.IMIN(16383, Inlines.IMIN(remaining_bits + 1, pulses[i] + curr_balance)));
                }
                else
                {
                    b = 0;
                }

                if (resynth != 0 && M * eBands[i] - N >= M * eBands[start] && (update_lowband != 0 || lowband_offset == 0))
                {
                    lowband_offset = i;
                }

                tf_change = tf_res[i];
                ctx.tf_change = tf_change;
                if (i >= m.effEBands)
                {
                    X = norm;
                    X_ptr = 0;
                    if (Y_ != null)
                    {
                        Y = norm;
                        Y_ptr = 0;
                    }
                    lowband_scratch = null;
                }
                if (i == end - 1)
                {
                    lowband_scratch = null;
                }

                /* Get a conservative estimate of the collapse_mask's for the bands we're
                   going to be folding from. */
                if (lowband_offset != 0 && (spread != Spread.SPREAD_AGGRESSIVE || B > 1 || tf_change < 0))
                {
                    int fold_start;
                    int fold_end;
                    int fold_i;
                    /* This ensures we never repeat spectral content within one band */
                    effective_lowband = Inlines.IMAX(0, M * eBands[lowband_offset] - norm_offset - N);
                    fold_start = lowband_offset;
                    while (M * eBands[--fold_start] > effective_lowband + norm_offset) ;
                    fold_end = lowband_offset - 1;
                    while (M * eBands[++fold_end] < effective_lowband + norm_offset + N) ;
                    x_cm = y_cm = 0;
                    fold_i = fold_start; do
                    {
                        x_cm |= collapse_masks[fold_i * C + 0];
                        y_cm |= collapse_masks[fold_i * C + C - 1];
                    } while (++fold_i < fold_end);
                }
                /* Otherwise, we'll be using the LCG to fold, so all blocks will (almost
                   always) be non-zero. */
                else
                {
                    x_cm = y_cm = (uint)((1 << B) - 1);
                }

                if (dual_stereo != 0 && i == intensity)
                {
                    int j;

                    /* Switch off dual stereo to do intensity. */
                    dual_stereo = 0;
                    if (resynth != 0)
                    {
                        for (j = 0; j < M * eBands[i] - norm_offset; j++)
                        {
                            norm[j] = (Inlines.HALF32(norm[j] + norm[norm2 + j]));
                        }
                    }
                }
                if (dual_stereo != 0)
                {
                    x_cm = quant_band(ctx,
                        X,
                        X_ptr,
                        N,
                        b / 2,
                        B,
                        effective_lowband != -1 ? norm : null,
                        effective_lowband,
                        LM,
                        last != 0 ? null : norm,
                        M * eBands[i] - norm_offset,
                        CeltConstants.Q15ONE,
                        lowband_scratch,
                        lowband_scratch_ptr,
                        (int)x_cm);
                    y_cm = quant_band(
                        ctx,
                        Y,
                        Y_ptr,
                        N,
                        b / 2,
                        B,
                        effective_lowband != -1 ? norm : null,
                        norm2 + effective_lowband,
                        LM,
                        last != 0 ? null : norm,
                        norm2 + (M * eBands[i] - norm_offset),
                        CeltConstants.Q15ONE,
                        lowband_scratch,
                        lowband_scratch_ptr,
                        (int)y_cm);
                }
                else
                {
                    if (Y != null)
                    {
                        x_cm = quant_band_stereo(
                            ctx,
                            X,
                            X_ptr,
                            Y,
                            Y_ptr,
                            N,
                            b,
                            B,
                            effective_lowband != -1 ? norm : null,
                            effective_lowband,
                            LM,
                            last != 0 ? null : norm,
                            M * eBands[i] - norm_offset,
                            lowband_scratch,
                            lowband_scratch_ptr,
                            (int)(x_cm | y_cm));
                    }
                    else
                    {
                        x_cm = quant_band(
                            ctx,
                            X,
                            X_ptr,
                            N,
                            b,
                            B,
                            effective_lowband != -1 ? norm : null,
                            effective_lowband,
                            LM,
                            last != 0 ? null : norm,
                            M * eBands[i] - norm_offset,
                            CeltConstants.Q15ONE,
                            lowband_scratch,
                            lowband_scratch_ptr,
                            (int)(x_cm | y_cm)); // opt: lots of pointers are created here too
                    }
                    y_cm = x_cm;
                }
                collapse_masks[i * C + 0] = (byte)(x_cm & 0xFF);
                collapse_masks[i * C + C - 1] = (byte)(y_cm & 0xFF);
                balance += pulses[i] + tell;

                /* Update the folding position only as long as we have 1 bit/sample depth. */
                update_lowband = (b > (N << EntropyCoder.BITRES)) ? 1 : 0;
            }

            seed = ctx.seed;
        }
    }
}
