using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    partial class BeaconMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            return;
        }
    }
}
