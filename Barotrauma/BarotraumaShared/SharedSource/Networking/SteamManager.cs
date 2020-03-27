using System;
using System.Collections.Generic;
using System.Linq;

#if USE_STEAM
namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        public const int STEAMP2P_OWNER_PORT = 30000;

        public const uint AppID = 602960;

        private static readonly List<string> initializationErrors = new List<string>();
        public static IEnumerable<string> InitializationErrors
        {
            get { return initializationErrors; }
        }

        public const string MetadataFileName = "filelist.xml";

        public const string CopyIndicatorFileName = ".copying";

        private static readonly Dictionary<string, int> tagCommonness = new Dictionary<string, int>()
        {
            { "submarine", 10 },
            { "item", 10 },
            { "monster", 8 },
            { "art", 8 },
            { "mission", 8 },
            { "event set", 8 },
            { "total conversion", 5 },
            { "environment", 5 },
            { "item assembly", 5 },
            { "language", 5 }
        };

        private static readonly List<string> popularTags = new List<string>();
        public static IEnumerable<string> PopularTags
        {
            get
            {
                if (!isInitialized) { return Enumerable.Empty<string>(); }
                return popularTags;
            }
        }
       
        private static bool isInitialized;
        public static bool IsInitialized => isInitialized;
        
        public static void Initialize()
        {
            InitializeProjectSpecific();
        }

        public static ulong GetSteamID()
        {
            if (!isInitialized || !Steamworks.SteamClient.IsValid)
            {
                return 0;
            }

            return Steamworks.SteamClient.SteamId;
        }

        public static string GetUsername()
        {
            if (!isInitialized || !Steamworks.SteamClient.IsValid)
            {
                return "";
            }
            return Steamworks.SteamClient.Name;
        }

        public static bool OverlayCustomURL(string url)
        {
            if (!isInitialized || !Steamworks.SteamClient.IsValid)
            {
                return false;
            }

            Steamworks.SteamFriends.OpenWebOverlay(url);
            return true;
        }
        
        public static bool UnlockAchievement(string achievementIdentifier)
        {
            if (!isInitialized || !Steamworks.SteamClient.IsValid)
            {
                return false;
            }

            DebugConsole.Log("Unlocked achievement \"" + achievementIdentifier + "\"");

            var achievements = Steamworks.SteamUserStats.Achievements.ToList();
            int achIndex = achievements.FindIndex(ach => ach.Identifier == achievementIdentifier);
            bool unlocked = achIndex >= 0 ? achievements[achIndex].Trigger() : false;
            if (!unlocked)
            {
                //can be caused by an incorrect identifier, but also happens during normal gameplay:
                //SteamAchievementManager tries to unlock achievements that may or may not exist 
                //(discovered[whateverbiomewasentered], kill[withwhateveritem], kill[somemonster] etc) so that we can add
                //some types of new achievements without the need for client-side changes.
#if DEBUG
                DebugConsole.NewMessage("Failed to unlock achievement \"" + achievementIdentifier + "\".");
#endif
            }

            return unlocked;
        }


        public static bool IncrementStat(string statName, int increment)
        {
            if (!isInitialized || !Steamworks.SteamClient.IsValid) { return false; }
            DebugConsole.Log("Incremented stat \"" + statName + "\" by " + increment);
            bool success = Steamworks.SteamUserStats.AddStat(statName, increment);
            if (!success)
            {
#if DEBUG
                DebugConsole.NewMessage("Failed to increment stat \"" + statName + "\".");
#endif
            }
            return success;
        }

        public static bool IncrementStat(string statName, float increment)
        {
            if (!isInitialized || !Steamworks.SteamClient.IsValid) { return false; }
            DebugConsole.Log("Incremented stat \"" + statName + "\" by " + increment);
            bool success = Steamworks.SteamUserStats.AddStat(statName, increment);
            if (!success)
            {
#if DEBUG
                DebugConsole.NewMessage("Failed to increment stat \"" + statName + "\".");
#endif
            }
            return success;
        }
        
        public static void Update(float deltaTime)
        {
            if (!isInitialized) { return; }

            if (Steamworks.SteamClient.IsValid) { Steamworks.SteamClient.RunCallbacks(); }
            if (Steamworks.SteamServer.IsValid) { Steamworks.SteamServer.RunCallbacks(); }

            SteamAchievementManager.Update(deltaTime);
            UpdateProjectSpecific(deltaTime);
        }

        public static void ShutDown()
        {
            if (!isInitialized) { return; }

            if (Steamworks.SteamClient.IsValid) { Steamworks.SteamClient.Shutdown(); }
            if (Steamworks.SteamServer.IsValid) { Steamworks.SteamServer.Shutdown(); }
            isInitialized = false;
        }

        public static UInt64 SteamIDStringToUInt64(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) { return 0; }
            UInt64 retVal;
            if (str.StartsWith("STEAM64_", StringComparison.InvariantCultureIgnoreCase)) { str = str.Substring(8); }
            if (UInt64.TryParse(str, out retVal) && retVal >(1<<52)) { return retVal; }
            if (!str.StartsWith("STEAM_", StringComparison.InvariantCultureIgnoreCase)) { return 0; }
            string[] split = str.Substring(6).Split(':');
            if (split.Length != 3) { return 0; }

            if (!UInt64.TryParse(split[0], out UInt64 universe)) { return 0; }
            if (!UInt64.TryParse(split[1], out UInt64 y)) { return 0; }
            if (!UInt64.TryParse(split[2], out UInt64 accountNumber)) { return 0; }

            UInt64 accountInstance = 1; UInt64 accountType = 1;

            return (universe << 56) | (accountType << 52) | (accountInstance << 32) | (accountNumber << 1) | y;
        }

        public static string SteamIDUInt64ToString(UInt64 uint64)
        {
            UInt64 y = uint64 & 0x1;
            UInt64 accountNumber = (uint64 >> 1) & 0x7fffffff;
            UInt64 universe = (uint64 >> 56) & 0xff;

            string retVal = "STEAM_" + universe.ToString() + ":" + y.ToString() + ":" + accountNumber.ToString();

            if (SteamIDStringToUInt64(retVal) != uint64) { return "STEAM64_" + uint64.ToString(); }

            return retVal;
        }
    }
}
#endif
