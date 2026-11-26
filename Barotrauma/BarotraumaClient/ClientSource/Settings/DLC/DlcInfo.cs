#nullable enable
using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma.IO;
using Steamworks;
using Steamworks.Data;

namespace Barotrauma
{
    public readonly struct DlcInfo(string name, string description, AppId appId, string iconPath, bool hasContentPackage)
    {
        public readonly string Name = name;
        public readonly string Description = description;
        public readonly AppId AppId = appId;
        public readonly bool HasContentPackage = hasContentPackage;

        public bool IsOwned =>
#if DEBUG
            true;
#else
            SteamApps.IsSubscribedToApp(AppId);
#endif

        public readonly Sprite? Sprite = FindCoverArt(iconPath);

        private static Sprite? FindCoverArt(string filePath)
        {
            if (File.Exists(filePath))
            {
                return new Sprite(filePath, null);
            }
            else
            {
                return null;
            }
        }

        public void OpenStorePage()
            => SteamFriends.OpenStoreOverlay(AppId, OverlayToStoreFlag.AddToCart);

        public bool Matches(ContentPackage contentPackage)
        {
            if (contentPackage.SteamDlcAppId.TryUnwrap(out AppId appId))
            {
                return appId == AppId;
            }

            return false;
        }

        public DlcDownloadProgress GetDownloadProgress()
        {
            DownloadProgress progress = SteamApps.DlcDownloadProgress(AppId);

            return new DlcDownloadProgress(IsActive: progress.Active,
                                           IsValid: progress.BytesTotal > 0,
                                           Progress: progress.BytesDownloaded / (float)progress.BytesTotal);
        }

        public void ForceInstall()
            => SteamApps.InstallDlc(AppId);

        public void Uninstall()
            => SteamApps.UninstallDlc(AppId);

        private static readonly string DlcCatalogPath = Path.Combine("Data", "dlc_catalog.xml");

        public static ImmutableArray<DlcInfo> RetrieveDLCsFromCatalog()
        {
            if (!File.Exists(DlcCatalogPath))
            {
                DebugConsole.ThrowError($"DLC catalog file not found: {DlcCatalogPath}");
                return ImmutableArray<DlcInfo>.Empty;
            }

            if (XMLExtensions.TryLoadXml(DlcCatalogPath)?.Root is not { } doc)
            {
                DebugConsole.ThrowError($"Failed to load DLC catalog file: {DlcCatalogPath}");
                return ImmutableArray<DlcInfo>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<DlcInfo>();
            foreach (XElement element in doc.Elements())
            {
                string name = element.GetAttributeString("name", string.Empty);
                string description = element.GetAttributeString("description", string.Empty);
                string iconPath = element.GetAttributeString("iconpath", string.Empty);
                uint appIdInt = element.GetAttributeUInt("appid", 0);
                bool hasContentPackage = element.GetAttributeBool("hascontentpackage", false);

                builder.Add(new DlcInfo(name, description, appIdInt, iconPath, hasContentPackage));
            }

            return builder.ToImmutableArray();
        }
    }
}