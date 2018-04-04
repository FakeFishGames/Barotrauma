using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma.Networking
{
    public struct ServerInfo
    {
        public string IP;
        public string Port;
        public string ServerName;
        public bool GameStarted;
        public int PlayerCount;
        public int MaxPlayers;
        public bool HasPassword;
    }
}
