using Facepunch.Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
#if DEBUG
        public static bool USE_STEAM
        {
            get { return GameMain.Config.UseSteam; }
        }
#else
        //cannot enable/disable steam in release builds
        public const bool USE_STEAM = true;
#endif

        public const uint AppID = 602960;
        
        private Facepunch.Steamworks.Client client;
        private Server server;

        private Dictionary<string, int> tagCommonness = new Dictionary<string, int>()
        {
            { "submarine", 10 },
            { "item", 10 },
            { "monster", 8 },
            { "art", 8 },
            { "mission", 8 },
            { "environment", 5 }
        };

        private List<string> popularTags = new List<string>();
        public static IEnumerable<string> PopularTags
        {
            get
            {
                if (instance == null || !instance.isInitialized) { return Enumerable.Empty<string>(); }
                return instance.popularTags;
            }
        }

        private static SteamManager instance;
        public static SteamManager Instance
        {
            get
            {
                if (instance == null) instance = new SteamManager();
                return instance;
            }
        }
                        
        private bool isInitialized;
        public static bool IsInitialized
        {
            get
            {
                return Instance.isInitialized;
            }
        }
        
        public static void Initialize()
        {
            if (!USE_STEAM) return;
            instance = new SteamManager();
        }

        private SteamManager()
        {
#if SERVER
            return;
#endif

            try
            {
                client = new Facepunch.Steamworks.Client(AppID);
                isInitialized = client.IsSubscribed && client.IsValid;

                if (isInitialized)
                {
                    DebugConsole.Log("Logged in as " + client.Username + " (SteamID " + client.SteamId + ")");
                }
            }
            catch (DllNotFoundException e)
            {
                isInitialized = false;
#if CLIENT
                new Barotrauma.GUIMessageBox(TextManager.Get("Error"), TextManager.Get("SteamDllNotFound"));
#else
                DebugConsole.ThrowError("Initializing Steam client failed (steam_api64.dll not found).", e);
#endif
            }
            catch (Exception e)
            {
                isInitialized = false;
#if CLIENT
                new Barotrauma.GUIMessageBox(TextManager.Get("Error"), TextManager.Get("SteamClientInitFailed"));
#else
                DebugConsole.ThrowError("Initializing Steam client failed.", e);
#endif
            }

            if (!isInitialized)
            {
                try
                {

                    Facepunch.Steamworks.Client.Instance.Dispose();
                }
                catch (Exception e)
                {
                    if (GameSettings.VerboseLogging) DebugConsole.ThrowError("Disposing Steam client failed.", e);
                }
            }
        }

        public static bool UnlockAchievement(string achievementName)
        {
            if (instance == null || !instance.isInitialized)
            {
                return false;
            }

            DebugConsole.Log("Unlocked achievement \"" + achievementName + "\"");

            bool unlocked = instance.client.Achievements.Trigger(achievementName);
            if (!unlocked)
            {
                //can be caused by an incorrect identifier, but also happens during normal gameplay:
                //SteamAchievementManager tries to unlock achievements that may or may not exist 
                //(discovered[whateverbiomewasentered], kill[withwhateveritem], kill[somemonster] etc) so that we can add
                //some types of new achievements without the need for client-side changes.
#if DEBUG
                DebugConsole.NewMessage("Failed to unlock achievement \"" + achievementName + "\".");
#endif
            }

            return unlocked;
        }


        public static bool IncrementStat(string statName, int increment)
        {
            if (instance == null || !instance.isInitialized || instance.client == null) { return false; }
            DebugConsole.Log("Incremented stat \"" + statName + "\" by " + increment);
            bool success = instance.client.Stats.Add(statName, increment);
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
            if (instance == null || !instance.isInitialized || instance.client == null) { return false; }
            DebugConsole.Log("Incremented stat \"" + statName + "\" by " + increment);
            bool success = instance.client.Stats.Add(statName, increment);
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
            if (instance == null || !instance.isInitialized) { return; }

            instance.client?.Update();
            instance.server?.Update();

            SteamAchievementManager.Update(deltaTime);
        }

        public static void ShutDown()
        {
            if (instance == null) { return; }

            instance.client?.Dispose();
            instance.client = null;
            instance.server?.Dispose();
            instance.server = null;
            instance = null;
        }
    }
}
