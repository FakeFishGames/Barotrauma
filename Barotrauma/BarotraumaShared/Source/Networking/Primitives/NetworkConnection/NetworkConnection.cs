using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public abstract class NetworkConnection
    {
        public string Name { get; set; }
        public abstract void Disconnect(string reason);
        public abstract void Ban(string reason, TimeSpan? duration);
    }
}
