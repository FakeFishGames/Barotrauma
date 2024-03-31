using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using Barotrauma.Networking;
using Barotrauma.IO;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        public const int STEAMP2P_OWNER_PORT = 30000;

        public const uint AppID = 602960;

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

        public static bool IsInitialized => IsInitializedProjectSpecific;

        private static readonly List<string> popularTags = new List<string>();
        public static IEnumerable<string> PopularTags
        {
            get
            {
                if (!IsInitialized) { return Enumerable.Empty<string>(); }
                return popularTags;
            }
        }

        public static bool SteamworksLibExists
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? File.Exists("steam_api64.dll")
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? File.Exists("libsteam_api64.dylib")
                    : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                        ? File.Exists("libsteam_api64.so")
                        : false;

        public static void Initialize()
        {
            InitializeProjectSpecific();
        }

        public static Option<SteamId> GetSteamId()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid)
            {
                return Option<SteamId>.None();
            }

            return Option<SteamId>.Some(new SteamId(Steamworks.SteamClient.SteamId));
        }

        public static Option<SteamId> GetOwnerSteamId()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid)
            {
                return Option<SteamId>.None();
            }

            return Option<SteamId>.Some(new SteamId(Steamworks.SteamClient.SteamId));
        }

        public static bool IsFamilyShared()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return false; }

            return Steamworks.SteamApps.IsSubscribedFromFamilySharing;
        }

        public static bool IsFreeWeekend()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return false; }

            return Steamworks.SteamApps.IsSubscribedFromFreeWeekend;
        }

        public static string GetUsername()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid)
            {
                return "";
            }
            return Steamworks.SteamClient.Name;
        }

        public static uint GetNumSubscribedItems()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid)
            {
                return 0;
            }
            return Steamworks.SteamUGC.NumSubscribedItems;
        }

        public static bool UnlockAchievement(string achievementIdentifier) =>
            UnlockAchievement(achievementIdentifier.ToIdentifier());

        public static bool UnlockAchievement(Identifier achievementIdentifier)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid)
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
                DebugConsole.Log($"Failed to unlock achievement \"{achievementIdentifier}\".");
            }

            return unlocked;
        }

        /// <summary>
        /// Increment multiple stats in bulk.
        /// Make sure to call StoreStats() after calling this method since it doesn't do it automatically.
        /// </summary>
        /// <param name="stats"></param>
        public static void IncrementStats(params (AchievementStat Identifier, float Increment)[] stats)
            => Array.ForEach(stats, static s
                => IncrementStat(s.Identifier, s.Increment, storeStats: false));

        public static bool IncrementStat(AchievementStat statName, int increment, bool storeStats = true)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return false; }
            DebugConsole.Log($"Incremented stat \"{statName}\" by " + increment);
            bool success = Steamworks.SteamUserStats.AddStatInt(statName.ToIdentifier().Value.ToLowerInvariant(), increment);
            if (!success)
            {
                DebugConsole.Log("Failed to increment stat \"" + statName + "\".");
            }
            else if (storeStats)
            {
                StoreStats();
            }
            return success;
        }

        public static bool IncrementStat(AchievementStat statName, float increment, bool storeStats = true)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return false; }
            DebugConsole.Log($"Incremented stat \"{statName}\" by " + increment);
            bool success = Steamworks.SteamUserStats.AddStatFloat(statName.ToIdentifier().Value.ToLowerInvariant(), increment);
            if (!success)
            {
                DebugConsole.Log("Failed to increment stat \"" + statName + "\".");
            }
            else if (storeStats)
            {
                StoreStats();
            }
            return success;
        }

        public static int GetStatInt(AchievementStat stat)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return 0; }
            return  Steamworks.SteamUserStats.GetStatInt(stat.ToString().ToLowerInvariant());
        }

        public static float GetStatFloat(AchievementStat stat)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return 0f; }
            return  Steamworks.SteamUserStats.GetStatFloat(stat.ToString().ToLowerInvariant());
        }

        public static ImmutableDictionary<AchievementStat, float> GetAllStats()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return ImmutableDictionary<AchievementStat, float>.Empty; }

            var builder = ImmutableDictionary.CreateBuilder<AchievementStat, float>();

            foreach (AchievementStat stat in AchievementStatExtension.SteamStats)
            {
                if (stat.IsFloatStat())
                {
                    builder.Add(stat, GetStatFloat(stat));
                }
                else
                {
                    builder.Add(stat, GetStatInt(stat));
                }
            }

            return builder.ToImmutable();
        }

        public static bool StoreStats()
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) { return false; }
            DebugConsole.Log("Storing Steam stats...");
            bool success = Steamworks.SteamUserStats.StoreStats();
            if (!success)
            {
                DebugConsole.Log("Failed to store Steam stats.");
            }
            return success;
        }

        public static bool TryGetUnlockedAchievements(out List<Steamworks.Data.Achievement> achievements)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid) 
            {
                achievements = null;
                return false; 
            }
            achievements = Steamworks.SteamUserStats.Achievements.Where(a => a.State).ToList();
            return true;
        }

        public static void Update(float deltaTime)
        {
            //this should be run even if SteamManager is uninitialized
            //servers need to be able to notify clients of unlocked talents even if the server isn't connected to Steam
            AchievementManager.Update(deltaTime);

            if (!IsInitialized) { return; }

            if (Steamworks.SteamClient.IsValid) { Steamworks.SteamClient.RunCallbacks(); }
            if (Steamworks.SteamServer.IsValid) { Steamworks.SteamServer.RunCallbacks(); }
        }

        public static void ShutDown()
        {
            if (!IsInitialized) { return; }

            if (Steamworks.SteamClient.IsValid) { Steamworks.SteamClient.Shutdown(); }
            if (Steamworks.SteamServer.IsValid) { Steamworks.SteamServer.Shutdown(); }
        }

        public static IEnumerable<ulong> WorkshopUrlsToIds(IEnumerable<string> urls)
        {
            return urls.Select((u) =>
            {
                if (string.IsNullOrEmpty(u))
                {
                    return (ulong)0;
                }
                else
                {
                    return GetWorkshopItemIDFromUrl(u);
                }
            });
        }

        public static ulong GetWorkshopItemIDFromUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string idStr = HttpUtility.ParseQueryString(uri.Query)["id".ToIdentifier()];
                if (ulong.TryParse(idStr, out ulong id))
                {
                    return id;
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to get Workshop item ID from the url \"" + url + "\"!", e);
            }

            return 0;
        }
    }
}
