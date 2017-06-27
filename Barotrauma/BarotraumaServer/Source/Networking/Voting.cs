using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    partial class Voting
    {
        public bool AllowSubVoting
        {
            get { return allowSubVoting; }
            set {  allowSubVoting = value; }
        }
        public bool AllowModeVoting
        {
            get { return allowModeVoting; }
            set { allowModeVoting = value; }
        }
        
    }
}
