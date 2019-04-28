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

using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Concentus
{
    internal static class OpusMultistream
    {
        internal static int validate_layout(ChannelLayout layout)
        {
            int i, max_channel;

            max_channel = layout.nb_streams + layout.nb_coupled_streams;
            if (max_channel > 255)
                return 0;
            for (i = 0; i < layout.nb_channels; i++)
            {
                if (layout.mapping[i] >= max_channel && layout.mapping[i] != 255)
                    return 0;
            }
            return 1;
        }


        internal static int get_left_channel(ChannelLayout layout, int stream_id, int prev)
        {
            int i;
            i = (prev < 0) ? 0 : prev + 1;
            for (; i < layout.nb_channels; i++)
            {
                if (layout.mapping[i] == stream_id * 2)
                    return i;
            }
            return -1;
        }

        internal static int get_right_channel(ChannelLayout layout, int stream_id, int prev)
        {
            int i;
            i = (prev < 0) ? 0 : prev + 1;
            for (; i < layout.nb_channels; i++)
            {
                if (layout.mapping[i] == stream_id * 2 + 1)
                    return i;
            }
            return -1;
        }

        internal static int get_mono_channel(ChannelLayout layout, int stream_id, int prev)
        {
            int i;
            i = (prev < 0) ? 0 : prev + 1;
            for (; i < layout.nb_channels; i++)
            {
                if (layout.mapping[i] == stream_id + layout.nb_coupled_streams)
                    return i;
            }
            return -1;
        }
    }
}
