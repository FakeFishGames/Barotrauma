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

namespace Concentus.Enums
{
    public enum OpusFramesize
    {
        /// <summary>
        /// Select frame size from the argument (default)
        /// </summary>
        OPUS_FRAMESIZE_ARG = 5000,

        /// <summary>
        /// Use 2.5 ms frames
        /// </summary>
        OPUS_FRAMESIZE_2_5_MS = 5001,

        /// <summary>
        /// Use 5 ms frames
        /// </summary>
        OPUS_FRAMESIZE_5_MS = 5002,

        /// <summary>
        /// Use 10 ms frames
        /// </summary>
        OPUS_FRAMESIZE_10_MS = 5003,

        /// <summary>
        /// Use 20 ms frames
        /// </summary>
        OPUS_FRAMESIZE_20_MS = 5004,

        /// <summary>
        /// Use 40 ms frames
        /// </summary>
        OPUS_FRAMESIZE_40_MS = 5005,

        /// <summary>
        /// Use 60 ms frames
        /// </summary>
        OPUS_FRAMESIZE_60_MS = 5006,

        /// <summary>
        /// Do not use - not fully implemented. Optimize the frame size dynamically.
        /// </summary>
        OPUS_FRAMESIZE_VARIABLE = 5010
    }
}
