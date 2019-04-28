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

using Concentus.Common.CPlusPlus;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;


namespace Concentus
{
    internal static class OpusCompare
    {
        private const int NBANDS = 21;
        private const int NFREQS = 240;
        private const int TEST_WIN_SIZE = 480;
        private const int TEST_WIN_STEP = 120;

        /*Bands on which we compute the pseudo-NMR (Bark-derived CELT bands).*/
        private static readonly int[] BANDS/*[NBANDS + 1]*/ ={
            0,2,4,6,8,10,12,14,16,20,24,28,32,40,48,56,68,80,96,120,156,200
        };

        private static void band_energy(Pointer<float> _out, Pointer<float> _ps,
            Pointer<int> _bands, int _nbands,
            Pointer<float> _in, int _nchannels, int _nframes, int _window_sz,
             int _step, int _downsample)
        {
            Pointer<float> window;
            Pointer<float> x;
            Pointer<float> c;
            Pointer<float> s;
            int xi;
            int xj;
            int ps_sz;
            window = Pointer.Malloc<float>((3 + _nchannels) * _window_sz);
            c = window.Point(_window_sz);
            s = c.Point(_window_sz);
            x = s.Point(_window_sz);
            ps_sz = _window_sz / 2;
            for (xj = 0; xj < _window_sz; xj++)
            {
                window[xj] = (float)(0.5 - 0.5 * Math.Cos((2 * Math.PI / (_window_sz - 1)) * xj));
            }
            for (xj = 0; xj < _window_sz; xj++)
            {
                c[xj] = (float)Math.Cos((2 * Math.PI / _window_sz) * xj);
            }
            for (xj = 0; xj < _window_sz; xj++)
            {
                s[xj] = (float)Math.Sin((2 * Math.PI / _window_sz) * xj);
            }
            for (xi = 0; xi < _nframes; xi++)
            {
                int ci;
                int xk;
                int bi;
                for (ci = 0; ci < _nchannels; ci++)
                {
                    for (xk = 0; xk < _window_sz; xk++)
                    {
                        x[ci * _window_sz + xk] = window[xk] * _in[(xi * _step + xk) * _nchannels + ci];
                    }
                }
                for (bi = xj = 0; bi < _nbands; bi++)
                {
                    float[] p = { 0, 0 };
                    for (; xj < _bands[bi + 1]; xj++)
                    {
                        for (ci = 0; ci < _nchannels; ci++)
                        {
                            float re;
                            float im;
                            int ti;
                            ti = 0;
                            re = im = 0;
                            for (xk = 0; xk < _window_sz; xk++)
                            {
                                re += c[ti] * x[ci * _window_sz + xk];
                                im -= s[ti] * x[ci * _window_sz + xk];
                                ti += xj;
                                if (ti >= _window_sz) ti -= _window_sz;
                            }
                            re *= _downsample;
                            im *= _downsample;
                            _ps[(xi * ps_sz + xj) * _nchannels + ci] = re * re + im * im + 100000;
                            p[ci] += _ps[(xi * ps_sz + xj) * _nchannels + ci];
                        }
                    }
                    if (_out != null)
                    {
                        _out[(xi * _nbands + bi) * _nchannels] = p[0] / (_bands[bi + 1] - _bands[bi]);
                        if (_nchannels == 2)
                        {
                            _out[(xi * _nbands + bi) * _nchannels + 1] = p[1] / (_bands[bi + 1] - _bands[bi]);
                        }
                    }
                }
            }
        }
        
        internal static float compare(float[] x, float[] y, int nchannels, int rate = 48000)
        {
            Pointer<float> xb;
            Pointer<float> X;
            Pointer<float> Y;
            double err;
            float Q;
            int xlength = x.Length;
            int ylength = y.Length;
            int nframes;
            int xi;
            int ci;
            int xj;
            int bi;
            int downsample;
            int ybands;
            int yfreqs;
            int max_compare;
            ybands = NBANDS;
            yfreqs = NFREQS;
            if (rate != 8000 && rate != 12000 && rate != 16000 && rate != 24000 && rate != 48000)
            {
                throw new ArgumentException("Sampling rate must be 8000, 12000, 16000, 24000, or 48000\n");
            }
            if (rate != 48000)
            {
                downsample = 48000 / rate;
                switch (rate)
                {
                    case 8000: ybands = 13; break;
                    case 12000: ybands = 15; break;
                    case 16000: ybands = 17; break;
                    case 24000: ybands = 19; break;
                }
                yfreqs = NFREQS / downsample;
            }
            else
            {
                downsample = 1;
            }

            if (xlength != ylength * downsample)
            {
                throw new ArgumentException("Sample counts do not match");
            }

            if (xlength < TEST_WIN_SIZE)
            {
                throw new ArgumentException("Insufficient sample data");
            }

            nframes = (xlength - TEST_WIN_SIZE + TEST_WIN_STEP) / TEST_WIN_STEP;
            xb = Pointer.Malloc<float>(nframes * NBANDS * nchannels);
            X = Pointer.Malloc<float>(nframes * NFREQS * nchannels);
            Y = Pointer.Malloc<float>(nframes * yfreqs * nchannels);
            /*Compute the per-band spectral energy of the original signal
               and the error.*/
            band_energy(xb, X, BANDS.GetPointer(), NBANDS, x.GetPointer(), nchannels, nframes,
             TEST_WIN_SIZE, TEST_WIN_STEP, 1);
            band_energy(null, Y, BANDS.GetPointer(), ybands, y.GetPointer(), nchannels, nframes,
             TEST_WIN_SIZE / downsample, TEST_WIN_STEP / downsample, downsample);
            for (xi = 0; xi < nframes; xi++)
            {
                /*Frequency masking (low to high): 10 dB/Bark slope.*/
                for (bi = 1; bi < NBANDS; bi++)
                {
                    for (ci = 0; ci < nchannels; ci++)
                    {
                        xb[(xi * NBANDS + bi) * nchannels + ci] +=
                         0.1F * xb[(xi * NBANDS + bi - 1) * nchannels + ci];
                    }
                }
                /*Frequency masking (high to low): 15 dB/Bark slope.*/
                for (bi = NBANDS - 1; bi-- > 0;)
                {
                    for (ci = 0; ci < nchannels; ci++)
                    {
                        xb[(xi * NBANDS + bi) * nchannels + ci] +=
                         0.03F * xb[(xi * NBANDS + bi + 1) * nchannels + ci];
                    }
                }
                if (xi > 0)
                {
                    /*Temporal masking: -3 dB/2.5ms slope.*/
                    for (bi = 0; bi < NBANDS; bi++)
                    {
                        for (ci = 0; ci < nchannels; ci++)
                        {
                            xb[(xi * NBANDS + bi) * nchannels + ci] +=
                             0.5F * xb[((xi - 1) * NBANDS + bi) * nchannels + ci];
                        }
                    }
                }
                /* Allowing some cross-talk */
                if (nchannels == 2)
                {
                    for (bi = 0; bi < NBANDS; bi++)
                    {
                        float l, r;
                        l = xb[(xi * NBANDS + bi) * nchannels + 0];
                        r = xb[(xi * NBANDS + bi) * nchannels + 1];
                        xb[(xi * NBANDS + bi) * nchannels + 0] += 0.01F * r;
                        xb[(xi * NBANDS + bi) * nchannels + 1] += 0.01F * l;
                    }
                }

                /* Apply masking */
                for (bi = 0; bi < ybands; bi++)
                {
                    for (xj = BANDS[bi]; xj < BANDS[bi + 1]; xj++)
                    {
                        for (ci = 0; ci < nchannels; ci++)
                        {
                            X[(xi * NFREQS + xj) * nchannels + ci] +=
                             0.1F * xb[(xi * NBANDS + bi) * nchannels + ci];
                            Y[(xi * yfreqs + xj) * nchannels + ci] +=
                             0.1F * xb[(xi * NBANDS + bi) * nchannels + ci];
                        }
                    }
                }
            }

            /* Average of consecutive frames to make comparison slightly less sensitive */
            for (bi = 0; bi < ybands; bi++)
            {
                for (xj = BANDS[bi]; xj < BANDS[bi + 1]; xj++)
                {
                    for (ci = 0; ci < nchannels; ci++)
                    {
                        float xtmp;
                        float ytmp;
                        xtmp = X[xj * nchannels + ci];
                        ytmp = Y[xj * nchannels + ci];
                        for (xi = 1; xi < nframes; xi++)
                        {
                            float xtmp2;
                            float ytmp2;
                            xtmp2 = X[(xi * NFREQS + xj) * nchannels + ci];
                            ytmp2 = Y[(xi * yfreqs + xj) * nchannels + ci];
                            X[(xi * NFREQS + xj) * nchannels + ci] += xtmp;
                            Y[(xi * yfreqs + xj) * nchannels + ci] += ytmp;
                            xtmp = xtmp2;
                            ytmp = ytmp2;
                        }
                    }
                }
            }

            /*If working at a lower sampling rate, don't take into account the last
               300 Hz to allow for different transition bands.
              For 12 kHz, we don't skip anything, because the last band already skips
               400 Hz.*/
            if (rate == 48000) max_compare = BANDS[NBANDS];
            else if (rate == 12000) max_compare = BANDS[ybands];
            else max_compare = BANDS[ybands] - 3;
            err = 0;
            for (xi = 0; xi < nframes; xi++)
            {
                double Ef;
                Ef = 0;
                for (bi = 0; bi < ybands; bi++)
                {
                    double Eb;
                    Eb = 0;
                    for (xj = BANDS[bi]; xj < BANDS[bi + 1] && xj < max_compare; xj++)
                    {
                        for (ci = 0; ci < nchannels; ci++)
                        {
                            float re;
                            float im;
                            re = Y[(xi * yfreqs + xj) * nchannels + ci] / X[(xi * NFREQS + xj) * nchannels + ci];
                            im = re - (float)Math.Log(re) - 1;
                            /*Make comparison less sensitive around the SILK/CELT cross-over to
                              allow for mode freedom in the filters.*/
                            if (xj >= 79 && xj <= 81) im *= 0.1F;
                            if (xj == 80) im *= 0.1F;
                            Eb += im;
                        }
                    }
                    Eb /= (BANDS[bi + 1] - BANDS[bi]) * nchannels;
                    Ef += Eb * Eb;
                }
                /*Using a fixed normalization value means we're willing to accept slightly
                   lower quality for lower sampling rates.*/
                Ef /= NBANDS;
                Ef *= Ef;
                err += Ef * Ef;
            }
            err = Math.Pow(err / nframes, 1.0 / 16);
            Q = (float)(100 * (1 - 0.5 * Math.Log(1 + err) / Math.Log(1.13)));

            if (Q < 0)
            {
                Debug.WriteLine("Test vector FAILS");
                Debug.WriteLine(string.Format("Internal weighted error is {0}", err));
            }
            else {
                Debug.WriteLine("Test vector PASSES");
                Debug.WriteLine(string.Format("Opus quality metric: {0} (internal weighted error is {1})", Q, err));
            }

            return Q;
        }
    }
}
