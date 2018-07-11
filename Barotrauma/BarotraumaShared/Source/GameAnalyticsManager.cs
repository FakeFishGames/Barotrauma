using GameAnalyticsSDK.Net;
using System;

namespace Barotrauma
{
    public static class GameAnalyticsManager
    {
        public static void Init()
        {
            GameAnalytics.SetEnabledInfoLog(true);
            GameAnalytics.ConfigureBuild(GameMain.Version.ToString());
            GameAnalytics.ConfigureAvailableCustomDimensions01("singleplayer", "multiplayer", "editor");
            GameAnalytics.Initialize("a3a073c20982de7c15d21e840e149122", "9010ad9a671233b8d9610d76cec8c897d9ff3ba7");

            string contentPackageName = GameMain.Config?.SelectedContentPackage?.Name;
            if (!string.IsNullOrEmpty(contentPackageName))
            {
                GameAnalytics.AddDesignEvent("ContentPackage:" +
                    contentPackageName.Replace(":", "").Substring(0, Math.Min(32, contentPackageName.Length)));
            }
        }
    }
}
