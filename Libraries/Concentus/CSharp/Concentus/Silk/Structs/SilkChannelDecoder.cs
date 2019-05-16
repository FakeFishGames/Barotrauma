/* Copyright (c) 2006-2011 Skype Limited. All Rights Reserved
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

namespace Concentus.Silk.Structs
{
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using Concentus.Silk.Enums;
    using System;
    /// <summary>
    /// Decoder state
    /// </summary>
    internal class SilkChannelDecoder
    {
        internal int prev_gain_Q16 = 0;
        internal readonly int[] exc_Q14 = new int[SilkConstants.MAX_FRAME_LENGTH];
        internal readonly int[] sLPC_Q14_buf = new int[SilkConstants.MAX_LPC_ORDER];
        internal readonly short[] outBuf = new short[SilkConstants.MAX_FRAME_LENGTH + 2 * SilkConstants.MAX_SUB_FRAME_LENGTH];  /* Buffer for output signal                     */
        internal int lagPrev = 0;                            /* Previous Lag                                                     */
        internal sbyte LastGainIndex = 0;                      /* Previous gain index                                              */
        internal int fs_kHz = 0;                             /* Sampling frequency in kHz                                        */
        internal int fs_API_hz = 0;                          /* API sample frequency (Hz)                                        */
        internal int nb_subfr = 0;                           /* Number of 5 ms subframes in a frame                              */
        internal int frame_length = 0;                       /* Frame length (samples)                                           */
        internal int subfr_length = 0;                       /* Subframe length (samples)                                        */
        internal int ltp_mem_length = 0;                     /* Length of LTP memory                                             */
        internal int LPC_order = 0;                          /* LPC order                                                        */
        internal readonly short[] prevNLSF_Q15 = new short[SilkConstants.MAX_LPC_ORDER];      /* Used to interpolate LSFs                                         */
        internal int first_frame_after_reset = 0;            /* Flag for deactivating NLSF interpolation                         */
        internal byte[] pitch_lag_low_bits_iCDF;           /* Pointer to iCDF table for low bits of pitch lag index            */
        internal byte[] pitch_contour_iCDF;                /* Pointer to iCDF table for pitch contour index                    */

        /* For buffering payload in case of more frames per packet */
        internal int nFramesDecoded = 0;
        internal int nFramesPerPacket = 0;

        /* Specifically for entropy coding */
        internal int ec_prevSignalType = 0;
        internal short ec_prevLagIndex = 0;

        internal readonly int[] VAD_flags = new int[SilkConstants.MAX_FRAMES_PER_PACKET];
        internal int LBRR_flag = 0;
        internal readonly int[] LBRR_flags = new int[SilkConstants.MAX_FRAMES_PER_PACKET];

        internal readonly SilkResamplerState resampler_state = new SilkResamplerState();

        internal NLSFCodebook psNLSF_CB = null;                         /* Pointer to NLSF codebook                                         */

        /* Quantization indices */
        internal readonly SideInfoIndices indices = new SideInfoIndices();

        /* CNG state */
        internal readonly CNGState sCNG = new CNGState();

        /* Stuff used for PLC */
        internal int lossCnt = 0;
        internal int prevSignalType = 0;

        internal readonly PLCStruct sPLC = new PLCStruct();
        
        internal void Reset()
        {
            prev_gain_Q16 = 0;
            Arrays.MemSetInt(exc_Q14, 0, SilkConstants.MAX_FRAME_LENGTH);
            Arrays.MemSetInt(sLPC_Q14_buf, 0, SilkConstants.MAX_LPC_ORDER);
            Arrays.MemSetShort(outBuf, 0, SilkConstants.MAX_FRAME_LENGTH + 2 * SilkConstants.MAX_SUB_FRAME_LENGTH);
            lagPrev = 0;
            LastGainIndex = 0;
            fs_kHz = 0;
            fs_API_hz = 0;
            nb_subfr = 0;
            frame_length = 0;
            subfr_length = 0;
            ltp_mem_length = 0;
            LPC_order = 0;
            Arrays.MemSetShort(prevNLSF_Q15, 0, SilkConstants.MAX_LPC_ORDER);
            first_frame_after_reset = 0;
            pitch_lag_low_bits_iCDF = null;
            pitch_contour_iCDF = null;
            nFramesDecoded = 0;
            nFramesPerPacket = 0;
            ec_prevSignalType = 0;
            ec_prevLagIndex = 0;
            Arrays.MemSetInt(VAD_flags, 0, SilkConstants.MAX_FRAMES_PER_PACKET);
            LBRR_flag = 0;
            Arrays.MemSetInt(LBRR_flags, 0, SilkConstants.MAX_FRAMES_PER_PACKET);
            resampler_state.Reset();
            psNLSF_CB = null;
            indices.Reset();
            sCNG.Reset();
            lossCnt = 0;
            prevSignalType = 0;
            sPLC.Reset();
        }

        /// <summary>
        /// Init Decoder State
        /// </summary>
        /// <returns></returns>
        internal int silk_init_decoder()
        {
            /* Clear the entire encoder state, except anything copied */
            this.Reset();

            /* Used to deactivate LSF interpolation */
            this.first_frame_after_reset = 1;
            this.prev_gain_Q16 = 65536;

            /* Reset CNG state */
            silk_CNG_Reset();

            /* Reset PLC state */
            silk_PLC_Reset();

            return (0);
        }

        /// <summary>
        /// Resets CNG state
        /// </summary>
        private void silk_CNG_Reset()
        {
            int i, NLSF_step_Q15, NLSF_acc_Q15;

            NLSF_step_Q15 = Inlines.silk_DIV32_16(short.MaxValue, this.LPC_order + 1);
            NLSF_acc_Q15 = 0;
            for (i = 0; i < this.LPC_order; i++)
            {
                NLSF_acc_Q15 += NLSF_step_Q15;
                this.sCNG.CNG_smth_NLSF_Q15[i] = (short)(NLSF_acc_Q15);
            }
            this.sCNG.CNG_smth_Gain_Q16 = 0;
            this.sCNG.rand_seed = 3176576;
        }

        /// <summary>
        /// Resets PLC state
        /// </summary>
        private void silk_PLC_Reset()
        {
            this.sPLC.pitchL_Q8 = Inlines.silk_LSHIFT(this.frame_length, 8 - 1);
            this.sPLC.prevGain_Q16[0] = ((int)((1) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(1, 16)*/;
            this.sPLC.prevGain_Q16[1] = ((int)((1) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(1, 16)*/;
            this.sPLC.subfr_length = 20;
            this.sPLC.nb_subfr = 2;
        }

        /* Set decoder sampling rate */
        internal int silk_decoder_set_fs(
            int fs_kHz,                         /* I    Sampling frequency (kHz)                    */
            int fs_API_Hz                       /* I    API Sampling frequency (Hz)                 */
        )
        {
            int frame_length, ret = 0;

            Inlines.OpusAssert(fs_kHz == 8 || fs_kHz == 12 || fs_kHz == 16);
            Inlines.OpusAssert(this.nb_subfr == SilkConstants.MAX_NB_SUBFR || this.nb_subfr == SilkConstants.MAX_NB_SUBFR / 2);

            /* New (sub)frame length */
            this.subfr_length = Inlines.silk_SMULBB(SilkConstants.SUB_FRAME_LENGTH_MS, fs_kHz);
            frame_length = Inlines.silk_SMULBB(this.nb_subfr, this.subfr_length);

            /* Initialize resampler when switching internal or external sampling frequency */
            if (this.fs_kHz != fs_kHz || this.fs_API_hz != fs_API_Hz)
            {
                /* Initialize the resampler for dec_API.c preparing resampling from fs_kHz to API_fs_Hz */
                ret += Resampler.silk_resampler_init(this.resampler_state, Inlines.silk_SMULBB(fs_kHz, 1000), fs_API_Hz, 0);

                this.fs_API_hz = fs_API_Hz;
            }

            if (this.fs_kHz != fs_kHz || frame_length != this.frame_length)
            {
                if (fs_kHz == 8)
                {
                    if (this.nb_subfr == SilkConstants.MAX_NB_SUBFR)
                    {
                        this.pitch_contour_iCDF = Tables.silk_pitch_contour_NB_iCDF;
                    }
                    else {
                        this.pitch_contour_iCDF = Tables.silk_pitch_contour_10_ms_NB_iCDF;
                    }
                }
                else {
                    if (this.nb_subfr == SilkConstants.MAX_NB_SUBFR)
                    {
                        this.pitch_contour_iCDF = Tables.silk_pitch_contour_iCDF;
                    }
                    else {
                        this.pitch_contour_iCDF = Tables.silk_pitch_contour_10_ms_iCDF;
                    }
                }
                if (this.fs_kHz != fs_kHz)
                {
                    this.ltp_mem_length = Inlines.silk_SMULBB(SilkConstants.LTP_MEM_LENGTH_MS, fs_kHz);
                    if (fs_kHz == 8 || fs_kHz == 12)
                    {
                        this.LPC_order = SilkConstants.MIN_LPC_ORDER;
                        this.psNLSF_CB = Tables.silk_NLSF_CB_NB_MB;
                    }
                    else {
                        this.LPC_order = SilkConstants.MAX_LPC_ORDER;
                        this.psNLSF_CB = Tables.silk_NLSF_CB_WB;
                    }
                    if (fs_kHz == 16)
                    {
                        this.pitch_lag_low_bits_iCDF = Tables.silk_uniform8_iCDF;
                    }
                    else if (fs_kHz == 12)
                    {
                        this.pitch_lag_low_bits_iCDF = Tables.silk_uniform6_iCDF;
                    }
                    else if (fs_kHz == 8)
                    {
                        this.pitch_lag_low_bits_iCDF = Tables.silk_uniform4_iCDF;
                    }
                    else {
                        /* unsupported sampling rate */
                        Inlines.OpusAssert(false);
                    }
                    this.first_frame_after_reset = 1;
                    this.lagPrev = 100;
                    this.LastGainIndex = 10;
                    this.prevSignalType = SilkConstants.TYPE_NO_VOICE_ACTIVITY;
                    Arrays.MemSetShort(this.outBuf, 0, SilkConstants.MAX_FRAME_LENGTH + 2 * SilkConstants.MAX_SUB_FRAME_LENGTH);
                    Arrays.MemSetInt(this.sLPC_Q14_buf, 0, SilkConstants.MAX_LPC_ORDER);
                }

                this.fs_kHz = fs_kHz;
                this.frame_length = frame_length;
            }

            /* Check that settings are valid */
            Inlines.OpusAssert(this.frame_length > 0 && this.frame_length <= SilkConstants.MAX_FRAME_LENGTH);

            return ret;
        }

        /****************/
        /* Decode frame */
        /****************/
        internal int silk_decode_frame(
            EntropyCoder psRangeDec,                    /* I/O  Compressor data structure                   */
            short[] pOut,                         /* O    Pointer to output speech frame              */
            int pOut_ptr,
            BoxedValueInt pN,                            /* O    Pointer to size of output frame             */
            int lostFlag,                       /* I    0: no loss, 1 loss, 2 decode fec            */
            int condCoding                     /* I    The type of conditional coding to use       */
        )
        {
            SilkDecoderControl thisCtrl = new SilkDecoderControl();
            int L, mv_len, ret = 0;

            L = this.frame_length;
            thisCtrl.LTP_scale_Q14 = 0;

            /* Safety checks */
            Inlines.OpusAssert(L > 0 && L <= SilkConstants.MAX_FRAME_LENGTH);

            if (lostFlag == DecoderAPIFlag.FLAG_DECODE_NORMAL ||
                (lostFlag == DecoderAPIFlag.FLAG_DECODE_LBRR && this.LBRR_flags[this.nFramesDecoded] == 1))
            {
                short[] pulses = new short[(L + SilkConstants.SHELL_CODEC_FRAME_LENGTH - 1) & ~(SilkConstants.SHELL_CODEC_FRAME_LENGTH - 1)];
                /*********************************************/
                /* Decode quantization indices of side info  */
                /*********************************************/
                DecodeIndices.silk_decode_indices(this, psRangeDec, this.nFramesDecoded, lostFlag, condCoding);

                /*********************************************/
                /* Decode quantization indices of excitation */
                /*********************************************/
                DecodePulses.silk_decode_pulses(psRangeDec, pulses, this.indices.signalType,
                        this.indices.quantOffsetType, this.frame_length);

                /********************************************/
                /* Decode parameters and pulse signal       */
                /********************************************/
                DecodeParameters.silk_decode_parameters(this, thisCtrl, condCoding);

                /********************************************************/
                /* Run inverse NSQ                                      */
                /********************************************************/
                DecodeCore.silk_decode_core(this, thisCtrl, pOut, pOut_ptr, pulses);

                /********************************************************/
                /* Update PLC state                                     */
                /********************************************************/
                PLC.silk_PLC(this, thisCtrl, pOut, pOut_ptr, 0);

                this.lossCnt = 0;
                this.prevSignalType = this.indices.signalType;
                Inlines.OpusAssert(this.prevSignalType >= 0 && this.prevSignalType <= 2);

                /* A frame has been decoded without errors */
                this.first_frame_after_reset = 0;
            }
            else
            {
                /* Handle packet loss by extrapolation */
                PLC.silk_PLC(this, thisCtrl, pOut, pOut_ptr, 1);
            }

            /*************************/
            /* Update output buffer. */
            /*************************/
            Inlines.OpusAssert(this.ltp_mem_length >= this.frame_length);
            mv_len = this.ltp_mem_length - this.frame_length;
            Arrays.MemMoveShort(this.outBuf, this.frame_length, 0, mv_len);
            Array.Copy(pOut, pOut_ptr, this.outBuf, mv_len, this.frame_length);

            /************************************************/
            /* Comfort noise generation / estimation        */
            /************************************************/
            CNG.silk_CNG(this, thisCtrl, pOut, pOut_ptr, L);

            /****************************************************************/
            /* Ensure smooth connection of extrapolated and good frames     */
            /****************************************************************/
            PLC.silk_PLC_glue_frames(this, pOut, pOut_ptr, L);

            /* Update some decoder state variables */
            this.lagPrev = thisCtrl.pitchL[this.nb_subfr - 1];

            /* Set output frame length */
            pN.Val = L;

            return ret;
        }
    }
}
