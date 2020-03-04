using System;

namespace Barotrauma.Networking
{
    public class PipeConnection : NetworkConnection
    {
        public PipeConnection()
        {
            EndPointString = "PIPE";
        }
    }
}

