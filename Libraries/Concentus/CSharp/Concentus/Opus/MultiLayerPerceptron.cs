/* Copyright (c) 2008-2011 Octasic Inc.
   Originally written by Jean-Marc Valin
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

using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Concentus
{
    /// <summary>
    /// multi-layer perceptron processor
    /// </summary>
    internal static class mlp
    {
        private const int MAX_NEURONS = 100;

        internal static float tansig_approx(float x)
        {
            int i;
            float y, dy;
            float sign = 1;
            /* Tests are reversed to catch NaNs */
            if (!(x < 8))
                return 1;
            if (!(x > -8))
                return -1;
            if (x < 0)
            {
                x = -x;
                sign = -1;
            }
            i = (int)Math.Floor(.5f + 25 * x);
            x -= .04f * i;
            y = Tables.tansig_table[i];
            dy = 1 - y * y;
            y = y + x * dy * (1 - y * x);
            return sign * y;
        }

        internal static void mlp_process(MLP m, float[] input, float[] output)
        {
            int j;
            float[] hidden = new float[MAX_NEURONS];
            float[] W = m.weights;
            int W_ptr = 0;

            /* Copy to tmp_in */

            for (j = 0; j < m.topo[1]; j++)
            {
                int k;
                float sum = W[W_ptr];
                W_ptr++;
                for (k = 0; k < m.topo[0]; k++)
                {
                    sum = sum + input[k] * W[W_ptr];
                    W_ptr++;
                }
                hidden[j] = tansig_approx(sum);
            }

            for (j = 0; j < m.topo[2]; j++)
            {
                int k;
                float sum = W[W_ptr];
                W_ptr++;
                for (k = 0; k < m.topo[1]; k++)
                {
                    sum = sum + hidden[k] * W[W_ptr];
                    W_ptr++;
                }
                output[j] = tansig_approx(sum);
            }
        }
    }
}
