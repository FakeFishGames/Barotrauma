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
            public ContentPackage SaveAndEnableRegularMod(ModProject modProject)
            {
                if (modProject.IsCore) { throw new ArgumentException("ModProject must not be a core package"); }
                
                //save the content package
                string fileListPath = Path.Combine(directory, ToolBox.RemoveInvalidFileNameChars(modProject.Name), ContentPackage.FileListFileName)
                    .CleanUpPathCrossPlatform(correctFilenameCase: false);
                Directory.CreateDirectory(Path.GetDirectoryName(fileListPath)!);
                modProject.Save(fileListPath);
                Refresh(); EnabledPackages.DisableRemovedMods();
                var newPackage = Regular.First(p => p.Path == fileListPath);

                //enable it
                EnabledPackages.EnableRegular(newPackage);

                return newPackage;
            }
        }
        
        private static async Task<IEnumerable<Steamworks.Ugc.Item>> EnqueueWorkshopUpdates()
        {
            ISet<Steamworks.Ugc.Item> subscribedItems = await SteamManager.Workshop.GetAllSubscribedItems();
            
            var needInstalling = subscribedItems.Where(item
                    => !WorkshopPackages.Any(p
                        => item.Id == p.SteamWorkshopId
                           && p.InstallTime.HasValue
                           && item.LatestUpdateTime <= p.InstallTime))
                .ToArray();
            if (needInstalling.Any())
            {
                await Task.WhenAll(
                    needInstalling.Select(SteamManager.Workshop.DownloadModThenEnqueueInstall));
            }

            return needInstalling;
        }
    }
}
