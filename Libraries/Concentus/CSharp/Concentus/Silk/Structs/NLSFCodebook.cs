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

    /// <summary>
    /// Structure containing NLSF codebook
    /// </summary>
    internal class NLSFCodebook
    {
        internal short nVectors = 0;

        internal short order = 0;

        /// <summary>
        /// Quantization step size
        /// </summary>
        internal short quantStepSize_Q16 = 0;

        /// <summary>
        /// Inverse quantization step size
        /// </summary>
        internal short invQuantStepSize_Q6 = 0;

        /// <summary>
        /// POINTER
        /// </summary>
        internal byte[] CB1_NLSF_Q8 = null;

        /// <summary>
        /// POINTER
        /// </summary>
        internal byte[] CB1_iCDF = null;

        /// <summary>
        /// POINTER to Backward predictor coefs [ order ]
        /// </summary>
        internal byte[] pred_Q8 = null;

        /// <summary>
        /// POINTER to Indices to entropy coding tables [ order ]
        /// </summary>
        internal byte[] ec_sel = null;

        /// <summary>
        /// POINTER
        /// </summary>
        internal byte[] ec_iCDF = null;

        /// <summary>
        /// POINTER
        /// </summary>
        internal byte[] ec_Rates_Q5 = null;

        /// <summary>
        /// POINTER
        /// </summary>
        internal short[] deltaMin_Q15 = null;
        
        internal void Reset()
        {
            nVectors = 0;
            order = 0;
            quantStepSize_Q16 = 0;
            invQuantStepSize_Q6 = 0;
            CB1_NLSF_Q8 = null;
            CB1_iCDF = null;
            pred_Q8 = null;
            ec_sel = null;
            ec_iCDF = null;
            ec_Rates_Q5 = null;
            deltaMin_Q15 = null;
        }
    }
}
