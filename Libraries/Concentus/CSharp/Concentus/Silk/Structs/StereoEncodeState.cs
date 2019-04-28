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

    internal class StereoEncodeState
    {
        internal readonly short[] pred_prev_Q13 = new short[2];
        internal readonly short[] sMid = new short[2];
        internal readonly short[] sSide = new short[2];
        internal readonly int[] mid_side_amp_Q0 = new int[4];
        internal short smth_width_Q14 = 0;
        internal short width_prev_Q14 = 0;
        internal short silent_side_len = 0;
        internal readonly sbyte[][][] predIx = Arrays.InitThreeDimensionalArray<sbyte>(SilkConstants.MAX_FRAMES_PER_PACKET, 2, 3);
        internal readonly sbyte[] mid_only_flags = new sbyte[SilkConstants.MAX_FRAMES_PER_PACKET];
        
        internal void Reset()
        {
            Arrays.MemSetShort(pred_prev_Q13, 0, 2);
            Arrays.MemSetShort(sMid, 0, 2);
            Arrays.MemSetShort(sSide, 0, 2);
            Arrays.MemSetInt(mid_side_amp_Q0, 0, 4);
            smth_width_Q14 = 0;
            width_prev_Q14 = 0;
            silent_side_len = 0;
            for (int x = 0; x < SilkConstants.MAX_FRAMES_PER_PACKET; x++)
            {
                for (int y= 0; y < 2; y++)
                {
                    Arrays.MemSetSbyte(predIx[x][y], 0, 3);
                }
            }

            Arrays.MemSetSbyte(mid_only_flags, 0, SilkConstants.MAX_FRAMES_PER_PACKET);
        }
    }
}
