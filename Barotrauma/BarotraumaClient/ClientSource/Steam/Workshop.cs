#nullable enable
using Barotrauma.IO;
using Microsoft.Xna.Framework.Graphics;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Barotrauma.Extensions;

namespace Barotrauma.Steam
{
    static partial class SteamManager
    {
        public static partial class Workshop
        {
            public const int MaxThumbnailSize = 1024 * 1024;

            public static readonly ImmutableArray<Identifier> Tags = new []
            {
                "submarine",
                "item",
                "monster",
                "art",
                "mission",
                "event set",
                "total conversion",
                "environment",
                "item assembly",
                "language",
            }.ToIdentifiers().ToImmutableArray();
            
            public class ItemThumbnail : IDisposable
            {
                private struct RefCounter
                {
                    internal bool Loading;
                    internal Texture2D? Texture;
                    internal int Count;
                }
                private readonly static Dictionary<UInt64, RefCounter> TextureRefs
                    = new Dictionary<ulong, RefCounter>();

                public UInt64 ItemId { get; private set; }
                public Texture2D? Texture
                {
                    get
                    {
                        lock (TextureRefs)
                        {
                            if (TextureRefs.TryGetValue(ItemId, out var refCounter))
                            {
                                return refCounter.Texture;
                            }
                        }
                        return null;
                    }
                }

                public bool Loading
                {
                    get
                    {
                        lock (TextureRefs)
                        {
                            if (TextureRefs.TryGetValue(ItemId, out var refCounter))
                            {
                                return refCounter.Loading;
                            }
                        }
                        return false;
                    }
                }

                public ItemThumbnail(in Steamworks.Ugc.Item item, CancellationToken cancellationToken)
                {
                    ItemId = item.Id;
                    lock (TextureRefs)
                    {
                        if (TextureRefs.TryGetValue(ItemId, out var refCounter))
                        {
                            TextureRefs[ItemId] = new RefCounter { Texture = refCounter.Texture, Count = refCounter.Count + 1, Loading = refCounter.Loading };
                        }
                        else
                        {
                            TextureRefs[ItemId] = new RefCounter { Texture = null, Count = 1, Loading = true };
                            TaskPool.Add($"Workshop thumbnail {item.Title}", GetTexture(item, cancellationToken), SaveTextureToRefCounter(item.Id));
                        }
                    }
                }

                ~ItemThumbnail()
                {
                    Dispose();
                }

                public void Dispose()
                {
                    if (ItemId == 0) { return; }
                    lock (TextureRefs)
                    {
                        var refCounter = TextureRefs[ItemId];
                        TextureRefs[ItemId] = new RefCounter { Texture = refCounter.Texture, Count = refCounter.Count - 1 };
                        if (TextureRefs[ItemId].Count <= 0)
                        {
                            TextureRefs[ItemId].Texture?.Dispose();
                            TextureRefs.Remove(ItemId);
                        }
                        ItemId = 0;
                    }
                }

                private static async Task<Texture2D?> GetTexture(Steamworks.Ugc.Item item, CancellationToken cancellationToken)
                {
                    await Task.Yield();

                    string thumbnailUrl = item.PreviewImageUrl;
                    if (thumbnailUrl.IsNullOrWhiteSpace()) { return null; }
                    var client = new RestClient(thumbnailUrl);
                    var request = new RestRequest(".", Method.GET);
                    IRestResponse response = await client.ExecuteAsync(request, cancellationToken);
                    if (response is { StatusCode: System.Net.HttpStatusCode.OK, ResponseStatus: ResponseStatus.Completed })
                    {
                        using var dataStream = new System.IO.MemoryStream();
                        await dataStream.WriteAsync(response.RawBytes, cancellationToken);
                        dataStream.Seek(0, System.IO.SeekOrigin.Begin);
                        return TextureLoader.FromStream(dataStream, compress: false);
                    }
                    return null;
                }

                private static Action<Task> SaveTextureToRefCounter(UInt64 itemId)
                    => (t) =>
                    {
                        if (t.IsCanceled) { return; }
                        Texture2D? texture = ((Task<Texture2D?>)t).Result;
                        lock (TextureRefs)
                        {
                            if (TextureRefs.TryGetValue(itemId, out var refCounter))
                            {
                                TextureRefs[itemId] = new RefCounter { Texture = texture, Count = refCounter.Count, Loading = false };
                            }
                            else if (texture != null)
                            {
                                texture.Dispose();
                            }
                        }
                    };

                public override int GetHashCode() => (int)ItemId;

                public override bool Equals(object? obj)
                    => obj is ItemThumbnail { ItemId: UInt64 otherId }
                        && otherId == ItemId;
            }

            public const string PublishStagingDir = "WorkshopStaging";

            public static void DeletePublishStagingCopy()
            {
                if (Directory.Exists(PublishStagingDir)) { Directory.Delete(PublishStagingDir, recursive: true); }
            }

            private static void RefreshLocalMods()
            {
                CrossThread.RequestExecutionOnMainThread(() => ContentPackageManager.LocalPackages.Refresh());
            }
            
            public static async Task CreatePublishStagingCopy(string modVersion, ContentPackage contentPackage)
            {
                await Task.Yield();
                
                if (!ContentPackageManager.LocalPackages.Contains(contentPackage))
                {
                    throw new Exception("Expected local package");
                }

                DeletePublishStagingCopy();
                Directory.CreateDirectory(PublishStagingDir);
                await CopyDirectory(contentPackage.Dir, contentPackage.Name, Path.GetDirectoryName(contentPackage.Path)!, PublishStagingDir, ShouldCorrectPaths.No);

                //Load filelist.xml and write the hash into it so anyone downloading this mod knows what it should be
                ModProject modProject = new ModProject(contentPackage)
                {
                    ModVersion = modVersion
                };
                modProject.Save(Path.Combine(PublishStagingDir, ContentPackage.FileListFileName));
            }

            public static async Task<ContentPackage?> CreateLocalCopy(ContentPackage contentPackage)
            {
                await Task.Yield();
                
                if (!ContentPackageManager.WorkshopPackages.Contains(contentPackage))
                {
                    throw new Exception("Expected Workshop package");
                }

                if (contentPackage.SteamWorkshopId == 0)
                {
                    throw new Exception($"Steam Workshop ID not set for {contentPackage.Name}");
                }

                string sanitizedName = ToolBox.RemoveInvalidFileNameChars(contentPackage.Name).Trim();
                if (sanitizedName.IsNullOrWhiteSpace())
                {
                    throw new Exception($"Sanitized name for {contentPackage.Name} is empty");
                }

                string newPath = $"{ContentPackage.LocalModsDir}/{sanitizedName}";
                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    newPath += $"_{contentPackage.SteamWorkshopId}";
                }

                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    throw new Exception($"{newPath} already exists");
                }

                await CopyDirectory(contentPackage.Dir, contentPackage.Name, Path.GetDirectoryName(contentPackage.Path)!, newPath, ShouldCorrectPaths.Yes);

                ModProject modProject = new ModProject(contentPackage);
                modProject.DiscardHashAndInstallTime();
                modProject.Save(Path.Combine(newPath, ContentPackage.FileListFileName));

                RefreshLocalMods();

                return ContentPackageManager.LocalPackages.FirstOrDefault(p => p.SteamWorkshopId == contentPackage.SteamWorkshopId);
            }

            private struct InstallWaiter
            {
                private static readonly HashSet<ulong> waitingIds = new HashSet<ulong>();
                public ulong Id { get; private set; }

                public InstallWaiter(ulong id)
                {
                    Id = id;
                    lock (waitingIds) { waitingIds.Add(Id); }
                }

                public bool Waiting
                {
                    get
                    {
                        if (Id == 0) { return false; }

                        lock (waitingIds)
                        {
                            return waitingIds.Contains(Id);
                        }
                    }
                }

                public static void StopWaiting(ulong id)
                {
                    lock (waitingIds)
                    {
                        waitingIds.Remove(id);
                    }
                }
            }

            public static async Task Reinstall(Steamworks.Ugc.Item workshopItem)
            {
                NukeDownload(workshopItem);
                var toUninstall
                    = ContentPackageManager.WorkshopPackages.Where(p => p.SteamWorkshopId == workshopItem.Id)
                        .ToHashSet();
                toUninstall.Select(p => p.Dir).ForEach(d => Directory.Delete(d));
                CrossThread.RequestExecutionOnMainThread(() => ContentPackageManager.WorkshopPackages.Refresh());
                var installWaiter = WaitForInstall(workshopItem);
                DownloadModThenEnqueueInstall(workshopItem);
                await installWaiter;
            }

            public static async Task WaitForInstall(Steamworks.Ugc.Item item)
                => await WaitForInstall(item.Id);
            
            public static async Task WaitForInstall(ulong item)
            {
                var installWaiter = new InstallWaiter(item);
                while (installWaiter.Waiting) { await Task.Delay(500); }
                await Task.Delay(500);
            }
            
            public static void OnItemDownloadComplete(ulong id, bool forceInstall = false)
            {
                if (!(Screen.Selected is MainMenuScreen) && !forceInstall)
                {
                    if (!MainMenuScreen.WorkshopItemsToUpdate.Contains(id))
                    {
                        MainMenuScreen.WorkshopItemsToUpdate.Enqueue(id);
                    }
                    return;
                }
                else if (CanBeInstalled(id)
                    && !ContentPackageManager.WorkshopPackages.Any(p => p.SteamWorkshopId == id)
                    && !InstallTaskCounter.IsInstalling(id))
                {
                    TaskPool.Add($"InstallItem{id}", InstallMod(id), t => InstallWaiter.StopWaiting(id));
                }
            }
        }
    }
}
