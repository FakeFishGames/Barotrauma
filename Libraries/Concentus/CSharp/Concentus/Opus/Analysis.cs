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

using Concentus.Celt;
using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Concentus
{
    internal static class Analysis
    {
        private const double M_PI = 3.141592653;
        private const float cA = 0.43157974f;
        private const float cB = 0.67848403f;
        private const float cC = 0.08595542f;
        private const float cE = ((float)M_PI / 2);

        private const int NB_TONAL_SKIP_BANDS = 9;

        internal static float fast_atan2f(float y, float x)
        {
            float x2, y2;
            /* Should avoid underflow on the values we'll get */
            if (Inlines.ABS16(x) + Inlines.ABS16(y) < 1e-9f)
            {
                x *= 1e12f;
                y *= 1e12f;
            }
            x2 = x * x;
            y2 = y * y;
            if (x2 < y2)
            {
                float den = (y2 + cB * x2) * (y2 + cC * x2);
                if (den != 0)
                    return -x * y * (y2 + cA * x2) / den + (y < 0 ? -cE : cE);
                else
                    return (y < 0 ? -cE : cE);
            }
            else {
                float den = (x2 + cB * y2) * (x2 + cC * y2);
                if (den != 0)
                    return x * y * (x2 + cA * y2) / den + (y < 0 ? -cE : cE) - (x * y < 0 ? -cE : cE);
                else
                    return (y < 0 ? -cE : cE) - (x * y < 0 ? -cE : cE);
            }
        }

        internal static void tonality_analysis_init(TonalityAnalysisState tonal)
        {
            tonal.Reset();
        }

        internal static void tonality_get_info(TonalityAnalysisState tonal, AnalysisInfo info_out, int len)
        {
            int pos;
            int curr_lookahead;
            float psum;
            int i;

            pos = tonal.read_pos;
            curr_lookahead = tonal.write_pos - tonal.read_pos;
            if (curr_lookahead < 0)
                curr_lookahead += OpusConstants.DETECT_SIZE;

            if (len > 480 && pos != tonal.write_pos)
            {
                pos++;
                if (pos == OpusConstants.DETECT_SIZE)
                    pos = 0;
            }
            if (pos == tonal.write_pos)
                pos--;
            if (pos < 0)
                pos = OpusConstants.DETECT_SIZE - 1;

            info_out.Assign(tonal.info[pos]);
            tonal.read_subframe += len / 120;
            while (tonal.read_subframe >= 4)
            {
                tonal.read_subframe -= 4;
                tonal.read_pos++;
            }
            if (tonal.read_pos >= OpusConstants.DETECT_SIZE)
                tonal.read_pos -= OpusConstants.DETECT_SIZE;

            /* Compensate for the delay in the features themselves.
               FIXME: Need a better estimate the 10 I just made up */
            curr_lookahead = Inlines.IMAX(curr_lookahead - 10, 0);

            psum = 0;
            /* Summing the probability of transition patterns that involve music at
               time (DETECT_SIZE-curr_lookahead-1) */
            for (i = 0; i < OpusConstants.DETECT_SIZE - curr_lookahead; i++)
                psum += tonal.pmusic[i];
            for (; i < OpusConstants.DETECT_SIZE; i++)
                psum += tonal.pspeech[i];
            psum = psum * tonal.music_confidence + (1 - psum) * tonal.speech_confidence;
            /*printf("%f %f %f\n", psum, info_out.music_prob, info_out.tonality);*/

            info_out.music_prob = psum;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T">The type of signal being handled (either short or float) - changes based on which API is used</typeparam>
        /// <param name="tonal"></param>
        /// <param name="celt_mode"></param>
        /// <param name="x"></param>
        /// <param name="len"></param>
        /// <param name="offset"></param>
        /// <param name="c1"></param>
        /// <param name="c2"></param>
        /// <param name="C"></param>
        /// <param name="lsb_depth"></param>
        /// <param name="downmix"></param>
        internal static void tonality_analysis<T>(TonalityAnalysisState tonal, CeltMode celt_mode, T[] x, int x_ptr, int len, int offset, int c1, int c2, int C, int lsb_depth, Downmix.downmix_func<T> downmix)
        {
            int i, b;
            FFTState kfft;
            int[] input;
            int[] output;
            int N = 480, N2 = 240;
            float[] A = tonal.angle;
            float[] dA = tonal.d_angle;
            float[] d2A = tonal.d2_angle;
            float[] tonality;
            float[] noisiness;
            float[] band_tonality = new float[OpusConstants.NB_TBANDS];
            float[] logE = new float[OpusConstants.NB_TBANDS];
            float[] BFCC = new float[8];
            float[] features = new float[25];
            float frame_tonality;
            float max_frame_tonality;
            /*float tw_sum=0;*/
            float frame_noisiness;
            float pi4 = (float)(M_PI * M_PI * M_PI * M_PI);
            float slope = 0;
            float frame_stationarity;
            float relativeE;
            float[] frame_probs = new float[2];
            float alpha, alphaE, alphaE2;
            float frame_loudness;
            float bandwidth_mask;
            int bandwidth = 0;
            float maxE = 0;
            float noise_floor;
            int remaining;
            AnalysisInfo info; //[porting note] pointer

            tonal.last_transition++;
            alpha = 1.0f / Inlines.IMIN(20, 1 + tonal.count);
            alphaE = 1.0f / Inlines.IMIN(50, 1 + tonal.count);
            alphaE2 = 1.0f / Inlines.IMIN(1000, 1 + tonal.count);

            if (tonal.count < 4)
                tonal.music_prob = 0.5f;
            kfft = celt_mode.mdct.kfft[0];
            if (tonal.count == 0)
                tonal.mem_fill = 240;

            downmix(x, x_ptr, tonal.inmem, tonal.mem_fill, Inlines.IMIN(len, OpusConstants.ANALYSIS_BUF_SIZE - tonal.mem_fill), offset, c1, c2, C);

            if (tonal.mem_fill + len < OpusConstants.ANALYSIS_BUF_SIZE)
            {
                tonal.mem_fill += len;
                /* Don't have enough to update the analysis */
                return;
            }

            info = tonal.info[tonal.write_pos++];
            if (tonal.write_pos >= OpusConstants.DETECT_SIZE)
                tonal.write_pos -= OpusConstants.DETECT_SIZE;

            input = new int[960];
            output = new int[960];
            tonality = new float[240];
            noisiness = new float[240];
            for (i = 0; i < N2; i++)
            {
                float w = Tables.analysis_window[i];
                input[2 * i] = (int)(w * tonal.inmem[i]);
                input[2 * i + 1] = (int)(w * tonal.inmem[N2 + i]);
                input[(2 * (N - i - 1))] = (int)(w * tonal.inmem[N - i - 1]);
                input[(2 * (N - i - 1)) + 1] = (int)(w * tonal.inmem[N + N2 - i - 1]);
            }
            Arrays.MemMoveInt(tonal.inmem, OpusConstants.ANALYSIS_BUF_SIZE - 240, 0, 240);

            remaining = len - (OpusConstants.ANALYSIS_BUF_SIZE - tonal.mem_fill);
            downmix(x, x_ptr, tonal.inmem, 240, remaining, offset + OpusConstants.ANALYSIS_BUF_SIZE - tonal.mem_fill, c1, c2, C);
            tonal.mem_fill = 240 + remaining;

            KissFFT.opus_fft(kfft, input, output);

            for (i = 1; i < N2; i++)
            {
                float X1r, X2r, X1i, X2i;
                float angle, d_angle, d2_angle;
                float angle2, d_angle2, d2_angle2;
                float mod1, mod2, avg_mod;
                X1r = (float)output[2 * i] + output[2 * (N - i)];
                X1i = (float)output[(2 * i) + 1] - output[2 * (N - i) + 1];
                X2r = (float)output[(2 * i) + 1] + output[2 * (N - i) + 1];
                X2i = (float)output[2 * (N - i)] - output[2 * i];

                angle = (float)(.5f / M_PI) * fast_atan2f(X1i, X1r);
                d_angle = angle - A[i];
                d2_angle = d_angle - dA[i];

                angle2 = (float)(.5f / M_PI) * fast_atan2f(X2i, X2r);
                d_angle2 = angle2 - angle;
                d2_angle2 = d_angle2 - d_angle;

                mod1 = d2_angle - (float)Math.Floor(0.5f + d2_angle);
                noisiness[i] = Inlines.ABS16(mod1);
                mod1 *= mod1;
                mod1 *= mod1;

                mod2 = d2_angle2 - (float)Math.Floor(0.5f + d2_angle2);
                noisiness[i] += Inlines.ABS16(mod2);
                mod2 *= mod2;
                mod2 *= mod2;

                avg_mod = .25f * (d2A[i] + 2.0f * mod1 + mod2);
                tonality[i] = 1.0f / (1.0f + 40.0f * 16.0f * pi4 * avg_mod) - .015f;

                A[i] = angle2;
                dA[i] = d_angle2;
                d2A[i] = mod2;
            }

            frame_tonality = 0;
            max_frame_tonality = 0;
            /*tw_sum = 0;*/
            info.activity = 0;
            frame_noisiness = 0;
            frame_stationarity = 0;
            if (tonal.count == 0)
            {
                for (b = 0; b < OpusConstants.NB_TBANDS; b++)
                {
                    tonal.lowE[b] = 1e10f;
                    tonal.highE[b] = -1e10f;
                }
            }
            relativeE = 0;
            frame_loudness = 0;
            for (b = 0; b < OpusConstants.NB_TBANDS; b++)
            {
                float E = 0, tE = 0, nE = 0;
                float L1, L2;
                float stationarity;
                for (i = Tables.tbands[b]; i < Tables.tbands[b + 1]; i++)
                {
                    float binE = output[2 * i] * (float)output[2 * i] + output[2 * (N - i)] * (float)output[2 * (N - i)]
                               + output[2 * i + 1] * (float)output[2 * i + 1] + output[2 * (N - i) + 1] * (float)output[2 * (N - i) + 1];
                    /* FIXME: It's probably best to change the BFCC filter initial state instead */
                    binE *= 5.55e-17f;
                    E += binE;
                    tE += binE * tonality[i];
                    nE += binE * 2.0f * (.5f - noisiness[i]);
                }

                tonal.E[tonal.E_count][b] = E;
                frame_noisiness += nE / (1e-15f + E);

                frame_loudness += (float)Math.Sqrt(E + 1e-10f);
                logE[b] = (float)Math.Log(E + 1e-10f);
                tonal.lowE[b] = Inlines.MIN32(logE[b], tonal.lowE[b] + 0.01f);
                tonal.highE[b] = Inlines.MAX32(logE[b], tonal.highE[b] - 0.1f);
                if (tonal.highE[b] < tonal.lowE[b] + 1.0f)
                {
                    tonal.highE[b] += 0.5f;
                    tonal.lowE[b] -= 0.5f;
                }
                relativeE += (logE[b] - tonal.lowE[b]) / (1e-15f + tonal.highE[b] - tonal.lowE[b]);

                L1 = L2 = 0;
                for (i = 0; i < OpusConstants.NB_FRAMES; i++)
                {
                    L1 += (float)Math.Sqrt(tonal.E[i][b]);
                    L2 += tonal.E[i][b];
                }

                stationarity = Inlines.MIN16(0.99f, L1 / (float)Math.Sqrt(1e-15 + OpusConstants.NB_FRAMES * L2));
                stationarity *= stationarity;
                stationarity *= stationarity;
                frame_stationarity += stationarity;
                /*band_tonality[b] = tE/(1e-15+E)*/
                band_tonality[b] = Inlines.MAX16(tE / (1e-15f + E), stationarity * tonal.prev_band_tonality[b]);
                frame_tonality += band_tonality[b];
                if (b >= OpusConstants.NB_TBANDS - OpusConstants.NB_TONAL_SKIP_BANDS)
                    frame_tonality -= band_tonality[b - OpusConstants.NB_TBANDS + OpusConstants.NB_TONAL_SKIP_BANDS];
                max_frame_tonality = Inlines.MAX16(max_frame_tonality, (1.0f + .03f * (b - OpusConstants.NB_TBANDS)) * frame_tonality);
                slope += band_tonality[b] * (b - 8);
                tonal.prev_band_tonality[b] = band_tonality[b];
            }

            bandwidth_mask = 0;
            bandwidth = 0;
            maxE = 0;
            noise_floor = 5.7e-4f / (1 << (Inlines.IMAX(0, lsb_depth - 8)));
            noise_floor *= 1 << (15 + CeltConstants.SIG_SHIFT);
            noise_floor *= noise_floor;
            for (b = 0; b < OpusConstants.NB_TOT_BANDS; b++)
            {
                float E = 0;
                int band_start, band_end;
                /* Keep a margin of 300 Hz for aliasing */
                band_start = Tables.extra_bands[b];
                band_end = Tables.extra_bands[b + 1];
                for (i = band_start; i < band_end; i++)
                {
                    float binE = output[2 * i] * (float)output[2 * i] + output[2 * (N - i)] * (float)output[2 * (N - i)]
                               + output[2 * i + 1] * (float)output[2 * i + 1] + output[2 * (N - i) + 1] * (float)output[2 * (N - i) + 1];
                    E += binE;
                }
                maxE = Inlines.MAX32(maxE, E);
                tonal.meanE[b] = Inlines.MAX32((1 - alphaE2) * tonal.meanE[b], E);
                E = Inlines.MAX32(E, tonal.meanE[b]);
                /* Use a simple follower with 13 dB/Bark slope for spreading function */
                bandwidth_mask = Inlines.MAX32(.05f * bandwidth_mask, E);
                /* Consider the band "active" only if all these conditions are met:
                   1) less than 10 dB below the simple follower
                   2) less than 90 dB below the peak band (maximal masking possible considering
                      both the ATH and the loudness-dependent slope of the spreading function)
                   3) above the PCM quantization noise floor
                */
                if (E > .1 * bandwidth_mask && E * 1e9f > maxE && E > noise_floor * (band_end - band_start))
                    bandwidth = b;
            }
            if (tonal.count <= 2)
                bandwidth = 20;
            frame_loudness = 20 * (float)Math.Log10(frame_loudness);
            tonal.Etracker = Inlines.MAX32(tonal.Etracker - .03f, frame_loudness);
            tonal.lowECount *= (1 - alphaE);
            if (frame_loudness < tonal.Etracker - 30)
                tonal.lowECount += alphaE;

            for (i = 0; i < 8; i++)
            {
                float sum = 0;
                for (b = 0; b < 16; b++)
                    sum += Tables.dct_table[i * 16 + b] * logE[b];
                BFCC[i] = sum;
            }

            frame_stationarity /= OpusConstants.NB_TBANDS;
            relativeE /= OpusConstants.NB_TBANDS;
            if (tonal.count < 10)
                relativeE = 0.5f;
            frame_noisiness /= OpusConstants.NB_TBANDS;
            info.activity = frame_noisiness + (1 - frame_noisiness) * relativeE;
            frame_tonality = (max_frame_tonality / (OpusConstants.NB_TBANDS - OpusConstants.NB_TONAL_SKIP_BANDS));
            frame_tonality = Inlines.MAX16(frame_tonality, tonal.prev_tonality * .8f);
            tonal.prev_tonality = frame_tonality;

            slope /= 8 * 8;
            info.tonality_slope = slope;

            tonal.E_count = (tonal.E_count + 1) % OpusConstants.NB_FRAMES;
            tonal.count++;
            info.tonality = frame_tonality;

            for (i = 0; i < 4; i++)
                features[i] = -0.12299f * (BFCC[i] + tonal.mem[i + 24]) + 0.49195f * (tonal.mem[i] + tonal.mem[i + 16]) + 0.69693f * tonal.mem[i + 8] - 1.4349f * tonal.cmean[i];

            for (i = 0; i < 4; i++)
                tonal.cmean[i] = (1 - alpha) * tonal.cmean[i] + alpha * BFCC[i];

            for (i = 0; i < 4; i++)
                features[4 + i] = 0.63246f * (BFCC[i] - tonal.mem[i + 24]) + 0.31623f * (tonal.mem[i] - tonal.mem[i + 16]);
            for (i = 0; i < 3; i++)
                features[8 + i] = 0.53452f * (BFCC[i] + tonal.mem[i + 24]) - 0.26726f * (tonal.mem[i] + tonal.mem[i + 16]) - 0.53452f * tonal.mem[i + 8];

            if (tonal.count > 5)
            {
                for (i = 0; i < 9; i++)
                    tonal.std[i] = (1 - alpha) * tonal.std[i] + alpha * features[i] * features[i];
            }

            for (i = 0; i < 8; i++)
            {
                tonal.mem[i + 24] = tonal.mem[i + 16];
                tonal.mem[i + 16] = tonal.mem[i + 8];
                tonal.mem[i + 8] = tonal.mem[i];
                tonal.mem[i] = BFCC[i];
            }
            for (i = 0; i < 9; i++)
                features[11 + i] = (float)Math.Sqrt(tonal.std[i]);
            features[20] = info.tonality;
            features[21] = info.activity;
            features[22] = frame_stationarity;
            features[23] = info.tonality_slope;
            features[24] = tonal.lowECount;
            
            mlp.mlp_process(Tables.net, features, frame_probs);
            frame_probs[0] = .5f * (frame_probs[0] + 1);
            /* Curve fitting between the MLP probability and the actual probability */
            frame_probs[0] = .01f + 1.21f * frame_probs[0] * frame_probs[0] - .23f * (float)Math.Pow(frame_probs[0], 10);
            /* Probability of active audio (as opposed to silence) */
            frame_probs[1] = .5f * frame_probs[1] + .5f;
            /* Consider that silence has a 50-50 probability. */
            frame_probs[0] = frame_probs[1] * frame_probs[0] + (1 - frame_probs[1]) * .5f;

            /*printf("%f %f ", frame_probs[0], frame_probs[1]);*/
            {
                /* Probability of state transition */
                float tau;
                /* Represents independence of the MLP probabilities, where
                    beta=1 means fully independent. */
                float beta;
                /* Denormalized probability of speech (p0) and music (p1) after update */
                float p0, p1;
                /* Probabilities for "all speech" and "all music" */
                float s0, m0;
                /* Probability sum for renormalisation */
                float psum;
                /* Instantaneous probability of speech and music, with beta pre-applied. */
                float speech0;
                float music0;

                /* One transition every 3 minutes of active audio */
                tau = .00005f * frame_probs[1];
                beta = .05f;
                //if (1)
                {
                    /* Adapt beta based on how "unexpected" the new prob is */
                    float p, q;
                    p = Inlines.MAX16(.05f, Inlines.MIN16(.95f, frame_probs[0]));
                    q = Inlines.MAX16(.05f, Inlines.MIN16(.95f, tonal.music_prob));
                    beta = .01f + .05f * Inlines.ABS16(p - q) / (p * (1 - q) + q * (1 - p));
                }
                /* p0 and p1 are the probabilities of speech and music at this frame
                    using only information from previous frame and applying the
                    state transition model */
                p0 = (1 - tonal.music_prob) * (1 - tau) + tonal.music_prob * tau;
                p1 = tonal.music_prob * (1 - tau) + (1 - tonal.music_prob) * tau;
                /* We apply the current probability with exponent beta to work around
                    the fact that the probability estimates aren't independent. */
                p0 *= (float)Math.Pow(1 - frame_probs[0], beta);
                p1 *= (float)Math.Pow(frame_probs[0], beta);
                /* Normalise the probabilities to get the Marokv probability of music. */
                tonal.music_prob = p1 / (p0 + p1);
                info.music_prob = tonal.music_prob;

                /* This chunk of code deals with delayed decision. */
                psum = 1e-20f;
                /* Instantaneous probability of speech and music, with beta pre-applied. */
                speech0 = (float)Math.Pow(1 - frame_probs[0], beta);
                music0 = (float)Math.Pow(frame_probs[0], beta);
                if (tonal.count == 1)
                {
                    tonal.pspeech[0] = 0.5f;
                    tonal.pmusic[0] = 0.5f;
                }
                /* Updated probability of having only speech (s0) or only music (m0),
                    before considering the new observation. */
                s0 = tonal.pspeech[0] + tonal.pspeech[1];
                m0 = tonal.pmusic[0] + tonal.pmusic[1];
                /* Updates s0 and m0 with instantaneous probability. */
                tonal.pspeech[0] = s0 * (1 - tau) * speech0;
                tonal.pmusic[0] = m0 * (1 - tau) * music0;
                /* Propagate the transition probabilities */
                for (i = 1; i < OpusConstants.DETECT_SIZE - 1; i++)
                {
                    tonal.pspeech[i] = tonal.pspeech[i + 1] * speech0;
                    tonal.pmusic[i] = tonal.pmusic[i + 1] * music0;
                }
                /* Probability that the latest frame is speech, when all the previous ones were music. */
                tonal.pspeech[OpusConstants.DETECT_SIZE - 1] = m0 * tau * speech0;
                /* Probability that the latest frame is music, when all the previous ones were speech. */
                tonal.pmusic[OpusConstants.DETECT_SIZE - 1] = s0 * tau * music0;

                /* Renormalise probabilities to 1 */
                for (i = 0; i < OpusConstants.DETECT_SIZE; i++)
                    psum += tonal.pspeech[i] + tonal.pmusic[i];
                psum = 1.0f / psum;
                for (i = 0; i < OpusConstants.DETECT_SIZE; i++)
                {
                    tonal.pspeech[i] *= psum;
                    tonal.pmusic[i] *= psum;
                }
                psum = tonal.pmusic[0];
                for (i = 1; i < OpusConstants.DETECT_SIZE; i++)
                    psum += tonal.pspeech[i];

                /* Estimate our confidence in the speech/music decisions */
                if (frame_probs[1] > .75)
                {
                    if (tonal.music_prob > .9)
                    {
                        float adapt;
                        adapt = 1.0f / (++tonal.music_confidence_count);
                        tonal.music_confidence_count = Inlines.IMIN(tonal.music_confidence_count, 500);
                        tonal.music_confidence += adapt * Inlines.MAX16(-.2f, frame_probs[0] - tonal.music_confidence);
                    }
                    if (tonal.music_prob < .1)
                    {
                        float adapt;
                        adapt = 1.0f / (++tonal.speech_confidence_count);
                        tonal.speech_confidence_count = Inlines.IMIN(tonal.speech_confidence_count, 500);
                        tonal.speech_confidence += adapt * Inlines.MIN16(.2f, frame_probs[0] - tonal.speech_confidence);
                    }
                }
                else {
                    if (tonal.music_confidence_count == 0)
                        tonal.music_confidence = .9f;
                    if (tonal.speech_confidence_count == 0)
                        tonal.speech_confidence = .1f;
                }
            }
            if (tonal.last_music != ((tonal.music_prob > .5f) ? 1 : 0))
                tonal.last_transition = 0;
            tonal.last_music = (tonal.music_prob > .5f) ? 1 : 0;

            info.bandwidth = bandwidth;
            info.noisiness = frame_noisiness;
            info.valid = 1;
        }

        internal static void run_analysis<T>(TonalityAnalysisState analysis, CeltMode celt_mode, T[] analysis_pcm, int analysis_pcm_ptr,
                         int analysis_frame_size, int frame_size, int c1, int c2, int C, int Fs,
                         int lsb_depth, Downmix.downmix_func<T> downmix, AnalysisInfo analysis_info)
        {
            int offset;
            int pcm_len;

            if (analysis_pcm != null)
            {
                /* Avoid overflow/wrap-around of the analysis buffer */
                analysis_frame_size = Inlines.IMIN((OpusConstants.DETECT_SIZE - 5) * Fs / 100, analysis_frame_size);

                pcm_len = analysis_frame_size - analysis.analysis_offset;
                offset = analysis.analysis_offset;
                do
                {
                    tonality_analysis(analysis, celt_mode, analysis_pcm, analysis_pcm_ptr, Inlines.IMIN(480, pcm_len), offset, c1, c2, C, lsb_depth, downmix);
                    offset += 480;
                    pcm_len -= 480;
                } while (pcm_len > 0);
                analysis.analysis_offset = analysis_frame_size;

                analysis.analysis_offset -= frame_size;
            }

            analysis_info.valid = 0;
            tonality_get_info(analysis, analysis_info, frame_size);
        }
    }
}
