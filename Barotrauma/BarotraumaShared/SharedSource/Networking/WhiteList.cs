using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class WhiteListedPlayer
    {
        public string Name;
        public string IP;

        public UInt16 UniqueIdentifier;
    }

    partial class WhiteList
    {
        const string SavePath = "Data/whitelist.txt";

        private List<WhiteListedPlayer> whitelistedPlayers;
        public List<WhiteListedPlayer> WhiteListedPlayers
        {
            get { return whitelistedPlayers; }
        }

        public bool Enabled;

        partial void InitProjSpecific();
        public WhiteList()
        {
            Enabled = false;
            whitelistedPlayers = new List<WhiteListedPlayer>();

            InitProjSpecific();
        }
    }
}
