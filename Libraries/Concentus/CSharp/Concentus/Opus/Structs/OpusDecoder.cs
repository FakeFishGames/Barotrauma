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
using Concentus.Enums;
using Concentus.Silk;
using Concentus.Silk.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Concentus.Structs
{
    /// <summary>
    /// The Opus decoder structure.
    /// 
    ///  Opus is a stateful codec with overlapping blocks and as a result Opus
    ///  packets are not coded independently of each other. Packets must be
    ///  passed into the decoder serially and in the correct order for a correct
    ///  decode. Lost packets can be replaced with loss concealment by calling
    ///  the decoder with a null reference and zero length for the missing packet.
    /// 
    ///  A single codec state may only be accessed from a single thread at
    ///  a time and any required locking must be performed by the caller. Separate
    ///  streams must be decoded with separate decoder states and can be decoded
    ///  in parallel.
    /// </summary>
    public class OpusDecoder
    {
        internal int channels;
        internal int Fs;          /** Sampling rate (at the API level) */
        internal readonly DecControlState DecControl = new DecControlState();
        internal int decode_gain;

        /* Everything beyond this point gets cleared on a reset */
        internal int stream_channels;
        internal OpusBandwidth bandwidth;
        internal OpusMode mode;
        internal OpusMode prev_mode;
        internal int frame_size;
        internal int prev_redundancy;
        internal int last_packet_duration;
        internal uint rangeFinal;
        internal SilkDecoder SilkDecoder = new SilkDecoder();
        internal CeltDecoder Celt_Decoder = new CeltDecoder();

        // Used by multistream decoder
        internal OpusDecoder() { }

        internal void Reset()
        {
            channels = 0;
            Fs = 0;          /* Sampling rate (at the API level) */
            DecControl.Reset();
            decode_gain = 0;
            PartialReset();
        }

        /// <summary>
        /// OPUS_DECODER_RESET_START
        /// </summary>
        internal void PartialReset()
        {
            stream_channels = 0;
            bandwidth = 0;
            mode = 0;
            prev_mode = 0;
            frame_size = 0;
            prev_redundancy = 0;
            last_packet_duration = 0;
            rangeFinal = 0;
            // fixme: do these get reset here? I don't think they do because init_celt and init_silk should both call RESET_STATE on their respective states
            //SilkDecoder.Reset();
            //CeltDecoder.Reset();
        }

        /// <summary>
        /// Deprecated. Just use the regular constructor
        /// </summary>
        public static OpusDecoder Create(int Fs, int channels)
        {
            return new OpusDecoder(Fs, channels);
        }

        /// <summary>
        /// Allocates and initializes a decoder state.
        /// Internally Opus stores data at 48000 Hz, so that should be the default
        /// value for Fs. However, the decoder can efficiently decode to buffers
        /// at 8, 12, 16, and 24 kHz so if for some reason the caller cannot use
        /// data at the full sample rate, or knows the compressed data doesn't
        /// use the full frequency range, it can request decoding at a reduced
        /// rate. Likewise, the decoder is capable of filling in either mono or
        /// interleaved stereo pcm buffers, at the caller's request.
        /// </summary>
        /// <param name="Fs">Sample rate to decode at (Hz). This must be one of 8000, 12000, 16000, 24000, or 48000.</param>
        /// <param name="channels">Number of channels (1 or 2) to decode</param>
        /// <returns>The created encoder</returns>
        public OpusDecoder(int Fs, int channels)
        {
            int ret;
            if ((Fs != 48000 && Fs != 24000 && Fs != 16000 && Fs != 12000 && Fs != 8000))
            {
                throw new ArgumentException("Sample rate is invalid (must be 8/12/16/24/48 Khz)");
            }
            if (channels != 1 && channels != 2)
            {
                throw new ArgumentException("Number of channels must be 1 or 2");
            }

            ret = this.opus_decoder_init(Fs, channels);
            if (ret != OpusError.OPUS_OK)
            {
                if (ret == OpusError.OPUS_BAD_ARG)
                    throw new ArgumentException("OPUS_BAD_ARG when creating decoder");
                throw new OpusException("Error while initializing decoder", ret);
            }
        }

        /** Initializes a previously allocated decoder state.
  * The state must be at least the size returned by opus_decoder_get_size().
  * This is intended for applications which use their own allocator instead of malloc. @see opus_decoder_create,opus_decoder_get_size
  * To reset a previously initialized state, use the #OPUS_RESET_STATE CTL.
  * @param [in] st <tt>OpusDecoder*</tt>: Decoder state.
  * @param [in] Fs <tt>opus_int32</tt>: Sampling rate to decode to (Hz).
  *                                     This must be one of 8000, 12000, 16000,
  *                                     24000, or 48000.
  * @param [in] channels <tt>int</tt>: Number of channels (1 or 2) to decode
  * @retval #OPUS_OK Success or @ref opus_errorcodes
  */
        internal int opus_decoder_init(int Fs, int channels)
        {
            SilkDecoder silk_dec;
            CeltDecoder celt_dec;
            int ret;

            if ((Fs != 48000 && Fs != 24000 && Fs != 16000 && Fs != 12000 && Fs != 8000)
             || (channels != 1 && channels != 2))
                return OpusError.OPUS_BAD_ARG;
            this.Reset();

            /* Initialize SILK encoder */
            silk_dec = this.SilkDecoder;
            celt_dec = this.Celt_Decoder;
            this.stream_channels = this.channels = channels;

            this.Fs = Fs;
            this.DecControl.API_sampleRate = this.Fs;
            this.DecControl.nChannelsAPI = this.channels;

            /* Reset decoder */
            ret = DecodeAPI.silk_InitDecoder(silk_dec);
            if (ret != 0) return OpusError.OPUS_INTERNAL_ERROR;

            /* Initialize CELT decoder */
            ret = celt_dec.celt_decoder_init(Fs, channels);
            if (ret != OpusError.OPUS_OK)
                return OpusError.OPUS_INTERNAL_ERROR;

            celt_dec.SetSignalling(0);

            this.prev_mode = 0;
            this.frame_size = Fs / 400;
            return OpusError.OPUS_OK;
        }
        
        private static readonly byte[] SILENCE = { 0xFF, 0xFF };

        internal int opus_decode_frame(byte[] data, int data_ptr,
      int len, short[] pcm, int pcm_ptr, int frame_size, int decode_fec)
        {
            SilkDecoder silk_dec;
            CeltDecoder celt_dec;
            int i, silk_ret = 0, celt_ret = 0;
            EntropyCoder dec = new EntropyCoder(); // porting note: stack var
            int silk_frame_size;
            int pcm_silk_size;
            short[] pcm_silk;
            int pcm_transition_silk_size;
            short[] pcm_transition_silk;
            int pcm_transition_celt_size;
            short[] pcm_transition_celt;
            short[] pcm_transition = null;
            int redundant_audio_size;
            short[] redundant_audio;

            int audiosize;
            OpusMode mode;
            int transition = 0;
            int start_band;
            int redundancy = 0;
            int redundancy_bytes = 0;
            int celt_to_silk = 0;
            int c;
            int F2_5, F5, F10, F20;
            int[] window;
            uint redundant_rng = 0;
            int celt_accum;

            silk_dec = this.SilkDecoder;
            celt_dec = this.Celt_Decoder;
            F20 = this.Fs / 50;
            F10 = F20 >> 1;
            F5 = F10 >> 1;
            F2_5 = F5 >> 1;
            if (frame_size < F2_5)
            {

                return OpusError.OPUS_BUFFER_TOO_SMALL;
            }
            /* Limit frame_size to avoid excessive stack allocations. */
            frame_size = Inlines.IMIN(frame_size, this.Fs / 25 * 3);
            /* Payloads of 1 (2 including ToC) or 0 trigger the PLC/DTX */
            if (len <= 1)
            {
                data = null;
                /* In that case, don't conceal more than what the ToC says */
                frame_size = Inlines.IMIN(frame_size, this.frame_size);
            }
            if (data != null)
            {
                audiosize = this.frame_size;
                mode = this.mode;
                dec.dec_init(data, data_ptr, (uint)len);
            }
            else {
                audiosize = frame_size;
                mode = this.prev_mode;

                if (mode == 0)
                {
                    /* If we haven't got any packet yet, all we can do is return zeros */
                    for (i = pcm_ptr; i < pcm_ptr + (audiosize * this.channels); i++)
                        pcm[i] = 0;

                    return audiosize;
                }

                /* Avoids trying to run the PLC on sizes other than 2.5 (CELT), 5 (CELT),
                   10, or 20 (e.g. 12.5 or 30 ms). */
                if (audiosize > F20)
                {
                    do
                    {
                        int ret = opus_decode_frame(null, 0, 0, pcm, pcm_ptr, Inlines.IMIN(audiosize, F20), 0);
                        if (ret < 0)
                        {

                            return ret;
                        }
                        pcm_ptr += ret * this.channels;
                        audiosize -= ret;
                    } while (audiosize > 0);

                    return frame_size;
                }
                else if (audiosize < F20)
                {
                    if (audiosize > F10)
                        audiosize = F10;
                    else if (mode != OpusMode.MODE_SILK_ONLY && audiosize > F5 && audiosize < F10)
                        audiosize = F5;
                }
            }

            /* In fixed-point, we can tell CELT to do the accumulation on top of the
               SILK PCM buffer. This saves some stack space. */
            celt_accum = ((mode != OpusMode.MODE_CELT_ONLY) && (frame_size >= F10)) ? 1 : 0;

            pcm_transition_silk_size = 0;
            pcm_transition_celt_size = 0;
            if (data != null && this.prev_mode > 0 && (
                (mode == OpusMode.MODE_CELT_ONLY && this.prev_mode != OpusMode.MODE_CELT_ONLY && (this.prev_redundancy == 0))
             || (mode != OpusMode.MODE_CELT_ONLY && this.prev_mode == OpusMode.MODE_CELT_ONLY))
               )
            {
                transition = 1;
                /* Decide where to allocate the stack memory for pcm_transition */
                if (mode == OpusMode.MODE_CELT_ONLY)
                    pcm_transition_celt_size = F5 * this.channels;
                else
                    pcm_transition_silk_size = F5 * this.channels;
            }
            pcm_transition_celt = new short[pcm_transition_celt_size];
            if (transition != 0 && mode == OpusMode.MODE_CELT_ONLY)
            {
                pcm_transition = pcm_transition_celt;
                opus_decode_frame(null, 0, 0, pcm_transition, 0, Inlines.IMIN(F5, audiosize), 0);
            }
            if (audiosize > frame_size)
            {
                /*fprintf(stderr, "PCM buffer too small: %d vs %d (mode = %d)\n", audiosize, frame_size, mode);*/

                return OpusError.OPUS_BAD_ARG;
            }
            else {
                frame_size = audiosize;
            }

            /* Don't allocate any memory when in CELT-only mode */
            pcm_silk_size = (mode != OpusMode.MODE_CELT_ONLY && (celt_accum == 0)) ? Inlines.IMAX(F10, frame_size) * this.channels : 0;
            pcm_silk = new short[pcm_silk_size];

            /* SILK processing */
            if (mode != OpusMode.MODE_CELT_ONLY)
            {
                int lost_flag, decoded_samples;
                short[] pcm_ptr2;
                int pcm_ptr2_ptr = 0;

                if (celt_accum != 0)
                {
                    pcm_ptr2 = pcm;
                    pcm_ptr2_ptr = pcm_ptr;
                }
                else
                {
                    pcm_ptr2 = pcm_silk;
                    pcm_ptr2_ptr = 0;
                }

                if (this.prev_mode == OpusMode.MODE_CELT_ONLY)
                    DecodeAPI.silk_InitDecoder(silk_dec);

                /* The SILK PLC cannot produce frames of less than 10 ms */
                this.DecControl.payloadSize_ms = Inlines.IMAX(10, 1000 * audiosize / this.Fs);

                if (data != null)
                {
                    this.DecControl.nChannelsInternal = this.stream_channels;
                    if (mode == OpusMode.MODE_SILK_ONLY)
                    {
                        if (this.bandwidth == OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND)
                        {
                            this.DecControl.internalSampleRate = 8000;
                        }
                        else if (this.bandwidth == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
                        {
                            this.DecControl.internalSampleRate = 12000;
                        }
                        else if (this.bandwidth == OpusBandwidth.OPUS_BANDWIDTH_WIDEBAND)
                        {
                            this.DecControl.internalSampleRate = 16000;
                        }
                        else {
                            this.DecControl.internalSampleRate = 16000;
                            Inlines.OpusAssert(false);
                        }
                    }
                    else {
                        /* Hybrid mode */
                        this.DecControl.internalSampleRate = 16000;
                    }
                }

                lost_flag = data == null ? 1 : 2 * decode_fec;
                decoded_samples = 0;
                do
                {
                    /* Call SILK decoder */
                    int first_frame = (decoded_samples == 0) ? 1 : 0;
                    silk_ret = DecodeAPI.silk_Decode(silk_dec, this.DecControl,
                                            lost_flag, first_frame, dec, pcm_ptr2, pcm_ptr2_ptr, out silk_frame_size);
                    if (silk_ret != 0)
                    {
                        if (lost_flag != 0)
                        {
                            /* PLC failure should not be fatal */
                            silk_frame_size = frame_size;
                            Arrays.MemSetWithOffset<short>(pcm_ptr2, 0, pcm_ptr2_ptr, frame_size * this.channels);
                        }
                        else {

                            return OpusError.OPUS_INTERNAL_ERROR;
                        }
                    }
                    pcm_ptr2_ptr += (silk_frame_size * this.channels);
                    decoded_samples += silk_frame_size;
                } while (decoded_samples < frame_size);
            }

            start_band = 0;
            if (decode_fec == 0 && mode != OpusMode.MODE_CELT_ONLY && data != null
             && dec.tell() + 17 + 20 * (this.mode == OpusMode.MODE_HYBRID ? 1 : 0) <= 8 * len)
            {
                /* Check if we have a redundant 0-8 kHz band */
                if (mode == OpusMode.MODE_HYBRID)
                    redundancy = dec.dec_bit_logp(12);
                else
                    redundancy = 1;
                if (redundancy != 0)
                {
                    celt_to_silk = dec.dec_bit_logp(1);
                    /* redundancy_bytes will be at least two, in the non-hybrid
                       case due to the ec_tell() check above */
                    redundancy_bytes = mode == OpusMode.MODE_HYBRID ?
                                      (int)dec.dec_uint(256) + 2 :
                                      len - ((dec.tell() + 7) >> 3);
                    len -= redundancy_bytes;
                    /* This is a sanity check. It should never happen for a valid
                       packet, so the exact behaviour is not normative. */
                    if (len * 8 < dec.tell())
                    {
                        len = 0;
                        redundancy_bytes = 0;
                        redundancy = 0;
                    }
                    /* Shrink decoder because of raw bits */
                    dec.storage = (uint)(dec.storage - redundancy_bytes);
                }
            }
            if (mode != OpusMode.MODE_CELT_ONLY)
                start_band = 17;

            {
                int endband = 21;

                switch (this.bandwidth)
                {
                    case OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND:
                        endband = 13;
                        break;
                    case OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND:
                    case OpusBandwidth.OPUS_BANDWIDTH_WIDEBAND:
                        endband = 17;
                        break;
                    case OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND:
                        endband = 19;
                        break;
                    case OpusBandwidth.OPUS_BANDWIDTH_FULLBAND:
                        endband = 21;
                        break;
                }
                celt_dec.SetEndBand(endband);
                celt_dec.SetChannels(this.stream_channels);
            }

            if (redundancy != 0)
            {
                transition = 0;
                pcm_transition_silk_size = 0;
            }

            pcm_transition_silk = new short[pcm_transition_silk_size];

            if (transition != 0 && mode != OpusMode.MODE_CELT_ONLY)
            {
                pcm_transition = pcm_transition_silk;
                opus_decode_frame(null, 0, 0, pcm_transition, 0, Inlines.IMIN(F5, audiosize), 0);
            }

            /* Only allocation memory for redundancy if/when needed */
            redundant_audio_size = redundancy != 0 ? F5 * this.channels : 0;
            redundant_audio = new short[redundant_audio_size];

            /* 5 ms redundant frame for CELT->SILK*/
            if (redundancy != 0 && celt_to_silk != 0)
            {
                celt_dec.SetStartBand(0);
                celt_dec.celt_decode_with_ec(data, (data_ptr + len), redundancy_bytes,
                                    redundant_audio, 0, F5, null, 0);
                redundant_rng = celt_dec.GetFinalRange();
            }

            /* MUST be after PLC */
            celt_dec.SetStartBand(start_band);

            if (mode != OpusMode.MODE_SILK_ONLY)
            {
                int celt_frame_size = Inlines.IMIN(F20, frame_size);
                /* Make sure to discard any previous CELT state */
                if (mode != this.prev_mode && this.prev_mode > 0 && this.prev_redundancy == 0)
                    celt_dec.ResetState();
                /* Decode CELT */
                celt_ret = celt_dec.celt_decode_with_ec(decode_fec != 0 ? null : data, data_ptr,
                                               len, pcm, pcm_ptr, celt_frame_size, dec, celt_accum);
            }
            else
            {
                if (celt_accum == 0)
                {
                    for (i = pcm_ptr; i < (frame_size * this.channels) + pcm_ptr; i++)
                        pcm[i] = 0;
                }
                /* For hybrid -> SILK transitions, we let the CELT MDCT
                   do a fade-out by decoding a silence frame */
                if (this.prev_mode == OpusMode.MODE_HYBRID && !(redundancy != 0 && celt_to_silk != 0 && this.prev_redundancy != 0))
                {
                    celt_dec.SetStartBand(0);
                    celt_dec.celt_decode_with_ec(SILENCE, 0, 2, pcm, pcm_ptr, F2_5, null, celt_accum);
                }
            }

            if (mode != OpusMode.MODE_CELT_ONLY && celt_accum == 0)
            {
                for (i = 0; i < frame_size * this.channels; i++)
                    pcm[pcm_ptr + i] = Inlines.SAT16(Inlines.ADD32(pcm[pcm_ptr + i], pcm_silk[i]));
            }

            window = celt_dec.GetMode().window;

            /* 5 ms redundant frame for SILK->CELT */
            if (redundancy != 0 && celt_to_silk == 0)
            {
                celt_dec.ResetState();
                celt_dec.SetStartBand(0);

                celt_dec.celt_decode_with_ec(data, data_ptr + len, redundancy_bytes, redundant_audio, 0, F5, null, 0);
                redundant_rng = celt_dec.GetFinalRange();
                CodecHelpers.smooth_fade(pcm, pcm_ptr + this.channels * (frame_size - F2_5), redundant_audio, this.channels * F2_5,
                           pcm, (pcm_ptr + this.channels * (frame_size - F2_5)), F2_5, this.channels, window, this.Fs);
            }
            if (redundancy != 0 && celt_to_silk != 0)
            {
                for (c = 0; c < this.channels; c++)
                {
                    for (i = 0; i < F2_5; i++)
                        pcm[this.channels * i + c + pcm_ptr] = redundant_audio[this.channels * i + c];
                }
                CodecHelpers.smooth_fade(redundant_audio,(this.channels * F2_5), pcm,(pcm_ptr + (this.channels * F2_5)),
                            pcm, (pcm_ptr + (this.channels * F2_5)), F2_5, this.channels, window, this.Fs);
            }
            if (transition != 0)
            {
                if (audiosize >= F5)
                {
                    for (i = 0; i < this.channels * F2_5; i++)
                        pcm[i] = pcm_transition[i];
                    CodecHelpers.smooth_fade(pcm_transition, (this.channels * F2_5), pcm, (pcm_ptr + (this.channels * F2_5)),
                                pcm, (pcm_ptr + (this.channels * F2_5)), F2_5,
                                this.channels, window, this.Fs);
                }
                else {
                    /* Not enough time to do a clean transition, but we do it anyway
                       This will not preserve amplitude perfectly and may introduce
                       a bit of temporal aliasing, but it shouldn't be too bad and
                       that's pretty much the best we can do. In any case, generating this
                       transition is pretty silly in the first place */
                    CodecHelpers.smooth_fade(pcm_transition, 0, pcm, pcm_ptr,
                                pcm, pcm_ptr, F2_5,
                                this.channels, window, this.Fs);
                }
            }

            if (this.decode_gain != 0)
            {
                int gain;
                gain = Inlines.celt_exp2(Inlines.MULT16_16_P15(((short)(0.5 + (6.48814081e-4f) * (((int)1) << (25))))/*Inlines.QCONST16(6.48814081e-4f, 25)*/, this.decode_gain));
                for (i = pcm_ptr; i < pcm_ptr + (frame_size * this.channels); i++)
                {
                    int x;
                    x = Inlines.MULT16_32_P16(pcm[i], gain);
                    pcm[i] = (short)Inlines.SATURATE(x, 32767);
                }
            }

            if (len <= 1)
                this.rangeFinal = 0;
            else
                this.rangeFinal = dec.rng ^ redundant_rng;

            this.prev_mode = mode;
            this.prev_redundancy = (redundancy != 0 && celt_to_silk == 0) ? 1 : 0;

            return celt_ret < 0 ? celt_ret : audiosize;
        }

        internal int opus_decode_native(byte[] data, int data_ptr,
          int len, short[] pcm_out, int pcm_out_ptr, int frame_size, int decode_fec,
          int self_delimited, out int packet_offset, int soft_clip)
        {
            int i, nb_samples;
            int count, offset;
            byte toc;
            int packet_frame_size, packet_stream_channels;
            packet_offset = 0;
            OpusBandwidth packet_bandwidth;
            OpusMode packet_mode;
            /* 48 x 2.5 ms = 120 ms */
            // fixme: make sure these values can fit in an int16
            short[] size = new short[48];
            if (decode_fec < 0 || decode_fec > 1)
                return OpusError.OPUS_BAD_ARG;
            /* For FEC/PLC, frame_size has to be to have a multiple of 2.5 ms */
            if ((decode_fec != 0 || len == 0 || data == null) && frame_size % (this.Fs / 400) != 0)
                return OpusError.OPUS_BAD_ARG;
            if (len == 0 || data == null)
            {
                int pcm_count = 0;
                do
                {
                    int ret;
                    ret = opus_decode_frame(null, 0, 0, pcm_out, pcm_out_ptr + (pcm_count * this.channels), frame_size - pcm_count, 0);
                    if (ret < 0)
                        return ret;
                    pcm_count += ret;
                } while (pcm_count < frame_size);
                Inlines.OpusAssert(pcm_count == frame_size);
                this.last_packet_duration = pcm_count;
                return pcm_count;
            }
            else if (len < 0)
                return OpusError.OPUS_BAD_ARG;

            packet_mode = OpusPacketInfo.GetEncoderMode(data, data_ptr);
            packet_bandwidth = OpusPacketInfo.GetBandwidth(data, data_ptr);
            packet_frame_size = OpusPacketInfo.GetNumSamplesPerFrame(data, data_ptr, this.Fs);
            packet_stream_channels = OpusPacketInfo.GetNumEncodedChannels(data, data_ptr);
            
            count = OpusPacketInfo.opus_packet_parse_impl(data, data_ptr, len, self_delimited, out toc, null, null, 0,
                                           size, 0, out offset, out packet_offset);

            if (count < 0)
                return count;

            data_ptr += offset;

            if (decode_fec != 0)
            {
                int dummy;
                int duration_copy;
                int ret;
                /* If no FEC can be present, run the PLC (recursive call) */
                if (frame_size < packet_frame_size || packet_mode == OpusMode.MODE_CELT_ONLY || this.mode == OpusMode.MODE_CELT_ONLY)
                    return opus_decode_native(null, 0, 0, pcm_out, pcm_out_ptr, frame_size, 0, 0, out dummy, soft_clip);
                /* Otherwise, run the PLC on everything except the size for which we might have FEC */
                duration_copy = this.last_packet_duration;
                if (frame_size - packet_frame_size != 0)
                {
                    ret = opus_decode_native(null, 0, 0, pcm_out, pcm_out_ptr, frame_size - packet_frame_size, 0, 0, out dummy, soft_clip);
                    if (ret < 0)
                    {
                        this.last_packet_duration = duration_copy;
                        return ret;
                    }
                    Inlines.OpusAssert(ret == frame_size - packet_frame_size);
                }
                /* Complete with FEC */
                this.mode = packet_mode;
                this.bandwidth = packet_bandwidth;
                this.frame_size = packet_frame_size;
                this.stream_channels = packet_stream_channels;
                ret = opus_decode_frame(data, data_ptr, size[0], pcm_out, pcm_out_ptr + (this.channels * (frame_size - packet_frame_size)),
                      packet_frame_size, 1);
                if (ret < 0)
                    return ret;
                else {
                    this.last_packet_duration = frame_size;
                    return frame_size;
                }
            }

            if (count * packet_frame_size > frame_size)
                return OpusError.OPUS_BUFFER_TOO_SMALL;

            /* Update the state as the last step to avoid updating it on an invalid packet */
            this.mode = packet_mode;
            this.bandwidth = packet_bandwidth;
            this.frame_size = packet_frame_size;
            this.stream_channels = packet_stream_channels;

            nb_samples = 0;
            for (i = 0; i < count; i++)
            {
                int ret;
                ret = opus_decode_frame(data, data_ptr, size[i], pcm_out, pcm_out_ptr + (nb_samples * this.channels), frame_size - nb_samples, 0);
                if (ret < 0)
                    return ret;
                Inlines.OpusAssert(ret == packet_frame_size);
                data_ptr += size[i];
                nb_samples += ret;
            }
            this.last_packet_duration = nb_samples;

            return nb_samples;
        }

        /// <summary>
        /// Decodes an Opus packet.
        /// </summary>
        /// <param name="in_data">The input payload. This may be NULL if that previous packet was lost in transit (when PLC is enabled)</param>
        /// <param name="in_data_offset">The offset to use when reading the input payload. Usually 0</param>
        /// <param name="len">The number of bytes in the payload</param>
        /// <param name="out_pcm">A buffer to put the output PCM. The output size is (# of samples) * (# of channels).
        /// You can use the OpusPacketInfo helpers to get a hint of the frame size before you decode the packet if you need
        /// exact sizing. Otherwise, the minimum safe buffer size is 5760 samples</param>
        /// <param name="out_pcm_offset">The offset to use when writing to the output buffer</param>
        /// <param name="frame_size">The number of samples (per channel) of available space in the output PCM buf.
        /// If this is less than the maximum packet duration (120ms; 5760 for 48khz), this function will
        /// not be capable of decoding some packets. In the case of PLC (data == NULL) or FEC (decode_fec == true),
        /// then frame_size needs to be exactly the duration of the audio that is missing, otherwise the decoder will
        /// not be in an optimal state to decode the next incoming packet. For the PLC and FEC cases, frame_size *must*
        /// be a multiple of 10 ms.</param>
        /// <param name="decode_fec">Indicates that we want to recreate the PREVIOUS (lost) packet using FEC data from THIS packet. Using this packet
        /// recovery scheme, you will actually decode this packet twice, first with decode_fec TRUE and then again with FALSE. If FEC data is not
        /// available in this packet, the decoder will simply generate a best-effort recreation of the lost packet.</param>
        /// <returns>The number of decoded samples</returns>
        public int Decode(byte[] in_data, int in_data_offset,
             int len, short[] out_pcm, int out_pcm_offset, int frame_size, bool decode_fec = false)
        {
            if (frame_size <= 0)
            {
                throw new ArgumentException("Frame size must be > 0");
            }

            try
            {
                int dummy;
                int ret = opus_decode_native(in_data, in_data_offset, len, out_pcm, out_pcm_offset, frame_size, decode_fec ? 1 : 0, 0, out dummy, 0);

                if (ret < 0)
                {
                    // An error happened; report it
                    if (ret == OpusError.OPUS_BAD_ARG)
                        throw new ArgumentException("OPUS_BAD_ARG while decoding");
                    throw new OpusException("An error occurred during decoding", ret);
                }

                return ret;
            }
            catch (ArgumentException e)
            {
                throw new OpusException("Internal error during decoding: " + e.Message);
            }
        }

        /// <summary>
        /// Decodes an Opus packet, putting the output data into a floating-point buffer.
        /// </summary>
        /// <param name="in_data">The input payload. This may be NULL if that previous packet was lost in transit (when PLC is enabled)</param>
        /// <param name="in_data_offset">The offset to use when reading the input payload. Usually 0</param>
        /// <param name="len">The number of bytes in the payload</param>
        /// <param name="out_pcm">A buffer to put the output PCM. The output size is (# of samples) * (# of channels).
        /// You can use the OpusPacketInfo helpers to get a hint of the frame size before you decode the packet if you need
        /// exact sizing. Otherwise, the minimum safe buffer size is 5760 samples</param>
        /// <param name="out_pcm_offset">The offset to use when writing to the output buffer</param>
        /// <param name="frame_size">The number of samples (per channel) of available space in the output PCM buf.
        /// If this is less than the maximum packet duration (120ms; 5760 for 48khz), this function will
        /// not be capable of decoding some packets. In the case of PLC (data == NULL) or FEC (decode_fec == true),
        /// then frame_size needs to be exactly the duration of the audio that is missing, otherwise the decoder will
        /// not be in an optimal state to decode the next incoming packet. For the PLC and FEC cases, frame_size *must*
        /// be a multiple of 10 ms.</param>
        /// <param name="decode_fec">Indicates that we want to recreate the PREVIOUS (lost) packet using FEC data from THIS packet. Using this packet
        /// recovery scheme, you will actually decode this packet twice, first with decode_fec TRUE and then again with FALSE. If FEC data is not
        /// available in this packet, the decoder will simply generate a best-effort recreation of the lost packet. In that case,
        /// the length of frame_size must be EXACTLY the length of the audio that was lost, or else the decoder will be in an inconsistent state.</param>
        /// <returns>The number of decoded samples (per channel)</returns>
        public int Decode(byte[] in_data, int in_data_offset,
            int len, float[] out_pcm, int out_pcm_offset, int frame_size, bool decode_fec = false)
        {
            short[] output;
            int ret, i;
            int nb_samples;

            if (frame_size <= 0)
            {
                throw new ArgumentException("Frame size must be > 0");
            }
            if (in_data != null && len > 0 && !decode_fec)
            {
                nb_samples = OpusPacketInfo.GetNumSamples(this, in_data, in_data_offset, len);
                if (nb_samples > 0)
                    frame_size = Inlines.IMIN(frame_size, nb_samples);
                else
                    throw new OpusException("An invalid packet was provided (unable to parse # of samples)");
            }
            output = new short[frame_size * this.channels];

            try
            {
                int dummy;
                ret = opus_decode_native(in_data, in_data_offset, len, output, 0, frame_size, decode_fec ? 1 : 0, 0, out dummy, 0);

                if (ret < 0)
                {
                    // An error happened; report it
                    if (ret == OpusError.OPUS_BAD_ARG)
                        throw new ArgumentException("OPUS_BAD_ARG when decoding");
                    throw new OpusException("An error occurred during decoding", ret);
                }

                if (ret > 0)
                {
                    for (i = 0; i < ret * this.channels; i++)
                        out_pcm[out_pcm_offset + i] = (1.0f / 32768.0f) * (output[i]);
                }

                return ret;
            }
            catch (ArgumentException e)
            {
                throw new OpusException("Internal error during decoding: " + e.Message);
            }
        }

        /// <summary>
        /// Gets the encoded bandwidth of the last packet decoded. This may be lower than the actual decoding sample rate,
        /// and is only an indicator of the encoded audio's quality
        /// </summary>
        public OpusBandwidth Bandwidth
        {
            get
            {
                return bandwidth;
            }
        }

        public uint FinalRange
        {
            get
            {
                return rangeFinal;
            }
        }

        /// <summary>
        /// Gets the sample rate that this decoder decodes to. Always constant for the lifetime of the decoder
        /// </summary>
        public int SampleRate
        {
            get
            {
                return Fs;
            }
        }

        /// <summary>
        /// Gets the number of channels that this decoder decodes to. Always constant for the lifetime of the decoder.
        /// </summary>
        public int NumChannels
        {
            get
            {
                return channels;
            }
        }
        
        /// <summary>
        /// Gets the last estimated pitch value of the decoded audio
        /// </summary>
        public int Pitch
        {
            get
            {
                if (prev_mode == OpusMode.MODE_CELT_ONLY)
                {
                    return Celt_Decoder.GetPitch();
                }
                else
                    return DecControl.prevPitchLag;
            }
        }

        /// <summary>
        /// Gets or sets the gain (Q8) to use in decoding
        /// </summary>
        public int Gain
        {
            get
            {
                return decode_gain;
            }
            set
            {
                if (value < -32768 || value > 32767)
                {
                    throw new ArgumentException("Gain must be within the range of a signed int16");
                }

                decode_gain = value;
            }
        }

        /// <summary>
        /// Gets the duration of the last packet, in PCM samples per channel
        /// </summary>
        public int LastPacketDuration
        {
            get
            {
                return last_packet_duration;
            }
        }

        /// <summary>
        /// Resets all buffers and prepares this decoder to process a fresh (unrelated) stream
        /// </summary>
        public void ResetState()
        {
            PartialReset();
            Celt_Decoder.ResetState();
            DecodeAPI.silk_InitDecoder(SilkDecoder);
            stream_channels = channels;
            frame_size = Fs / 400;
        }
    }
}
