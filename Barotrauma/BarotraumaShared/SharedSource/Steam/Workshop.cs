#nullable enable
using Barotrauma.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Steamworks.Data;
using WorkshopItemSet = System.Collections.Generic.ISet<Steamworks.Ugc.Item>;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        public const string WorkshopItemPreviewImageFolder = "Workshop";
        public const string PreviewImageName = "PreviewImage.png";
        public const string DefaultPreviewImagePath = "Content/DefaultWorkshopPreviewImage.png";

        public static bool TryExtractSteamWorkshopId(this ContentPackage contentPackage, [NotNullWhen(true)]out SteamWorkshopId? workshopId)
        {
            workshopId = null;
            if (!contentPackage.UgcId.TryUnwrap(out var ugcId)) { return false; }
            if (!(ugcId is SteamWorkshopId steamWorkshopId)) { return false; }

            workshopId = steamWorkshopId;
            return true;
        }
        
        public static partial class Workshop
        {
            private struct ItemEqualityComparer : IEqualityComparer<Steamworks.Ugc.Item>
            {
                public static readonly ItemEqualityComparer Instance = new ItemEqualityComparer();
                
                public bool Equals(Steamworks.Ugc.Item x, Steamworks.Ugc.Item y)
                    => x.Id == y.Id;

                public int GetHashCode(Steamworks.Ugc.Item obj)
                    => (int)obj.Id.Value;
            }

            private static async Task<WorkshopItemSet> GetWorkshopItems(Steamworks.Ugc.Query query, int? maxPages = null)
            {
                if (!IsInitialized) { return new HashSet<Steamworks.Ugc.Item>(); }

                await Task.Yield();
                query = query.WithKeyValueTags(true).WithLongDescription(true);
                var set = new HashSet<Steamworks.Ugc.Item>(ItemEqualityComparer.Instance);
                int prevSize = 0;
                for (int i = 1; maxPages is null || i <= maxPages; i++)
                {
                    Steamworks.Ugc.ResultPage? page = await query.GetPageAsync(i);
                    if (page is null || !page.Value.Entries.Any()) { break; }
                    set.UnionWith(page.Value.Entries);
                    
                    if (set.Count == prevSize) { break; }
                    prevSize = set.Count;
                }
                
                // Remove items that do not have the correct consumer app ID,
                // which can happen on items that are not visible to the currently
                // logged in player (i.e. private & friends-only items)
                set.RemoveWhere(it => it.ConsumerApp != AppID);
                
                return set;
            }

            public static async Task<WorkshopItemSet> GetAllSubscribedItems()
            {
                if (!IsInitialized) { return new HashSet<Steamworks.Ugc.Item>(); }

                return await GetWorkshopItems(
                    Steamworks.Ugc.Query.Items
                    .WhereUserSubscribed());
            }

            public static async Task<WorkshopItemSet> GetPopularItems()
            {
                if (!IsInitialized) { return new HashSet<Steamworks.Ugc.Item>(); }

                return await GetWorkshopItems(
                    Steamworks.Ugc.Query.Items
                    .WithTrendDays(7)
                    .RankedByTrend(), maxPages: 1);
            }

            public static async Task<WorkshopItemSet> GetPublishedItems()
            {
                if (!IsInitialized) { return new HashSet<Steamworks.Ugc.Item>(); }

                return await GetWorkshopItems(
                    Steamworks.Ugc.Query.All
                    .WhereUserPublished());
            }

            public static async Task<Steamworks.Ugc.Item?> GetItem(UInt64 itemId)
            {
                if (!IsInitialized) { return null; }

                var items = await GetWorkshopItems(
                    Steamworks.Ugc.Query.All
                        .WithFileId(itemId));
                return items.Any() ? items.First() : (Steamworks.Ugc.Item?)null;
            }
            
            public static async Task ForceRedownload(UInt64 itemId)
                => await ForceRedownload(new Steamworks.Ugc.Item(itemId));

            public static void NukeDownload(Steamworks.Ugc.Item item)
            {
                try
                {
                    System.IO.Directory.Delete(item.Directory, recursive: true);
                }
                catch
                {
                    //don't care in the slightest about what happens here
                }
            }

            public static void Uninstall(Steamworks.Ugc.Item workshopItem)
            {
                NukeDownload(workshopItem);
                var toUninstall
                    = ContentPackageManager.WorkshopPackages.Where(p =>
                            p.UgcId.TryUnwrap(out var ugcId)
                            && ugcId is SteamWorkshopId { Value: var itemId }
                            && itemId == workshopItem.Id)
                        .ToHashSet();
                ContentPackageManager.EnabledPackages.DisableMods(toUninstall);
                toUninstall.Select(p => p.Dir).ForEach(d => Directory.Delete(d));
                ContentPackageManager.WorkshopPackages.Refresh();
                ContentPackageManager.EnabledPackages.DisableRemovedMods();
            }
            
            public static async Task ForceRedownload(Steamworks.Ugc.Item item, CancellationTokenSource? cancellationTokenSrc = null)
            {
                NukeDownload(item);
                cancellationTokenSrc ??= new CancellationTokenSource();
                await item.DownloadAsync(ct: cancellationTokenSrc.Token);
            }

            /// <summary>
            /// This class creates a file called ".copying" that
            /// serves to keep mod copy operations in the same
            /// directory from overlapping.
            /// </summary>
            private class CopyIndicator : IDisposable
            {
                private readonly string path;

                public CopyIndicator(string path)
                {
                    this.path = path;
                    using (var f = File.Create(path))
                    {
                        if (f is null)
                        {
                            throw new Exception($"File.Create returned null");
                        }
                        f.WriteByte((byte)0);
                    }
                }

                public void Dispose()
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        //don't care!
                    }
                }
            }

            /// <summary>
            /// This class serves the purpose of preventing
            /// more than 10 mod install tasks from proceeding
            /// at the same time.
            /// </summary>
            private class InstallTaskCounter : IDisposable
            {
                private static readonly HashSet<InstallTaskCounter> installers = new HashSet<InstallTaskCounter>();
                private readonly static object mutex = new object();
                private const int MaxTasks = 7;
                
                private readonly UInt64 itemId;
                private InstallTaskCounter(UInt64 id) { itemId = id; }

                public static bool IsInstalling(Steamworks.Ugc.Item item)
                    => IsInstalling(item.Id);
                
                public static bool IsInstalling(ulong itemId)
                {
                    lock (mutex)
                    {
                        return installers.Any(i => i.itemId == itemId);
                    }
                }
                
                private async Task Init()
                {
                    await Task.Yield();
                    while (true)
                    {
                        lock (mutex)
                        {
                            if (installers.Count < MaxTasks) { installers.Add(this); return; }
                        }
                        await Task.Delay(5000);
                    }
                }

                public static async Task<InstallTaskCounter> Create(ulong itemId)
                {
                    var retVal = new InstallTaskCounter(itemId);
                    await retVal.Init();
                    return retVal;
                }

                public void Dispose()
                {
                    lock (mutex) { installers.Remove(this); }
                }
            }

            public static bool IsItemDirectoryUpToDate(in Steamworks.Ugc.Item item)
            {
                string itemDirectory = item.Directory;
                return Directory.Exists(itemDirectory)
                    && File.GetLastWriteTime(itemDirectory).ToUniversalTime() >= item.LatestUpdateTime;
            }

            public static bool CanBeInstalled(ulong itemId)
                => CanBeInstalled(new Steamworks.Ugc.Item(itemId));
            
            public static bool CanBeInstalled(in Steamworks.Ugc.Item item)
            {
                bool needsUpdate = item.NeedsUpdate;
                bool isDownloading = item.IsDownloading;
                bool isInstalled = item.IsInstalled;
                bool directoryIsUpToDate = IsItemDirectoryUpToDate(item);
                
                return !needsUpdate
                       && !isDownloading
                       && isInstalled
                       && directoryIsUpToDate;
            }

            public static async Task DownloadModThenEnqueueInstall(Steamworks.Ugc.Item item)
            {
                if (!CanBeInstalled(item))
                {
                    if (!item.IsDownloading && !item.IsDownloadPending) { await ForceRedownload(item); }
                }
#if CLIENT
                else
                {
                    OnItemDownloadComplete(item.Id);
                }
#endif
            }

            public static void DeleteFailedCopies()
            {
                if (Directory.Exists(ContentPackage.WorkshopModsDir))
                {
                    foreach (var dir in Directory.EnumerateDirectories(ContentPackage.WorkshopModsDir, "**"))
                    {
                        string copyingIndicatorPath = Path.Combine(dir, ContentPackageManager.CopyIndicatorFileName);
                        if (File.Exists(copyingIndicatorPath))
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                    }
                }
            }

            public static ISet<ulong> GetInstalledItems()
                => ContentPackageManager.WorkshopPackages
                    .Select(p => p.UgcId)
                    .NotNone()
                    .OfType<SteamWorkshopId>()
                    .Select(id => id.Value)
                    .ToHashSet();
            
            public static async Task<ISet<Steamworks.Ugc.Item>> GetPublishedAndSubscribedItems()
            {
                var allItems = (await GetAllSubscribedItems()).ToHashSet();
                allItems.UnionWith(await GetPublishedItems());

                // This is a hack that eliminates subscribed mods that have been
                // made private. Players cannot download updates for these, so
                // we treat them as if they were deleted.
                allItems = (await Task.WhenAll(allItems.Select(it => GetItem(it.Id.Value))))
                    .NotNull()
                    .Where(it => it.ConsumerApp == AppID)
                    .ToHashSet();

                return allItems;
            }
            
            public static void DeleteUnsubscribedMods(Action<ContentPackage[]>? callback = null)
            {
#if SERVER
                // Servers do not run this because they can't subscribe to anything
                return;
#endif
                //If Steamworks isn't initialized then we can't know what the user has unsubscribed from
                if (!IsInitialized) { return; }
                if (!Steamworks.SteamClient.IsValid) { return; }
                if (!Steamworks.SteamClient.IsLoggedOn) { return; }
                
                TaskPool.Add("DeleteUnsubscribedMods", GetPublishedAndSubscribedItems().WaitForLoadingScreen(), t =>
                {
                    if (!t.TryGetResult(out ISet<Steamworks.Ugc.Item> items)) { return; }
                    var ids = items.Select(it => it.Id.Value).ToHashSet();
                    var toUninstall = ContentPackageManager.WorkshopPackages
                        .Where(pkg
                            => !pkg.UgcId.TryUnwrap(out SteamWorkshopId workshopId)
                               || !ids.Contains(workshopId.Value))
                        .ToArray();
                    if (toUninstall.Any())
                    {
                        foreach (var pkg in toUninstall)
                        {
                            Directory.TryDelete(pkg.Dir, recursive: true);
                        }
                        ContentPackageManager.UpdateContentPackageList();
                    }
                    callback?.Invoke(toUninstall);
                });
            }
            
            public static bool IsInstallingToPath(string path)
                => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, ContentPackageManager.CopyIndicatorFileName));

            public static bool IsInstalling(Steamworks.Ugc.Item item)
                => InstallTaskCounter.IsInstalling(item);

            private static async Task InstallMod(ulong id)
            {
                using var installCounter = await InstallTaskCounter.Create(id);

                var itemNullable = await GetItem(id);
                if (!(itemNullable is { } item)) { return; }
                await Task.Yield();
                
                string itemTitle = item.Title.Trim();
                UInt64 itemId = item.Id;
                string itemDirectory = item.Directory;
                DateTime updateTime = item.LatestUpdateTime;

                if (!CanBeInstalled(item))
                {
                    ForceRedownload(item);
                    throw new InvalidOperationException($"Item {itemTitle} (id {itemId}) is not available for copying");
                }

                const string workshopModDirReadme =
                    "DO NOT MODIFY THE CONTENTS OF THIS FOLDER, EVEN IF\n"
                    + "YOU ARE EDITING A MOD YOU PUBLISHED YOURSELF.\n"
                    + "\n"
                    + "If you do you may run into networking issues and\n"
                    + "unexpected deletion of your hard work.\n"
                    + "Instead, modify a copy of your mod in LocalMods.\n";

                string workshopModDirReadmeLocation = Path.Combine(SaveUtil.DefaultSaveFolder, "WorkshopMods", "README.txt");
                if (!File.Exists(workshopModDirReadmeLocation))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(workshopModDirReadmeLocation)!);
                    File.WriteAllText(
                        path: workshopModDirReadmeLocation,
                        contents: workshopModDirReadme);
                }
                
                string installDir = Path.Combine(ContentPackage.WorkshopModsDir, itemId.ToString());
                Directory.CreateDirectory(installDir);

                string copyIndicatorPath = Path.Combine(installDir, ContentPackageManager.CopyIndicatorFileName);

                XDocument fileListSrc = XMLExtensions.TryLoadXml(Path.Combine(itemDirectory, ContentPackage.FileListFileName));
                string modName = fileListSrc.Root.GetAttributeString("name", item.Title).Trim();
                string[] modPathSplit = fileListSrc.Root.GetAttributeString("path", "")
                    .CleanUpPathCrossPlatform(correctFilenameCase: false).Split("/");
                string? modPathDirName = modPathSplit.Length > 1 && modPathSplit[0] == "Mods"
                    ? modPathSplit[1]
                    : null;
                string modVersion = fileListSrc.Root.GetAttributeString("modversion", ContentPackage.DefaultModVersion);
                Version gameVersion = fileListSrc.Root.GetAttributeVersion("gameversion", GameMain.Version);
                bool isCorePackage = fileListSrc.Root.GetAttributeBool("corepackage", false);
                string expectedHash = fileListSrc.Root.GetAttributeString("expectedhash", "");

                using (var copyIndicator = new CopyIndicator(copyIndicatorPath))
                {
                    await CopyDirectory(itemDirectory, modPathDirName ?? modName, itemDirectory, installDir,
                        gameVersion < new Version(0, 18, 3, 0)
                            ? ShouldCorrectPaths.Yes
                            : ShouldCorrectPaths.No);

                    string fileListDestPath = Path.Combine(installDir, ContentPackage.FileListFileName);
                    XDocument fileListDest = XMLExtensions.TryLoadXml(fileListDestPath);
                    XElement root = fileListDest.Root ?? throw new NullReferenceException("Unable to install mod: file list root is null.");
                    root.Attributes().Remove();

                    root.Add(
                        new XAttribute("name", itemTitle),
                        new XAttribute("steamworkshopid", itemId),
                        new XAttribute("corepackage", isCorePackage),
                        new XAttribute("modversion", modVersion),
                        new XAttribute("gameversion", gameVersion),
                        new XAttribute("installtime", ToolBox.Epoch.FromDateTime(updateTime)));
                    if ((modPathDirName ?? modName).ToIdentifier() != itemTitle)
                    {
                        root.Add(new XAttribute("altnames", modPathDirName ?? modName));
                    }
                    if (!expectedHash.IsNullOrEmpty())
                    {
                        root.Add(new XAttribute("expectedhash", expectedHash));
                    }
                    fileListDest.SaveSafe(fileListDestPath);
                }
            }

            private static async Task CorrectPaths(string fileListDir, string modName, XElement element)
            {
                foreach (var attribute in element.Attributes())
                {
                    await Task.Yield();

                    string val = attribute.Value.CleanUpPathCrossPlatform(correctFilenameCase: false);

                    bool isPath = false;
                    
                    //Handle mods that have been mangled by pre-modding-refactor
                    //copying of post-modding-refactor mods (what a clusterfuck)
                    int modDirStrIndex = val.IndexOf(ContentPath.ModDirStr, StringComparison.OrdinalIgnoreCase);
                    if (modDirStrIndex >= 0)
                    {
                        val = val[modDirStrIndex..];
                        isPath = true;
                    }
                    
                    //Handle really old mods (0.9.0.4-era) that might be structured as
                    //%ModDir%/Mods/[NAME]/[RESOURCE]
                    string fullSrcPath = Path.Combine(fileListDir, val).CleanUpPath();
                    if (File.Exists(fullSrcPath))
                    {
                        val = $"{ContentPath.ModDirStr}/{val}";
                        isPath = true;
                    }
                    
                    //Handle old mods that installed to the fixed Mods directory
                    //that no longer exists
                    string oldModDir = $"Mods/{modName}";
                    if (val.StartsWith(oldModDir, StringComparison.OrdinalIgnoreCase))
                    {
                        val = $"{ContentPath.ModDirStr}{val.Remove(0, oldModDir.Length)}";
                        isPath = true;
                    }
                    //Handle old mods that depend on other mods
                    else if (val.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
                    {
                        string otherModName = val.Substring(val.IndexOf('/')+1);
                        otherModName = otherModName.Substring(0, otherModName.IndexOf('/'));
                        val = $"{string.Format(ContentPath.OtherModDirFmt, otherModName)}{val.Remove(0, $"Mods/{otherModName}".Length)}";
                        isPath = true;
                    }
                    //Handle really old mods that installed Submarines in the wrong place
                    else if (val.StartsWith("Submarines/", StringComparison.OrdinalIgnoreCase))
                    {
                        val = $"{ContentPath.ModDirStr}/{val}";
                        isPath = true;
                    }
                    if (isPath) { attribute.Value = val; }
                }
                await Task.WhenAll(
                    element.Elements()
                    .Select(subElement => CorrectPaths(
                        fileListDir: fileListDir,
                        modName: modName,
                        element: subElement)));
            }

            private static async Task CopyFile(string fileListDir, string modName, string from, string to, ShouldCorrectPaths shouldCorrectPaths)
            {
                await Task.Yield();
                Identifier extension = Path.GetExtension(from).ToIdentifier();
                if (extension == ".xml")
                {
                    try
                    {
                        XDocument? doc = XMLExtensions.TryLoadXml(from, out var exception);
                        if (exception is { Message: string exceptionMsg })
                        {
                            throw new Exception($"Could not load \"{from}\": {exceptionMsg}");
                        }
                        if (doc is null)
                        {
                            throw new Exception($"Could not load \"{from}\": doc is null");
                        }

                        if (shouldCorrectPaths == ShouldCorrectPaths.Yes)
                        {
                            await CorrectPaths(
                                fileListDir: fileListDir,
                                modName: modName,
                                element: doc.Root ?? throw new NullReferenceException());
                        }
                        doc.SaveSafe(to);
                        return;
                    }
                    catch (Exception e)
                    {
                        DebugConsole.AddWarning(
                            $"An exception was thrown when attempting to copy \"{from}\" to \"{to}\": {e.Message}\n{e.StackTrace}");
                    }
                }
                File.Copy(from, to, overwrite: true);
            }

            public enum ShouldCorrectPaths
            {
                Yes, No
            }
            
            public static async Task CopyDirectory(string fileListDir, string modName, string from, string to, ShouldCorrectPaths shouldCorrectPaths)
            {
                from = Path.GetFullPath(from); to = Path.GetFullPath(to);
                Directory.CreateDirectory(to);

                string convertFromTo(string from)
                    => Path.Combine(to, Path.GetFileName(from));

                string[] files = Directory.GetFiles(from);
                string[] subDirs = Directory.GetDirectories(from);
                foreach (var file in files)
                {
                    await CopyFile(fileListDir, modName, file, convertFromTo(file), shouldCorrectPaths);
                }

                foreach (var dir in subDirs) { await CopyDirectory(fileListDir, modName, dir, convertFromTo(dir), shouldCorrectPaths); }
            }
        }
    }
}
