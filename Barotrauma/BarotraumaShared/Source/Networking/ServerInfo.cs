using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    public class ServerInfo
    {
        public string IP;
        public string Port;
        public string ServerName;
        public bool GameStarted;
        public int PlayerCount;
        public int MaxPlayers;
        public bool HasPassword;

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
