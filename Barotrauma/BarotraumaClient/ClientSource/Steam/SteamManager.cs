﻿using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Color = Microsoft.Xna.Framework.Color;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        private static readonly List<Identifier> initializationErrors = new List<Identifier>();
        public static IReadOnlyList<Identifier> InitializationErrors => initializationErrors;

        private static bool IsInitializedProjectSpecific
            => Steamworks.SteamClient.IsValid && Steamworks.SteamClient.IsLoggedOn;

        private static void InitializeProjectSpecific()
        {
            if (IsInitialized) { return; }

            try
            {
                Steamworks.SteamClient.Init(AppID, false);

                if (IsInitialized)
                {
                    DebugConsole.NewMessage(
                        $"Logged in as {GetUsername()} (SteamID {(GetSteamId().TryUnwrap(out var steamId) ? steamId.ToString() : "[NULL]")})");

                    popularTags.Clear();
                    int i = 0;
                    foreach (KeyValuePair<string, int> commonness in tagCommonness)
                    {
                        popularTags.Insert(i, commonness.Key);
                        i++;
                    }
                }

                Steamworks.SteamNetworkingUtils.OnDebugOutput += LogSteamworksNetworking;
            }
            catch (DllNotFoundException)
            {
                initializationErrors.Add("SteamDllNotFound".ToIdentifier());
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("SteamManager initialization threw an exception", e);
                initializationErrors.Add("SteamClientInitFailed".ToIdentifier());
            }

            if (!IsInitialized)
            {
                try
                {
                    if (Steamworks.SteamClient.IsValid) { Steamworks.SteamClient.Shutdown(); }
                }
                catch (Exception e)
                {
                    if (GameSettings.CurrentConfig.VerboseLogging) DebugConsole.ThrowError("Disposing Steam client failed.", e);
                }
            }
            else
            {
                //Steamworks is completely insane so the following needs comments:
                
                //This callback seems to take place when the item in question has not been downloaded recently
                Steamworks.SteamUGC.GlobalOnItemInstalled = id => Workshop.OnItemDownloadComplete(id);
                
                //This callback seems to take place when the item has been downloaded recently and an update
                //or a redownload has taken place
                Steamworks.SteamUGC.OnDownloadItemResult += (result, id) =>
                {
                    if (result == Steamworks.Result.OK)
                    {
                        Workshop.OnItemDownloadComplete(id);
                    }
                };
                
                //Maybe I'm completely wrong! All I know is that we need to handle both!
            }
        }

        public static bool NetworkingDebugLog { get; private set; } = false;

        private static void LogSteamworksNetworking(Steamworks.NetDebugOutput nType, string pszMsg)
        {
            DebugConsole.NewMessage($"({nType}) {pszMsg}", Color.Orange);
        }

        public static void SetSteamworksNetworkingDebugLog(bool enabled)
        {
            if (enabled == NetworkingDebugLog) { return; }
            if (enabled)
            {
                Steamworks.SteamNetworkingUtils.DebugLevel = Steamworks.NetDebugOutput.Everything;
            }
            else
            {
                Steamworks.SteamNetworkingUtils.DebugLevel = Steamworks.NetDebugOutput.None;
            }
            NetworkingDebugLog = enabled;
        }

        public static async Task InitRelayNetworkAccess()
        {
            if (!IsInitialized) { return; }

            await Task.Yield();
            Steamworks.SteamNetworkingUtils.InitRelayNetworkAccess();

            //SetSteamworksNetworkingDebugLog(true);
            var status = Steamworks.SteamNetworkingUtils.Status;
            while (status.Avail != Steamworks.SteamNetworkingAvailability.Current)
            {
                if (status.Avail == Steamworks.SteamNetworkingAvailability.CannotTry ||
                    status.Avail == Steamworks.SteamNetworkingAvailability.Previously ||
                    status.Avail == Steamworks.SteamNetworkingAvailability.Failed)
                {
                    DebugConsole.ThrowError($"Failed to initialize Steamworks network relay: " +
                        $"{Steamworks.SteamNetworkingUtils.Status.Avail}, " +
                        $"{Steamworks.SteamNetworkingUtils.Status.AvailNetConfig}, " +
                        $"{Steamworks.SteamNetworkingUtils.Status.Avail}, " +
                        $"{Steamworks.SteamNetworkingUtils.Status.Msg}");
                    break;
                }
                await Task.Delay(25);
                status = Steamworks.SteamNetworkingUtils.Status;
            }
            //SetSteamworksNetworkingDebugLog(false);
        }
        
        
        public static bool OverlayCustomUrl(string url)
        {
            if (!IsInitialized || !Steamworks.SteamClient.IsValid)
            {
                return false;
            }

            Steamworks.SteamFriends.OpenWebOverlay(url);
            return true;
        }

        public static void OverlayProfile(SteamId steamId)
        {
            OverlayCustomUrl($"https://steamcommunity.com/profiles/{steamId.Value}");
        }
    }
}