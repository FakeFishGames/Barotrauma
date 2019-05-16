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

    internal class CeltEncoder
    {
        #region Encoder state
        internal CeltMode mode = null;     /*< Mode used by the encoder. Without custom modes, this always refers to the same predefined struct */
        internal int channels = 0;
        internal int stream_channels = 0;

        internal int force_intra = 0;
        internal int clip = 0;
        internal int disable_pf = 0;
        internal int complexity = 0;
        internal int upsample = 0;
        internal int start = 0;
        internal int end = 0;

        internal int bitrate = 0;
        internal int vbr = 0;
        internal int signalling = 0;

        /* If zero, VBR can do whatever it likes with the rate */
        internal int constrained_vbr = 0;
        internal int loss_rate = 0;
        internal int lsb_depth = 0;
        internal OpusFramesize variable_duration = 0;
        internal int lfe = 0;

        /* Everything beyond this point gets cleared on a reset */

        internal uint rng = 0;
        internal int spread_decision = 0;
        internal int delayedIntra = 0;
        internal int tonal_average = 0;
        internal int lastCodedBands = 0;
        internal int hf_average = 0;
        internal int tapset_decision = 0;

        internal int prefilter_period = 0;
        internal int prefilter_gain = 0;
        internal int prefilter_tapset = 0;
        internal int consec_transient = 0;
        internal AnalysisInfo analysis = new AnalysisInfo();

        internal readonly int[] preemph_memE = new int[2];
        internal readonly int[] preemph_memD = new int[2];

        /* VBR-related parameters */
        internal int vbr_reservoir = 0;
        internal int vbr_drift = 0;
        internal int vbr_offset = 0;
        internal int vbr_count = 0;
        internal int overlap_max = 0;
        internal int stereo_saving = 0;
        internal int intensity = 0;
        internal int[] energy_mask = null;
        internal int spec_avg = 0;

        /// <summary>
        /// The original C++ defined in_mem as a single float[1] which was the "caboose"
        /// to the overall encoder struct, containing 5 separate variable-sized buffer
        /// spaces of heterogeneous datatypes. I have laid them out into separate variables here,
        /// but these were the original definitions:
        /// val32 in_mem[],        Size = channels*mode.overlap
        /// val32 prefilter_mem[], Size = channels*COMBFILTER_MAXPERIOD
        /// val16 oldBandE[],      Size = channels*mode.nbEBands
        /// val16 oldLogE[],       Size = channels*mode.nbEBands
        /// val16 oldLogE2[],      Size = channels*mode.nbEBands
        /// </summary>
        internal int[][] in_mem = null;
        internal int[][] prefilter_mem = null;
        internal int[][] oldBandE = null;
        internal int[][] oldLogE = null;
        internal int[][] oldLogE2 = null;

        private void Reset()
        {
            mode = null;
            channels = 0;
            stream_channels = 0;
            force_intra = 0;
            clip = 0;
            disable_pf = 0;
            complexity = 0;
            upsample = 0;
            start = 0;
            end = 0;
            bitrate = 0;
            vbr = 0;
            signalling = 0;
            constrained_vbr = 0;
            loss_rate = 0;
            lsb_depth = 0;
            variable_duration = 0;
            lfe = 0;
            PartialReset();
        }

        private void PartialReset()
        {
            rng = 0;
            spread_decision = 0;
            delayedIntra = 0;
            tonal_average = 0;
            lastCodedBands = 0;
            hf_average = 0;
            tapset_decision = 0;
            prefilter_period = 0;
            prefilter_gain = 0;
            prefilter_tapset = 0;
            consec_transient = 0;
            analysis.Reset();
            preemph_memE[0] = 0;
            preemph_memE[1] = 0;
            preemph_memD[0] = 0;
            preemph_memD[1] = 0;
            vbr_reservoir = 0;
            vbr_drift = 0;
            vbr_offset = 0;
            vbr_count = 0;
            overlap_max = 0;
            stereo_saving = 0;
            intensity = 0;
            energy_mask = null;
            spec_avg = 0;
            in_mem = null;
            prefilter_mem = null;
            oldBandE = null;
            oldLogE = null;
            oldLogE2 = null;
        }

#endregion

#region API functions

        internal void ResetState()
        {
            int i;

            this.PartialReset();

            // We have to reconstitute the dynamic buffers here.
            this.in_mem = Arrays.InitTwoDimensionalArray<int>(this.channels, this.mode.overlap);
            this.prefilter_mem = Arrays.InitTwoDimensionalArray<int>(this.channels, CeltConstants.COMBFILTER_MAXPERIOD);
            this.oldBandE = Arrays.InitTwoDimensionalArray<int>(this.channels, this.mode.nbEBands);
            this.oldLogE = Arrays.InitTwoDimensionalArray<int>(this.channels, this.mode.nbEBands);
            this.oldLogE2 = Arrays.InitTwoDimensionalArray<int>(this.channels, this.mode.nbEBands);

            for (i = 0; i < this.mode.nbEBands; i++)
            {
                this.oldLogE[0][i] = this.oldLogE2[0][i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
            }
            if (this.channels == 2)
            {
                for (i = 0; i < this.mode.nbEBands; i++)
                {
                    this.oldLogE[1][i] = this.oldLogE2[1][i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
                }
            }
            this.vbr_offset = 0;
            this.delayedIntra = 1;
            this.spread_decision = Spread.SPREAD_NORMAL;
            this.tonal_average = 256;
            this.hf_average = 0;
            this.tapset_decision = 0;
        }

        internal int opus_custom_encoder_init_arch(CeltMode mode,
                                                 int channels)
        {
            if (channels < 0 || channels > 2)
                return OpusError.OPUS_BAD_ARG;

            if (this == null || mode == null)
                return OpusError.OPUS_ALLOC_FAIL;

            this.Reset();

            this.mode = mode;
            this.stream_channels = this.channels = channels;

            this.upsample = 1;
            this.start = 0;
            this.end = this.mode.effEBands;
            this.signalling = 1;

            this.constrained_vbr = 1;
            this.clip = 1;

            this.bitrate = OpusConstants.OPUS_BITRATE_MAX;
            this.vbr = 0;
            this.force_intra = 0;
            this.complexity = 5;
            this.lsb_depth = 24;

            //this.in_mem = new int[channels * mode.overlap);
            //this.prefilter_mem = new int[channels * CeltConstants.COMBFILTER_MAXPERIOD);
            //this.oldBandE = new int[channels * mode.nbEBands);
            //this.oldLogE = new int[channels * mode.nbEBands);
            //this.oldLogE2 = new int[channels * mode.nbEBands);

            this.ResetState();

            return OpusError.OPUS_OK;
        }

        internal int celt_encoder_init(int sampling_rate, int channels)
        {
            int ret;
            ret = this.opus_custom_encoder_init_arch(CeltMode.mode48000_960_120, channels);
            if (ret != OpusError.OPUS_OK)
                return ret;
            this.upsample = CeltCommon.resampling_factor(sampling_rate);
            return OpusError.OPUS_OK;
        }
        
        internal int run_prefilter(int[][] input, int[][] prefilter_mem, int CC, int N,
              int prefilter_tapset, out int pitch, out int gain, out int qgain, int enabled, int nbAvailableBytes)
        {
            int c;
            int[][] pre = new int[CC][];
            CeltMode mode; // [porting note] pointer
            int pitch_index;
            int gain1;
            int pf_threshold;
            int pf_on;
            int qg;
            int overlap;

            mode = this.mode;
            overlap = mode.overlap;
            for (int z = 0; z < CC; z++)
            {
                pre[z] = new int[N + CeltConstants.COMBFILTER_MAXPERIOD];
            }

            c = 0;
            do
            {
                Array.Copy(prefilter_mem[c], 0, pre[c], 0, CeltConstants.COMBFILTER_MAXPERIOD);
                Array.Copy(input[c], overlap, pre[c], CeltConstants.COMBFILTER_MAXPERIOD, N);
            } while (++c < CC);

            if (enabled != 0)
            {
                int[] pitch_buf = new int[(CeltConstants.COMBFILTER_MAXPERIOD + N) >> 1];

                Concentus.Celt.Pitch.pitch_downsample(pre, pitch_buf, CeltConstants.COMBFILTER_MAXPERIOD + N, CC);
                /* Don't search for the fir last 1.5 octave of the range because
                   there's too many false-positives due to short-term correlation */
                Concentus.Celt.Pitch.pitch_search(pitch_buf, CeltConstants.COMBFILTER_MAXPERIOD >> 1, pitch_buf, N,
                      CeltConstants.COMBFILTER_MAXPERIOD - 3 * CeltConstants.COMBFILTER_MINPERIOD, out pitch_index);
                pitch_index = CeltConstants.COMBFILTER_MAXPERIOD - pitch_index;
                gain1 = Concentus.Celt.Pitch.remove_doubling(pitch_buf, CeltConstants.COMBFILTER_MAXPERIOD, CeltConstants.COMBFILTER_MINPERIOD,
                      N, ref pitch_index, this.prefilter_period, this.prefilter_gain);
                if (pitch_index > CeltConstants.COMBFILTER_MAXPERIOD - 2)
                    pitch_index = CeltConstants.COMBFILTER_MAXPERIOD - 2;
                gain1 = Inlines.MULT16_16_Q15(((short)(0.5 + (.7f) * (((int)1) << (15))))/*Inlines.QCONST16(.7f, 15)*/, gain1);
                /*printf("%d %d %f %f\n", pitch_change, pitch_index, gain1, st.analysis.tonality);*/
                if (this.loss_rate > 2)
                    gain1 = Inlines.HALF32(gain1);
                if (this.loss_rate > 4)
                    gain1 = Inlines.HALF32(gain1);
                if (this.loss_rate > 8)
                    gain1 = 0;
            }
            else {
                gain1 = 0;
                pitch_index = CeltConstants.COMBFILTER_MINPERIOD;
            }

            /* Gain threshold for enabling the prefilter/postfilter */
            pf_threshold = ((short)(0.5 + (.2f) * (((int)1) << (15))))/*Inlines.QCONST16(.2f, 15)*/;

            /* Adjusting the threshold based on rate and continuity */
            if (Inlines.abs(pitch_index - this.prefilter_period) * 10 > pitch_index)
                pf_threshold += ((short)(0.5 + (.2f) * (((int)1) << (15))))/*Inlines.QCONST16(.2f, 15)*/;
            if (nbAvailableBytes < 25)
                pf_threshold += ((short)(0.5 + (.1f) * (((int)1) << (15))))/*Inlines.QCONST16(.1f, 15)*/;
            if (nbAvailableBytes < 35)
                pf_threshold += ((short)(0.5 + (.1f) * (((int)1) << (15))))/*Inlines.QCONST16(.1f, 15)*/;
            if (this.prefilter_gain > ((short)(0.5 + (.4f) * (((int)1) << (15))))/*Inlines.QCONST16(.4f, 15)*/)
                pf_threshold -= ((short)(0.5 + (.1f) * (((int)1) << (15))))/*Inlines.QCONST16(.1f, 15)*/;
            if (this.prefilter_gain > ((short)(0.5 + (.55f) * (((int)1) << (15))))/*Inlines.QCONST16(.55f, 15)*/)
                pf_threshold -= ((short)(0.5 + (.1f) * (((int)1) << (15))))/*Inlines.QCONST16(.1f, 15)*/;

            /* Hard threshold at 0.2 */
            pf_threshold = Inlines.MAX16(pf_threshold, ((short)(0.5 + (.2f) * (((int)1) << (15))))/*Inlines.QCONST16(.2f, 15)*/);

            if (gain1 < pf_threshold)
            {
                gain1 = 0;
                pf_on = 0;
                qg = 0;
            }
            else
            {
                /*This block is not gated by a total bits check only because
                  of the nbAvailableBytes check above.*/
                if (Inlines.ABS32(gain1 - this.prefilter_gain) < ((short)(0.5 + (.1f) * (((int)1) << (15))))/*Inlines.QCONST16(.1f, 15)*/)
                    gain1 = this.prefilter_gain;

                qg = ((gain1 + 1536) >> 10) / 3 - 1;
                qg = Inlines.IMAX(0, Inlines.IMIN(7, qg));
                gain1 = ((short)(0.5 + (0.09375f) * (((int)1) << (15))))/*Inlines.QCONST16(0.09375f, 15)*/ * (qg + 1);
                pf_on = 1;
            }
            /*printf("%d %f\n", pitch_index, gain1);*/

            c = 0;
            do
            {
                int offset = mode.shortMdctSize - overlap;
                this.prefilter_period = Inlines.IMAX(this.prefilter_period, CeltConstants.COMBFILTER_MINPERIOD);
                Array.Copy(this.in_mem[c], 0, input[c], 0, overlap);
                if (offset != 0)
                {
                    CeltCommon.comb_filter(input[c], (overlap), pre[c], (CeltConstants.COMBFILTER_MAXPERIOD),
                          this.prefilter_period, this.prefilter_period, offset, -this.prefilter_gain, -this.prefilter_gain,
                          this.prefilter_tapset, this.prefilter_tapset, null, 0); // opt: lots of pointer allocations here
                }

                CeltCommon.comb_filter(input[c], (overlap + offset), pre[c], (CeltConstants.COMBFILTER_MAXPERIOD + offset),
                      this.prefilter_period, pitch_index, N - offset, -this.prefilter_gain, -gain1,
                      this.prefilter_tapset, prefilter_tapset, mode.window, overlap);
                Array.Copy(input[c], N, this.in_mem[c], 0, overlap);

                if (N > CeltConstants.COMBFILTER_MAXPERIOD)
                {
                    Array.Copy(pre[c], N, prefilter_mem[c], 0, CeltConstants.COMBFILTER_MAXPERIOD);
                }
                else
                {
                    Arrays.MemMove(prefilter_mem[c], N, 0, CeltConstants.COMBFILTER_MAXPERIOD - N);
                    Array.Copy(pre[c], CeltConstants.COMBFILTER_MAXPERIOD, prefilter_mem[c], CeltConstants.COMBFILTER_MAXPERIOD - N, N);
                }
            } while (++c < CC);


            gain = gain1;
            pitch = pitch_index;
            qgain = qg;
            return pf_on;
        }

        
        internal int celt_encode_with_ec(short[] pcm, int pcm_ptr, int frame_size, byte[] compressed, int compressed_ptr, int nbCompressedBytes, EntropyCoder enc)
        {
            int i, c, N;
            int bits;
            int[][] input;
            int[][] freq;
            int[][] X;
            int[][] bandE;
            int[][] bandLogE;
            int[][] bandLogE2;
            int[] fine_quant;
            int[][] error;
            int[] pulses;
            int[] cap;
            int[] offsets;
            int[] fine_priority;
            int[] tf_res;
            byte[] collapse_masks;
            int shortBlocks = 0;
            int isTransient = 0;
            int CC = this.channels;
            int C = this.stream_channels;
            int LM, M;
            int tf_select;
            int nbFilledBytes, nbAvailableBytes;
            int start;
            int end;
            int effEnd;
            int codedBands;
            int tf_sum;
            int alloc_trim;
            int pitch_index = CeltConstants.COMBFILTER_MINPERIOD;
            int gain1 = 0;
            int dual_stereo = 0;
            int effectiveBytes;
            int dynalloc_logp;
            int vbr_rate;
            int total_bits;
            int total_boost;
            int balance;
            int tell;
            int prefilter_tapset = 0;
            int pf_on;
            int anti_collapse_rsv;
            int anti_collapse_on = 0;
            int silence = 0;
            int tf_chan = 0;
            int tf_estimate;
            int pitch_change = 0;
            int tot_boost;
            int sample_max;
            int maxDepth;
            CeltMode mode;
            int nbEBands;
            int overlap;
            short[] eBands;
            int secondMdct;
            int signalBandwidth;
            int transient_got_disabled = 0;
            int surround_masking = 0;
            int temporal_vbr = 0;
            int surround_trim = 0;
            int equiv_rate = 510000;
            int[] surround_dynalloc;

            mode = this.mode;
            nbEBands = mode.nbEBands;
            overlap = mode.overlap;
            eBands = mode.eBands;
            start = this.start;
            end = this.end;
            tf_estimate = 0;
            if (nbCompressedBytes < 2 || pcm == null)
            {
                return OpusError.OPUS_BAD_ARG;
            }

            frame_size *= this.upsample;
            for (LM = 0; LM <= mode.maxLM; LM++)
                if (mode.shortMdctSize << LM == frame_size)
                    break;
            if (LM > mode.maxLM)
            {
                return OpusError.OPUS_BAD_ARG;
            }
            M = 1 << LM;
            N = M * mode.shortMdctSize;

            if (enc == null)
            {
                tell = 1;
                nbFilledBytes = 0;
            }
            else {
                tell = enc.tell();
                nbFilledBytes = (tell + 4) >> 3;
            }

            Inlines.OpusAssert(this.signalling == 0);

            /* Can't produce more than 1275 output bytes */
            nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, 1275);
            nbAvailableBytes = nbCompressedBytes - nbFilledBytes;

            if (this.vbr != 0 && this.bitrate != OpusConstants.OPUS_BITRATE_MAX)
            {
                int den = mode.Fs >> EntropyCoder.BITRES;
                vbr_rate = (this.bitrate * frame_size + (den >> 1)) / den;
                effectiveBytes = vbr_rate >> (3 + EntropyCoder.BITRES);
            }
            else {
                int tmp;
                vbr_rate = 0;
                tmp = this.bitrate * frame_size;
                if (tell > 1)
                    tmp += tell;
                if (this.bitrate != OpusConstants.OPUS_BITRATE_MAX)
                    nbCompressedBytes = Inlines.IMAX(2, Inlines.IMIN(nbCompressedBytes,
                          (tmp + 4 * mode.Fs) / (8 * mode.Fs) - (this.signalling != 0 ? 1 : 0)));
                effectiveBytes = nbCompressedBytes;
            }
            if (this.bitrate != OpusConstants.OPUS_BITRATE_MAX)
                equiv_rate = this.bitrate - (40 * C + 20) * ((400 >> LM) - 50);

            if (enc == null)
            {
                enc = new EntropyCoder();
                enc.enc_init(compressed, compressed_ptr, (uint)nbCompressedBytes);
            }

            if (vbr_rate > 0)
            {
                /* Computes the max bit-rate allowed in VBR mode to avoid violating the
                    target rate and buffering.
                   We must do this up front so that bust-prevention logic triggers
                    correctly if we don't have enough bits. */
                if (this.constrained_vbr != 0)
                {
                    int vbr_bound;
                    int max_allowed;
                    /* We could use any multiple of vbr_rate as bound (depending on the
                        delay).
                       This is clamped to ensure we use at least two bytes if the encoder
                        was entirely empty, but to allow 0 in hybrid mode. */
                    vbr_bound = vbr_rate;
                    max_allowed = Inlines.IMIN(Inlines.IMAX(tell == 1 ? 2 : 0,
                          (vbr_rate + vbr_bound - this.vbr_reservoir) >> (EntropyCoder.BITRES + 3)),
                          nbAvailableBytes);
                    if (max_allowed < nbAvailableBytes)
                    {
                        nbCompressedBytes = nbFilledBytes + max_allowed;
                        nbAvailableBytes = max_allowed;
                        enc.enc_shrink((uint)nbCompressedBytes);
                    }
                }
            }
            total_bits = nbCompressedBytes * 8;

            effEnd = end;
            if (effEnd > mode.effEBands)
                effEnd = mode.effEBands;

            input = Arrays.InitTwoDimensionalArray<int>(CC, N + overlap);

            sample_max = Inlines.MAX32(this.overlap_max, Inlines.celt_maxabs32(pcm, pcm_ptr, C * (N - overlap) / this.upsample));
            this.overlap_max = Inlines.celt_maxabs32(pcm, pcm_ptr + (C * (N - overlap) / this.upsample), C * overlap / this.upsample);
            sample_max = Inlines.MAX32(sample_max, this.overlap_max);
            silence = (sample_max == 0) ? 1 : 0;
#if FUZZING
            if ((new Random().Next() & 0x3F) == 0)
                silence = 1;
#endif
            if (tell == 1)
                enc.enc_bit_logp(silence, 15);
            else
                silence = 0;
            if (silence != 0)
            {
                /*In VBR mode there is no need to send more than the minimum. */
                if (vbr_rate > 0)
                {
                    effectiveBytes = nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, nbFilledBytes + 2);
                    total_bits = nbCompressedBytes * 8;
                    nbAvailableBytes = 2;
                    enc.enc_shrink((uint)nbCompressedBytes);
                }
                /* Pretend we've filled all the remaining bits with zeros
                      (that's what the initialiser did anyway) */
                tell = nbCompressedBytes * 8;
                enc.nbits_total += tell - enc.tell();
            }
            c = 0;
            do
            {
                int need_clip = 0;
                CeltCommon.celt_preemphasis(pcm, pcm_ptr + c, input[c], overlap, N, CC, this.upsample,
                            mode.preemph, ref this.preemph_memE[c], need_clip);
            } while (++c < CC);

            /* Find pitch period and gain */
            {
                int enabled;
                int qg;
                enabled = (((this.lfe != 0 && nbAvailableBytes > 3) || nbAvailableBytes > 12 * C) && start == 0 && silence == 0 && this.disable_pf == 0
                      && this.complexity >= 5 && !(this.consec_transient != 0 && LM != 3 && this.variable_duration == OpusFramesize.OPUS_FRAMESIZE_VARIABLE)) ? 1 : 0;

                prefilter_tapset = this.tapset_decision;
                pf_on = this.run_prefilter(input, this.prefilter_mem, CC, N, prefilter_tapset, out pitch_index, out gain1, out qg, enabled, nbAvailableBytes);

                if ((gain1 > ((short)(0.5 + (.4f) * (((int)1) << (15))))/*Inlines.QCONST16(.4f, 15)*/ || this.prefilter_gain > ((short)(0.5 + (.4f) * (((int)1) << (15))))/*Inlines.QCONST16(.4f, 15)*/) && (this.analysis.valid == 0 || this.analysis.tonality > .3)
                      && (pitch_index > 1.26 * this.prefilter_period || pitch_index < .79 * this.prefilter_period))
                    pitch_change = 1;
                if (pf_on == 0)
                {
                    if (start == 0 && tell + 16 <= total_bits)
                        enc.enc_bit_logp(0, 1);
                }
                else {
                    /*This block is not gated by a total bits check only because
                      of the nbAvailableBytes check above.*/
                    int octave;
                    enc.enc_bit_logp(1, 1);
                    pitch_index += 1;
                    octave = Inlines.EC_ILOG((uint)pitch_index) - 5;
                    enc.enc_uint((uint)octave, 6);
                    enc.enc_bits((uint)(pitch_index - (16 << octave)), (uint)(4 + octave));
                    pitch_index -= 1;
                    enc.enc_bits((uint)qg, 3);
                    enc.enc_icdf(prefilter_tapset, Tables.tapset_icdf, 2);
                }
            }

            isTransient = 0;
            shortBlocks = 0;
            if (this.complexity >= 1 && this.lfe == 0)
            {
                isTransient = CeltCommon.transient_analysis(input, N + overlap, CC,
                      out tf_estimate, out tf_chan);
            }

            if (LM > 0 && enc.tell() + 3 <= total_bits)
            {
                if (isTransient != 0)
                    shortBlocks = M;
            }
            else {
                isTransient = 0;
                transient_got_disabled = 1;
            }

            freq = Arrays.InitTwoDimensionalArray<int>(CC, N); /*< Interleaved signal MDCTs */
            bandE = Arrays.InitTwoDimensionalArray<int>(CC, nbEBands);
            bandLogE = Arrays.InitTwoDimensionalArray<int>(CC, nbEBands);

            secondMdct = (shortBlocks != 0 && this.complexity >= 8) ? 1 : 0;
            bandLogE2 = Arrays.InitTwoDimensionalArray<int>(CC, nbEBands);
            //Arrays.MemSetInt(bandLogE2, 0, C * nbEBands); // not explicitly needed
            if (secondMdct != 0)
            {
                CeltCommon.compute_mdcts(mode, 0, input, freq, C, CC, LM, this.upsample);
                Bands.compute_band_energies(mode, freq, bandE, effEnd, C, LM);
                QuantizeBands.amp2Log2(mode, effEnd, end, bandE, bandLogE2, C);
                for (i = 0; i < nbEBands; i++)
                {
                    bandLogE2[0][i] += Inlines.HALF16(Inlines.SHL16(LM, CeltConstants.DB_SHIFT));
                }
                if (C == 2)
                {
                    for (i = 0; i < nbEBands; i++)
                    {
                        bandLogE2[1][i] += Inlines.HALF16(Inlines.SHL16(LM, CeltConstants.DB_SHIFT));
                    }
                }
            }

            CeltCommon.compute_mdcts(mode, shortBlocks, input, freq, C, CC, LM, this.upsample);
            if (CC == 2 && C == 1)
                tf_chan = 0;
            Bands.compute_band_energies(mode, freq, bandE, effEnd, C, LM);

            if (this.lfe != 0)
            {
                for (i = 2; i < end; i++)
                {
                    bandE[0][i] = Inlines.IMIN(bandE[0][i], Inlines.MULT16_32_Q15(((short)(0.5 + (1e-4f) * (((int)1) << (15))))/*Inlines.QCONST16(1e-4f, 15)*/, bandE[0][0]));
                    bandE[0][i] = Inlines.MAX32(bandE[0][i], CeltConstants.EPSILON);
                }
            }

            QuantizeBands.amp2Log2(mode, effEnd, end, bandE, bandLogE, C);

            surround_dynalloc = new int[C * nbEBands];
            //Arrays.MemSetInt(surround_dynalloc, 0, end); // not strictly needed
            /* This computes how much masking takes place between surround channels */
            if (start == 0 && this.energy_mask != null && this.lfe == 0)
            {
                int mask_end;
                int midband;
                int count_dynalloc;
                int mask_avg = 0;
                int diff = 0;
                int count = 0;
                mask_end = Inlines.IMAX(2, this.lastCodedBands);
                for (c = 0; c < C; c++)
                {
                    for (i = 0; i < mask_end; i++)
                    {
                        int mask;
                        mask = Inlines.MAX16(Inlines.MIN16(this.energy_mask[nbEBands * c + i],
                               ((short)(0.5 + (.25f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.25f, CeltConstants.DB_SHIFT)*/), -((short)(0.5 + (2.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(2.0f, CeltConstants.DB_SHIFT)*/);
                        if (mask > 0)
                            mask = Inlines.HALF16(mask);
                        mask_avg += Inlines.MULT16_16(mask, eBands[i + 1] - eBands[i]);
                        count += eBands[i + 1] - eBands[i];
                        diff += Inlines.MULT16_16(mask, 1 + 2 * i - mask_end);
                    }
                }
                Inlines.OpusAssert(count > 0);
                mask_avg = Inlines.DIV32_16(mask_avg, count);
                mask_avg += ((short)(0.5 + (.2f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.2f, CeltConstants.DB_SHIFT)*/;
                diff = diff * 6 / (C * (mask_end - 1) * (mask_end + 1) * mask_end);
                /* Again, being conservative */
                diff = Inlines.HALF32(diff);
                diff = Inlines.MAX32(Inlines.MIN32(diff, ((int)(0.5 + (.031f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST32(.031f, CeltConstants.DB_SHIFT)*/), 0 - ((int)(0.5 + (.031f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST32(.031f, CeltConstants.DB_SHIFT)*/);
                /* Find the band that's in the middle of the coded spectrum */
                for (midband = 0; eBands[midband + 1] < eBands[mask_end] / 2; midband++) ;
                count_dynalloc = 0;
                for (i = 0; i < mask_end; i++)
                {
                    int lin;
                    int unmask;
                    lin = mask_avg + diff * (i - midband);
                    if (C == 2)
                        unmask = Inlines.MAX16(this.energy_mask[i], this.energy_mask[nbEBands + i]);
                    else
                        unmask = this.energy_mask[i];
                    unmask = Inlines.MIN16(unmask, ((short)(0.5 + (.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.0f, CeltConstants.DB_SHIFT)*/);
                    unmask -= lin;
                    if (unmask > ((short)(0.5 + (.25f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.25f, CeltConstants.DB_SHIFT)*/)
                    {
                        surround_dynalloc[i] = unmask - ((short)(0.5 + (.25f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.25f, CeltConstants.DB_SHIFT)*/;
                        count_dynalloc++;
                    }
                }
                if (count_dynalloc >= 3)
                {
                    /* If we need dynalloc in many bands, it's probably because our
                       initial masking rate was too low. */
                    mask_avg += ((short)(0.5 + (.25f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.25f, CeltConstants.DB_SHIFT)*/;
                    if (mask_avg > 0)
                    {
                        /* Something went really wrong in the original calculations,
                           disabling masking. */
                        mask_avg = 0;
                        diff = 0;
                        Arrays.MemSetInt(surround_dynalloc, 0, mask_end);
                    }
                    else {
                        for (i = 0; i < mask_end; i++)
                            surround_dynalloc[i] = Inlines.MAX16(0, surround_dynalloc[i] - ((short)(0.5 + (.25f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.25f, CeltConstants.DB_SHIFT)*/);
                    }
                }
                mask_avg += ((short)(0.5 + (.2f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(.2f, CeltConstants.DB_SHIFT)*/;
                /* Convert to 1/64th units used for the trim */
                surround_trim = 64 * diff;
                /*printf("%d %d ", mask_avg, surround_trim);*/
                surround_masking = mask_avg;
            }
            /* Temporal VBR (but not for LFE) */
            if (this.lfe == 0)
            {
                int follow = -((short)(0.5 + (10.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(10.0f, CeltConstants.DB_SHIFT)*/;
                int frame_avg = 0;
                int offset = shortBlocks != 0 ? Inlines.HALF16(Inlines.SHL16(LM, CeltConstants.DB_SHIFT)) : 0;
                for (i = start; i < end; i++)
                {
                    follow = Inlines.MAX16(follow - ((short)(0.5 + (1.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(1.0f, CeltConstants.DB_SHIFT)*/, bandLogE[0][i] - offset);
                    if (C == 2)
                        follow = Inlines.MAX16(follow, bandLogE[1][i] - offset);
                    frame_avg += follow;
                }
                frame_avg /= (end - start);
                temporal_vbr = Inlines.SUB16(frame_avg, this.spec_avg);
                temporal_vbr = Inlines.MIN16(((short)(0.5 + (3.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(3.0f, CeltConstants.DB_SHIFT)*/, Inlines.MAX16(-((short)(0.5 + (1.5f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(1.5f, CeltConstants.DB_SHIFT)*/, temporal_vbr));
                this.spec_avg += (short)(Inlines.MULT16_16_Q15(((short)(0.5 + (.02f) * (((int)1) << (15))))/*Inlines.QCONST16(.02f, 15)*/, temporal_vbr));
            }
            /*for (i=0;i<21;i++)
               printf("%f ", bandLogE[i]);
            printf("\n");*/

            if (secondMdct == 0)
            {
                Array.Copy(bandLogE[0], bandLogE2[0], nbEBands);
                if (C == 2)
                    Array.Copy(bandLogE[1], bandLogE2[1], nbEBands);
            }

            /* Last chance to catch any transient we might have missed in the
               time-domain analysis */
            if (LM > 0 && enc.tell() + 3 <= total_bits && isTransient == 0 && this.complexity >= 5 && this.lfe == 0)
            {
                if (CeltCommon.patch_transient_decision(bandLogE, this.oldBandE, nbEBands, start, end, C) != 0)
                {
                    isTransient = 1;
                    shortBlocks = M;
                    CeltCommon.compute_mdcts(mode, shortBlocks, input, freq, C, CC, LM, this.upsample);
                    Bands.compute_band_energies(mode, freq, bandE, effEnd, C, LM);
                    QuantizeBands.amp2Log2(mode, effEnd, end, bandE, bandLogE, C);
                    /* Compensate for the scaling of short vs long mdcts */
                    for (i = 0; i < nbEBands; i++)
                        bandLogE2[0][i] += Inlines.HALF16(Inlines.SHL16(LM, CeltConstants.DB_SHIFT));
                    if (C == 2)
                    {
                        for (i = 0; i < nbEBands; i++)
                            bandLogE2[1][i] += Inlines.HALF16(Inlines.SHL16(LM, CeltConstants.DB_SHIFT));
                    }
                    tf_estimate = ((short)(0.5 + (.2f) * (((int)1) << (14))))/*Inlines.QCONST16(.2f, 14)*/;
                }
            }

            if (LM > 0 && enc.tell() + 3 <= total_bits)
                enc.enc_bit_logp(isTransient, 3);

            X = Arrays.InitTwoDimensionalArray<int>(C, N);         /*< Interleaved normalised MDCTs */

            /* Band normalisation */
            Bands.normalise_bands(mode, freq, X, bandE, effEnd, C, M);

            tf_res = new int[nbEBands];
            /* Disable variable tf resolution for hybrid and at very low bitrate */
            if (effectiveBytes >= 15 * C && start == 0 && this.complexity >= 2 && this.lfe == 0)
            {
                int lambda;
                if (effectiveBytes < 40)
                    lambda = 12;
                else if (effectiveBytes < 60)
                    lambda = 6;
                else if (effectiveBytes < 100)
                    lambda = 4;
                else
                    lambda = 3;
                lambda *= 2;
                tf_select = CeltCommon.tf_analysis(mode, effEnd, isTransient, tf_res, lambda, X, N, LM, out tf_sum, tf_estimate, tf_chan);

                for (i = effEnd; i < end; i++)
                    tf_res[i] = tf_res[effEnd - 1];
            }
            else {
                tf_sum = 0;
                for (i = 0; i < end; i++)
                    tf_res[i] = isTransient;
                tf_select = 0;
            }

            error = Arrays.InitTwoDimensionalArray<int>(C, nbEBands);
            QuantizeBands.quant_coarse_energy(mode, start, end, effEnd, bandLogE,
                  this.oldBandE, (uint)total_bits, error, enc,
                  C, LM, nbAvailableBytes, this.force_intra,
                  ref this.delayedIntra, this.complexity >= 4 ? 1 : 0, this.loss_rate, this.lfe);

            CeltCommon.tf_encode(start, end, isTransient, tf_res, LM, tf_select, enc);

            if (enc.tell() + 4 <= total_bits)
            {
                if (this.lfe != 0)
                {
                    this.tapset_decision = 0;
                    this.spread_decision = Spread.SPREAD_NORMAL;
                }
                else if (shortBlocks != 0 || this.complexity < 3 || nbAvailableBytes < 10 * C || start != 0)
                {
                    if (this.complexity == 0)
                        this.spread_decision = Spread.SPREAD_NONE;
                    else
                        this.spread_decision = Spread.SPREAD_NORMAL;
                }
                else
                {
                        this.spread_decision = Bands.spreading_decision(mode, X,
                            ref this.tonal_average, this.spread_decision, ref this.hf_average,
                            ref this.tapset_decision, (pf_on != 0 && shortBlocks == 0) ? 1 : 0, effEnd, C, M);

                    /*printf("%d %d\n", st.tapset_decision, st.spread_decision);*/
                    /*printf("%f %d %f %d\n\n", st.analysis.tonality, st.spread_decision, st.analysis.tonality_slope, st.tapset_decision);*/
                }
                enc.enc_icdf(this.spread_decision, Tables.spread_icdf, 5);
            }

            offsets = new int[nbEBands];

            maxDepth = CeltCommon.dynalloc_analysis(bandLogE, bandLogE2, nbEBands, start, end, C, offsets,
                  this.lsb_depth, mode.logN, isTransient, this.vbr, this.constrained_vbr,
                  eBands, LM, effectiveBytes, out tot_boost, this.lfe, surround_dynalloc);

            /* For LFE, everything interesting is in the first band */
            if (this.lfe != 0)
                offsets[0] = Inlines.IMIN(8, effectiveBytes / 3);
            cap = new int[nbEBands];
            CeltCommon.init_caps(mode, cap, LM, C);

            dynalloc_logp = 6;
            total_bits <<= EntropyCoder.BITRES;
            total_boost = 0;
            tell = (int)enc.tell_frac();
            for (i = start; i < end; i++)
            {
                int width, quanta;
                int dynalloc_loop_logp;
                int boost;
                int j;
                width = C * (eBands[i + 1] - eBands[i]) << LM;
                /* quanta is 6 bits, but no more than 1 bit/sample
                   and no less than 1/8 bit/sample */
                quanta = Inlines.IMIN(width << EntropyCoder.BITRES, Inlines.IMAX(6 << EntropyCoder.BITRES, width));
                dynalloc_loop_logp = dynalloc_logp;
                boost = 0;
                for (j = 0; tell + (dynalloc_loop_logp << EntropyCoder.BITRES) < total_bits - total_boost
                      && boost < cap[i]; j++)
                {
                    int flag;
                    flag = j < offsets[i] ? 1 : 0;
                    enc.enc_bit_logp(flag, (uint)dynalloc_loop_logp);
                    tell = (int)enc.tell_frac();
                    if (flag == 0)
                        break;
                    boost += quanta;
                    total_boost += quanta;
                    dynalloc_loop_logp = 1;
                }
                /* Making dynalloc more likely */
                if (j != 0)
                    dynalloc_logp = Inlines.IMAX(2, dynalloc_logp - 1);
                offsets[i] = boost;
            }

            if (C == 2)
            {
                /* Always use MS for 2.5 ms frames until we can do a better analysis */
                if (LM != 0)
                    dual_stereo = CeltCommon.stereo_analysis(mode, X, LM);

                this.intensity = Bands.hysteresis_decision((int)(equiv_rate / 1000),
                      Tables.intensity_thresholds, Tables.intensity_histeresis, 21, this.intensity);
                this.intensity = Inlines.IMIN(end, Inlines.IMAX(start, this.intensity));
            }

            alloc_trim = 5;
            if (tell + (6 << EntropyCoder.BITRES) <= total_bits - total_boost)
            {
                if (this.lfe != 0)
                {
                    alloc_trim = 5;
                }
                else
                {
                    alloc_trim = CeltCommon.alloc_trim_analysis(mode, X, bandLogE,
                       end, LM, C, this.analysis, ref this.stereo_saving, tf_estimate,
                       this.intensity, surround_trim);
                }
                enc.enc_icdf(alloc_trim, Tables.trim_icdf, 7);
                tell = (int)enc.tell_frac();
            }

            /* Variable bitrate */
            if (vbr_rate > 0)
            {
                int alpha;
                int delta;
                /* The target rate in 8th bits per frame */
                int target, base_target;
                int min_allowed;
                int lm_diff = mode.maxLM - LM;

                /* Don't attempt to use more than 510 kb/s, even for frames smaller than 20 ms.
                   The CELT allocator will just not be able to use more than that anyway. */
                nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, 1275 >> (3 - LM));
                base_target = vbr_rate - ((40 * C + 20) << EntropyCoder.BITRES);

                if (this.constrained_vbr != 0)
                    base_target += (this.vbr_offset >> lm_diff);

                target = CeltCommon.compute_vbr(mode, this.analysis, base_target, LM, equiv_rate,
                      this.lastCodedBands, C, this.intensity, this.constrained_vbr,
                      this.stereo_saving, tot_boost, tf_estimate, pitch_change, maxDepth,
                      this.variable_duration, this.lfe, this.energy_mask != null ? 1 : 0, surround_masking,
                      temporal_vbr);

                /* The current offset is removed from the target and the space used
                   so far is added*/
                target = target + tell;
                /* In VBR mode the frame size must not be reduced so much that it would
                    result in the encoder running out of bits.
                   The margin of 2 bytes ensures that none of the bust-prevention logic
                    in the decoder will have triggered so far. */
                min_allowed = ((tell + total_boost + (1 << (EntropyCoder.BITRES + 3)) - 1) >> (EntropyCoder.BITRES + 3)) + 2 - nbFilledBytes;

                nbAvailableBytes = (target + (1 << (EntropyCoder.BITRES + 2))) >> (EntropyCoder.BITRES + 3);
                nbAvailableBytes = Inlines.IMAX(min_allowed, nbAvailableBytes);
                nbAvailableBytes = Inlines.IMIN(nbCompressedBytes, nbAvailableBytes + nbFilledBytes) - nbFilledBytes;

                /* By how much did we "miss" the target on that frame */
                delta = target - vbr_rate;

                target = nbAvailableBytes << (EntropyCoder.BITRES + 3);

                /*If the frame is silent we don't adjust our drift, otherwise
                  the encoder will shoot to very high rates after hitting a
                  span of silence, but we do allow the EntropyCoder.BITRES to refill.
                  This means that we'll undershoot our target in CVBR/VBR modes
                  on files with lots of silence. */
                if (silence != 0)
                {
                    nbAvailableBytes = 2;
                    target = 2 * 8 << EntropyCoder.BITRES;
                    delta = 0;
                }

                if (this.vbr_count < 970)
                {
                    this.vbr_count++;
                    alpha = Inlines.celt_rcp(Inlines.SHL32((this.vbr_count + 20), 16));
                }
                else
                    alpha = ((short)(0.5 + (.001f) * (((int)1) << (15))))/*Inlines.QCONST16(.001f, 15)*/;
                /* How many bits have we used in excess of what we're allowed */
                if (this.constrained_vbr != 0)
                    this.vbr_reservoir += target - vbr_rate;
                /*printf ("%d\n", st.vbr_reservoir);*/

                /* Compute the offset we need to apply in order to reach the target */
                if (this.constrained_vbr != 0)
                {
                    this.vbr_drift += (int)Inlines.MULT16_32_Q15(alpha, (delta * (1 << lm_diff)) - this.vbr_offset - this.vbr_drift);
                    this.vbr_offset = -this.vbr_drift;
                }
                /*printf ("%d\n", st.vbr_drift);*/

                if (this.constrained_vbr != 0 && this.vbr_reservoir < 0)
                {
                    /* We're under the min value -- increase rate */
                    int adjust = (-this.vbr_reservoir) / (8 << EntropyCoder.BITRES);
                    /* Unless we're just coding silence */
                    nbAvailableBytes += silence != 0 ? 0 : adjust;
                    this.vbr_reservoir = 0;
                    /*printf ("+%d\n", adjust);*/
                }
                nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, nbAvailableBytes + nbFilledBytes);
                /*printf("%d\n", nbCompressedBytes*50*8);*/
                /* This moves the raw bits to take into account the new compressed size */
                enc.enc_shrink((uint)nbCompressedBytes);
            }

            /* Bit allocation */
            fine_quant = new int[nbEBands];
            pulses = new int[nbEBands];
            fine_priority = new int[nbEBands];

            /* bits =    packet size                                     - where we are                        - safety*/
            bits = (((int)nbCompressedBytes * 8) << EntropyCoder.BITRES) - (int)enc.tell_frac() - 1;
            anti_collapse_rsv = isTransient != 0 && LM >= 2 && bits >= ((LM + 2) << EntropyCoder.BITRES) ? (1 << EntropyCoder.BITRES) : 0;
            bits -= anti_collapse_rsv;
            signalBandwidth = end - 1;
            
            if (this.analysis.valid != 0)
            {
                int min_bandwidth;
                if (equiv_rate < (int)32000 * C)
                    min_bandwidth = 13;
                else if (equiv_rate < (int)48000 * C)
                    min_bandwidth = 16;
                else if (equiv_rate < (int)60000 * C)
                    min_bandwidth = 18;
                else if (equiv_rate < (int)80000 * C)
                    min_bandwidth = 19;
                else
                    min_bandwidth = 20;
                signalBandwidth = Inlines.IMAX(this.analysis.bandwidth, min_bandwidth);
            }

            if (this.lfe != 0)
            {
                signalBandwidth = 1;
            }

            codedBands = Rate.compute_allocation(mode, start, end, offsets, cap,
                  alloc_trim, ref this.intensity, ref dual_stereo, bits, out balance, pulses,
                  fine_quant, fine_priority, C, LM, enc, 1, this.lastCodedBands, signalBandwidth);

            if (this.lastCodedBands != 0)
                this.lastCodedBands = Inlines.IMIN(this.lastCodedBands + 1, Inlines.IMAX(this.lastCodedBands - 1, codedBands));
            else
                this.lastCodedBands = codedBands;

            QuantizeBands.quant_fine_energy(mode, start, end, this.oldBandE, error, fine_quant, enc, C);

            /* Residual quantisation */
            collapse_masks = new byte[C * nbEBands];
            Bands.quant_all_bands(1, mode, start, end, X[0], C == 2 ? X[1] : null, collapse_masks,
                  bandE, pulses, shortBlocks, this.spread_decision,
                  dual_stereo, this.intensity, tf_res, nbCompressedBytes * (8 << EntropyCoder.BITRES) - anti_collapse_rsv,
                  balance, enc, LM, codedBands, ref this.rng);

            if (anti_collapse_rsv > 0)
            {
                anti_collapse_on = (this.consec_transient < 2) ? 1 : 0;
#if FUZZING
                anti_collapse_on = new Random().Next() & 0x1;
#endif
                enc.enc_bits((uint)anti_collapse_on, 1);
            }

            QuantizeBands.quant_energy_finalise(mode, start, end, this.oldBandE, error, fine_quant, fine_priority, nbCompressedBytes * 8 - (int)enc.tell(), enc, C);

            if (silence != 0)
            {
                for (i = 0; i < nbEBands; i++)
                {
                    this.oldBandE[0][i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
                }
                if (C == 2)
                {
                    for (i = 0; i < nbEBands; i++)
                    {
                        this.oldBandE[1][i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
                    }
                }
            }

            this.prefilter_period = pitch_index;
            this.prefilter_gain = gain1;
            this.prefilter_tapset = prefilter_tapset;

            if (CC == 2 && C == 1)
            {
                Array.Copy(oldBandE[0], 0, oldBandE[1], 0, nbEBands);
            }

            if (isTransient == 0)
            {
                Array.Copy(oldLogE[0], 0, oldLogE2[0], 0, nbEBands);
                Array.Copy(oldBandE[0], 0, oldLogE[0], 0, nbEBands);
                if (CC == 2)
                {
                    Array.Copy(oldLogE[1], 0, oldLogE2[1], 0, nbEBands);
                    Array.Copy(oldBandE[1], 0, oldLogE[1], 0, nbEBands);
                }
            }
            else
            {
                for (i = 0; i < nbEBands; i++)
                {
                    oldLogE[0][i] = Inlines.MIN16(oldLogE[0][i], oldBandE[0][i]);
                }
                if (CC == 2)
                {
                    for (i = 0; i < nbEBands; i++)
                    {
                        oldLogE[1][i] = Inlines.MIN16(oldLogE[1][i], oldBandE[1][i]);
                    }
                }
            }

            /* In case start or end were to change */
            c = 0;
            do
            {
                for (i = 0; i < start; i++)
                {
                    oldBandE[c][i] = 0;
                    oldLogE[c][i] = oldLogE2[c][i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
                }
                for (i = end; i < nbEBands; i++)
                {
                    oldBandE[c][i] = 0;
                    oldLogE[c][i] = oldLogE2[c][i] = -((short)(0.5 + (28.0f) * (((int)1) << (CeltConstants.DB_SHIFT))))/*Inlines.QCONST16(28.0f, CeltConstants.DB_SHIFT)*/;
                }
            } while (++c < CC);

            if (isTransient != 0 || transient_got_disabled != 0)
                this.consec_transient++;
            else
                this.consec_transient = 0;
            this.rng = enc.rng;

            /* If there's any room left (can only happen for very high rates),
               it's already filled with zeros */
            enc.enc_done();


            if (enc.get_error() != 0)
                return OpusError.OPUS_INTERNAL_ERROR;
            else
                return nbCompressedBytes;
        }

#endregion

#region Getters and Setters

        internal void SetComplexity(int value)
        {
            if (value < 0 || value > 10)
                throw new ArgumentException("Complexity must be between 0 and 10 inclusive");
            this.complexity = value;
        }

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

        internal void SetPacketLossPercent(int value)
        {
            if (value < 0 || value > 100)
                throw new ArgumentException("Packet loss must be between 0 and 100");
            this.loss_rate = value;
        }

        internal void SetPrediction(int value)
        {
            if (value < 0 || value > 2)
                throw new ArgumentException("CELT prediction mode must be 0, 1, or 2");
            this.disable_pf = (value <= 1) ? 1 : 0;
            this.force_intra = (value == 0) ? 1 : 0;
        }

        internal void SetVBRConstraint(bool value)
        {
            this.constrained_vbr = value ? 1 : 0;
        }

        internal void SetVBR(bool value)
        {
            this.vbr = value ? 1 : 0;
        }

        internal void SetBitrate(int value)
        {
            if (value <= 500 && value != OpusConstants.OPUS_BITRATE_MAX)
                throw new ArgumentException("Bitrate out of range");
            value = Inlines.IMIN(value, 260000 * this.channels);
            this.bitrate = value;
        }

        internal void SetChannels(int value)
        {
            if (value < 1 || value > 2)
                throw new ArgumentException("Channel count must be 1 or 2");
            this.stream_channels = value;
        }

        internal void SetLSBDepth(int value)
        {
            if (value < 8 || value > 24)
                throw new ArgumentException("Bit depth must be between 8 and 24");
            this.lsb_depth = value;
        }

        internal int GetLSBDepth()
        {
            return this.lsb_depth;
        }

        internal void SetExpertFrameDuration(OpusFramesize value)
        {
            this.variable_duration = value;
        }

        internal void SetSignalling(int value)
        {
            this.signalling = value;
        }

        internal void SetAnalysis(AnalysisInfo value)
        {
            if (value == null)
                throw new ArgumentNullException("AnalysisInfo");
            this.analysis.Assign(value);
        }

        internal CeltMode GetMode()
        {
            return this.mode;
        }

        internal uint GetFinalRange()
        {
            return this.rng;
        }

        internal void SetLFE(int value)
        {
            this.lfe = value;
        }

        internal void SetEnergyMask(int[] value)
        {
            this.energy_mask = value;
        }

#endregion
    }
}
