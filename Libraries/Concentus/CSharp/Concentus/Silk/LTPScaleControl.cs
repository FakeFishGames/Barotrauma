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

    internal static class LTPScaleControl
    {
        /* Calculation of LTP state scaling */
        internal static void silk_LTP_scale_ctrl(
            SilkChannelEncoder psEnc,                                 /* I/O  encoder state                                                               */
            SilkEncoderControl psEncCtrl,                             /* I/O  encoder control                                                             */
            int condCoding                              /* I    The type of conditional coding to use                                       */
        )
        {
            int round_loss;

            if (condCoding == SilkConstants.CODE_INDEPENDENTLY)
            {
                /* Only scale if first frame in packet */
                round_loss = psEnc.PacketLoss_perc + psEnc.nFramesPerPacket;
                psEnc.indices.LTP_scaleIndex = (sbyte)Inlines.silk_LIMIT(
                    Inlines.silk_SMULWB(Inlines.silk_SMULBB(round_loss, psEncCtrl.LTPredCodGain_Q7), ((int)((0.1f) * ((long)1 << (9)) + 0.5))/*Inlines.SILK_CONST(0.1f, 9)*/), 0, 2);
            }
            else {
                /* Default is minimum scaling */
                psEnc.indices.LTP_scaleIndex = 0;
            }
            psEncCtrl.LTP_scale_Q14 = Tables.silk_LTPScales_table_Q14[psEnc.indices.LTP_scaleIndex];
        }
    }
}
