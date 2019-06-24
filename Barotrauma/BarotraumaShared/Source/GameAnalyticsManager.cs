using GameAnalyticsSDK.Net;
using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Barotrauma
{
    public static class GameAnalyticsManager
    {
        private static HashSet<string> sentEventIdentifiers = new HashSet<string>();

        public static void Init()
        {
#if DEBUG
            try
            {
                GameAnalytics.SetEnabledInfoLog(true);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Initializing GameAnalytics failed. Disabling user statistics...", e);
                GameSettings.SendUserStatistics = false;
                return;
            }
#endif

            string exePath = Assembly.GetEntryAssembly().Location;
            string exeName = null;
            Md5Hash exeHash = null;
            exeName = Path.GetFileNameWithoutExtension(exePath).Replace(":", "");
            var md5 = MD5.Create();
            try
            {
                using (var stream = File.OpenRead(exePath))
                {
                    exeHash = new Md5Hash(stream);
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Error while calculating MD5 hash for the executable \"" + exePath + "\"", e);
            }
            try
            {
                GameAnalytics.ConfigureBuild(GameMain.Version.ToString()
                    + (string.IsNullOrEmpty(exeName) ? "Unknown" : exeName) + ":"
                    + ((exeHash?.ShortHash == null) ? "Unknown" : exeHash.ShortHash));
                GameAnalytics.ConfigureAvailableCustomDimensions01("singleplayer", "multiplayer", "editor");

                GameAnalytics.Initialize("a3a073c20982de7c15d21e840e149122", "9010ad9a671233b8d9610d76cec8c897d9ff3ba7");

                GameAnalytics.AddDesignEvent("Executable:"
                    + (string.IsNullOrEmpty(exeName) ? "Unknown" : exeName) + ":"
                    + ((exeHash?.ShortHash == null) ? "Unknown" : exeHash.ShortHash));
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Initializing GameAnalytics failed. Disabling user statistics...", e);
                GameSettings.SendUserStatistics = false;
                return;
            }
            
            if (GameMain.Config?.SelectedContentPackages.Count > 0)
            {
                StringBuilder sb = new StringBuilder("ContentPackage: ");
                int i = 0;
                foreach (ContentPackage cp in GameMain.Config.SelectedContentPackages)
                {
                    string trimmedName = cp.Name.Replace(":", "").Replace(" ", "");
                    sb.Append(trimmedName.Substring(0, Math.Min(32, trimmedName.Length)));
                    if (i < GameMain.Config.SelectedContentPackages.Count - 1) { sb.Append(" "); }
                }
                GameAnalytics.AddDesignEvent(sb.ToString());
            }
        }

        /// <summary>
        /// Adds an error event to GameAnalytics if an event with the same identifier has not been added yet.
        /// </summary>
        public static void AddErrorEventOnce(string identifier, EGAErrorSeverity errorSeverity, string message)
        {
            if (!GameSettings.SendUserStatistics) return;
            if (sentEventIdentifiers.Contains(identifier)) return;

            GameAnalytics.AddErrorEvent(errorSeverity, message);
            sentEventIdentifiers.Add(identifier);
        }

        public static void AddDesignEvent(string eventID)
        {
            if (!GameSettings.SendUserStatistics) return;
            GameAnalytics.AddDesignEvent(eventID);
        }

        public static void AddDesignEvent(string eventID, double value)
        {
            if (!GameSettings.SendUserStatistics) return;
            GameAnalytics.AddDesignEvent(eventID, value);
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01)
        {
            if (!GameSettings.SendUserStatistics) return;
            GameAnalytics.AddProgressionEvent(progressionStatus, progression01);
        }

        public static void AddProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02)
        {
            if (!GameSettings.SendUserStatistics) return;
            GameAnalytics.AddProgressionEvent(progressionStatus, progression01, progression02);
        }

        public static void SetCustomDimension01(string dimension)
        {
            if (!GameSettings.SendUserStatistics) return;
            GameAnalytics.SetCustomDimension01(dimension);
        }
    }
}
