using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    partial class ServerInfo
    {
        public string IP;
        public string Port;
        public string ServerName;
        public string ServerMessage;
        public bool GameStarted;
        public int PlayerCount;
        public int MaxPlayers;
        public bool HasPassword;

        public bool PingChecked;
        public int Ping = -1;

        //null value means that the value isn't known (the server may be using 
        //an old version of the game that didn't report these values or the FetchRules query to Steam may not have finished yet)
        public bool? UsingWhiteList;
        public SelectionMode? ModeSelectionMode;
        public SelectionMode? SubSelectionMode;
        public bool? AllowSpectating;
        public bool? AllowRespawn;
        public YesNoMaybe? TraitorsEnabled;
        public string GameMode;

        public string GameVersion;
        public List<string> ContentPackageNames
        {
            get;
            private set;
        } = new List<string>();
        public List<string> ContentPackageHashes
        {
            get;
            private set;
        } = new List<string>();
        public List<string> ContentPackageWorkshopUrls
        {
            get;
            private set;
        } = new List<string>();

        public bool ContentPackagesMatch(IEnumerable<ContentPackage> myContentPackages)
        {
            return ContentPackagesMatch(myContentPackages.Select(cp => cp.MD5hash.Hash));
        }

        public bool ContentPackagesMatch(IEnumerable<string> myContentPackageHashes)
        {
            HashSet<string> contentPackageHashes = new HashSet<string>(ContentPackageHashes);
            return contentPackageHashes.SetEquals(myContentPackageHashes);
        }
    }
}
