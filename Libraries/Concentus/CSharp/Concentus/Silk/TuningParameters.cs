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
    using System.Diagnostics;

    internal class TuningParameters
    {
        /* Decay time for EntropyCoder.BITREServoir */
        internal const int BITRESERVOIR_DECAY_TIME_MS = 500;

        /*******************/
        /* Pitch estimator */
        /*******************/

        /* Level of noise floor for whitening filter LPC analysis in pitch analysis */
        internal const float FIND_PITCH_WHITE_NOISE_FRACTION = 1e-3f;

        /* Bandwidth expansion for whitening filter in pitch analysis */
        internal const float FIND_PITCH_BANDWIDTH_EXPANSION = 0.99f;

        /*********************/
        /* Linear prediction */
        /*********************/

        /* LPC analysis regularization */
        internal const float FIND_LPC_COND_FAC = 1e-5f;

        /* LTP analysis defines */
        internal const float FIND_LTP_COND_FAC = 1e-5f;
        internal const float LTP_DAMPING = 0.05f;
        internal const float LTP_SMOOTHING = 0.1f;

        /* LTP quantization settings */
        internal const float MU_LTP_QUANT_NB = 0.03f;
        internal const float MU_LTP_QUANT_MB = 0.025f;
        internal const float MU_LTP_QUANT_WB = 0.02f;

        /* Max cumulative LTP gain */
        internal const float MAX_SUM_LOG_GAIN_DB = 250.0f;

        /***********************/
        /* High pass filtering */
        /***********************/

        /* Smoothing parameters for low end of pitch frequency range estimation */
        internal const float VARIABLE_HP_SMTH_COEF1 = 0.1f;
        internal const float VARIABLE_HP_SMTH_COEF2 = 0.015f;
        internal const float VARIABLE_HP_MAX_DELTA_FREQ = 0.4f;

        /* Min and max cut-off frequency values (-3 dB points) */
        internal const int VARIABLE_HP_MIN_CUTOFF_HZ = 60;
        internal const int VARIABLE_HP_MAX_CUTOFF_HZ = 100;

        /***********/
        /* Various */
        /***********/

        /* VAD threshold */
        internal const float SPEECH_ACTIVITY_DTX_THRES = 0.05f;

        /* Speech Activity LBRR enable threshold */
        internal const float LBRR_SPEECH_ACTIVITY_THRES = 0.3f;

        /*************************/
        /* Perceptual parameters */
        /*************************/

        /* reduction in coding SNR during low speech activity */
        internal const float BG_SNR_DECR_dB = 2.0f;

        /* factor for reducing quantization noise during voiced speech */
        internal const float HARM_SNR_INCR_dB = 2.0f;

        /* factor for reducing quantization noise for unvoiced sparse signals */
        internal const float SPARSE_SNR_INCR_dB = 2.0f;

        /* threshold for sparseness measure above which to use lower quantization offset during unvoiced */
        internal const float SPARSENESS_THRESHOLD_QNT_OFFSET = 0.75f;

        /* warping control */
        internal const float WARPING_MULTIPLIER = 0.015f;

        /* fraction added to first autocorrelation value */
        internal const float SHAPE_WHITE_NOISE_FRACTION = 5e-5f;

        /* noise shaping filter chirp factor */
        internal const float BANDWIDTH_EXPANSION = 0.95f;

        /* difference between chirp factors for analysis and synthesis noise shaping filters at low bitrates */
        internal const float LOW_RATE_BANDWIDTH_EXPANSION_DELTA = 0.01f;

        /* extra harmonic boosting (signal shaping) at low bitrates */
        internal const float LOW_RATE_HARMONIC_BOOST = 0.1f;

        /* extra harmonic boosting (signal shaping) for noisy input signals */
        internal const float LOW_INPUT_QUALITY_HARMONIC_BOOST = 0.1f;

        /* harmonic noise shaping */
        internal const float HARMONIC_SHAPING = 0.3f;

        /* extra harmonic noise shaping for high bitrates or noisy input */
        internal const float HIGH_RATE_OR_LOW_QUALITY_HARMONIC_SHAPING = 0.2f;

        /* parameter for shaping noise towards higher frequencies */
        internal const float HP_NOISE_COEF = 0.25f;

        /* parameter for shaping noise even more towards higher frequencies during voiced speech */
        internal const float HARM_HP_NOISE_COEF = 0.35f;

        /* parameter for applying a high-pass tilt to the input signal */
        internal const float INPUT_TILT = 0.05f;

        /* parameter for extra high-pass tilt to the input signal at high rates */
        internal const float HIGH_RATE_INPUT_TILT = 0.1f;

        /* parameter for reducing noise at the very low frequencies */
        internal const float LOW_FREQ_SHAPING = 4.0f;

        /* less reduction of noise at the very low frequencies for signals with low SNR at low frequencies */
        internal const float LOW_QUALITY_LOW_FREQ_SHAPING_DECR = 0.5f;

        /* subframe smoothing coefficient for HarmBoost, HarmShapeGain, Tilt (lower . more smoothing) */
        internal const float SUBFR_SMTH_COEF = 0.4f;

        /* parameters defining the R/D tradeoff in the residual quantizer */
        internal const float LAMBDA_OFFSET = 1.2f;
        internal const float LAMBDA_SPEECH_ACT = -0.2f;
        internal const float LAMBDA_DELAYED_DECISIONS = -0.05f;
        internal const float LAMBDA_INPUT_QUALITY = -0.1f;
        internal const float LAMBDA_CODING_QUALITY = -0.2f;
        internal const float LAMBDA_QUANT_OFFSET = 0.8f;

        /* Compensation in bitrate calculations for 10 ms modes */
        internal const int REDUCE_BITRATE_10_MS_BPS = 2200;

        /* Maximum time before allowing a bandwidth transition */
        internal const int MAX_BANDWIDTH_SWITCH_DELAY_MS = 5000;
    }
}
