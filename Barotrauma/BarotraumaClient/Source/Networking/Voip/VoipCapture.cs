using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lidgren.Network;

namespace Barotrauma.Networking
{
    class VoipCapture : VoipQueue
    {
        public VoipCapture(GameClient client) : base(client.ID) { }

        public override void Write(NetBuffer msg)
        {
            lock (buffers)
            {
                base.Write(msg);
            }
        }

        public override void Read(NetBuffer msg)
        {
            throw new Exception("Called Read on a VoipCapture object");
        }
    }
}
