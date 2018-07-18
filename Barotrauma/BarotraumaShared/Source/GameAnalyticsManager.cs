using GameAnalyticsSDK.Net;
using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Barotrauma
{
    public static class GameAnalyticsManager
    {
        public static void Init()
        {
#if DEBUG
            GameAnalytics.SetEnabledInfoLog(true);
#endif
            GameAnalytics.ConfigureBuild(GameMain.Version.ToString());
            
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
            
            GameAnalytics.AddDesignEvent("Executable:"
                + (string.IsNullOrEmpty(exeName) ? "Unknown" : exeName) + ":"
                + ((exeHash == null) ? "Unknown" : exeHash.ShortHash));

            GameAnalytics.ConfigureAvailableCustomDimensions01("singleplayer", "multiplayer", "editor");
            GameAnalytics.Initialize("a3a073c20982de7c15d21e840e149122", "9010ad9a671233b8d9610d76cec8c897d9ff3ba7");
            
            if (GameMain.Config?.SelectedContentPackages.Count > 0)
            {
                StringBuilder sb = new StringBuilder("ContentPackage:");
                int i = 0;
                foreach (ContentPackage cp in GameMain.Config.SelectedContentPackages)
                {
                    sb.Append(cp.Name.Replace(":", "").Substring(0, Math.Min(32, cp.Name.Length)));
                    if (i < GameMain.Config.SelectedContentPackages.Count - 1) sb.Append(",");
                }
                GameAnalytics.AddDesignEvent(sb.ToString());
            }
        }
    }
}
