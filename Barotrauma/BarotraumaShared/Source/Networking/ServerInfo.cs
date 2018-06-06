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
        public HashSet<string> ContentPackageNames
        {
            get;
            private set;
        } = new HashSet<string>();
        public HashSet<string> ContentPackageHashes
        {
            get;
            private set;
        } = new HashSet<string>();

        public bool ContentPackagesMatch(IEnumerable<ContentPackage> myContentPackages)
        {
            return ContentPackagesMatch(myContentPackages.Select(cp => cp.Name), myContentPackages.Select(cp => cp.MD5hash.Hash));
        }

        public bool ContentPackagesMatch(IEnumerable<string> myContentPackageNames, IEnumerable<string> myContentPackageHashes)
        {
            return ContentPackageNames.SetEquals(myContentPackageNames) && ContentPackageHashes.SetEquals(myContentPackageHashes);
        }
    }
}
