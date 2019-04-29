/* Copyright (c) 2003-2004, Mark Borgerding
   Copyright (c) 2007-2008 CSIRO
   Copyright (c) 2007-2011 Xiph.Org Foundation
   Modified from KISS-FFT by Jean-Marc Valin
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

    internal static class MDCT
    {

#if !UNSAFE
        /* Forward MDCT trashes the input array */
        internal static void clt_mdct_forward(MDCTLookup l, int[] input, int input_ptr, int[] output, int output_ptr,
            int[] window, int overlap, int shift, int stride)
        {
            int i;
            int N, N2, N4;
            int[] f;
            int[] f2;
            FFTState st = l.kfft[shift];
            short[] trig;
            int trig_ptr = 0;
            int scale;
            
            int scale_shift = st.scale_shift - 1;
            scale = st.scale;

            N = l.n;
            trig = l.trig;
            for (i = 0; i < shift; i++)
            {
                N = N >> 1;
                trig_ptr += N;
            }
            N2 = N >> 1;
            N4 = N >> 2;

            f = new int[N2];
            f2 = new int[N4 * 2];

            /* Consider the input to be composed of four blocks: [a, b, c, d] */
            /* Window, shuffle, fold */
            {
                /* Temp pointers to make it really clear to the compiler what we're doing */
                int xp1 = input_ptr + (overlap >> 1);
                int xp2 = input_ptr + N2 - 1 + (overlap >> 1);
                int yp = 0;
                int wp1 = (overlap >> 1);
                int wp2 = ((overlap >> 1) - 1);
                for (i = 0; i < ((overlap + 3) >> 2); i++)
                {
                    /* Real part arranged as -d-cR, Imag part arranged as -b+aR*/
                    f[yp++] = Inlines.MULT16_32_Q15(window[wp2], input[xp1 + N2]) + Inlines.MULT16_32_Q15(window[wp1], input[xp2]);
                    f[yp++] = Inlines.MULT16_32_Q15(window[wp1], input[xp1]) - Inlines.MULT16_32_Q15(window[wp2], input[xp2 - N2]);
                    xp1 += 2;
                    xp2 -= 2;
                    wp1 += 2;
                    wp2 -= 2;
                }
                wp1 = 0;
                wp2 = (overlap - 1);
                for (; i < N4 - ((overlap + 3) >> 2); i++)
                {
                    /* Real part arranged as a-bR, Imag part arranged as -c-dR */
                    f[yp++] = input[xp2];
                    f[yp++] = input[xp1];
                    xp1 += 2;
                    xp2 -= 2;
                }
                for (; i < N4; i++)
                {
                    /* Real part arranged as a-bR, Imag part arranged as -c-dR */
                    f[yp++] = Inlines.MULT16_32_Q15(window[wp2], input[xp2]) - Inlines.MULT16_32_Q15(window[wp1], input[xp1 - N2]);
                    f[yp++] = Inlines.MULT16_32_Q15(window[wp2], input[xp1]) + Inlines.MULT16_32_Q15(window[wp1], input[xp2 + N2]);
                    xp1 += 2;
                    xp2 -= 2;
                    wp1 += 2;
                    wp2 -= 2;
                }
            }
            /* Pre-rotation */
            {
                int yp = 0;
                int t = trig_ptr;
                for (i = 0; i < N4; i++)
                {
                    short t0, t1;
                    int re, im, yr, yi;
                    t0 = trig[t + i];
                    t1 = trig[t + N4 + i];
                    re = f[yp++];
                    im = f[yp++];
                    yr = KissFFT.S_MUL(re, t0) - KissFFT.S_MUL(im, t1);
                    yi = KissFFT.S_MUL(im, t0) + KissFFT.S_MUL(re, t1);
                    f2[2 * st.bitrev[i]] = Inlines.PSHR32(Inlines.MULT16_32_Q16(scale, yr), scale_shift);
                    f2[2 * st.bitrev[i] + 1] = Inlines.PSHR32(Inlines.MULT16_32_Q16(scale, yi), scale_shift);
                }
            }

            /* N/4 complex FFT, does not downscale anymore */
            KissFFT.opus_fft_impl(st, f2, 0);

            /* Post-rotate */
            {
                /* Temp pointers to make it really clear to the compiler what we're doing */
                int fp = 0;
                int yp1 = output_ptr;
                int yp2 = output_ptr + (stride * (N2 - 1));
                int t = trig_ptr;
                for (i = 0; i < N4; i++)
                {
                    int yr, yi;
                    yr = KissFFT.S_MUL(f2[fp + 1], trig[t + N4 + i]) - KissFFT.S_MUL(f2[fp], trig[t + i]);
                    yi = KissFFT.S_MUL(f2[fp], trig[t + N4 + i]) + KissFFT.S_MUL(f2[fp + 1], trig[t + i]);
                    output[yp1] = yr;
                    output[yp2] = yi;
                    fp += 2;
                    yp1 += (2 * stride);
                    yp2 -= (2 * stride);
                }
            }
        }
#else
        /* Forward MDCT trashes the input array */
        internal static unsafe void clt_mdct_forward(MDCTLookup l, int[] input, int input_ptr, int[] output, int output_ptr,
            int[] window, int overlap, int shift, int stride)
        {
            int i;
            int N, N2, N4;
            int[] f;
            int[] f2;
            FFTState st = l.kfft[shift];
            int scale;

            int scale_shift = st.scale_shift - 1;
            scale = st.scale;

            N = l.n;
            fixed (short* ptrig_base = l.trig)
            {
                short* trig = ptrig_base;
                for (i = 0; i < shift; i++)
                {
                    N = N >> 1;
                    trig += N;
                }
                N2 = N >> 1;
                N4 = N >> 2;

                f = new int[N2];
                f2 = new int[N4 * 2];
                fixed (int* pinput_base = input, pwindow = window, pf = f, pf2 = f2)
                {
                    int* pinput = pinput_base + input_ptr;
                    /* Consider the input to be composed of four blocks: [a, b, c, d] */
                    /* Window, shuffle, fold */
                    {
                        /* Temp pointers to make it really clear to the compiler what we're doing */
                        int* xp1 = pinput + (overlap >> 1);
                        int* xp2 = pinput + N2 - 1 + (overlap >> 1);
                        int* yp = pf;
                        int* wp1 = pwindow + (overlap >> 1);
                        int* wp2 = pwindow + ((overlap >> 1) - 1);
                        for (i = 0; i < ((overlap + 3) >> 2); i++)
                        {
                            /* Real part arranged as -d-cR, Imag part arranged as -b+aR*/
                            *yp++ = Inlines.MULT16_32_Q15(*wp2, xp1[N2]) + Inlines.MULT16_32_Q15(*wp1, *xp2);
                            *yp++ = Inlines.MULT16_32_Q15(*wp1, *xp1) - Inlines.MULT16_32_Q15(*wp2, xp2[0 - N2]);
                            xp1 += 2;
                            xp2 -= 2;
                            wp1 += 2;
                            wp2 -= 2;
                        }
                        wp1 = pwindow;
                        wp2 = pwindow + (overlap - 1);
                        for (; i < N4 - ((overlap + 3) >> 2); i++)
                        {
                            /* Real part arranged as a-bR, Imag part arranged as -c-dR */
                            *yp++ = *xp2;
                            *yp++ = *xp1;
                            xp1 += 2;
                            xp2 -= 2;
                        }
                        for (; i < N4; i++)
                        {
                            /* Real part arranged as a-bR, Imag part arranged as -c-dR */
                            *yp++ = Inlines.MULT16_32_Q15(*wp2, *xp2) - Inlines.MULT16_32_Q15(*wp1, xp1[0 - N2]);
                            *yp++ = Inlines.MULT16_32_Q15(*wp2, *xp1) + Inlines.MULT16_32_Q15(*wp1, xp2[N2]);
                            xp1 += 2;
                            xp2 -= 2;
                            wp1 += 2;
                            wp2 -= 2;
                        }
                    }
                    /* Pre-rotation */
                    {
                        int* yp = pf;
                        short* t = trig;
                        for (i = 0; i < N4; i++)
                        {
                            short t0, t1;
                            int re, im, yr, yi;
                            t0 = t[i];
                            t1 = t[N4 + i];
                            re = *yp++;
                            im = *yp++;
                            yr = KissFFT.S_MUL(re, t0) - KissFFT.S_MUL(im, t1);
                            yi = KissFFT.S_MUL(im, t0) + KissFFT.S_MUL(re, t1);
                            pf2[2 * st.bitrev[i]] = Inlines.PSHR32(Inlines.MULT16_32_Q16(scale, yr), scale_shift);
                            pf2[2 * st.bitrev[i] + 1] = Inlines.PSHR32(Inlines.MULT16_32_Q16(scale, yi), scale_shift);
                        }
                    }

                    /* N/4 complex FFT, does not downscale anymore */
                    KissFFT.opus_fft_impl(st, f2, 0);

                    /* Post-rotate */
                    fixed (int* poutput_base = output)
                    {
                        /* Temp pointers to make it really clear to the compiler what we're doing */
                        int* fp = pf2;
                        int* yp1 = poutput_base + output_ptr;
                        int* yp2 = poutput_base + output_ptr + (stride * (N2 - 1));
                        short* t = trig;
                        for (i = 0; i < N4; i++)
                        {
                            int yr, yi;
                            yr = KissFFT.S_MUL(fp[1], t[N4 + i]) - KissFFT.S_MUL(fp[0], t[i]);
                            yi = KissFFT.S_MUL(fp[0], t[N4 + i]) + KissFFT.S_MUL(fp[1], t[i]);
                            *yp1 = yr;
                            *yp2 = yi;
                            fp += 2;
                            yp1 += (2 * stride);
                            yp2 -= (2 * stride);
                        }
                    }
                }
            }
        }
#endif

        internal static void clt_mdct_backward(MDCTLookup l, int[] input, int input_ptr, int[] output, int output_ptr,
              int[] window, int overlap, int shift, int stride)
        {
            int i;
            int N, N2, N4;
            int trig = 0;
            int xp1, xp2, yp, yp0, yp1;

            N = l.n;
            for (i = 0; i < shift; i++)
            {
                N >>= 1;
                trig += N;
            }
            N2 = N >> 1;
            N4 = N >> 2;

            /* Pre-rotate */
            /* Temp pointers to make it really clear to the compiler what we're doing */
            xp2 = input_ptr + (stride * (N2 - 1));
            yp = output_ptr + (overlap >> 1);
            short[] bitrev = l.kfft[shift].bitrev;
            int bitrav_ptr = 0;
            for (i = 0; i < N4; i++)
            {
                int rev = bitrev[bitrav_ptr++];
                int ypr = yp + 2 * rev;
                /* We swap real and imag because we use an FFT instead of an IFFT. */
                output[ypr + 1] = KissFFT.S_MUL(input[xp2], l.trig[trig + i]) + KissFFT.S_MUL(input[input_ptr], l.trig[trig + N4 + i]); //yr
                output[ypr] = KissFFT.S_MUL(input[input_ptr], l.trig[trig + i]) - KissFFT.S_MUL(input[xp2], l.trig[trig + N4 + i]); //yi
                /* Storing the pre-rotation directly in the bitrev order. */
                input_ptr += (2 * stride);
                xp2 -= (2 * stride);
            }
            
            KissFFT.opus_fft_impl(l.kfft[shift], output, output_ptr + (overlap >> 1));
            
            /* Post-rotate and de-shuffle from both ends of the buffer at once to make
                it in-place. */
            yp0 = output_ptr + (overlap >> 1);
            yp1 = output_ptr + (overlap >> 1) + N2 - 2;
            int t = trig;

            /* Loop to (N4+1)>>1 to handle odd N4. When N4 is odd, the
                middle pair will be computed twice. */
            int tN4m1 = t + N4 - 1;
            int tN2m1 = t + N2 - 1;
            for (i = 0; i < (N4 + 1) >> 1; i++)
            {
                int re, im, yr, yi;
                short t0, t1;
                /* We swap real and imag because we're using an FFT instead of an IFFT. */
                re = output[yp0 + 1];
                im = output[yp0];
                t0 = l.trig[t + i];
                t1 = l.trig[t + N4 + i];
                /* We'd scale up by 2 here, but instead it's done when mixing the windows */
                yr = KissFFT.S_MUL(re, t0) + KissFFT.S_MUL(im, t1);
                yi = KissFFT.S_MUL(re, t1) - KissFFT.S_MUL(im, t0);
                /* We swap real and imag because we're using an FFT instead of an IFFT. */
                re = output[yp1 + 1];
                im = output[yp1];
                output[yp0] = yr;
                output[yp1 + 1] = yi;
                t0 = l.trig[tN4m1 - i];
                t1 = l.trig[tN2m1 - i];
                /* We'd scale up by 2 here, but instead it's done when mixing the windows */
                yr = KissFFT.S_MUL(re, t0) + KissFFT.S_MUL(im, t1);
                yi = KissFFT.S_MUL(re, t1) - KissFFT.S_MUL(im, t0);
                output[yp1] = yr;
                output[yp0 + 1] = yi;
                yp0 += 2;
                yp1 -= 2;
            }

            /* Mirror on both sides for TDAC */
            xp1 = output_ptr + overlap - 1;
            yp1 = output_ptr;
            int wp1 = 0;
            int wp2 = (overlap - 1);

            for (i = 0; i < overlap / 2; i++)
            {
                int x1 = output[xp1];
                int x2 = output[yp1];
                output[yp1++] = Inlines.MULT16_32_Q15(window[wp2], x2) - Inlines.MULT16_32_Q15(window[wp1], x1);
                output[xp1--] = Inlines.MULT16_32_Q15(window[wp1], x2) + Inlines.MULT16_32_Q15(window[wp2], x1);
                wp1++;
                wp2--;
            }
        }
    }
}
