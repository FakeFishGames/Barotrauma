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

namespace Concentus.Silk
{
    using Concentus.Common;
    using Concentus.Common.CPlusPlus;
    using Concentus.Silk.Enums;
    using Concentus.Silk.Structs;
    using System;
    using System.Diagnostics;

    internal static class SilkConstants
    {
        /* Max number of encoder channels (1/2) */
        public const int ENCODER_NUM_CHANNELS = 2;
        /* Number of decoder channels (1/2) */
        public const int DECODER_NUM_CHANNELS = 2;

        public const int MAX_FRAMES_PER_PACKET = 3;

        /* Limits on bitrate */
        public const int MIN_TARGET_RATE_BPS = 5000;
        public const int MAX_TARGET_RATE_BPS = 80000;
        public const int TARGET_RATE_TAB_SZ = 8;

        /* LBRR thresholds */
        public const int LBRR_NB_MIN_RATE_BPS = 12000;
        public const int LBRR_MB_MIN_RATE_BPS = 14000;
        public const int LBRR_WB_MIN_RATE_BPS = 16000;

        /* DTX settings */
        public const int NB_SPEECH_FRAMES_BEFORE_DTX = 10;      /* eq 200 ms */
        public const int MAX_CONSECUTIVE_DTX = 20;      /* eq 400 ms */

        /* Maximum sampling frequency */
        public const int MAX_FS_KHZ = 16;
        public const int MAX_API_FS_KHZ = 48;

        // FIXME can use enums here
        /* Signal types */
        public const int TYPE_NO_VOICE_ACTIVITY = 0;
        public const int TYPE_UNVOICED = 1;
        public const int TYPE_VOICED = 2;
        
        /* Conditional coding types */
        public const int CODE_INDEPENDENTLY = 0;
        public const int CODE_INDEPENDENTLY_NO_LTP_SCALING = 1;
        public const int CODE_CONDITIONALLY = 2;

        /* Settings for stereo processing */
        public const int STEREO_QUANT_TAB_SIZE = 16;
        public const int STEREO_QUANT_SUB_STEPS = 5;
        public const int STEREO_INTERP_LEN_MS = 8;       /* must be even */
        public const float STEREO_RATIO_SMOOTH_COEF = 0.01f;    /* smoothing coef for signal norms and stereo width */

        /* Range of pitch lag estimates */
        public const int PITCH_EST_MIN_LAG_MS = 2;       /* 2 ms . 500 Hz */
        public const int PITCH_EST_MAX_LAG_MS = 18;      /* 18 ms . 56 Hz */

        /* Maximum number of subframes */
        public const int MAX_NB_SUBFR = 4;

        /* Number of samples per frame */
        public const int LTP_MEM_LENGTH_MS = 20;
        public const int SUB_FRAME_LENGTH_MS = 5;
        public const int MAX_SUB_FRAME_LENGTH = (SUB_FRAME_LENGTH_MS * MAX_FS_KHZ);
        public const int MAX_FRAME_LENGTH_MS = (SUB_FRAME_LENGTH_MS * MAX_NB_SUBFR);
        public const int MAX_FRAME_LENGTH = (MAX_FRAME_LENGTH_MS * MAX_FS_KHZ);

        /* Milliseconds of lookahead for pitch analysis */
        public const int LA_PITCH_MS = 2;
        public const int LA_PITCH_MAX = (LA_PITCH_MS * MAX_FS_KHZ);

        /* Order of LPC used in find pitch */
        public const int MAX_FIND_PITCH_LPC_ORDER = 16;

        /* Length of LPC window used in find pitch */
        public const int FIND_PITCH_LPC_WIN_MS = (20 + (LA_PITCH_MS << 1));
        public const int FIND_PITCH_LPC_WIN_MS_2_SF = (10 + (LA_PITCH_MS << 1));
        public const int FIND_PITCH_LPC_WIN_MAX = (FIND_PITCH_LPC_WIN_MS * MAX_FS_KHZ);

        /* Milliseconds of lookahead for noise shape analysis */
        public const int LA_SHAPE_MS = 5;
        public const int LA_SHAPE_MAX = (LA_SHAPE_MS * MAX_FS_KHZ);

        /* Maximum length of LPC window used in noise shape analysis */
        public const int SHAPE_LPC_WIN_MAX = (15 * MAX_FS_KHZ);

        /* dB level of lowest gain quantization level */
        public const int MIN_QGAIN_DB = 2;
        /* dB level of highest gain quantization level */
        public const int MAX_QGAIN_DB = 88;
        /* Number of gain quantization levels */
        public const int N_LEVELS_QGAIN = 64;
        /* Max increase in gain quantization index */
        public const int MAX_DELTA_GAIN_QUANT = 36;
        /* Max decrease in gain quantization index */
        public const int MIN_DELTA_GAIN_QUANT = -4;

        /* Quantization offsets (multiples of 4) */
        public const short OFFSET_VL_Q10 = 32;
        public const short OFFSET_VH_Q10 = 100;
        public const short OFFSET_UVL_Q10 = 100;
        public const short OFFSET_UVH_Q10 = 240;

        public const int QUANT_LEVEL_ADJUST_Q10 = 80;

        /* Maximum numbers of iterations used to stabilize an LPC vector */
        public const int MAX_LPC_STABILIZE_ITERATIONS = 16;
        public const float MAX_PREDICTION_POWER_GAIN = 1e4f;
        public const float MAX_PREDICTION_POWER_GAIN_AFTER_RESET = 1e2f;

        public const int SILK_MAX_ORDER_LPC = 16;
        public const int MAX_LPC_ORDER = 16;
        public const int MIN_LPC_ORDER = 10;

        /* Find Pred Coef defines */
        public const int LTP_ORDER = 5;

        /* LTP quantization settings */
        public const int NB_LTP_CBKS = 3;

        /* Flag to use harmonic noise shaping */
        public const int USE_HARM_SHAPING = 1;

        /* Max LPC order of noise shaping filters */
        public const int MAX_SHAPE_LPC_ORDER = 16;

        public const int HARM_SHAPE_FIR_TAPS = 3;

        /* Maximum number of delayed decision states */
        public const int MAX_DEL_DEC_STATES = 4;

        public const int LTP_BUF_LENGTH = 512;
        public const int LTP_MASK = (LTP_BUF_LENGTH - 1);

        public const int DECISION_DELAY = 32;
        public const int DECISION_DELAY_MASK = (DECISION_DELAY - 1);

        /* Number of subframes for excitation entropy coding */
        public const int SHELL_CODEC_FRAME_LENGTH = 16;
        public const int LOG2_SHELL_CODEC_FRAME_LENGTH = 4;
        public const int MAX_NB_SHELL_BLOCKS = (MAX_FRAME_LENGTH / SHELL_CODEC_FRAME_LENGTH);

        /* Number of rate levels, for entropy coding of excitation */
        public const int N_RATE_LEVELS = 10;

        /* Maximum sum of pulses per shell coding frame */
        public const int SILK_MAX_PULSES = 16;

        public const int MAX_MATRIX_SIZE = MAX_LPC_ORDER; /* Max of LPC Order and LTP order */

        internal static readonly int NSQ_LPC_BUF_LENGTH = Math.Max(MAX_LPC_ORDER, DECISION_DELAY);

        /***************************/
        /* Voice activity detector */
        /***************************/
        public const int VAD_N_BANDS = 4;

        public const int VAD_INTERNAL_SUBFRAMES_LOG2 = 2;
        public const int VAD_INTERNAL_SUBFRAMES = (1 << VAD_INTERNAL_SUBFRAMES_LOG2);

        public const int VAD_NOISE_LEVEL_SMOOTH_COEF_Q16 = 1024;    /* Must be <  4096 */
        public const int VAD_NOISE_LEVELS_BIAS = 50;

        /* Sigmoid settings */
        public const int VAD_NEGATIVE_OFFSET_Q5 = 128;     /* sigmoid is 0 at -128 */
        public const int VAD_SNR_FACTOR_Q16 = 45000;

        /* smoothing for SNR measurement */
        public const int VAD_SNR_SMOOTH_COEF_Q18 = 4096;

        /* Size of the piecewise linear cosine approximation table for the LSFs */
        public const int LSF_COS_TAB_SZ = 128;

        /******************/
        /* NLSF quantizer */
        /******************/
        public const int NLSF_W_Q = 2;
        public const int NLSF_VQ_MAX_VECTORS = 32;
        public const int NLSF_VQ_MAX_SURVIVORS = 32;
        public const int NLSF_QUANT_MAX_AMPLITUDE = 4;
        public const int NLSF_QUANT_MAX_AMPLITUDE_EXT = 10;
        public const float NLSF_QUANT_LEVEL_ADJ = 0.1f;
        public const int NLSF_QUANT_DEL_DEC_STATES_LOG2 = 2;
        public const int NLSF_QUANT_DEL_DEC_STATES = (1 << NLSF_QUANT_DEL_DEC_STATES_LOG2);

        /* Transition filtering for mode switching */
        public const int TRANSITION_TIME_MS = 5120;    /* 5120 = 64 * FRAME_LENGTH_MS * ( TRANSITION_INT_NUM - 1 ) = 64*(20*4)*/
        public const int TRANSITION_NB = 3;       /* Hardcoded in tables */
        public const int TRANSITION_NA = 2;       /* Hardcoded in tables */
        public const int TRANSITION_INT_NUM = 5;       /* Hardcoded in tables */
        public const int TRANSITION_FRAMES = (TRANSITION_TIME_MS / MAX_FRAME_LENGTH_MS);
        public const int TRANSITION_INT_STEPS = (TRANSITION_FRAMES / (TRANSITION_INT_NUM - 1));

        /* BWE factors to apply after packet loss */
        public const int BWE_AFTER_LOSS_Q16 = 63570;

        /* Defines for CN generation */
        public const int CNG_BUF_MASK_MAX = 255;     /* 2^floor(log2(MAX_FRAME_LENGTH))-1    */
        public const int CNG_GAIN_SMTH_Q16 = 4634;    /* 0.25^(1/4)                           */
        public const int CNG_NLSF_SMTH_Q16 = 16348;   /* 0.25                                 */

        /********************************************************/
        /* Definitions for pitch estimator (from pitch_est_defines.h) */
        /********************************************************/

        public const int PE_MAX_FS_KHZ = 16; /* Maximum sampling frequency used */

        public const int PE_MAX_NB_SUBFR = 4;
        public const int PE_SUBFR_LENGTH_MS = 5;   /* 5 ms */

        public const int PE_LTP_MEM_LENGTH_MS = (4 * PE_SUBFR_LENGTH_MS);

        public const int PE_MAX_FRAME_LENGTH_MS = (PE_LTP_MEM_LENGTH_MS + PE_MAX_NB_SUBFR * PE_SUBFR_LENGTH_MS);
        public const int PE_MAX_FRAME_LENGTH = (PE_MAX_FRAME_LENGTH_MS * PE_MAX_FS_KHZ );
        public const int PE_MAX_FRAME_LENGTH_ST_1 = (PE_MAX_FRAME_LENGTH >> 2);
        public const int PE_MAX_FRAME_LENGTH_ST_2 = (PE_MAX_FRAME_LENGTH >> 1);

        public const int PE_MAX_LAG_MS = 18;           /* 18 ms . 56 Hz */
        public const int PE_MIN_LAG_MS = 2;            /* 2 ms . 500 Hz */
        public const int PE_MAX_LAG = (PE_MAX_LAG_MS * PE_MAX_FS_KHZ);
        public const int PE_MIN_LAG = (PE_MIN_LAG_MS * PE_MAX_FS_KHZ);

        public const int PE_D_SRCH_LENGTH = 24;

        public const int PE_NB_STAGE3_LAGS = 5;

        public const int PE_NB_CBKS_STAGE2 = 3;
        public const int PE_NB_CBKS_STAGE2_EXT = 11;

        public const int PE_NB_CBKS_STAGE3_MAX = 34;
        public const int PE_NB_CBKS_STAGE3_MID = 24;
        public const int PE_NB_CBKS_STAGE3_MIN = 16;

        public const int PE_NB_CBKS_STAGE3_10MS = 12;
        public const int PE_NB_CBKS_STAGE2_10MS = 3;

        public const float PE_SHORTLAG_BIAS = 0.2f;    /* for logarithmic weighting    */
        public const float PE_PREVLAG_BIAS = 0.2f;    /* for logarithmic weighting    */
        public const float PE_FLATCONTOUR_BIAS = 0.05f;

        public const int SILK_PE_MIN_COMPLEX = 0;
        public const int SILK_PE_MID_COMPLEX = 1;
        public const int SILK_PE_MAX_COMPLEX = 2;

        // Definitions for PLC (from plc.h)

        public const float BWE_COEF = 0.99f;
        public const int V_PITCH_GAIN_START_MIN_Q14 = 11469;               /* 0.7 in Q14               */
        public const int V_PITCH_GAIN_START_MAX_Q14 = 15565;               /* 0.95 in Q14              */
        public const int MAX_PITCH_LAG_MS = 18;
        public const int RAND_BUF_SIZE = 128;
        public const int RAND_BUF_MASK = (RAND_BUF_SIZE - 1);
        public const int LOG2_INV_LPC_GAIN_HIGH_THRES = 3;                   /* 2^3 = 8 dB LPC gain      */
        public const int LOG2_INV_LPC_GAIN_LOW_THRES = 8;                   /* 2^8 = 24 dB LPC gain     */
        public const int PITCH_DRIFT_FAC_Q16 = 655;                 /* 0.01 in Q16              */

        // Definitions for resampler (from resampler_structs.h)

        public const int SILK_RESAMPLER_MAX_FIR_ORDER = 36;
        public const int SILK_RESAMPLER_MAX_IIR_ORDER = 6;

        // from resampler_rom.h
        public const int RESAMPLER_DOWN_ORDER_FIR0 = 18;
        public const int RESAMPLER_DOWN_ORDER_FIR1 = 24;
        public const int RESAMPLER_DOWN_ORDER_FIR2 = 36;
        public const int RESAMPLER_ORDER_FIR_12 = 8;

        // from resampler_private.h

        /* Number of input samples to process in the inner loop */
        public const int RESAMPLER_MAX_BATCH_SIZE_MS = 10;
        public const int RESAMPLER_MAX_FS_KHZ = 48;
        public const int RESAMPLER_MAX_BATCH_SIZE_IN = (RESAMPLER_MAX_BATCH_SIZE_MS * RESAMPLER_MAX_FS_KHZ);

        // from api.h

        public const int SILK_MAX_FRAMES_PER_PACKET = 3;
    }
}
