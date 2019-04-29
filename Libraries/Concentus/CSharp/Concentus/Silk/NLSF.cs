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

    /// <summary>
    /// Normalized line spectrum frequency processor
    /// </summary>
    internal static class NLSF
    {
        private const int MAX_STABILIZE_LOOPS = 20;

        private const int QA = 16;

        /// <summary>
        /// Number of binary divisions, when not in low complexity mode
        /// </summary>
        private const int BIN_DIV_STEPS_A2NLSF = 3; /* must be no higher than 16 - log2( LSF_COS_TAB_SZ ) */

        private const int MAX_ITERATIONS_A2NLSF = 30;

        /// <summary>
        /// Compute quantization errors for an LPC_order element input vector for a VQ codebook
        /// </summary>
        /// <param name="err_Q26">(O) Quantization errors [K]</param>
        /// <param name="in_Q15">(I) Input vectors to be quantized [LPC_order]</param>
        /// <param name="pCB_Q8">(I) Codebook vectors [K*LPC_order]</param>
        /// <param name="K">(I) Number of codebook vectors</param>
        /// <param name="LPC_order">(I) Number of LPCs</param>
        internal static void silk_NLSF_VQ(int[] err_Q26, short[] in_Q15, byte[] pCB_Q8, int K, int LPC_order)
        {
            int diff_Q15, sum_error_Q30, sum_error_Q26;
            int pCB_idx = 0;

            Inlines.OpusAssert(err_Q26 != null);
            Inlines.OpusAssert(LPC_order <= 16);
            Inlines.OpusAssert((LPC_order & 1) == 0);

            // Loop over codebook
            for (int i = 0; i < K; i++)
            {
                sum_error_Q26 = 0;

                for (int m = 0; m < LPC_order; m += 2)
                {
                    // Compute weighted squared quantization error for index m
                    diff_Q15 = Inlines.silk_SUB_LSHIFT32(in_Q15[m], pCB_Q8[pCB_idx++], 7); // range: [ -32767 : 32767 ]
                    sum_error_Q30 = Inlines.silk_SMULBB(diff_Q15, diff_Q15);

                    // Compute weighted squared quantization error for index m + 1
                    diff_Q15 = Inlines.silk_SUB_LSHIFT32(in_Q15[m + 1], pCB_Q8[pCB_idx++], 7); // range: [ -32767 : 32767 ]
                    sum_error_Q30 = Inlines.silk_SMLABB(sum_error_Q30, diff_Q15, diff_Q15);

                    sum_error_Q26 = Inlines.silk_ADD_RSHIFT32(sum_error_Q26, sum_error_Q30, 4);

                    Inlines.OpusAssert(sum_error_Q26 >= 0);
                    Inlines.OpusAssert(sum_error_Q30 >= 0);
                }

                err_Q26[i] = sum_error_Q26;
            }
        }

        /// <summary>
        /// Laroia low complexity NLSF weights
        /// </summary>
        /// <param name="pNLSFW_Q_OUT">(O) Pointer to input vector weights [D]</param>
        /// <param name="pNLSF_Q15">(I) Pointer to input vector [D]</param>
        /// <param name="D">(I) Input vector dimension (even)</param>
        internal static void silk_NLSF_VQ_weights_laroia(short[] pNLSFW_Q_OUT, short[] pNLSF_Q15, int D)
        {
            int k;
            int tmp1_int, tmp2_int;

            Inlines.OpusAssert(pNLSFW_Q_OUT != null);
            Inlines.OpusAssert(D > 0);
            Inlines.OpusAssert((D & 1) == 0);

            // First value
            tmp1_int = Inlines.silk_max_int(pNLSF_Q15[0], 1);
            tmp1_int = Inlines.silk_DIV32((int)1 << (15 + SilkConstants.NLSF_W_Q), tmp1_int);
            tmp2_int = Inlines.silk_max_int(pNLSF_Q15[1] - pNLSF_Q15[0], 1);
            tmp2_int = Inlines.silk_DIV32((int)1 << (15 + SilkConstants.NLSF_W_Q), tmp2_int);
            pNLSFW_Q_OUT[0] = (short)Inlines.silk_min_int(tmp1_int + tmp2_int, short.MaxValue);

            Inlines.OpusAssert(pNLSFW_Q_OUT[0] > 0);

            // Main loop
            for (k = 1; k < D - 1; k += 2)
            {
                tmp1_int = Inlines.silk_max_int(pNLSF_Q15[k + 1] - pNLSF_Q15[k], 1);
                tmp1_int = Inlines.silk_DIV32((int)1 << (15 + SilkConstants.NLSF_W_Q), tmp1_int);
                pNLSFW_Q_OUT[k] = (short)Inlines.silk_min_int(tmp1_int + tmp2_int, short.MaxValue);
                Inlines.OpusAssert(pNLSFW_Q_OUT[k] > 0);

                tmp2_int = Inlines.silk_max_int(pNLSF_Q15[k + 2] - pNLSF_Q15[k + 1], 1);
                tmp2_int = Inlines.silk_DIV32((int)1 << (15 + SilkConstants.NLSF_W_Q), tmp2_int);
                pNLSFW_Q_OUT[k + 1] = (short)Inlines.silk_min_int(tmp1_int + tmp2_int, short.MaxValue);
                Inlines.OpusAssert(pNLSFW_Q_OUT[k + 1] > 0);
            }

            // Last value
            tmp1_int = Inlines.silk_max_int((1 << 15) - pNLSF_Q15[D - 1], 1);
            tmp1_int = Inlines.silk_DIV32((int)1 << (15 + SilkConstants.NLSF_W_Q), tmp1_int);
            pNLSFW_Q_OUT[D - 1] = (short)Inlines.silk_min_int(tmp1_int + tmp2_int, short.MaxValue);

            Inlines.OpusAssert(pNLSFW_Q_OUT[D - 1] > 0);
        }

        /// <summary>
        /// Returns RD value in Q30
        /// </summary>
        /// <param name="x_Q10">(O) Output [ order ]</param>
        /// <param name="indices">(I) Quantization indices [ order ]</param>
        /// <param name="pred_coef_Q8">(I) Backward predictor coefs [ order ]</param>
        /// <param name="quant_step_size_Q16">(I) Quantization step size</param>
        /// <param name="order">(I) Number of input values</param>
        internal static void silk_NLSF_residual_dequant(
                short[] x_Q10,
                sbyte[] indices,
                int indices_ptr,
                byte[] pred_coef_Q8,
                int quant_step_size_Q16,
                short order)
        {
            int i, pred_Q10;
            short out_Q10;

            out_Q10 = 0;
            for (i = order - 1; i >= 0; i--)
            {
                pred_Q10 = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(out_Q10, (short)pred_coef_Q8[i]), 8);
                out_Q10 = Inlines.silk_LSHIFT16((short)indices[indices_ptr + i], 10);
                if (out_Q10 > 0)
                {
                    out_Q10 = Inlines.silk_SUB16(out_Q10, (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                }
                else if (out_Q10 < 0)
                {
                    out_Q10 = Inlines.silk_ADD16(out_Q10, (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                }
                out_Q10 = (short)(Inlines.silk_SMLAWB(pred_Q10, (int)out_Q10, quant_step_size_Q16));
                x_Q10[i] = out_Q10;
            }
        }

        /// <summary>
        /// Unpack predictor values and indices for entropy coding tables
        /// </summary>
        /// <param name="ec_ix">(O) Indices to entropy tables [ LPC_ORDER ]</param>
        /// <param name="pred_Q8">(O) LSF predictor [ LPC_ORDER ]</param>
        /// <param name="psNLSF_CB">(I) Codebook object</param>
        /// <param name="CB1_index">(I) Index of vector in first LSF codebook</param>
        internal static void silk_NLSF_unpack(short[] ec_ix, byte[] pred_Q8, NLSFCodebook psNLSF_CB, int CB1_index)
        {
            int i;
            byte entry;
            byte[] ec_sel = psNLSF_CB.ec_sel;
            int ec_sel_ptr = CB1_index * psNLSF_CB.order / 2;

            for (i = 0; i < psNLSF_CB.order; i += 2)
            {
                entry = ec_sel[ec_sel_ptr];
                ec_sel_ptr++;
                ec_ix[i] = (short)(Inlines.silk_SMULBB(Inlines.silk_RSHIFT(entry, 1) & 7, 2 * SilkConstants.NLSF_QUANT_MAX_AMPLITUDE + 1));
                pred_Q8[i] = psNLSF_CB.pred_Q8[i + (entry & 1) * (psNLSF_CB.order - 1)];
                ec_ix[i + 1] = (short)(Inlines.silk_SMULBB(Inlines.silk_RSHIFT(entry, 5) & 7, 2 * SilkConstants.NLSF_QUANT_MAX_AMPLITUDE + 1));
                pred_Q8[i + 1] = psNLSF_CB.pred_Q8[i + (Inlines.silk_RSHIFT(entry, 4) & 1) * (psNLSF_CB.order - 1) + 1];
            }
        }

        /// <summary>
        /// NLSF stabilizer, for a single input data vector
        /// </summary>
        /// <param name="NLSF_Q15">(I/O) Unstable/stabilized normalized LSF vector in Q15 [L]</param>
        /// <param name="NDeltaMin_Q15">(I) Min distance vector, NDeltaMin_Q15[L] must be >= 1 [L+1]</param>
        /// <param name="L">(I) Number of NLSF parameters in the input vector</param>
        internal static void silk_NLSF_stabilize(short[] NLSF_Q15, short[] NDeltaMin_Q15, int L)
        {
            int i, I = 0, k, loops;
            short center_freq_Q15;
            int diff_Q15, min_diff_Q15, min_center_Q15, max_center_Q15;

            // This is necessary to ensure an output within range of a short
            Inlines.OpusAssert(NDeltaMin_Q15[L] >= 1);

            for (loops = 0; loops < MAX_STABILIZE_LOOPS; loops++)
            {
                /**************************/
                /* Find smallest distance */
                /**************************/
                // First element
                min_diff_Q15 = NLSF_Q15[0] - NDeltaMin_Q15[0];
                I = 0;

                // Middle elements
                for (i = 1; i <= L - 1; i++)
                {
                    diff_Q15 = NLSF_Q15[i] - (NLSF_Q15[i - 1] + NDeltaMin_Q15[i]);
                    if (diff_Q15 < min_diff_Q15)
                    {
                        min_diff_Q15 = diff_Q15;
                        I = i;
                    }
                }

                // Last element
                diff_Q15 = (1 << 15) - (NLSF_Q15[L - 1] + NDeltaMin_Q15[L]);
                if (diff_Q15 < min_diff_Q15)
                {
                    min_diff_Q15 = diff_Q15;
                    I = L;
                }

                /***************************************************/
                /* Now check if the smallest distance non-negative */
                /***************************************************/
                if (min_diff_Q15 >= 0)
                {
                    return;
                }

                if (I == 0)
                {
                    // Move away from lower limit
                    NLSF_Q15[0] = NDeltaMin_Q15[0];
                }
                else if (I == L)
                {
                    // Move away from higher limit
                    NLSF_Q15[L - 1] = (short)((1 << 15) - NDeltaMin_Q15[L]);
                }
                else
                {
                    // Find the lower extreme for the location of the current center frequency
                    min_center_Q15 = 0;
                    for (k = 0; k < I; k++)
                    {
                        min_center_Q15 += NDeltaMin_Q15[k];
                    }

                    min_center_Q15 += Inlines.silk_RSHIFT(NDeltaMin_Q15[I], 1);

                    // Find the upper extreme for the location of the current center frequency
                    max_center_Q15 = 1 << 15;
                    for (k = L; k > I; k--)
                    {
                        max_center_Q15 -= NDeltaMin_Q15[k];
                    }

                    max_center_Q15 -= Inlines.silk_RSHIFT(NDeltaMin_Q15[I], 1);

                    // Move apart, sorted by value, keeping the same center frequency
                    center_freq_Q15 = (short)(Inlines.silk_LIMIT_32(Inlines.silk_RSHIFT_ROUND((int)NLSF_Q15[I - 1] + (int)NLSF_Q15[I], 1),
                                    min_center_Q15, max_center_Q15));
                    NLSF_Q15[I - 1] = (short)(center_freq_Q15 - Inlines.silk_RSHIFT(NDeltaMin_Q15[I], 1));
                    NLSF_Q15[I] = (short)(NLSF_Q15[I - 1] + NDeltaMin_Q15[I]);
                }
            }

            // Safe and simple fall back method, which is less ideal than the above
            if (loops == MAX_STABILIZE_LOOPS)
            {
                Sort.silk_insertion_sort_increasing_all_values_int16(NLSF_Q15, L);

                // First NLSF should be no less than NDeltaMin[0]
                NLSF_Q15[0] = (short)(Inlines.silk_max_int(NLSF_Q15[0], NDeltaMin_Q15[0]));

                // Keep delta_min distance between the NLSFs
                for (i = 1; i < L; i++)
                {
                    NLSF_Q15[i] = (short)(Inlines.silk_max_int(NLSF_Q15[i], NLSF_Q15[i - 1] + NDeltaMin_Q15[i]));
                }

                // Last NLSF should be no higher than 1 - NDeltaMin[L]
                NLSF_Q15[L - 1] = (short)(Inlines.silk_min_int(NLSF_Q15[L - 1], (1 << 15) - NDeltaMin_Q15[L]));

                // Keep NDeltaMin distance between the NLSFs
                for (i = L - 2; i >= 0; i--)
                {
                    NLSF_Q15[i] = (short)(Inlines.silk_min_int(NLSF_Q15[i], NLSF_Q15[i + 1] - NDeltaMin_Q15[i + 1]));
                }
            }
        }

        /// <summary>
        /// NLSF vector decoder
        /// </summary>
        /// <param name="pNLSF_Q15">(O) Quantized NLSF vector [ LPC_ORDER ]</param>
        /// <param name="NLSFIndices">(I) Codebook path vector [ LPC_ORDER + 1 ]</param>
        /// <param name="psNLSF_CB">(I) Codebook object</param>
        internal static void silk_NLSF_decode(short[] pNLSF_Q15, sbyte[] NLSFIndices, NLSFCodebook psNLSF_CB)
        {
            int i;
            byte[] pred_Q8 = new byte[psNLSF_CB.order];
            short[] ec_ix = new short[psNLSF_CB.order];
            short[] res_Q10 = new short[psNLSF_CB.order];
            short[] W_tmp_QW = new short[psNLSF_CB.order];
            int W_tmp_Q9, NLSF_Q15_tmp;

            // Decode first stage 
            byte[] pCB = psNLSF_CB.CB1_NLSF_Q8;
            int pCB_element = NLSFIndices[0] * psNLSF_CB.order;

            for (i = 0; i < psNLSF_CB.order; i++)
            {
                pNLSF_Q15[i] = Inlines.silk_LSHIFT16((short)pCB[pCB_element + i], 7);
            }

            // Unpack entropy table indices and predictor for current CB1 index
            silk_NLSF_unpack(ec_ix, pred_Q8, psNLSF_CB, NLSFIndices[0]);

            // Predictive residual dequantizer
            silk_NLSF_residual_dequant(res_Q10,
                NLSFIndices,
                1,
                pred_Q8,
                psNLSF_CB.quantStepSize_Q16,
                psNLSF_CB.order);

            // Weights from codebook vector
            silk_NLSF_VQ_weights_laroia(W_tmp_QW, pNLSF_Q15, psNLSF_CB.order);

            // Apply inverse square-rooted weights and add to output
            for (i = 0; i < psNLSF_CB.order; i++)
            {
                W_tmp_Q9 = Inlines.silk_SQRT_APPROX(Inlines.silk_LSHIFT((int)W_tmp_QW[i], 18 - SilkConstants.NLSF_W_Q));
                NLSF_Q15_tmp = Inlines.silk_ADD32(pNLSF_Q15[i], Inlines.silk_DIV32_16(Inlines.silk_LSHIFT((int)res_Q10[i], 14), (short)(W_tmp_Q9)));
                pNLSF_Q15[i] = (short)(Inlines.silk_LIMIT(NLSF_Q15_tmp, 0, 32767));
            }

            // NLSF stabilization
            silk_NLSF_stabilize(pNLSF_Q15, psNLSF_CB.deltaMin_Q15, psNLSF_CB.order);
        }

        /// <summary>
        /// Delayed-decision quantizer for NLSF residuals
        /// </summary>
        /// <param name="indices">(O) Quantization indices [ order ]</param>
        /// <param name="x_Q10">(O) Input [ order ]</param>
        /// <param name="w_Q5">(I) Weights [ order ] </param>
        /// <param name="pred_coef_Q8">(I) Backward predictor coefs [ order ]</param>
        /// <param name="ec_ix">(I) Indices to entropy coding tables [ order ]</param>
        /// <param name="ec_rates_Q5">(I) Rates []</param>
        /// <param name="quant_step_size_Q16">(I) Quantization step size</param>
        /// <param name="inv_quant_step_size_Q6">(I) Inverse quantization step size</param>
        /// <param name="mu_Q20">(I) R/D tradeoff</param>
        /// <param name="order">(I) Number of input values</param>
        /// <returns>RD value in Q25</returns>
        /// Fixme: Optimize this method!
        internal static int silk_NLSF_del_dec_quant(
            sbyte[] indices,
            short[] x_Q10,
            short[] w_Q5,
            byte[] pred_coef_Q8,
            short[] ec_ix,
            byte[] ec_rates_Q5,
            int quant_step_size_Q16,
            short inv_quant_step_size_Q6,
            int mu_Q20,
            short order)
        {
            int i, j, nStates, ind_tmp, ind_min_max, ind_max_min, in_Q10, res_Q10;
            int pred_Q10, diff_Q10, out0_Q10, out1_Q10, rate0_Q5, rate1_Q5;
            int RD_tmp_Q25, min_Q25, min_max_Q25, max_min_Q25, pred_coef_Q16;
            int[] ind_sort = new int[SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
            sbyte[][] ind = new sbyte[SilkConstants.NLSF_QUANT_DEL_DEC_STATES][];
            for (i = 0; i < SilkConstants.NLSF_QUANT_DEL_DEC_STATES; i++)
            {
                ind[i] = new sbyte[SilkConstants.MAX_LPC_ORDER];
            }

            short[] prev_out_Q10 = new short[2 * SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
            int[] RD_Q25 = new int[2 * SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
            int[] RD_min_Q25 = new int[SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
            int[] RD_max_Q25 = new int[SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
            int rates_Q5;

            int[] out0_Q10_table = new int[2 * SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT];
            int[] out1_Q10_table = new int[2 * SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT];

            for (i = 0 - SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT; i <= SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT - 1; i++)
            {
                out0_Q10 = Inlines.silk_LSHIFT(i, 10);
                out1_Q10 = Inlines.silk_ADD16((short)(out0_Q10), 1024);

                if (i > 0)
                {
                    out0_Q10 = Inlines.silk_SUB16((short)(out0_Q10), (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                    out1_Q10 = Inlines.silk_SUB16((short)(out1_Q10), (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                }
                else if (i == 0)
                {
                    out1_Q10 = Inlines.silk_SUB16((short)(out1_Q10), (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                }
                else if (i == -1)
                {
                    out0_Q10 = Inlines.silk_ADD16((short)(out0_Q10), (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                }
                else
                {
                    out0_Q10 = Inlines.silk_ADD16((short)(out0_Q10), (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                    out1_Q10 = Inlines.silk_ADD16((short)(out1_Q10), (short)(((int)((SilkConstants.NLSF_QUANT_LEVEL_ADJ) * ((long)1 << (10)) + 0.5))/*Inlines.SILK_CONST(SilkConstants.NLSF_QUANT_LEVEL_ADJ, 10)*/));
                }

                out0_Q10_table[i + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT] = Inlines.silk_SMULWB((int)out0_Q10, quant_step_size_Q16);
                out1_Q10_table[i + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT] = Inlines.silk_SMULWB((int)out1_Q10, quant_step_size_Q16);
            }

            Inlines.OpusAssert((SilkConstants.NLSF_QUANT_DEL_DEC_STATES & (SilkConstants.NLSF_QUANT_DEL_DEC_STATES - 1)) == 0); // must be power of two

            nStates = 1;
            RD_Q25[0] = 0;
            prev_out_Q10[0] = 0;

            for (i = order - 1; ; i--)
            {
                pred_coef_Q16 = Inlines.silk_LSHIFT((int)pred_coef_Q8[i], 8);
                in_Q10 = x_Q10[i];

                for (j = 0; j < nStates; j++)
                {
                    pred_Q10 = Inlines.silk_SMULWB(pred_coef_Q16, prev_out_Q10[j]);
                    res_Q10 = Inlines.silk_SUB16((short)(in_Q10), (short)(pred_Q10));
                    ind_tmp = Inlines.silk_SMULWB((int)inv_quant_step_size_Q6, res_Q10);
                    ind_tmp = Inlines.silk_LIMIT(ind_tmp, 0 - SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT, SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT - 1);
                    ind[j][i] = (sbyte)ind_tmp;
                    rates_Q5 = ec_ix[i] + ind_tmp;

                    // compute outputs for ind_tmp and ind_tmp + 1
                    out0_Q10 = out0_Q10_table[ind_tmp + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT];
                    out1_Q10 = out1_Q10_table[ind_tmp + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT];

                    out0_Q10 = Inlines.silk_ADD16((short)(out0_Q10), (short)(pred_Q10));
                    out1_Q10 = Inlines.silk_ADD16((short)(out1_Q10), (short)(pred_Q10));
                    prev_out_Q10[j] = (short)(out0_Q10);
                    prev_out_Q10[j + nStates] = (short)(out1_Q10);

                    // compute RD for ind_tmp and ind_tmp + 1
                    if (ind_tmp + 1 >= SilkConstants.NLSF_QUANT_MAX_AMPLITUDE)
                    {
                        if (ind_tmp + 1 == SilkConstants.NLSF_QUANT_MAX_AMPLITUDE)
                        {
                            rate0_Q5 = ec_rates_Q5[rates_Q5 + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE];
                            rate1_Q5 = 280;
                        }
                        else
                        {
                            rate0_Q5 = Inlines.silk_SMLABB(280 - (43 * SilkConstants.NLSF_QUANT_MAX_AMPLITUDE), 43, ind_tmp);
                            rate1_Q5 = Inlines.silk_ADD16((short)(rate0_Q5), 43);
                        }
                    }
                    else if (ind_tmp <= 0 - SilkConstants.NLSF_QUANT_MAX_AMPLITUDE)
                    {
                        if (ind_tmp == 0 - SilkConstants.NLSF_QUANT_MAX_AMPLITUDE)
                        {
                            rate0_Q5 = 280;
                            rate1_Q5 = ec_rates_Q5[rates_Q5 + 1 + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE];
                        }
                        else
                        {
                            rate0_Q5 = Inlines.silk_SMLABB(280 - 43 * SilkConstants.NLSF_QUANT_MAX_AMPLITUDE, -43, ind_tmp);
                            rate1_Q5 = Inlines.silk_SUB16((short)(rate0_Q5), 43);
                        }
                    }
                    else
                    {
                        rate0_Q5 = ec_rates_Q5[rates_Q5 + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE];
                        rate1_Q5 = ec_rates_Q5[rates_Q5 + 1 + SilkConstants.NLSF_QUANT_MAX_AMPLITUDE];
                    }

                    RD_tmp_Q25 = RD_Q25[j];
                    diff_Q10 = Inlines.silk_SUB16((short)(in_Q10), (short)(out0_Q10));
                    RD_Q25[j] = Inlines.silk_SMLABB(Inlines.silk_MLA(RD_tmp_Q25, Inlines.silk_SMULBB(diff_Q10, diff_Q10), w_Q5[i]), mu_Q20, rate0_Q5);
                    diff_Q10 = Inlines.silk_SUB16((short)(in_Q10), (short)(out1_Q10));
                    RD_Q25[j + nStates] = Inlines.silk_SMLABB(Inlines.silk_MLA(RD_tmp_Q25, Inlines.silk_SMULBB(diff_Q10, diff_Q10), w_Q5[i]), mu_Q20, rate1_Q5);
                }

                if (nStates <= (SilkConstants.NLSF_QUANT_DEL_DEC_STATES >> 1))
                {
                    // double number of states and copy
                    for (j = 0; j < nStates; j++)
                    {
                        ind[j + nStates][i] = (sbyte)(ind[j][i] + 1);
                    }
                    nStates = Inlines.silk_LSHIFT(nStates, 1);

                    for (j = nStates; j < SilkConstants.NLSF_QUANT_DEL_DEC_STATES; j++)
                    {
                        ind[j][i] = ind[j - nStates][i];
                    }
                }
                else if (i > 0)
                {
                    // sort lower and upper half of RD_Q25, pairwise
                    for (j = 0; j < SilkConstants.NLSF_QUANT_DEL_DEC_STATES; j++)
                    {
                        if (RD_Q25[j] > RD_Q25[j + SilkConstants.NLSF_QUANT_DEL_DEC_STATES])
                        {
                            RD_max_Q25[j] = RD_Q25[j];
                            RD_min_Q25[j] = RD_Q25[j + SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
                            RD_Q25[j] = RD_min_Q25[j];
                            RD_Q25[j + SilkConstants.NLSF_QUANT_DEL_DEC_STATES] = RD_max_Q25[j];

                            // swap prev_out values
                            out0_Q10 = prev_out_Q10[j];
                            prev_out_Q10[j] = prev_out_Q10[j + SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
                            prev_out_Q10[j + SilkConstants.NLSF_QUANT_DEL_DEC_STATES] = (short)(out0_Q10);
                            ind_sort[j] = j + SilkConstants.NLSF_QUANT_DEL_DEC_STATES;
                        }
                        else
                        {
                            RD_min_Q25[j] = RD_Q25[j];
                            RD_max_Q25[j] = RD_Q25[j + SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
                            ind_sort[j] = j;
                        }
                    }

                    // compare the highest RD values of the winning half with the lowest one in the losing half, and copy if necessary
                    // afterwards ind_sort[] will contain the indices of the NLSF_QUANT_DEL_DEC_STATES winning RD values
                    while (true)
                    {
                        min_max_Q25 = int.MaxValue;
                        max_min_Q25 = 0;
                        ind_min_max = 0;
                        ind_max_min = 0;

                        for (j = 0; j < SilkConstants.NLSF_QUANT_DEL_DEC_STATES; j++)
                        {
                            if (min_max_Q25 > RD_max_Q25[j])
                            {
                                min_max_Q25 = RD_max_Q25[j];
                                ind_min_max = j;
                            }
                            if (max_min_Q25 < RD_min_Q25[j])
                            {
                                max_min_Q25 = RD_min_Q25[j];
                                ind_max_min = j;
                            }
                        }

                        if (min_max_Q25 >= max_min_Q25)
                        {
                            break;
                        }

                        // copy ind_min_max to ind_max_min
                        ind_sort[ind_max_min] = ind_sort[ind_min_max] ^ SilkConstants.NLSF_QUANT_DEL_DEC_STATES;
                        RD_Q25[ind_max_min] = RD_Q25[ind_min_max + SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
                        prev_out_Q10[ind_max_min] = prev_out_Q10[ind_min_max + SilkConstants.NLSF_QUANT_DEL_DEC_STATES];
                        RD_min_Q25[ind_max_min] = 0;
                        RD_max_Q25[ind_min_max] = int.MaxValue;
                        Buffer.BlockCopy(ind[ind_min_max], 0, ind[ind_max_min], 0, order * sizeof(sbyte));
                    }

                    // increment index if it comes from the upper half
                    for (j = 0; j < SilkConstants.NLSF_QUANT_DEL_DEC_STATES; j++)
                    {
                        var x = (sbyte)Inlines.silk_RSHIFT(ind_sort[j], SilkConstants.NLSF_QUANT_DEL_DEC_STATES_LOG2);
                        ind[j][i] += x;
                    }
                }
                else
                {
                    // i == 0
                    break;
                }
            }

            // last sample: find winner, copy indices and return RD value
            ind_tmp = 0;
            min_Q25 = int.MaxValue;
            for (j = 0; j < 2 * SilkConstants.NLSF_QUANT_DEL_DEC_STATES; j++)
            {
                if (min_Q25 > RD_Q25[j])
                {
                    min_Q25 = RD_Q25[j];
                    ind_tmp = j;
                }
            }

            for (j = 0; j < order; j++)
            {
                indices[j] = ind[ind_tmp & (SilkConstants.NLSF_QUANT_DEL_DEC_STATES - 1)][j];
                Inlines.OpusAssert(indices[j] >= 0 - SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT);
                Inlines.OpusAssert(indices[j] <= SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT);
            }

            indices[0] = (sbyte)(indices[0] + Inlines.silk_RSHIFT(ind_tmp, SilkConstants.NLSF_QUANT_DEL_DEC_STATES_LOG2));
            Inlines.OpusAssert(indices[0] <= SilkConstants.NLSF_QUANT_MAX_AMPLITUDE_EXT);
            Inlines.OpusAssert(min_Q25 >= 0);
            return min_Q25;
        }

        /// <summary>
        /// NLSF vector encoder
        /// </summary>
        /// <param name="NLSFIndices">(I) Codebook path vector [ LPC_ORDER + 1 ]</param>
        /// <param name="pNLSF_Q15">(I/O) Quantized NLSF vector [ LPC_ORDER ]</param>
        /// <param name="psNLSF_CB">(I) Codebook object</param>
        /// <param name="pW_QW">(I) NLSF weight vector [ LPC_ORDER ]</param>
        /// <param name="NLSF_mu_Q20">(I) Rate weight for the RD optimization</param>
        /// <param name="nSurvivors">(I) Max survivors after first stage</param>
        /// <param name="signalType">(I) Signal type: 0/1/2</param>
        /// <returns>RD value in Q25</returns>
        internal static int silk_NLSF_encode(
            sbyte[] NLSFIndices,
            short[] pNLSF_Q15,
            NLSFCodebook psNLSF_CB,
            short[] pW_QW,
            int NLSF_mu_Q20,
            int nSurvivors,
            int signalType)
        {
            int i, s, ind1, prob_Q8, bits_q7;
            int W_tmp_Q9;
            int[] err_Q26;
            int[] RD_Q25;
            int[] tempIndices1;
            sbyte[][] tempIndices2;
            short[] res_Q15 = new short[psNLSF_CB.order];
            short[] res_Q10 = new short[psNLSF_CB.order];
            short[] NLSF_tmp_Q15 = new short[psNLSF_CB.order];
            short[] W_tmp_QW = new short[psNLSF_CB.order];
            short[] W_adj_Q5 = new short[psNLSF_CB.order];
            byte[] pred_Q8 = new byte[psNLSF_CB.order];
            short[] ec_ix = new short[psNLSF_CB.order];
            byte[] pCB = psNLSF_CB.CB1_NLSF_Q8;
            int iCDF_ptr;
            int pCB_element;
            
            Inlines.OpusAssert(nSurvivors <= SilkConstants.NLSF_VQ_MAX_SURVIVORS);
            Inlines.OpusAssert(signalType >= 0 && signalType <= 2);
            Inlines.OpusAssert(NLSF_mu_Q20 <= 32767 && NLSF_mu_Q20 >= 0);

            // NLSF stabilization
            silk_NLSF_stabilize(pNLSF_Q15, psNLSF_CB.deltaMin_Q15, psNLSF_CB.order);

            // First stage: VQ
            err_Q26 = new int[psNLSF_CB.nVectors];
            silk_NLSF_VQ(err_Q26, pNLSF_Q15, psNLSF_CB.CB1_NLSF_Q8, psNLSF_CB.nVectors, psNLSF_CB.order);

            // Sort the quantization errors
            tempIndices1 = new int[nSurvivors];
            Sort.silk_insertion_sort_increasing(err_Q26, tempIndices1, psNLSF_CB.nVectors, nSurvivors);

            RD_Q25 = new int[nSurvivors];
            tempIndices2 = Arrays.InitTwoDimensionalArray<sbyte>(nSurvivors, SilkConstants.MAX_LPC_ORDER); 
            

            // Loop over survivors
            for (s = 0; s < nSurvivors; s++)
            {
                ind1 = tempIndices1[s];

                // Residual after first stage
                
                pCB_element = ind1 * psNLSF_CB.order; // opt: potential 1:2 partitioned buffer
                for (i = 0; i < psNLSF_CB.order; i++)
                {
                    NLSF_tmp_Q15[i] = Inlines.silk_LSHIFT16((short)pCB[pCB_element + i], 7);
                    res_Q15[i] = (short)(pNLSF_Q15[i] - NLSF_tmp_Q15[i]);
                }

                // Weights from codebook vector
                silk_NLSF_VQ_weights_laroia(W_tmp_QW, NLSF_tmp_Q15, psNLSF_CB.order);

                // Apply square-rooted weights
                for (i = 0; i < psNLSF_CB.order; i++)
                {
                    W_tmp_Q9 = Inlines.silk_SQRT_APPROX(Inlines.silk_LSHIFT((int)W_tmp_QW[i], 18 - SilkConstants.NLSF_W_Q));
                    res_Q10[i] = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(res_Q15[i], W_tmp_Q9), 14);
                }

                // Modify input weights accordingly
                for (i = 0; i < psNLSF_CB.order; i++)
                {
                    W_adj_Q5[i] = (short)(Inlines.silk_DIV32_16(Inlines.silk_LSHIFT((int)pW_QW[i], 5), W_tmp_QW[i]));
                }

                // Unpack entropy table indices and predictor for current CB1 index
                silk_NLSF_unpack(ec_ix, pred_Q8, psNLSF_CB, ind1);

                // Trellis quantizer
                RD_Q25[s] = silk_NLSF_del_dec_quant(
                    tempIndices2[s],
                    res_Q10,
                    W_adj_Q5,
                    pred_Q8,
                    ec_ix,
                    psNLSF_CB.ec_Rates_Q5,
                    psNLSF_CB.quantStepSize_Q16,
                    psNLSF_CB.invQuantStepSize_Q6,
                    NLSF_mu_Q20,
                    psNLSF_CB.order);

                // Add rate for first stage
                iCDF_ptr = (signalType >> 1) * psNLSF_CB.nVectors;

                if (ind1 == 0)
                {
                    prob_Q8 = 256 - psNLSF_CB.CB1_iCDF[iCDF_ptr + ind1];
                }
                else
                {
                    prob_Q8 = psNLSF_CB.CB1_iCDF[iCDF_ptr + ind1 - 1] - psNLSF_CB.CB1_iCDF[iCDF_ptr + ind1];
                }

                bits_q7 = (8 << 7) - Inlines.silk_lin2log(prob_Q8);
                RD_Q25[s] = Inlines.silk_SMLABB(RD_Q25[s], bits_q7, Inlines.silk_RSHIFT(NLSF_mu_Q20, 2));
            }

            // Find the lowest rate-distortion error
            int[] bestIndex = new int[1];
            Sort.silk_insertion_sort_increasing(RD_Q25, bestIndex, nSurvivors, 1);

            NLSFIndices[0] = (sbyte)tempIndices1[bestIndex[0]];
            Array.Copy(tempIndices2[bestIndex[0]], 0, NLSFIndices, 1, psNLSF_CB.order);

            // Decode
            silk_NLSF_decode(pNLSF_Q15, NLSFIndices, psNLSF_CB);

            return RD_Q25[0];
        }

        /// <summary>
        /// helper function for NLSF2A(..)
        /// </summary>
        /// <param name="o">(O) intermediate polynomial, QA [dd+1]</param>
        /// <param name="cLSF">(I) vector of interleaved 2*cos(LSFs), QA [d]</param>
        /// <param name="dd">(I) polynomial order (= 1/2 * filter order)</param>
        internal static void silk_NLSF2A_find_poly(
            int[] o,
            int[] cLSF,
            int cLSF_ptr,
            int dd)
        {
            int k, n, ftmp;

            o[0] = Inlines.silk_LSHIFT(1, QA);
            o[1] = 0 - cLSF[cLSF_ptr];
            for (k = 1; k < dd; k++)
            {
                ftmp = cLSF[cLSF_ptr + (2 * k)];            /* QA*/
                o[k + 1] = Inlines.silk_LSHIFT(o[k - 1], 1) - (int)Inlines.silk_RSHIFT_ROUND64(Inlines.silk_SMULL(ftmp, o[k]), QA);
                for (n = k; n > 1; n--)
                {
                    o[n] += o[n - 2] - (int)Inlines.silk_RSHIFT_ROUND64(Inlines.silk_SMULL(ftmp, o[n - 1]), QA);
                }
                o[1] -= ftmp;
            }
        }

        /* This ordering was found to maximize quality. It improves numerical accuracy of
               silk_NLSF2A_find_poly() compared to "standard" ordering. */
        private static readonly byte[] ordering16 = { 0, 15, 8, 7, 4, 11, 12, 3, 2, 13, 10, 5, 6, 9, 14, 1 };
        private static readonly byte[] ordering10 = { 0, 9, 6, 3, 4, 5, 8, 1, 2, 7 };


        /// <summary>
        /// compute whitening filter coefficients from normalized line spectral frequencies
        /// </summary>
        /// <param name="a_Q12">(O) monic whitening filter coefficients in Q12,  [ d ]</param>
        /// <param name="NLSF">(I) normalized line spectral frequencies in Q15, [ d ]</param>
        /// <param name="d">(I) filter order (should be even)</param>
        internal static void silk_NLSF2A(
            short[] a_Q12,
            short[] NLSF,
            int d)
        {
            
            byte[] ordering;
            int k, i, dd;
            int[] cos_LSF_QA = new int[d];
            int[] P = new int[d / 2 + 1];
            int[] Q = new int[d / 2 + 1];
            int[] a32_QA1 = new int[d];

            int Ptmp, Qtmp, f_int, f_frac, cos_val, delta;
            int maxabs, absval, idx = 0, sc_Q16;

            Inlines.OpusAssert (SilkConstants.LSF_COS_TAB_SZ == 128);
            Inlines.OpusAssert (d == 10 || d == 16);

            /* convert LSFs to 2*cos(LSF), using piecewise linear curve from table */
            ordering = d == 16 ? ordering16 : ordering10;

            for (k = 0; k < d; k++)
            {
                Inlines.OpusAssert(NLSF[k] >= 0);

                /* f_int on a scale 0-127 (rounded down) */
                f_int = Inlines.silk_RSHIFT(NLSF[k], 15 - 7);

                /* f_frac, range: 0..255 */
                f_frac = NLSF[k] - Inlines.silk_LSHIFT(f_int, 15 - 7);

                Inlines.OpusAssert(f_int >= 0);
                Inlines.OpusAssert(f_int < SilkConstants.LSF_COS_TAB_SZ);

                /* Read start and end value from table */
                cos_val = Tables.silk_LSFCosTab_Q12[f_int];                /* Q12 */
                delta = Tables.silk_LSFCosTab_Q12[f_int + 1] - cos_val;  /* Q12, with a range of 0..200 */

                /* Linear interpolation */
                cos_LSF_QA[ordering[k]] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_LSHIFT(cos_val, 8) + Inlines.silk_MUL(delta, f_frac), 20 - QA); /* QA */
            }

            dd = Inlines.silk_RSHIFT(d, 1);

            /* generate even and odd polynomials using convolution */
            silk_NLSF2A_find_poly(P, cos_LSF_QA, 0, dd);
            silk_NLSF2A_find_poly(Q, cos_LSF_QA, 1, dd);

            /* convert even and odd polynomials to opus_int32 Q12 filter coefs */
            for (k = 0; k < dd; k++)
            {
                Ptmp = P[k + 1] + P[k];
                Qtmp = Q[k + 1] - Q[k];

                /* the Ptmp and Qtmp values at this stage need to fit in int32 */
                a32_QA1[k] = -Qtmp - Ptmp;        /* QA+1 */
                a32_QA1[d - k - 1] = Qtmp - Ptmp;        /* QA+1 */
            }

            /* Limit the maximum absolute value of the prediction coefficients, so that they'll fit in int16 */
            for (i = 0; i < 10; i++)
            {
                /* Find maximum absolute value and its index */
                maxabs = 0;
                for (k = 0; k < d; k++)
                {
                    absval = Inlines.silk_abs(a32_QA1[k]);
                    if (absval > maxabs)
                    {
                        maxabs = absval;
                        idx = k;
                    }
                }

                maxabs = Inlines.silk_RSHIFT_ROUND(maxabs, QA + 1 - 12);                                          /* QA+1 . Q12 */

                if (maxabs > short.MaxValue)
                {
                    /* Reduce magnitude of prediction coefficients */
                    maxabs = Inlines.silk_min(maxabs, 163838);  /* ( silk_int32_MAX >> 14 ) + silk_int16_MAX = 163838 */
                    sc_Q16 = ((int)((0.999f) * ((long)1 << (16)) + 0.5))/*Inlines.SILK_CONST(0.999f, 16)*/ - Inlines.silk_DIV32(Inlines.silk_LSHIFT(maxabs - short.MaxValue, 14),
                                                Inlines.silk_RSHIFT32(Inlines.silk_MUL(maxabs, idx + 1), 2));
                    Filters.silk_bwexpander_32(a32_QA1, d, sc_Q16);
                }
                else
                {
                    break;
                }
            }

            if (i == 10)
            {
                /* Reached the last iteration, clip the coefficients */
                for (k = 0; k < d; k++)
                {
                    a_Q12[k] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(a32_QA1[k], QA + 1 - 12));  /* QA+1 . Q12 */
                    a32_QA1[k] = Inlines.silk_LSHIFT((int)a_Q12[k], QA + 1 - 12);
                }
            }
            else
            {
                for (k = 0; k < d; k++)
                {
                    a_Q12[k] = (short)Inlines.silk_RSHIFT_ROUND(a32_QA1[k], QA + 1 - 12);                /* QA+1 . Q12 */
                }
            }

            for (i = 0; i < SilkConstants.MAX_LPC_STABILIZE_ITERATIONS; i++)
            {
                if (Filters.silk_LPC_inverse_pred_gain(a_Q12, d) < ((int)((1.0f / SilkConstants.MAX_PREDICTION_POWER_GAIN) * ((long)1 << (30)) + 0.5))/*Inlines.SILK_CONST(1.0f / SilkConstants.MAX_PREDICTION_POWER_GAIN, 30)*/)
                {
                    /* Prediction coefficients are (too close to) unstable; apply bandwidth expansion   */
                    /* on the unscaled coefficients, convert to Q12 and measure again                   */
                    Filters.silk_bwexpander_32(a32_QA1, d, 65536 - Inlines.silk_LSHIFT(2, i));

                    for (k = 0; k < d; k++)
                    {
                        a_Q12[k] = (short)Inlines.silk_RSHIFT_ROUND(a32_QA1[k], QA + 1 - 12);            /* QA+1 . Q12 */
                    }
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Helper function for A2NLSF(..) Transforms polynomials from cos(n*f) to cos(f)^n
        /// </summary>
        /// <param name="p">(I/O) Polynomial</param>
        /// <param name="dd">(I) Polynomial order (= filter order / 2 )</param>
        internal static void silk_A2NLSF_trans_poly(int[] p, int dd)
        {
            int k, n;

            for (k = 2; k <= dd; k++)
            {
                for (n = dd; n > k; n--)
                {
                    p[n - 2] -= p[n];
                }
                p[k - 2] -= Inlines.silk_LSHIFT(p[k], 1);
            }
        }

        /// <summary>
        /// Helper function for A2NLSF(..) Polynomial evaluation
        /// </summary>
        /// <param name="p">(I) Polynomial, Q16</param>
        /// <param name="x">(I) Evaluation point, Q12</param>
        /// <param name="dd">(I) Order</param>
        /// <returns>the polynomial evaluation, in Q16</returns>
        internal static int silk_A2NLSF_eval_poly(int[] p, int x, int dd)
        {
            int n;
            int x_Q16, y32;

            y32 = p[dd];                                  /* Q16 */
            x_Q16 = Inlines.silk_LSHIFT(x, 4);
            
            if (8 == dd)
            {
                y32 = Inlines.silk_SMLAWW(p[7], y32, x_Q16);
                y32 = Inlines.silk_SMLAWW(p[6], y32, x_Q16);
                y32 = Inlines.silk_SMLAWW(p[5], y32, x_Q16);
                y32 = Inlines.silk_SMLAWW(p[4], y32, x_Q16);
                y32 = Inlines.silk_SMLAWW(p[3], y32, x_Q16);
                y32 = Inlines.silk_SMLAWW(p[2], y32, x_Q16);
                y32 = Inlines.silk_SMLAWW(p[1], y32, x_Q16);
                y32 = Inlines.silk_SMLAWW(p[0], y32, x_Q16);
            }
            else
            {
                for (n = dd - 1; n >= 0; n--)
                {
                    y32 = Inlines.silk_SMLAWW(p[n], y32, x_Q16);    /* Q16 */
                }
            }

            return y32;
        }

        internal static void silk_A2NLSF_init(
             int[] a_Q16,
             int[] P,
             int[] Q,
             int dd)
        {
            int k;

            /* Convert filter coefs to even and odd polynomials */
            P[dd] = Inlines.silk_LSHIFT(1, 16);
            Q[dd] = Inlines.silk_LSHIFT(1, 16);
            for (k = 0; k < dd; k++)
            {
                P[k] = -a_Q16[dd - k - 1] - a_Q16[dd + k];    /* Q16 */
                Q[k] = -a_Q16[dd - k - 1] + a_Q16[dd + k];    /* Q16 */
            }

            /* Divide out zeros as we have that for even filter orders, */
            /* z =  1 is always a root in Q, and                        */
            /* z = -1 is always a root in P                             */
            for (k = dd; k > 0; k--)
            {
                P[k - 1] -= P[k];
                Q[k - 1] += Q[k];
            }

            /* Transform polynomials from cos(n*f) to cos(f)^n */
            silk_A2NLSF_trans_poly(P, dd);
            silk_A2NLSF_trans_poly(Q, dd);
        }

        /// <summary>
        /// Compute Normalized Line Spectral Frequencies (NLSFs) from whitening filter coefficients
        /// If not all roots are found, the a_Q16 coefficients are bandwidth expanded until convergence.
        /// </summary>
        /// <param name="NLSF">(O) Normalized Line Spectral Frequencies in Q15 (0..2^15-1) [d]</param>
        /// <param name="a_Q16">(I/O) Monic whitening filter coefficients in Q16 [d]</param>
        /// <param name="d">(I) Filter order (must be even)</param>
        internal static void silk_A2NLSF(short[] NLSF, int[] a_Q16, int d)
        {
            int i, k, m, dd, root_ix, ffrac;
            int xlo, xhi, xmid;
            int ylo, yhi, ymid, thr;
            int nom, den;
            int[] P = new int[SilkConstants.SILK_MAX_ORDER_LPC / 2 + 1];
            int[] Q = new int[SilkConstants.SILK_MAX_ORDER_LPC / 2 + 1];
            int[][] PQ = new int[2][];
            int[] p;

            /* Store pointers to array */
            PQ[0] = P;
            PQ[1] = Q;

            dd = Inlines.silk_RSHIFT(d, 1);

            silk_A2NLSF_init(a_Q16, P, Q, dd);

            /* Find roots, alternating between P and Q */
            p = P;                          /* Pointer to polynomial */

            xlo = Tables.silk_LSFCosTab_Q12[0]; /* Q12*/
            ylo = silk_A2NLSF_eval_poly(p, xlo, dd);

            if (ylo < 0)
            {
                /* Set the first NLSF to zero and move on to the next */
                NLSF[0] = 0;
                p = Q;                      /* Pointer to polynomial */
                ylo = silk_A2NLSF_eval_poly(p, xlo, dd);
                root_ix = 1;                /* Index of current root */
            }
            else {
                root_ix = 0;                /* Index of current root */
            }
            k = 1;                          /* Loop counter */
            i = 0;                          /* Counter for bandwidth expansions applied */
            thr = 0;
            while (true)
            {
                /* Evaluate polynomial */
                xhi = Tables.silk_LSFCosTab_Q12[k]; /* Q12 */
                yhi = silk_A2NLSF_eval_poly(p, xhi, dd);

                /* Detect zero crossing */
                if ((ylo <= 0 && yhi >= thr) || (ylo >= 0 && yhi <= -thr))
                {
                    if (yhi == 0)
                    {
                        /* If the root lies exactly at the end of the current       */
                        /* interval, look for the next root in the next interval    */
                        thr = 1;
                    }
                    else {
                        thr = 0;
                    }
                    /* Binary division */
                    ffrac = -256;
                    for (m = 0; m < BIN_DIV_STEPS_A2NLSF; m++)
                    {
                        /* Evaluate polynomial */
                        xmid = Inlines.silk_RSHIFT_ROUND(xlo + xhi, 1);
                        ymid = silk_A2NLSF_eval_poly(p, xmid, dd);

                        /* Detect zero crossing */
                        if ((ylo <= 0 && ymid >= 0) || (ylo >= 0 && ymid <= 0))
                        {
                            /* Reduce frequency */
                            xhi = xmid;
                            yhi = ymid;
                        }
                        else {
                            /* Increase frequency */
                            xlo = xmid;
                            ylo = ymid;
                            ffrac = Inlines.silk_ADD_RSHIFT(ffrac, 128, m);
                        }
                    }

                    /* Interpolate */
                    if (Inlines.silk_abs(ylo) < 65536)
                    {
                        /* Avoid dividing by zero */
                        den = ylo - yhi;
                        nom = Inlines.silk_LSHIFT(ylo, 8 - BIN_DIV_STEPS_A2NLSF) + Inlines.silk_RSHIFT(den, 1);
                        if (den != 0)
                        {
                            ffrac += Inlines.silk_DIV32(nom, den);
                        }
                    }
                    else
                    {
                        /* No risk of dividing by zero because abs(ylo - yhi) >= abs(ylo) >= 65536 */
                        ffrac += Inlines.silk_DIV32(ylo, Inlines.silk_RSHIFT(ylo - yhi, 8 - BIN_DIV_STEPS_A2NLSF));
                    }
                    NLSF[root_ix] = (short)Inlines.silk_min_32(Inlines.silk_LSHIFT((int)k, 8) + ffrac, short.MaxValue);

                    Inlines.OpusAssert(NLSF[root_ix] >= 0);

                    root_ix++;        /* Next root */
                    if (root_ix >= d)
                    {
                        /* Found all roots */
                        break;
                    }

                    /* Alternate pointer to polynomial */
                    p = PQ[root_ix & 1];

                    /* Evaluate polynomial */
                    xlo = Tables.silk_LSFCosTab_Q12[k - 1]; /* Q12*/
                    ylo = Inlines.silk_LSHIFT(1 - (root_ix & 2), 12);
                }
                else
                {
                    /* Increment loop counter */
                    k++;
                    xlo = xhi;
                    ylo = yhi;
                    thr = 0;

                    if (k > SilkConstants.LSF_COS_TAB_SZ)
                    {
                        i++;
                        if (i > MAX_ITERATIONS_A2NLSF)
                        {
                            /* Set NLSFs to white spectrum and exit */
                            NLSF[0] = (short)Inlines.silk_DIV32_16(1 << 15, (short)(d + 1));
                            for (k = 1; k < d; k++)
                            {
                                NLSF[k] = (short)Inlines.silk_SMULBB(k + 1, NLSF[0]);
                            }
                            return;
                        }

                        /* Error: Apply progressively more bandwidth expansion and run again */
                        Filters.silk_bwexpander_32(a_Q16, d, 65536 - Inlines.silk_SMULBB(10 + i, i)); /* 10_Q16 = 0.00015*/

                        silk_A2NLSF_init(a_Q16, P, Q, dd);
                        p = P;                            /* Pointer to polynomial */
                        xlo = Tables.silk_LSFCosTab_Q12[0]; /* Q12*/
                        ylo = silk_A2NLSF_eval_poly(p, xlo, dd);
                        if (ylo < 0)
                        {
                            /* Set the first NLSF to zero and move on to the next */
                            NLSF[0] = 0;
                            p = Q;                        /* Pointer to polynomial */
                            ylo = silk_A2NLSF_eval_poly(p, xlo, dd);
                            root_ix = 1;                  /* Index of current root */
                        }
                        else {
                            root_ix = 0;                  /* Index of current root */
                        }
                        k = 1;                            /* Reset loop counter */
                    }
                }
            }
        }

        /// <summary>
        /// Limit, stabilize, convert and quantize NLSFs
        /// </summary>
        /// <param name="psEncC">I/O  Encoder state</param>
        /// <param name="PredCoef_Q12">O    Prediction coefficients [ 2 ][MAX_LPC_ORDER]</param>
        /// <param name="pNLSF_Q15">I/O  Normalized LSFs (quant out) (0 - (2^15-1)) [MAX_LPC_ORDER]</param>
        /// <param name="prev_NLSFq_Q15">I    Previous Normalized LSFs (0 - (2^15-1)) [MAX_LPC_ORDER]</param>
        internal static void silk_process_NLSFs(
            SilkChannelEncoder psEncC,
            short[][] PredCoef_Q12,
            short[] pNLSF_Q15,
            short[] prev_NLSFq_Q15)
        {
            int i;
            bool doInterpolate;
            int NLSF_mu_Q20;
            int i_sqr_Q15;
            short[] pNLSF0_temp_Q15 = new short[SilkConstants.MAX_LPC_ORDER];
            short[] pNLSFW_QW = new short[SilkConstants.MAX_LPC_ORDER];
            short[] pNLSFW0_temp_QW = new short[SilkConstants.MAX_LPC_ORDER];

            Inlines.OpusAssert(psEncC.speech_activity_Q8 >= 0);
            Inlines.OpusAssert(psEncC.speech_activity_Q8 <= ((int)((1.0f) * ((long)1 << (8)) + 0.5))/*Inlines.SILK_CONST(1.0f, 8)*/);
            Inlines.OpusAssert(psEncC.useInterpolatedNLSFs == 1 || psEncC.indices.NLSFInterpCoef_Q2 == (1 << 2));

            /***********************/
            /* Calculate mu values */
            /***********************/
            /* NLSF_mu  = 0.003 - 0.0015 * psEnc.speech_activity; */
            NLSF_mu_Q20 = Inlines.silk_SMLAWB(((int)((0.003f) * ((long)1 << (20)) + 0.5))/*Inlines.SILK_CONST(0.003f, 20)*/, ((int)((-0.001f) * ((long)1 << (28)) + 0.5))/*Inlines.SILK_CONST(-0.001f, 28)*/, psEncC.speech_activity_Q8);
            if (psEncC.nb_subfr == 2)
            {
                /* Multiply by 1.5 for 10 ms packets */
                NLSF_mu_Q20 = Inlines.silk_ADD_RSHIFT(NLSF_mu_Q20, NLSF_mu_Q20, 1);
            }

            Inlines.OpusAssert(NLSF_mu_Q20 > 0);
            Inlines.OpusAssert(NLSF_mu_Q20 <= ((int)((0.005f) * ((long)1 << (20)) + 0.5))/*Inlines.SILK_CONST(0.005f, 20)*/);

            /* Calculate NLSF weights */
            silk_NLSF_VQ_weights_laroia(pNLSFW_QW, pNLSF_Q15, psEncC.predictLPCOrder);

            /* Update NLSF weights for interpolated NLSFs */
            doInterpolate = (psEncC.useInterpolatedNLSFs == 1) && (psEncC.indices.NLSFInterpCoef_Q2 < 4);
            if (doInterpolate)
            {
                /* Calculate the interpolated NLSF vector for the first half */
                Inlines.silk_interpolate(pNLSF0_temp_Q15, prev_NLSFq_Q15, pNLSF_Q15,
                    psEncC.indices.NLSFInterpCoef_Q2, psEncC.predictLPCOrder);

                /* Calculate first half NLSF weights for the interpolated NLSFs */
                silk_NLSF_VQ_weights_laroia(pNLSFW0_temp_QW, pNLSF0_temp_Q15, psEncC.predictLPCOrder);

                /* Update NLSF weights with contribution from first half */
                i_sqr_Q15 = Inlines.silk_LSHIFT(Inlines.silk_SMULBB(psEncC.indices.NLSFInterpCoef_Q2, psEncC.indices.NLSFInterpCoef_Q2), 11);

                for (i = 0; i < psEncC.predictLPCOrder; i++)
                {
                    pNLSFW_QW[i] = (short)(Inlines.silk_SMLAWB(Inlines.silk_RSHIFT(pNLSFW_QW[i], 1), (int)pNLSFW0_temp_QW[i], i_sqr_Q15));
                    Inlines.OpusAssert(pNLSFW_QW[i] >= 1);
                }
            }

             //////////////////////////////////////////////////////////////////////////

            silk_NLSF_encode(psEncC.indices.NLSFIndices, pNLSF_Q15, psEncC.psNLSF_CB, pNLSFW_QW,
                NLSF_mu_Q20, psEncC.NLSF_MSVQ_Survivors, psEncC.indices.signalType);

            /* Convert quantized NLSFs back to LPC coefficients */
            silk_NLSF2A(PredCoef_Q12[1], pNLSF_Q15, psEncC.predictLPCOrder);

            if (doInterpolate)
            {
                /* Calculate the interpolated, quantized LSF vector for the first half */
                Inlines.silk_interpolate(pNLSF0_temp_Q15, prev_NLSFq_Q15, pNLSF_Q15,
                    psEncC.indices.NLSFInterpCoef_Q2, psEncC.predictLPCOrder);

                /* Convert back to LPC coefficients */
                silk_NLSF2A(PredCoef_Q12[0], pNLSF0_temp_Q15, psEncC.predictLPCOrder);

            }
            else
            {
                /* Copy LPC coefficients for first half from second half */
                Array.Copy(PredCoef_Q12[1], 0, PredCoef_Q12[0], 0, psEncC.predictLPCOrder);
            }
        }
    }
}
