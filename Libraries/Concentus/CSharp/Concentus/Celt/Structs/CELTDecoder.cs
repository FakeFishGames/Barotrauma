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
    
    /// <summary>
    /// Decoder state
    /// </summary>
    internal class CeltDecoder
    {
        internal CeltMode mode = null;
        internal int overlap = 0;
        internal int channels = 0;
        internal int stream_channels = 0;

        internal int downsample = 0;
        internal int start = 0;
        internal int end = 0;
        internal int signalling = 0;

        /* Everything beyond this point gets cleared on a reset */
        internal uint rng = 0;
        internal int error = 0;
        internal int last_pitch_index = 0;
        internal int loss_count = 0;
        internal int postfilter_period = 0;
        internal int postfilter_period_old = 0;
        internal int postfilter_gain = 0;
        internal int postfilter_gain_old = 0;
        internal int postfilter_tapset = 0;
        internal int postfilter_tapset_old = 0;

        internal readonly int[] preemph_memD = new int[2];

        /// <summary>
        /// Scratch space used by the decoder. It is actually a variable-sized
        /// field that resulted in a variable-sized struct. There are 6 distinct regions inside.
        /// I have laid them out into separate variables here,
        /// but these were the original definitions:
        /// val32 decode_mem[],     Size = channels*(DECODE_BUFFER_SIZE+mode.overlap)
        /// val16 lpc[],            Size = channels*LPC_ORDER
        /// val16 oldEBands[],      Size = 2*mode.nbEBands
        /// val16 oldLogE[],        Size = 2*mode.nbEBands
        /// val16 oldLogE2[],       Size = 2*mode.nbEBands
        /// val16 backgroundLogE[], Size = 2*mode.nbEBands
        /// </summary>
        internal int[][] decode_mem = null;
        internal int[][] lpc = null; // Porting note: Split two-part array into separate arrays (one per channel)
        internal int[] oldEBands = null;
        internal int[] oldLogE = null;
        internal int[] oldLogE2 = null;
        internal int[] backgroundLogE = null;

        private void Reset()
        {
            mode = null;
            overlap = 0;
            channels = 0;
            stream_channels = 0;
            downsample = 0;
            start = 0;
            end = 0;
            signalling = 0;
            PartialReset();
        }

        private void PartialReset()
        {
            rng = 0;
            error = 0;
            last_pitch_index = 0;
            loss_count = 0;
            postfilter_period = 0;
            postfilter_period_old = 0;
            postfilter_gain = 0;
            postfilter_gain_old = 0;
            postfilter_tapset = 0;
            postfilter_tapset_old = 0;
            Arrays.MemSetInt(preemph_memD, 0, 2);
            decode_mem = null;
            lpc = null;
            oldEBands = null;
            oldLogE = null;
            oldLogE2 = null;
            backgroundLogE = null;
        }

        #region API functions

        internal void ResetState()
        {
            int i;

            this.PartialReset();

            // We have to reconstitute the dynamic buffers here. fixme: this could be better implemented
            this.decode_mem = new int[this.channels][];
            this.lpc = new int[this.channels][];
            for (int c = 0; c < this.channels; c++)
            {
                this.decode_mem[c] = new int[CeltConstants.DECODE_BUFFER_SIZE + this.mode.overlap];
                this.lpc[c] = new int[CeltConstants.LPC_ORDER];
            }
            this.oldEBands = new int[2 * this.mode.nbEBands];
            this.oldLogE = new int[2 * this.mode.nbEBands];
            this.oldLogE2 = new int[2 * this.mode.nbEBands];
            this.backgroundLogE = new int[2 * this.mode.nbEBands];

            for (i = 0; i < 2 * this.mode.nbEBands; i++)
                this.oldLogE[i] = this.oldLogE2[i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
        }

        internal int celt_decoder_init(int sampling_rate, int channels)
        {
            int ret;
            ret = this.opus_custom_decoder_init(CeltMode.mode48000_960_120, channels);
            if (ret != OpusError.OPUS_OK)
                return ret;
            this.downsample = CeltCommon.resampling_factor(sampling_rate);
            if (this.downsample == 0)
                return OpusError.OPUS_BAD_ARG;
            else
                return OpusError.OPUS_OK;
        }

        private int opus_custom_decoder_init(CeltMode mode, int channels)
        {
            if (channels < 0 || channels > 2)
                return OpusError.OPUS_BAD_ARG;

            if (this == null)
                return OpusError.OPUS_ALLOC_FAIL;

            this.Reset();

            this.mode = mode;
            this.overlap = mode.overlap;
            this.stream_channels = this.channels = channels;

            this.downsample = 1;
            this.start = 0;
            this.end = this.mode.effEBands;
            this.signalling = 1;

            this.loss_count = 0;
            
            //this.decode_mem = new int[channels * (CeltConstants.DECODE_BUFFER_SIZE + mode.overlap));
            //this.lpc = new int[channels * CeltConstants.LPC_ORDER);
            //this.oldEBands = new int[2 * mode.nbEBands);
            //this.oldLogE = new int[2 * mode.nbEBands);
            //this.oldLogE2 = new int[2 * mode.nbEBands);
            //this.backgroundLogE = new int[2 * mode.nbEBands);

            this.ResetState();

            return OpusError.OPUS_OK;
        }

        internal void celt_decode_lost(int N, int LM)
        {
            int c;
            int i;
            int C = this.channels;
            int[][] out_syn = new int[2][];
            int[] out_syn_ptrs = new int[2];
            CeltMode mode;
            int nbEBands;
            int overlap;
            int noise_based;
            short[] eBands;

            mode = this.mode;
            nbEBands = mode.nbEBands;
            overlap = mode.overlap;
            eBands = mode.eBands;

            c = 0; do
            {
                out_syn[c] = this.decode_mem[c];
                out_syn_ptrs[c] = CeltConstants.DECODE_BUFFER_SIZE - N;
            } while (++c < C);
            
            noise_based = (loss_count >= 5 || start != 0) ? 1 : 0;
            if (noise_based != 0)
            {
                /* Noise-based PLC/CNG */
                int[][] X;
                uint seed;
                int end;
                int effEnd;
                int decay;
                end = this.end;
                effEnd = Inlines.IMAX(start, Inlines.IMIN(end, mode.effEBands));

                X = Arrays.InitTwoDimensionalArray<int>(C, N);   /*< Interleaved normalised MDCTs */

                /* Energy decay */
                decay = loss_count == 0 ? ((short)(0.5 + (1.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(1.5f, CeltConstants.DB_SHIFT)*/ : ((short)(0.5 + (0.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(0.5f, CeltConstants.DB_SHIFT)*/;
                c = 0; do
                {
                    for (i = start; i < end; i++)
                        this.oldEBands[c * nbEBands + i] = Inlines.MAX16(backgroundLogE[c * nbEBands + i], this.oldEBands[c * nbEBands + i] - decay);
                } while (++c < C);
                seed = this.rng;
                for (c = 0; c < C; c++)
                {
                    for (i = start; i < effEnd; i++)
                    {
                        int j;
                        int boffs;
                        int blen;
                        boffs = (eBands[i] << LM);
                        blen = (eBands[i + 1] - eBands[i]) << LM;
                        for (j = 0; j < blen; j++)
                        {
                            seed = Bands.celt_lcg_rand(seed);
                            X[c][boffs + j] = (unchecked((int)seed) >> 20);
                        }

                        VQ.renormalise_vector(X[c], 0, blen, CeltConstants.Q15ONE);
                    }
                }
                this.rng = seed;

                c = 0;
                do
                {
                    Arrays.MemMoveInt(this.decode_mem[c], N, 0, CeltConstants.DECODE_BUFFER_SIZE - N + (overlap >> 1));
                } while (++c < C);

                CeltCommon.celt_synthesis(mode, X, out_syn, out_syn_ptrs, this.oldEBands, start, effEnd, C, C, 0, LM, this.downsample, 0);
            }
            else
            {
                /* Pitch-based PLC */
                int[] window;
                int fade = CeltConstants.Q15ONE;
                int pitch_index;
                int[] etmp;
                int[] exc;

                if (loss_count == 0)
                {
                    this.last_pitch_index = pitch_index = CeltCommon.celt_plc_pitch_search(this.decode_mem, C);
                }
                else {
                    pitch_index = this.last_pitch_index;
                    fade = ((short)(0.5 + (.8f) * (((int)1) << (15))))/*Inlines.QCONST16(.8f, 15)*/;
                }

                etmp = new int[overlap];
                exc = new int[CeltConstants.MAX_PERIOD];
                window = mode.window;
                c = 0; do
                {
                    int decay;
                    int attenuation;
                    int S1 = 0;
                    int[] buf;
                    int extrapolation_offset;
                    int extrapolation_len;
                    int exc_length;
                    int j;

                    buf = this.decode_mem[c];
                    for (i = 0; i < CeltConstants.MAX_PERIOD; i++)
                    {
                        exc[i] = Inlines.ROUND16(buf[CeltConstants.DECODE_BUFFER_SIZE - CeltConstants.MAX_PERIOD + i], CeltConstants.SIG_SHIFT);
                    }

                    if (loss_count == 0)
                    {
                        int[] ac = new int[CeltConstants.LPC_ORDER + 1];
                        /* Compute LPC coefficients for the last MAX_PERIOD samples before
                           the first loss so we can work in the excitation-filter domain. */
                        Autocorrelation._celt_autocorr(exc, ac, window, overlap,
                               CeltConstants.LPC_ORDER, CeltConstants.MAX_PERIOD);
                        /* Add a noise floor of -40 dB. */
                        ac[0] += Inlines.SHR32(ac[0], 13);
                        /* Use lag windowing to stabilize the Levinson-Durbin recursion. */
                        for (i = 1; i <= CeltConstants.LPC_ORDER; i++)
                        {
                            /*ac[i] *= exp(-.5*(2*M_PI*.002*i)*(2*M_PI*.002*i));*/
                            ac[i] -= Inlines.MULT16_32_Q15(2 * i * i, ac[i]);
                        }
                        CeltLPC.celt_lpc(this.lpc[c], ac, CeltConstants.LPC_ORDER);
                    }
                    /* We want the excitation for 2 pitch periods in order to look for a
                       decaying signal, but we can't get more than MAX_PERIOD. */
                    exc_length = Inlines.IMIN(2 * pitch_index, CeltConstants.MAX_PERIOD);
                    /* Initialize the LPC history with the samples just before the start
                       of the region for which we're computing the excitation. */
                    {
                        int[] lpc_mem = new int[CeltConstants.LPC_ORDER];
                        for (i = 0; i < CeltConstants.LPC_ORDER; i++)
                        {
                            lpc_mem[i] =
                                  Inlines.ROUND16(buf[CeltConstants.DECODE_BUFFER_SIZE - exc_length - 1 - i], CeltConstants.SIG_SHIFT);
                        }

                        /* Compute the excitation for exc_length samples before the loss. */
#if UNSAFE
                        unsafe
                        {
                            fixed (int* pexc_base = exc, lpc = this.lpc[c])
                            {
                                int* pexc = pexc_base + (CeltConstants.MAX_PERIOD - exc_length);
                                Kernels.celt_fir(pexc, lpc, pexc, exc_length, CeltConstants.LPC_ORDER, lpc_mem);
                            }
                        }
#else
                        Kernels.celt_fir(exc, (CeltConstants.MAX_PERIOD - exc_length), this.lpc[c], 0,
                              exc, (CeltConstants.MAX_PERIOD - exc_length), exc_length, CeltConstants.LPC_ORDER, lpc_mem);
#endif
                    }

                    /* Check if the waveform is decaying, and if so how fast.
                       We do this to avoid adding energy when concealing in a segment
                       with decaying energy. */
                    {
                        int E1 = 1, E2 = 1;
                        int decay_length;
                        int shift = Inlines.IMAX(0, 2 * Inlines.celt_zlog2(Inlines.celt_maxabs16(exc, (CeltConstants.MAX_PERIOD - exc_length), exc_length)) - 20);
                        decay_length = exc_length >> 1;
                        for (i = 0; i < decay_length; i++)
                        {
                            int e;
                            e = exc[CeltConstants.MAX_PERIOD - decay_length + i];
                            E1 += Inlines.SHR32(Inlines.MULT16_16(e, e), shift);
                            e = exc[CeltConstants.MAX_PERIOD - 2 * decay_length + i];
                            E2 += Inlines.SHR32(Inlines.MULT16_16(e, e), shift);
                        }
                        E1 = Inlines.MIN32(E1, E2);
                        decay = Inlines.celt_sqrt(Inlines.frac_div32(Inlines.SHR32(E1, 1), E2));
                    }

                    /* Move the decoder memory one frame to the left to give us room to
                       add the data for the new frame. We ignore the overlap that extends
                       past the end of the buffer, because we aren't going to use it. */
                    Arrays.MemMoveInt(buf, N, 0, CeltConstants.DECODE_BUFFER_SIZE - N);

                    /* Extrapolate from the end of the excitation with a period of
                       "pitch_index", scaling down each period by an additional factor of
                       "decay". */
                    extrapolation_offset = CeltConstants.MAX_PERIOD - pitch_index;
                    /* We need to extrapolate enough samples to cover a complete MDCT
                       window (including overlap/2 samples on both sides). */
                    extrapolation_len = N + overlap;
                    /* We also apply fading if this is not the first loss. */
                    attenuation = Inlines.MULT16_16_Q15(fade, decay);
                    for (i = j = 0; i < extrapolation_len; i++, j++)
                    {
                        int tmp;
                        if (j >= pitch_index)
                        {
                            j -= pitch_index;
                            attenuation = Inlines.MULT16_16_Q15(attenuation, decay);
                        }
                        buf[CeltConstants.DECODE_BUFFER_SIZE - N + i] =
                              Inlines.SHL32((Inlines.MULT16_16_Q15(attenuation,
                                    exc[extrapolation_offset + j])), CeltConstants.SIG_SHIFT);
                        /* Compute the energy of the previously decoded signal whose
                           excitation we're copying. */
                        tmp = Inlines.ROUND16(
                              buf[CeltConstants.DECODE_BUFFER_SIZE - CeltConstants.MAX_PERIOD - N + extrapolation_offset + j],
                              CeltConstants.SIG_SHIFT);
                        S1 += Inlines.SHR32(Inlines.MULT16_16(tmp, tmp), 8);
                    }

                    {
                        int[] lpc_mem = new int[CeltConstants.LPC_ORDER];
                        /* Copy the last decoded samples (prior to the overlap region) to
                           synthesis filter memory so we can have a continuous signal. */
                        for (i = 0; i < CeltConstants.LPC_ORDER; i++)
                            lpc_mem[i] = Inlines.ROUND16(buf[CeltConstants.DECODE_BUFFER_SIZE - N - 1 - i], CeltConstants.SIG_SHIFT);
                        /* Apply the synthesis filter to convert the excitation back into
                           the signal domain. */
                        CeltLPC.celt_iir(buf, CeltConstants.DECODE_BUFFER_SIZE - N, this.lpc[c],
                              buf, CeltConstants.DECODE_BUFFER_SIZE - N, extrapolation_len, CeltConstants.LPC_ORDER,
                              lpc_mem);
                    }

                    /* Check if the synthesis energy is higher than expected, which can
                       happen with the signal changes during our window. If so,
                       attenuate. */
                    {
                        int S2 = 0;
                        for (i = 0; i < extrapolation_len; i++)
                        {
                            int tmp = Inlines.ROUND16(buf[CeltConstants.DECODE_BUFFER_SIZE - N + i], CeltConstants.SIG_SHIFT);
                            S2 += Inlines.SHR32(Inlines.MULT16_16(tmp, tmp), 8);
                        }
                        /* This checks for an "explosion" in the synthesis. */
                        if (!(S1 > Inlines.SHR32(S2, 2)))
                        {
                            for (i = 0; i < extrapolation_len; i++)
                                buf[CeltConstants.DECODE_BUFFER_SIZE - N + i] = 0;
                        }
                        else if (S1 < S2)
                        {
                            int ratio = Inlines.celt_sqrt(Inlines.frac_div32(Inlines.SHR32(S1, 1) + 1, S2 + 1));
                            for (i = 0; i < overlap; i++)
                            {
                                int tmp_g = CeltConstants.Q15ONE
                                      - Inlines.MULT16_16_Q15(window[i], CeltConstants.Q15ONE - ratio);
                                buf[CeltConstants.DECODE_BUFFER_SIZE - N + i] =
                                      Inlines.MULT16_32_Q15(tmp_g, buf[CeltConstants.DECODE_BUFFER_SIZE - N + i]);
                            }
                            for (i = overlap; i < extrapolation_len; i++)
                            {
                                buf[CeltConstants.DECODE_BUFFER_SIZE - N + i] =
                                      Inlines.MULT16_32_Q15(ratio, buf[CeltConstants.DECODE_BUFFER_SIZE - N + i]);
                            }
                        }
                    }

                    /* Apply the pre-filter to the MDCT overlap for the next frame because
                       the post-filter will be re-applied in the decoder after the MDCT
                       overlap. */
                    CeltCommon.comb_filter(etmp, 0, buf, CeltConstants.DECODE_BUFFER_SIZE,
                         this.postfilter_period, this.postfilter_period, overlap,
                         -this.postfilter_gain, -this.postfilter_gain,
                         this.postfilter_tapset, this.postfilter_tapset, null, 0);

                    /* Simulate TDAC on the concealed audio so that it blends with the
                       MDCT of the next frame. */
                    for (i = 0; i < overlap / 2; i++)
                    {
                        buf[CeltConstants.DECODE_BUFFER_SIZE + i] =
                           Inlines.MULT16_32_Q15(window[i], etmp[overlap - 1 - i])
                           + Inlines.MULT16_32_Q15(window[overlap - i - 1], etmp[i]);
                    }
                } while (++c < C);
            }

            this.loss_count = loss_count + 1;
        }

        internal int celt_decode_with_ec(byte[] data, int data_ptr,
              int len, short[] pcm, int pcm_ptr, int frame_size, EntropyCoder dec, int accum)
        {
            int c, i, N;
            int spread_decision;
            int bits;
            int[][] X;
            int[] fine_quant;
            int[] pulses;
            int[] cap;
            int[] offsets;
            int[] fine_priority;
            int[] tf_res;
            byte[] collapse_masks;
            int[][] out_syn = new int[2][];
            int[] out_syn_ptrs = new int[2];
            int[] oldBandE, oldLogE, oldLogE2, backgroundLogE;

            int shortBlocks;
            int isTransient;
            int intra_ener;
            int CC = this.channels;
            int LM, M;
            int start;
            int end;
            int effEnd;
            int codedBands;
            int alloc_trim;
            int postfilter_pitch;
            int postfilter_gain;
            int intensity = 0;
            int dual_stereo = 0;
            int total_bits;
            int balance;
            int tell;
            int dynalloc_logp;
            int postfilter_tapset;
            int anti_collapse_rsv;
            int anti_collapse_on = 0;
            int silence;
            int C = this.stream_channels;
            CeltMode mode; // porting note: pointer
            int nbEBands;
            int overlap;
            short[] eBands;

            mode = this.mode;
            nbEBands = mode.nbEBands;
            overlap = mode.overlap;
            eBands = mode.eBands;
            start = this.start;
            end = this.end;
            frame_size *= this.downsample;
            
            oldBandE = this.oldEBands;
            oldLogE = this.oldLogE;
            oldLogE2 = this.oldLogE2;
            backgroundLogE = this.backgroundLogE;

            {
                for (LM = 0; LM <= mode.maxLM; LM++)
                    if (mode.shortMdctSize << LM == frame_size)
                        break;
                if (LM > mode.maxLM)
                    return OpusError.OPUS_BAD_ARG;
            }
            M = 1 << LM;

            if (len < 0 || len > 1275 || pcm == null)
                return OpusError.OPUS_BAD_ARG;

            N = M * mode.shortMdctSize;
            c = 0; do
            {
                out_syn[c] = this.decode_mem[c];
                out_syn_ptrs[c] = CeltConstants.DECODE_BUFFER_SIZE - N;
            } while (++c < CC);

            effEnd = end;
            if (effEnd > mode.effEBands)
                effEnd = mode.effEBands;

            if (data == null || len <= 1)
            {
                this.celt_decode_lost(N, LM);
                CeltCommon.deemphasis(out_syn, out_syn_ptrs, pcm, pcm_ptr, N, CC, this.downsample, mode.preemph, this.preemph_memD, accum);

                return frame_size / this.downsample;
            }

            if (dec == null)
            {
                // If no entropy decoder was passed into this function, we need to create
                // a new one here for local use only. It only exists in this function scope.
                dec = new EntropyCoder();
                dec.dec_init(data, data_ptr, (uint)len);
            }

            if (C == 1)
            {
                for (i = 0; i < nbEBands; i++)
                    oldBandE[i] = Inlines.MAX16(oldBandE[i], oldBandE[nbEBands + i]);
            }

            total_bits = len * 8;
            tell = dec.tell();

            if (tell >= total_bits)
                silence = 1;
            else if (tell == 1)
                silence = dec.dec_bit_logp(15);
            else
                silence = 0;

            if (silence != 0)
            {
                /* Pretend we've read all the remaining bits */
                tell = len * 8;
                dec.nbits_total += tell - dec.tell();
            }

            postfilter_gain = 0;
            postfilter_pitch = 0;
            postfilter_tapset = 0;
            if (start == 0 && tell + 16 <= total_bits)
            {
                if (dec.dec_bit_logp(1) != 0)
                {
                    int qg, octave;
                    octave = (int)dec.dec_uint(6);
                    postfilter_pitch = (16 << octave) + (int)dec.dec_bits(4 + (uint)octave) - 1;
                    qg = (int)dec.dec_bits(3);
                    if (dec.tell() + 2 <= total_bits)
                        postfilter_tapset = dec.dec_icdf(Tables.tapset_icdf, 2);
                    postfilter_gain = ((short)(0.5 + (.09375f) * (((int)1) << (15))))/*Inlines.QCONST16(.09375f, 15)*/ * (qg + 1);
                }
                tell = dec.tell();
            }

            if (LM > 0 && tell + 3 <= total_bits)
            {
                isTransient = dec.dec_bit_logp(3);
                tell = dec.tell();
            }
            else
                isTransient = 0;

            if (isTransient != 0)
                shortBlocks = M;
            else
                shortBlocks = 0;

            /* Decode the global flags (first symbols in the stream) */
            intra_ener = tell + 3 <= total_bits ? dec.dec_bit_logp(3) : 0;
            /* Get band energies */
            QuantizeBands.unquant_coarse_energy(mode, start, end, oldBandE,
                  intra_ener, dec, C, LM);

            tf_res = new int[nbEBands];
            CeltCommon.tf_decode(start, end, isTransient, tf_res, LM, dec);

            tell = dec.tell();
            spread_decision = Spread.SPREAD_NORMAL;
            if (tell + 4 <= total_bits)
                spread_decision = dec.dec_icdf(Tables.spread_icdf, 5);

            cap = new int[nbEBands];

            CeltCommon.init_caps(mode, cap, LM, C);

            offsets = new int[nbEBands];

            dynalloc_logp = 6;
            total_bits <<= EntropyCoder.BITRES;
            tell = (int)dec.tell_frac();
            for (i = start; i < end; i++)
            {
                int width, quanta;
                int dynalloc_loop_logp;
                int boost;
                width = C * (eBands[i + 1] - eBands[i]) << LM;
                /* quanta is 6 bits, but no more than 1 bit/sample
                   and no less than 1/8 bit/sample */
                quanta = Inlines.IMIN(width << EntropyCoder.BITRES, Inlines.IMAX(6 << EntropyCoder.BITRES, width));
                dynalloc_loop_logp = dynalloc_logp;
                boost = 0;
                while (tell + (dynalloc_loop_logp << EntropyCoder.BITRES) < total_bits && boost < cap[i])
                {
                    int flag;
                    flag = dec.dec_bit_logp((uint)dynalloc_loop_logp);
                    tell = (int)dec.tell_frac();
                    if (flag == 0)
                        break;
                    boost += quanta;
                    total_bits -= quanta;
                    dynalloc_loop_logp = 1;
                }
                offsets[i] = boost;
                /* Making dynalloc more likely */
                if (boost > 0)
                    dynalloc_logp = Inlines.IMAX(2, dynalloc_logp - 1);
            }

            fine_quant = new int[nbEBands];
            alloc_trim = tell + (6 << EntropyCoder.BITRES) <= total_bits ?
                  dec.dec_icdf(Tables.trim_icdf, 7) : 5;

            bits = (((int)len * 8) << EntropyCoder.BITRES) - (int)dec.tell_frac() - 1;
            anti_collapse_rsv = isTransient != 0 && LM >= 2 && bits >= ((LM + 2) << EntropyCoder.BITRES) ? (1 << EntropyCoder.BITRES) : 0;
            bits -= anti_collapse_rsv;

            pulses = new int[nbEBands];
            fine_priority = new int[nbEBands];
            
            codedBands = Rate.compute_allocation(mode, start, end, offsets, cap,
                  alloc_trim, ref intensity, ref dual_stereo, bits, out balance, pulses,
                  fine_quant, fine_priority, C, LM, dec, 0, 0, 0);

            QuantizeBands.unquant_fine_energy(mode, start, end, oldBandE, fine_quant, dec, C);

            c = 0;
            do
            {
                Arrays.MemMoveInt(decode_mem[c], N, 0, CeltConstants.DECODE_BUFFER_SIZE - N + overlap / 2);
            } while (++c < CC);

            /* Decode fixed codebook */
            collapse_masks = new byte[C * nbEBands];

            X = Arrays.InitTwoDimensionalArray<int>(C, N);   /*< Interleaved normalised MDCTs */
            
            Bands.quant_all_bands(0, mode, start, end, X[0], C == 2 ? X[1] : null, collapse_masks,
                  null, pulses, shortBlocks, spread_decision, dual_stereo, intensity, tf_res,
                  len * (8 << EntropyCoder.BITRES) - anti_collapse_rsv, balance, dec, LM, codedBands, ref this.rng);

            if (anti_collapse_rsv > 0)
            {
                anti_collapse_on = (int)dec.dec_bits(1);
            }

            QuantizeBands.unquant_energy_finalise(mode, start, end, oldBandE,
                  fine_quant, fine_priority, len * 8 - dec.tell(), dec, C);

            if (anti_collapse_on != 0)
                Bands.anti_collapse(mode, X, collapse_masks, LM, C, N,
                      start, end, oldBandE, oldLogE, oldLogE2, pulses, this.rng);

            if (silence != 0)
            {
                for (i = 0; i < C * nbEBands; i++)
                    oldBandE[i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
            }

            CeltCommon.celt_synthesis(mode, X, out_syn, out_syn_ptrs, oldBandE, start, effEnd,
                           C, CC, isTransient, LM, this.downsample, silence);

            c = 0; do
            {
                this.postfilter_period = Inlines.IMAX(this.postfilter_period, CeltConstants.COMBFILTER_MINPERIOD);
                this.postfilter_period_old = Inlines.IMAX(this.postfilter_period_old, CeltConstants.COMBFILTER_MINPERIOD);
                CeltCommon.comb_filter(out_syn[c], out_syn_ptrs[c], out_syn[c], out_syn_ptrs[c], this.postfilter_period_old, this.postfilter_period, mode.shortMdctSize,
                      this.postfilter_gain_old, this.postfilter_gain, this.postfilter_tapset_old, this.postfilter_tapset,
                      mode.window, overlap);
                if (LM != 0)
                {
                    CeltCommon.comb_filter(
                        out_syn[c], out_syn_ptrs[c] + (mode.shortMdctSize),
                        out_syn[c], out_syn_ptrs[c] + (mode.shortMdctSize),
                        this.postfilter_period, postfilter_pitch, N - mode.shortMdctSize,
                        this.postfilter_gain, postfilter_gain, this.postfilter_tapset, postfilter_tapset,
                        mode.window, overlap);
                }

            } while (++c < CC);
            this.postfilter_period_old = this.postfilter_period;
            this.postfilter_gain_old = this.postfilter_gain;
            this.postfilter_tapset_old = this.postfilter_tapset;
            this.postfilter_period = postfilter_pitch;
            this.postfilter_gain = postfilter_gain;
            this.postfilter_tapset = postfilter_tapset;
            if (LM != 0)
            {
                this.postfilter_period_old = this.postfilter_period;
                this.postfilter_gain_old = this.postfilter_gain;
                this.postfilter_tapset_old = this.postfilter_tapset;
            }

            if (C == 1)
            {
                Array.Copy(oldBandE, 0, oldBandE, nbEBands, nbEBands);
            }

            /* In case start or end were to change */
            if (isTransient == 0)
            {
                int max_background_increase;
                Array.Copy(oldLogE, oldLogE2, 2 * nbEBands);
                Array.Copy(oldBandE, oldLogE, 2 * nbEBands);
                /* In normal circumstances, we only allow the noise floor to increase by
                   up to 2.4 dB/second, but when we're in DTX, we allow up to 6 dB
                   increase for each update.*/
                if (this.loss_count < 10)
                    max_background_increase = M * ((short)(0.5 + (0.001f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(0.001f, CeltConstants.DB_SHIFT)*/;
                else
                    max_background_increase = ((short)(0.5 + (1.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(1.0f, CeltConstants.DB_SHIFT)*/;
                for (i = 0; i < 2 * nbEBands; i++)
                    backgroundLogE[i] = Inlines.MIN16(backgroundLogE[i] + max_background_increase, oldBandE[i]);
            }
            else {
                for (i = 0; i < 2 * nbEBands; i++)
                    oldLogE[i] = Inlines.MIN16(oldLogE[i], oldBandE[i]);
            }
            c = 0; do
            {
                for (i = 0; i < start; i++)
                {
                    oldBandE[c * nbEBands + i] = 0;
                    oldLogE[c * nbEBands + i] = oldLogE2[c * nbEBands + i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
                }
                for (i = end; i < nbEBands; i++)
                {
                    oldBandE[c * nbEBands + i] = 0;
                    oldLogE[c * nbEBands + i] = oldLogE2[c * nbEBands + i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
                }
            } while (++c < 2);
            this.rng = dec.rng;

            CeltCommon.deemphasis(out_syn, out_syn_ptrs, pcm, pcm_ptr, N, CC, this.downsample, mode.preemph, this.preemph_memD, accum);
            this.loss_count = 0;

            if (dec.tell() > 8 * len)
                return OpusError.OPUS_INTERNAL_ERROR;
            if (dec.get_error() != 0)
                this.error = 1;
            return frame_size / this.downsample;
        }

#endregion

#region Getters and Setters

        internal void SetStartBand(int value)
        {
            if (value < 0 || value >= this.mode.nbEBands)
                throw new ArgumentException("Start band above max number of ebands (or negative)");
            this.start = value;
        }

        internal void SetEndBand(int value)
        {
            if (value < 1 || value > this.mode.nbEBands)
                throw new ArgumentException("End band above max number of ebands (or less than 1)");
            this.end = value;
        }

        internal void SetChannels(int value)
        {
            if (value < 1 || value > 2)
                throw new ArgumentException("Channel count must be 1 or 2");
            this.stream_channels = value;
        }

        internal int GetAndClearError()
        {
            int returnVal = this.error;
            this.error = 0;
            return returnVal;
        }

        public int GetLookahead()
        {
            return this.overlap / this.downsample;
        }

        public int GetPitch()
        {
            return this.postfilter_period;
        }

        public CeltMode GetMode()
        {
            return this.mode;
        }

        public void SetSignalling(int value)
        {
            this.signalling = value;
        }

        public uint GetFinalRange()
        {
            return this.rng;
        }

#endregion
    }
}
