#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.IO;
using Barotrauma.Steam;

namespace Barotrauma
{
    public static partial class ContentPackageManager
    {
        public sealed partial class PackageSource : ICollection<ContentPackage>
        {
            public string SaveRegularMod(ModProject modProject)
            {
                if (modProject.IsCore) { throw new ArgumentException("ModProject must not be a core package"); }

                string fileListPath = Path.Combine(directory, ToolBox.RemoveInvalidFileNameChars(modProject.Name), ContentPackage.FileListFileName)
                    .CleanUpPathCrossPlatform(correctFilenameCase: false);
                modProject.Save(fileListPath);
                Refresh(); EnabledPackages.DisableRemovedMods();

                return fileListPath;
            }

            public RegularPackage GetRegularModByPath(string fileListPath)
            {
                return Regular.First(p => p.Path == fileListPath);
            }
            
            public RegularPackage SaveAndEnableRegularMod(ModProject modProject)
            {
                string fileListPath = SaveRegularMod(modProject);
                var package = GetRegularModByPath(fileListPath);
                EnabledPackages.EnableRegular(package);

                return package;
            }
        }
        
        private static async Task<IEnumerable<Steamworks.Ugc.Item>> EnqueueWorkshopUpdates()
        {
            ISet<Steamworks.Ugc.Item> subscribedItems = await SteamManager.Workshop.GetAllSubscribedItems();
            
            var needInstalling = subscribedItems.Where(item
                    => !WorkshopPackages.Any(p
                        => p.UgcId.TryUnwrap(out var ugcId)
                           && ugcId is SteamWorkshopId workshopId
                           && item.Id == workshopId.Value
                           && p.InstallTime.TryUnwrap(out var installTime)
                           && item.LatestUpdateTime <= installTime.ToUtcValue()))
                .ToArray();
            if (!needInstalling.Any()) { return Enumerable.Empty<Steamworks.Ugc.Item>(); }
            
            await Task.WhenAll(
                needInstalling.Select(SteamManager.Workshop.DownloadModThenEnqueueInstall));

            return needInstalling;
        }
    }
}
