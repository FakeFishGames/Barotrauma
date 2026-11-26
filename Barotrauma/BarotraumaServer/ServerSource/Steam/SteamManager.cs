using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma.Steam
{
    partial class SteamManager
    {
        private static void InitializeProjectSpecific() { }

        private static bool IsInitializedProjectSpecific
            => Steamworks.SteamServer.IsValid;

        public static bool CreateServer(Networking.GameServer server, bool isPublic)
        {
            Steamworks.SteamServerInit options = new Steamworks.SteamServerInit("Barotrauma", "Barotrauma")
            {
                GamePort = (ushort)server.Port,
                QueryPort = isPublic ? (ushort)server.QueryPort : (ushort)0,
                Mode = isPublic ? Steamworks.InitServerMode.Authentication : Steamworks.InitServerMode.NoAuthentication
            };
            //options.QueryShareGamePort();

            Steamworks.SteamServer.Init(AppID, options, false);
            if (!Steamworks.SteamServer.IsValid)
            {
                Steamworks.SteamServer.Shutdown();
                DebugConsole.ThrowError("Initializing Steam server failed.");
                return false;
            }

            RefreshServerDetails(server);

            Steamworks.SteamServer.LogOnAnonymous();

            return true;
        }

        public static bool RefreshServerDetails(Networking.GameServer server)
        {
            if (!IsInitialized || !Steamworks.SteamServer.IsValid)
            {
                return false;
            }

            Steamworks.SteamServer.MapName = GameMain.NetLobbyScreen?.SelectedSub?.DisplayName?.Value ?? "";
            server.ServerSettings.UpdateServerListInfo(SetServerListInfo);

            Steamworks.SteamServer.DedicatedServer = true;

            return true;
        }

        private static void SetServerListInfo(Identifier key, object value)
        {
            switch (value)
            {
                case string stringValue when key == "ServerName":
                    Steamworks.SteamServer.ServerName = stringValue;
                    return;
                case int maxPlayers when key == "MaxPlayers":
                    Steamworks.SteamServer.MaxPlayers = maxPlayers;
                    return;
                case bool hasPassword when key == "HasPassword":
                    Steamworks.SteamServer.Passworded = hasPassword;
                    return;
                case string serverMessage when key == "message":
                    int maxValueLength = 127;
                    int totalMaxLength = 2000;
                    if (serverMessage.Length > totalMaxLength)
                    {
                        serverMessage = serverMessage.Substring(0, totalMaxLength);
                        //don't leave half of a surrogate pair at the end
                        //e.g. cyrillic characters need to be represented by two 16-bit "surrogate symbols"
                        //and we should not split the message between those
                        if (char.IsHighSurrogate(serverMessage[serverMessage.Length - 1])) { serverMessage = serverMessage.Substring(0, serverMessage.Length - 1); }
                    }
                    int chunkIndex = 0;
                    int chunkStart = 0;
                    while (chunkStart < serverMessage.Length)
                    {
                        int chunkEnd = chunkStart;
                        int chunkBytes = 0;
                        while (chunkEnd < serverMessage.Length)
                        {
                            //keep surrogate pairs together so we never split a character in half
                            int charLength = char.IsSurrogatePair(serverMessage, chunkEnd) ? 2 : 1;
                            int charBytes = System.Text.Encoding.UTF8.GetByteCount(serverMessage.AsSpan(chunkEnd, charLength));
                            if (chunkBytes + charBytes > maxValueLength) { break; }
                            chunkBytes += charBytes;
                            chunkEnd += charLength;
                        }
                        Steamworks.SteamServer.SetKey($"message{chunkIndex}", serverMessage.Substring(chunkStart, chunkEnd - chunkStart));
                        chunkStart = chunkEnd;
                        chunkIndex++;
                    }
                    //clear out the next key so clients don't read leftover chunks from a previous, longer message
                    Steamworks.SteamServer.SetKey($"message{chunkIndex}", string.Empty);
                    return;
                case IEnumerable<ContentPackage> contentPackages:
                    //a2s seems to break if too much data is added (seems to be related to MTU?)
                    //let's restrict the number of packages to 10, clients can use packagecount to tell when the list has been truncated
                    const int MaxPackagesToList = 10;
                    int index = 0;
                    foreach (var contentPackage in contentPackages.Take(MaxPackagesToList))
                    {
                        Steamworks.SteamServer.SetKey(
                            $"contentpackage{index}",
                            new ServerListContentPackageInfo(contentPackage).ToString());
                        index++;
                    }
                    Steamworks.SteamServer.SetKey("packagecount", contentPackages.Count().ToString());
                    return;
            }

            Steamworks.SteamServer.SetKey(key.Value.ToLowerInvariant(), value.ToString());
        }

        public static bool CloseServer()
        {
            if (!IsInitialized || !Steamworks.SteamServer.IsValid) return false;

            Steamworks.SteamServer.Shutdown();

            return true;
        }
    }
}
