#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Steam
{
    public static class BulkDownloader
    {
        private static void CloseAllMessageBoxes()
        {
            GUIMessageBox.MessageBoxes.ForEachMod(b =>
            {
                if (b is GUIMessageBox m) { m.Close(); }
                else { GUIMessageBox.MessageBoxes.Remove(b); }
            });
        }
        
        public static void PrepareUpdates()
        {
            CloseAllMessageBoxes();
            GUIMessageBox msgBox = new GUIMessageBox(headerText: "", text: TextManager.Get("DeterminingRequiredModUpdates"),
                    buttons: Array.Empty<LocalizedString>());
            TaskPool.Add(
                "BulkDownloader.PrepareUpdates > GetItemsThatNeedUpdating",
                GetItemsThatNeedUpdating(),
                t =>
                {
                    msgBox.Close();
                    if (!t.TryGetResult(out IReadOnlyList<Steamworks.Ugc.Item> items)) { return; }
                    
                    InitiateDownloads(items);
                });
        }

        internal static void SubscribeToServerMods(IEnumerable<UInt64> missingIds, ConnectCommand rejoinCommand)
        {
            CloseAllMessageBoxes();
            GUIMessageBox msgBox = new GUIMessageBox(headerText: "", text: TextManager.Get("PreparingWorkshopDownloads"),
                buttons: Array.Empty<LocalizedString>());
            TaskPool.Add(
                "BulkDownloader.SubscribeToServerMods > GetItems",
                Task.WhenAll(missingIds.Select(SteamManager.Workshop.GetItem)),
                t =>
                {
                    msgBox.Close();
                    if (!t.TryGetResult(out Steamworks.Ugc.Item?[] itemsNullable)) { return; }

                    var items = itemsNullable
                        .Where(it => it.HasValue)
                        .Select(it => it ?? default)
                        .ToArray();
                    
                    items.ForEach(it => it.Subscribe());
                    InitiateDownloads(items, onComplete: () =>
                    {
                        ContentPackageManager.UpdateContentPackageList();
                        GameMain.Instance.ConnectCommand = Option<ConnectCommand>.Some(rejoinCommand);
                    });
                });
        }

        private static async Task<IReadOnlyList<Steamworks.Ugc.Item>> GetItemsThatNeedUpdating()
        {
            var determiningTasks = ContentPackageManager.WorkshopPackages.Select(async p => (p, await p.IsUpToDate()));
            (ContentPackage Package, bool IsUpToDate)[] outOfDatePackages = await Task.WhenAll(determiningTasks);

            return (await Task.WhenAll(outOfDatePackages.Where(p => !p.IsUpToDate)
                    .Select(p => p.Package.UgcId)
                    .NotNone()
                    .OfType<SteamWorkshopId>()
                    .Select(async id => await SteamManager.Workshop.GetItem(id.Value))))
                .Where(p => p.HasValue).Select(p => p ?? default).ToArray();
        }

        public static void InitiateDownloads(IReadOnlyList<Steamworks.Ugc.Item> itemsToDownload, Action? onComplete = null)
        {
            var msgBox = new GUIMessageBox(TextManager.Get("WorkshopItemDownloading"), "", relativeSize: (0.5f, 0.6f),
                buttons: new LocalizedString[] { TextManager.Get("Cancel") });
            msgBox.Buttons[0].OnClicked = msgBox.Close;
            var modsList = new GUIListBox(new RectTransform((1.0f, 0.8f), msgBox.Content.RectTransform))
            {
                HoverCursor = CursorState.Default
            };
            foreach (var item in itemsToDownload)
            {
                var itemFrame = new GUIFrame(new RectTransform((1.0f, 0.08f), modsList.Content.RectTransform),
                    style: null)
                {
                    CanBeFocused = false
                };
                var itemTitle = new GUITextBlock(new RectTransform(Vector2.One, itemFrame.RectTransform),
                    text: item.Title);
                var itemDownloadProgress
                    = new GUIProgressBar(new RectTransform((0.5f, 0.75f),
                            itemFrame.RectTransform, Anchor.CenterRight), 0.0f)
                    {
                        Color = GUIStyle.Green
                    };
                var itemDownloadProgressUpdater = new GUICustomComponent(
                    new RectTransform(Vector2.Zero, msgBox.Content.RectTransform),
                    onUpdate: (f, component) =>
                    {
                        float progress = 0.0f;
                        if (item.IsDownloading) { progress = item.DownloadAmount; }
                        else if (itemDownloadProgress.BarSize > 0.0f) { progress = 1.0f; }

                        itemDownloadProgress.BarSize = Math.Max(itemDownloadProgress.BarSize,
                            MathHelper.Lerp(itemDownloadProgress.BarSize, progress, 0.1f));
                    });
            }
            TaskPool.Add("DownloadItems", DownloadItems(itemsToDownload, msgBox), _ =>
            {
                if (GUIMessageBox.MessageBoxes.Contains(msgBox))
                {
                    onComplete?.Invoke();
                }
                msgBox.Close();
                ContentPackageManager.WorkshopPackages.Refresh();
                ContentPackageManager.EnabledPackages.RefreshUpdatedMods();
                if (SettingsMenu.Instance?.WorkshopMenu is MutableWorkshopMenu mutableWorkshopMenu)
                {
                    mutableWorkshopMenu.PopulateInstalledModLists(forceRefreshEnabled: true);
                }
            });
        }

        private static async Task DownloadItems(IReadOnlyList<Steamworks.Ugc.Item> itemsToDownload, GUIMessageBox msgBox)
        {
            foreach (var item in itemsToDownload)
            {
                await SteamManager.Workshop.Reinstall(item);
                if (!GUIMessageBox.MessageBoxes.Contains(msgBox)) { break; }
            }
        }
    }
}
