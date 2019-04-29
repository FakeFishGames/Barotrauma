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
    using System.Diagnostics;

    internal static class VQ
    {
        internal static void exp_rotation1(int[] X, int X_ptr, int len, int stride, int c, int s)
        {
            int i;
            int ms;
            int Xptr;
            Xptr = X_ptr;
            ms = Inlines.NEG16(s);
            for (i = 0; i < len - stride; i++)
            {
                int x1, x2;
                x1 = X[Xptr];
                x2 = X[Xptr + stride];
                X[Xptr + stride] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MAC16_16(Inlines.MULT16_16(c, x2), s, x1), 15));
                X[Xptr] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MAC16_16(Inlines.MULT16_16(c, x1), ms, x2), 15));
                Xptr++;
            }
            Xptr = X_ptr + (len - 2 * stride - 1);
            for (i = len - 2 * stride - 1; i >= 0; i--)
            {
                int x1, x2;
                x1 = X[Xptr];
                x2 = X[Xptr + stride];
                X[Xptr + stride] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MAC16_16(Inlines.MULT16_16(c, x2), s, x1), 15));
                X[Xptr] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MAC16_16(Inlines.MULT16_16(c, x1), ms, x2), 15));
                Xptr--;
            }
        }

        private static int[] SPREAD_FACTOR = { 15, 10, 5 };
        
        internal static void exp_rotation(int[] X, int X_ptr, int len, int dir, int stride, int K, int spread)
        {
            int i;
            int c, s;
            int gain, theta;
            int stride2 = 0;
            int factor;

            if (2 * K >= len || spread == Spread.SPREAD_NONE)
            {
                return;
            }

            factor = SPREAD_FACTOR[spread - 1];

            gain = (Inlines.celt_div((int)Inlines.MULT16_16(CeltConstants.Q15_ONE, len), (int)(len + factor * K)));
            theta = Inlines.HALF16(Inlines.MULT16_16_Q15(gain, gain));

            c = Inlines.celt_cos_norm(Inlines.EXTEND32(theta));
            s = Inlines.celt_cos_norm(Inlines.EXTEND32(Inlines.SUB16(CeltConstants.Q15ONE, theta))); /*  sin(theta) */

            if (len >= 8 * stride)
            {
                stride2 = 1;
                /* This is just a simple (equivalent) way of computing sqrt(len/stride) with rounding.
                   It's basically incrementing long as (stride2+0.5)^2 < len/stride. */
                while ((stride2 * stride2 + stride2) * stride + (stride >> 2) < len)
                {
                    stride2++;
                }
            }

            /*NOTE: As a minor optimization, we could be passing around log2(B), not B, for both this and for
               extract_collapse_mask().*/
            len = Inlines.celt_udiv(len, stride);
            for (i = 0; i < stride; i++)
            {
                if (dir < 0)
                {
                    if (stride2 != 0)
                    {
                        exp_rotation1(X, X_ptr + (i * len), len, stride2, s, c);
                    }

                    exp_rotation1(X, X_ptr + (i * len), len, 1, c, s);
                }
                else
                {
                    exp_rotation1(X, X_ptr + (i * len), len, 1, c, (short)(0 - s));

                    if (stride2 != 0)
                    {
                        exp_rotation1(X, X_ptr + (i * len), len, stride2, s, (short)(0 - c));
                    }
                }
            }
        }

        /** Takes the pitch vector and the decoded residual vector, computes the gain
            that will give ||p+g*y||=1 and mixes the residual with the pitch. */
        internal static void normalise_residual(int[] iy, int[] X, int X_ptr,
              int N, int Ryy, int gain)
        {
            int i;
            int k;
            int t;
            int g;

            k = Inlines.celt_ilog2(Ryy) >> 1;
            t = Inlines.VSHR32(Ryy, 2 * (k - 7));
            g = Inlines.MULT16_16_P15(Inlines.celt_rsqrt_norm(t), gain);

            i = 0;
            do
                X[X_ptr + i] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MULT16_16(g, iy[i]), k + 1));
            while (++i < N);
        }

        internal static uint extract_collapse_mask(int[] iy, int N, int B)
        {
            uint collapse_mask;
            int N0;
            int i;
            if (B <= 1)
                return 1;
            /*NOTE: As a minor optimization, we could be passing around log2(B), not B, for both this and for
               exp_rotation().*/
            N0 = Inlines.celt_udiv(N, B);
            collapse_mask = 0;
            i = 0;
            do
            {
                int j;
                uint tmp = 0;
                j = 0;
                do
                {
                    tmp |= unchecked((uint)iy[i * N0 + j]);
                } while (++j < N0);

                collapse_mask |= (tmp != 0 ? 1U : 0) << i;
            } while (++i < B);

            return collapse_mask;
        }

        internal static uint alg_quant(int[] X, int X_ptr, int N, int K, int spread, int B, EntropyCoder enc
           )
        {
            int[] y = new int[N];
            int[] iy = new int[N];
            int[] signx = new int[N];
            int i, j;
            int s;
            int pulsesLeft;
            int sum;
            int xy;
            int yy;
            uint collapse_mask;
            
            Inlines.OpusAssert(K > 0, "alg_quant() needs at least one pulse");
            Inlines.OpusAssert(N > 1, "alg_quant() needs at least two dimensions");
            
            exp_rotation(X, X_ptr, N, 1, B, K, spread);

            /* Get rid of the sign */
            sum = 0;
            j = 0;
            do
            {
                int xpj = X_ptr + j;

                /* OPT: Make sure the following two lines result in conditional moves
                   rather than branches. */
                signx[j] = X[xpj] > 0 ? 1 : -1;
                X[xpj] = Inlines.ABS16(X[xpj]);

                iy[j] = 0;
                y[j] = 0;
            } while (++j < N);

            xy = yy = 0;

            pulsesLeft = K;

            /* Do a pre-search by projecting on the pyramid */
            if (K > (N >> 1))
            {
                int rcp;
                j = 0; do
                {
                    sum += X[X_ptr + j];
                } while (++j < N);

                /* If X is too small, just replace it with a pulse at 0 */
                /* Prevents infinities and NaNs from causing too many pulses
                   to be allocated. 64 is an approximation of infinity here. */
                if (sum <= K)
                {
                    X[X_ptr] = ((short)(0.5 + (1.0f) * (((int)1) << (14))))/*Inlines.QCONST16(1.0f, 14)*/;
                    j = X_ptr + 1;
                    do
                    {
                        X[j] = 0;
                    } while (++j < N + X_ptr);

                    sum = ((short)(0.5 + (1.0f) * (((int)1) << (14))))/*Inlines.QCONST16(1.0f, 14)*/;
                }

                rcp = Inlines.EXTRACT16(Inlines.MULT16_32_Q16((K - 1), Inlines.celt_rcp(sum)));
                j = 0;

                do
                {
                    /* It's really important to round *towards zero* here */
                    iy[j] = Inlines.MULT16_16_Q15(X[X_ptr + j], rcp);
                    y[j] = (int)iy[j];
                    yy = (Inlines.MAC16_16(yy, y[j], y[j]));
                    xy = Inlines.MAC16_16(xy, X[X_ptr + j], y[j]);
                    y[j] *= 2;
                    pulsesLeft -= iy[j];
                } while (++j < N);
            }

            Inlines.OpusAssert(pulsesLeft >= 1, "Allocated too many pulses in the quick pass");

            /* This should never happen, but just in case it does (e.g. on silence)
               we fill the first bin with pulses. */
            if (pulsesLeft > N + 3)
            {
                int tmp = (int)pulsesLeft;
                yy = (Inlines.MAC16_16(yy, tmp, tmp));
                yy = (Inlines.MAC16_16(yy, tmp, y[0]));
                iy[0] += pulsesLeft;
                pulsesLeft = 0;
            }

            s = 1;
            for (i = 0; i < pulsesLeft; i++)
            {
                int best_id;
                int best_num = 0 - CeltConstants.VERY_LARGE16;
                int best_den = 0;
                int rshift = 1 + Inlines.celt_ilog2(K - pulsesLeft + i + 1);
                best_id = 0;
                /* The squared magnitude term gets added anyway, so we might as well
                   add it outside the loop */
                yy = Inlines.ADD16(yy, 1); // opus bug - was add32
                j = 0;
                do
                {
                    int Rxy, Ryy;
                    /* Temporary sums of the new pulse(s) */
                    Rxy = Inlines.EXTRACT16(Inlines.SHR32(Inlines.ADD32(xy, Inlines.EXTEND32(X[X_ptr + j])), rshift));
                    /* We're multiplying y[j] by two so we don't have to do it here */
                    Ryy = Inlines.ADD16(yy, y[j]);

                    /* Approximate score: we maximise Rxy/sqrt(Ryy) (we're guaranteed that
                       Rxy is positive because the sign is pre-computed) */
                    Rxy = Inlines.MULT16_16_Q15(Rxy, Rxy);
                    /* The idea is to check for num/den >= best_num/best_den, but that way
                       we can do it without any division */
                    /* OPT: Make sure to use conditional moves here */
                    if (Inlines.MULT16_16(best_den, Rxy) > Inlines.MULT16_16(Ryy, best_num))
                    {
                        best_den = Ryy;
                        best_num = Rxy;
                        best_id = j;
                    }
                } while (++j < N);

                /* Updating the sums of the new pulse(s) */
                xy = Inlines.ADD32(xy, Inlines.EXTEND32(X[X_ptr + best_id]));
                /* We're multiplying y[j] by two so we don't have to do it here */
                yy = Inlines.ADD16(yy, y[best_id]);

                /* Only now that we've made the final choice, update y/iy */
                /* Multiplying y[j] by 2 so we don't have to do it everywhere else */
                y[best_id] = (y[best_id] + (2 * s));
                iy[best_id]++;
            }

            /* Put the original sign back */
            j = 0;
            do
            {
                X[X_ptr + j] = (Inlines.MULT16_16(signx[j], X[X_ptr + j]));
                /* OPT: Make sure your compiler uses a conditional move here rather than
                     a branch. */
                iy[j] = signx[j] < 0 ? -iy[j] : iy[j];
            } while (++j < N);

            CWRS.encode_pulses(iy, N, K, enc);

            collapse_mask = extract_collapse_mask(iy, N, B);

            return collapse_mask;
        }

        /** Decode pulse vector and combine the result with the pitch vector to produce
            the final normalised signal in the current band. */
        internal static uint alg_unquant(int[] X, int X_ptr, int N, int K, int spread, int B,
              EntropyCoder dec, int gain)
        {
            int Ryy;
            uint collapse_mask;
            int[] iy = new int[N];
            Inlines.OpusAssert(K > 0, "alg_unquant() needs at least one pulse");
            Inlines.OpusAssert(N > 1, "alg_unquant() needs at least two dimensions");
            Ryy = CWRS.decode_pulses(iy, N, K, dec);
            normalise_residual(iy, X, X_ptr, N, Ryy, gain);
            exp_rotation(X, X_ptr, N, -1, B, K, spread);
            collapse_mask = extract_collapse_mask(iy, N, B);

            return collapse_mask;
        }

        internal static void renormalise_vector(int[] X, int X_ptr, int N, int gain)
        {
            int i;
            int k;
            int E;
            int g;
            int t;
            int xptr;
#if UNSAFE
            unsafe
            {
                fixed (int* px_base = X)
                {
                    int* px = px_base + X_ptr;
                    E = CeltConstants.EPSILON + Kernels.celt_inner_prod(px, px, N);
                }
            }
#else
            E = CeltConstants.EPSILON + Kernels.celt_inner_prod(X, X_ptr, X, X_ptr, N);
#endif
            k = Inlines.celt_ilog2(E) >> 1;
            t = Inlines.VSHR32(E, 2 * (k - 7));
            g = Inlines.MULT16_16_P15(Inlines.celt_rsqrt_norm(t), gain);

            xptr = X_ptr;
            for (i = 0; i < N; i++)
            {
                X[xptr] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MULT16_16(g, X[xptr]), k + 1));
                xptr++;
            }
            /*return celt_sqrt(E);*/
        }

        internal static int stereo_itheta(int[] X, int X_ptr, int[] Y, int Y_ptr, int stereo, int N)
        {
            int i;
            int itheta;
            int mid, side;
            int Emid, Eside;

            Emid = Eside = CeltConstants.EPSILON;
            if (stereo != 0)
            {
                for (i = 0; i < N; i++)
                {
                    int m, s;
                    m = Inlines.ADD16(Inlines.SHR16(X[X_ptr + i], 1), Inlines.SHR16(Y[Y_ptr + i], 1));
                    s = Inlines.SUB16(Inlines.SHR16(X[X_ptr + i], 1), Inlines.SHR16(Y[Y_ptr + i], 1));
                    Emid = Inlines.MAC16_16(Emid, m, m);
                    Eside = Inlines.MAC16_16(Eside, s, s);
                }
            }
            else {
#if UNSAFE
                unsafe
                {
                    fixed (int* px_base = X, py_base = Y)
                    {
                        int* px = px_base + X_ptr;
                        int* py = py_base + Y_ptr;
                        Emid += Kernels.celt_inner_prod(px, px, N);
                        Eside += Kernels.celt_inner_prod(py, py, N);
                    }
                }
#else
                Emid += Kernels.celt_inner_prod(X, X_ptr, X, X_ptr, N);
                Eside += Kernels.celt_inner_prod(Y, Y_ptr, Y, Y_ptr, N);
#endif
            }
            mid = (Inlines.celt_sqrt(Emid));
            side = (Inlines.celt_sqrt(Eside));
            /* 0.63662 = 2/pi */
            itheta = Inlines.MULT16_16_Q15(((short)(0.5 + (0.63662f) * (((int)1) << (15))))/*Inlines.QCONST16(0.63662f, 15)*/, Inlines.celt_atan2p(side, mid));

            return itheta;
        }
    }
}
