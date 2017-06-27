using System;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.ComponentModel;
using System.Linq;

namespace Barotrauma.Networking
{
    class GameClient : NetworkMember
    {
        public GameClient(string newName)
        {
            throw new Exception("Tried to create GameClient in dedicated server build");
        }
    }
}
