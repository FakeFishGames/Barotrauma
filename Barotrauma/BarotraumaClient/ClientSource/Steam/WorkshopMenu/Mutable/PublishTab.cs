#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Directory = Barotrauma.IO.Directory;
using ItemOrPackage = Barotrauma.Either<Steamworks.Ugc.Item, Barotrauma.ContentPackage>;
using Path = Barotrauma.IO.Path;

namespace Barotrauma.Steam
{
    sealed partial class MutableWorkshopMenu : WorkshopMenu
    {
        private class LocalThumbnail : IDisposable
        {
            public Texture2D? Texture { get; private set; } = null;
            public bool Loading = true;

            public LocalThumbnail(string path)
            {
                TaskPool.Add($"LocalThumbnail {path}",
                    Task.Run(async () =>
                    {
                        await Task.Yield();
                        return TextureLoader.FromFile(path, compress: false, mipmap: false);
                    }),
                    (t) =>
                    {
                        Loading = false;
                        Task<Texture2D?> texTask = (t as Task<Texture2D?>)!;
                        if (disposed)
                        {
                            texTask.Result?.Dispose();
                        }
                        else
                        {
                            Texture = texTask.Result;
                        }
                    });
            }

            private bool disposed = false;
            public void Dispose()
            {
                if (disposed) { return; }

                disposed = true;
                Texture?.Dispose();
            }
        }

        private LocalThumbnail? localThumbnail = null;

        private void CreateLocalThumbnail(string path, GUIFrame thumbnailContainer)
        {
            thumbnailContainer.ClearChildren();
            localThumbnail?.Dispose();
            localThumbnail = new LocalThumbnail(path);
            CreateAsyncThumbnailComponent(thumbnailContainer, () => localThumbnail?.Texture, () => localThumbnail is { Loading: true });
        }

        private static async Task<(int FileCount, int ByteCount)> GetModDirInfo(string dir, GUITextBlock label)
        {
            int fileCount = 0;
            int byteCount = 0;

            var files = Directory.GetFiles(dir, pattern: "*", option: System.IO.SearchOption.AllDirectories);
            foreach (var file in files)
            {
                await Task.Yield();
                fileCount++;
                byteCount += (int)(new Barotrauma.IO.FileInfo(file).Length);
                label.Text = TextManager.GetWithVariables(
                    "ModDirInfo",
                    ("[filecount]", fileCount.ToString(CultureInfo.InvariantCulture)),
                    ("[size]", MathUtils.GetBytesReadable(byteCount)));
            }

            return (fileCount, byteCount);
        }

        private void DeselectPublishedItem()
        {
            var deselectCarrier = selfModsList.Parent.FindChild(c => c.UserData is ActionCarrier { Id: var id } && id == "deselect");
            Action? deselectAction = deselectCarrier.UserData is ActionCarrier { Action: var action }
                ? action
                : null;
            deselectAction?.Invoke();
            SelectTab(Tab.Publish);
        }
        
        private void PopulatePublishTab(ItemOrPackage itemOrPackage, GUIFrame parentFrame)
        {
            ContentPackageManager.LocalPackages.Refresh();
            ContentPackageManager.WorkshopPackages.Refresh();

            parentFrame.ClearChildren();
            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(Vector2.One, parentFrame.RectTransform),
                childAnchor: Anchor.TopCenter);

            Steamworks.Ugc.Item workshopItem = itemOrPackage.TryGet(out Steamworks.Ugc.Item item) ? item : default;
            ContentPackage? localPackage = itemOrPackage.TryGet(out ContentPackage package)
                ? package
                : ContentPackageManager.LocalPackages.FirstOrDefault(p => p.SteamWorkshopId == workshopItem.Id);
            ContentPackage? workshopPackage
                = ContentPackageManager.WorkshopPackages.FirstOrDefault(p => p.SteamWorkshopId == workshopItem.Id);
            if (localPackage is null)
            {
                new GUIFrame(new RectTransform((1.0f, 0.15f), mainLayout.RectTransform), style: null);

                //Local copy does not exist; check for Workshop copy
                bool workshopCopyExists =
                    ContentPackageManager.WorkshopPackages.Any(p => p.SteamWorkshopId == workshopItem.Id);

                new GUITextBlock(new RectTransform((0.7f, 0.4f), mainLayout.RectTransform),
                    TextManager.Get(workshopCopyExists ? "LocalCopyRequired" : "ItemInstallRequired"),
                    wrap: true);

                var buttonLayout = new GUILayoutGroup(new RectTransform((0.6f, 0.1f), mainLayout.RectTransform),
                    isHorizontal: true);
                var yesButton = new GUIButton(new RectTransform((0.5f, 1.0f), buttonLayout.RectTransform),
                    text: TextManager.Get("Yes"))
                {
                    OnClicked = (button, o) =>
                    {
                        CoroutineManager.StartCoroutine(MessageBoxCoroutine((currentStepText, messageBox)
                                => CreateLocalCopy(currentStepText, workshopItem, parentFrame)),
                            $"CreateLocalCopy {workshopItem.Id}");
                        return false;
                    }
                };
                var noButton = new GUIButton(new RectTransform((0.5f, 1.0f), buttonLayout.RectTransform),
                    text: TextManager.Get("No"))
                {
                    OnClicked = (button, o) =>
                    {
                        DeselectPublishedItem();
                        return false;
                    }
                };
            }
            else
            {
                if (!ContentPackageManager.LocalPackages.Contains(localPackage))
                {
                    throw new Exception($"Content package \"{localPackage.Name}\" is not a local package!");
                }

                var selectedTitle =
                    new GUITextBlock(new RectTransform((1.0f, 0.05f), mainLayout.RectTransform), workshopItem.Title ?? localPackage.Name,
                        font: GUIStyle.LargeFont);
                if (workshopItem.Id != 0)
                {
                    var showInSteamButton = CreateShowInSteamButton(workshopItem, new RectTransform((0.2f, 1.0f), selectedTitle.RectTransform, Anchor.CenterRight));
                }
                
                Spacer(mainLayout, height: 0.03f);

                var (leftTop, _, rightTop)
                    = CreateSidebars(mainLayout, leftWidth: 0.2f, centerWidth: 0.01f, rightWidth: 0.79f,
                        height: 0.4f);
                leftTop.Stretch = true;
                rightTop.Stretch = true;

                Label(leftTop, TextManager.Get("WorkshopItemPreviewImage"), GUIStyle.SubHeadingFont);
                string? thumbnailPath = null;
                var thumbnailContainer = CreateThumbnailContainer(leftTop, Vector2.One, ScaleBasis.BothWidth);
                if (workshopItem.Id != 0)
                {
                    CreateItemThumbnail(workshopItem, taskCancelSrc.Token, thumbnailContainer);
                }

                var browseThumbnail =
                    new GUIButton(NewItemRectT(leftTop),
                        TextManager.Get("WorkshopItemBrowse"), style: "GUIButtonSmall")
                    {
                        OnClicked = (button, o) =>
                        {
                            FileSelection.ClearFileTypeFilters();
                            FileSelection.AddFileTypeFilter("PNG", "*.png");
                            FileSelection.AddFileTypeFilter("JPEG", "*.jpg, *.jpeg");
                            FileSelection.AddFileTypeFilter("All files", "*.*");
                            FileSelection.SelectFileTypeFilter("*.png");
                            FileSelection.CurrentDirectory
                                = Path.GetFullPath(Path.GetDirectoryName(localPackage.Path)!);

                            FileSelection.OnFileSelected = (fn) =>
                            {
                                if (new FileInfo(fn).Length > SteamManager.Workshop.MaxThumbnailSize)
                                {
                                    new GUIMessageBox(TextManager.Get("Error"), TextManager.Get("WorkshopItemPreviewImageTooLarge"));
                                    return;
                                }
                                thumbnailPath = fn;
                                CreateLocalThumbnail(thumbnailPath, thumbnailContainer);
                            };

                            FileSelection.Open = true;

                            return false;
                        }
                    };

                Label(rightTop, TextManager.Get("WorkshopItemTitle"), GUIStyle.SubHeadingFont);
                var titleTextBox = new GUITextBox(NewItemRectT(rightTop), workshopItem.Title ?? localPackage.Name);

                Label(rightTop, TextManager.Get("WorkshopItemDescription"), GUIStyle.SubHeadingFont);
                var descriptionTextBox
                    = ScrollableTextBox(rightTop, 6.0f, workshopItem.Description ?? string.Empty);

                var (leftBottom, _, rightBottom)
                    = CreateSidebars(mainLayout, leftWidth: 0.49f, centerWidth: 0.01f, rightWidth: 0.5f, height: 0.5f);
                leftBottom.Stretch = true;
                rightBottom.Stretch = true;
                
                Label(leftBottom, TextManager.Get("WorkshopItemVersion"), GUIStyle.SubHeadingFont);
                var modVersion = localPackage.ModVersion;
                if (workshopPackage is { ModVersion: { } workshopVersion } &&
                    modVersion.Equals(workshopVersion, StringComparison.OrdinalIgnoreCase))
                {
                    modVersion = ModProject.IncrementModVersion(modVersion);
                }

                char[] forbiddenVersionCharacters = { ';', '=' };
                var versionTextBox = new GUITextBox(NewItemRectT(leftBottom), modVersion);
                versionTextBox.OnTextChanged += (box, text) =>
                {
                    if (text.Any(c => forbiddenVersionCharacters.Contains(c)))
                    {
                        foreach (var c in forbiddenVersionCharacters)
                        {
                            text = text.Replace($"{c}", "");
                        }

                        box.Text = text;
                        box.Flash(GUIStyle.Red);
                    }

                    return true;
                };

                Label(leftBottom, TextManager.Get("WorkshopItemChangeNote"), GUIStyle.SubHeadingFont);
                var changeNoteTextBox = ScrollableTextBox(leftBottom, 5.0f, "");

                Label(rightBottom, TextManager.Get("WorkshopItemTags"), GUIStyle.SubHeadingFont);
                var tagsList = CreateTagsList(SteamManager.Workshop.Tags, NewItemRectT(rightBottom, heightScale: 4.0f),
                    canBeFocused: true);
                Dictionary<Identifier, GUIButton> tagButtons = tagsList.Content.Children.Cast<GUIButton>()
                    .Select(b => ((Identifier)b.UserData, b)).ToDictionary();
                if (workshopItem.Tags != null)
                {
                    foreach (Identifier tag in workshopItem.Tags.ToIdentifiers())
                    {
                        if (tagButtons.TryGetValue(tag, out var button)) { button.Selected = true; }
                    }
                }

                GUILayoutGroup visibilityLayout = new GUILayoutGroup(NewItemRectT(rightBottom), isHorizontal: true);

                var visibilityLabel = Label(visibilityLayout, TextManager.Get("WorkshopItemVisibility"), GUIStyle.SubHeadingFont);
                visibilityLabel.RectTransform.RelativeSize = (0.6f, 1.0f);
                visibilityLabel.TextAlignment = Alignment.CenterRight;
                
                Steamworks.Ugc.Visibility visibility = workshopItem.Visibility;
                var visibilityDropdown = DropdownEnum(
                    visibilityLayout,
                    (v) => TextManager.Get($"WorkshopItemVisibility.{v}"),
                    visibility,
                    (v) => visibility = v);
                visibilityDropdown.RectTransform.RelativeSize = (0.4f, 1.0f);

                var fileInfoLabel = Label(rightBottom, "", GUIStyle.Font, heightScale: 1.0f);
                fileInfoLabel.TextAlignment = Alignment.CenterRight;
                TaskPool.Add($"FileInfoLabel{workshopItem.Id}", GetModDirInfo(localPackage.Dir, fileInfoLabel), t => { });

                GUILayoutGroup buttonLayout = new GUILayoutGroup(NewItemRectT(rightBottom), isHorizontal: true, childAnchor: Anchor.CenterRight);

                RectTransform newButtonRectT()
                    => new RectTransform((0.4f, 1.0f), buttonLayout.RectTransform);

                var publishItemButton = new GUIButton(newButtonRectT(), TextManager.Get(workshopItem.Id != 0 ? "WorkshopItemUpdate" : "WorkshopItemPublish"))
                {
                    OnClicked = (button, o) =>
                    {
                        //Reload the package to force hash recalculation
                        string packageName = localPackage.Name;
                        localPackage = ContentPackageManager.ReloadContentPackage(localPackage);
                        if (localPackage is null)
                        {
                            throw new Exception($"\"{packageName}\" was removed upon reload");
                        }

                        //Set up the Ugc.Editor object that we'll need to publish
                        Steamworks.Ugc.Editor ugcEditor =
                            workshopItem.Id == 0
                                ? Steamworks.Ugc.Editor.NewCommunityFile
                                : new Steamworks.Ugc.Editor(workshopItem.Id);
                        ugcEditor = ugcEditor.WithTitle(titleTextBox.Text)
                            .WithDescription(descriptionTextBox.Text)
                            .WithTags(tagButtons.Where(kvp => kvp.Value.Selected).Select(kvp => kvp.Key.Value))
                            .WithChangeLog(changeNoteTextBox.Text)
                            .WithMetaData($"gameversion={localPackage.GameVersion};modversion={versionTextBox.Text}")
                            .WithVisibility(visibility)
                            .WithPreviewFile(thumbnailPath);

                        CoroutineManager.StartCoroutine(
                            MessageBoxCoroutine((currentStepText, messageBox)
                                => PublishItem(currentStepText, messageBox, versionTextBox.Text, ugcEditor, localPackage)));

                        return false;
                    }
                };

                if (workshopItem.Id != 0)
                {
                    var deleteItemButton = new GUIButton(newButtonRectT(), TextManager.Get("WorkshopItemDelete"), color: GUIStyle.Red)
                    {
                        OnClicked = (button, o) =>
                        {
                            var confirmDeletion = new GUIMessageBox(
                                headerText: TextManager.Get("WorkshopItemDelete"),
                                text: TextManager.GetWithVariable("WorkshopItemDeleteVerification", "[itemname]", workshopItem.Title!),
                                buttons: new[] { TextManager.Get("Yes"), TextManager.Get("No") });
                            confirmDeletion.Buttons[0].OnClicked = (yesBuffer, o1) =>
                            {
                                TaskPool.Add($"Delete{workshopItem.Id}", Steamworks.SteamUGC.DeleteFileAsync(workshopItem.Id),
                                    t =>
                                    {
                                        SteamManager.Workshop.Uninstall(workshopItem);
                                        confirmDeletion.Close();
                                        DeselectPublishedItem();
                                    });
                                return false;
                            };
                            confirmDeletion.Buttons[1].OnClicked = (noButton, o1) =>
                            {
                                confirmDeletion.Close();
                                return false;
                            };
                        
                            return false;
                        },
                        HoverColor = Color.Lerp(GUIStyle.Red, Color.White, 0.3f),
                        PressedColor = Color.Lerp(GUIStyle.Red, Color.Black, 0.3f),
                    };
                    deleteItemButton.TextBlock.TextColor = Color.Black;
                    deleteItemButton.TextBlock.HoverTextColor = Color.Black;
                }
            }
        }

        private IEnumerable<CoroutineStatus> MessageBoxCoroutine(Func<GUITextBlock, GUIMessageBox, IEnumerable<CoroutineStatus>> subcoroutine)
        {
            var messageBox = new GUIMessageBox("", "...", buttons: new [] { TextManager.Get("Cancel") });
            messageBox.Buttons[0].OnClicked = (button, o) =>
            {
                messageBox.Close();
                return false;
            };

            var coroutineEval = subcoroutine(messageBox.Text, messageBox).GetEnumerator();
            while (true)
            {
                var status = coroutineEval.Current;
                if (messageBox.Closed)
                {
                    yield return CoroutineStatus.Success;
                    yield break;
                }
                else if (status == CoroutineStatus.Failure || status == CoroutineStatus.Success)
                {
                    messageBox.Close();
                    yield return status;
                    yield break;
                }
                else
                {
                    yield return status;
                }
                bool moveNext = true;
                try
                {
                    moveNext = coroutineEval.MoveNext();
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"{e.Message} {e.StackTrace.CleanupStackTrace()}");
                    messageBox.Close();
                }
                if (!moveNext)
                {
                    messageBox.Close();
                }
            }
        }
        
        private IEnumerable<CoroutineStatus> CreateLocalCopy(GUITextBlock currentStepText, Steamworks.Ugc.Item workshopItem, GUIFrame parentFrame)
        {
            ContentPackage? workshopCopy =
                ContentPackageManager.WorkshopPackages.FirstOrDefault(p => p.SteamWorkshopId == workshopItem.Id);
            if (workshopCopy is null)
            {
                if (!SteamManager.Workshop.CanBeInstalled(workshopItem))
                {
                    SteamManager.Workshop.NukeDownload(workshopItem);
                }
                SteamManager.Workshop.DownloadModThenEnqueueInstall(workshopItem);
                TaskPool.Add($"Install {workshopItem.Title}",
                    SteamManager.Workshop.WaitForInstall(workshopItem),
                    (t) =>
                    {
                        ContentPackageManager.WorkshopPackages.Refresh();
                    });
                while (!ContentPackageManager.WorkshopPackages.Any(p => p.SteamWorkshopId == workshopItem.Id))
                {
                    currentStepText.Text = SteamManager.Workshop.CanBeInstalled(workshopItem)
                        ? TextManager.Get("PublishPopupInstall")
                        : TextManager.GetWithVariable("PublishPopupDownload", "[percentage]", Percentage(workshopItem.DownloadAmount));
                    yield return new WaitForSeconds(0.5f);
                }

                workshopCopy =
                    ContentPackageManager.WorkshopPackages.First(p => p.SteamWorkshopId == workshopItem.Id);
            }

            bool localCopyMade = false;
            TaskPool.Add($"Create local copy {workshopItem.Title}",
                SteamManager.Workshop.CreateLocalCopy(workshopCopy),
                (t) =>
                {
                    ContentPackageManager.LocalPackages.Refresh();
                    localCopyMade = true;
                });
            while (!localCopyMade)
            {
                currentStepText.Text = TextManager.Get("PublishPopupCreateLocal");
                yield return new WaitForSeconds(0.5f);
            }
            PopulatePublishTab(workshopItem, parentFrame);

            yield return CoroutineStatus.Success;
        }
        
        private IEnumerable<CoroutineStatus> PublishItem(
            GUITextBlock currentStepText, GUIMessageBox messageBox,
            string modVersion, Steamworks.Ugc.Editor editor, ContentPackage localPackage)
        {
            if (!SteamManager.IsInitialized)
            {
                yield return CoroutineStatus.Failure;
            }

            bool stagingReady = false;
            Exception? stagingException = null;
            TaskPool.Add("CreatePublishStagingCopy",
                SteamManager.Workshop.CreatePublishStagingCopy(modVersion, localPackage),
                (t) =>
                {
                    stagingReady = true;
                    stagingException = t.Exception?.GetInnermost();
                });
            currentStepText.Text = TextManager.Get("PublishPopupStaging");
            while (!stagingReady) { yield return new WaitForSeconds(0.5f); }

            if (stagingException != null)
            {
                throw new Exception($"Failed to create staging copy: {stagingException.Message} {stagingException.StackTrace.CleanupStackTrace()}");
            }
            
            editor = editor
                .WithContent(SteamManager.Workshop.PublishStagingDir)
                .ForAppId(SteamManager.AppID);

            messageBox.Buttons[0].Enabled = false;
            Steamworks.Ugc.PublishResult? result = null;
            Exception? resultException = null;
            TaskPool.Add($"Publishing {localPackage.Name} ({localPackage.SteamWorkshopId})",
                editor.SubmitAsync(),
                t =>
                {
                    if (t.TryGetResult(out Steamworks.Ugc.PublishResult publishResult))
                    {
                        result = publishResult;
                    }
                    resultException = t.Exception?.GetInnermost();
                });
            currentStepText.Text = TextManager.Get("PublishPopupSubmit");
            while (!result.HasValue && resultException is null) { yield return new WaitForSeconds(0.5f); }

            if (result is { Success: true })
            {
                var resultId = result.Value.FileId;
                Steamworks.Ugc.Item resultItem = new Steamworks.Ugc.Item(resultId);
                Task downloadTask = SteamManager.Workshop.ForceRedownload(resultItem);
                while (!resultItem.IsInstalled && !downloadTask.IsCompleted)
                {
                    currentStepText.Text = TextManager.GetWithVariable("PublishPopupDownload", "[percentage]", Percentage(resultItem.DownloadAmount));
                    yield return new WaitForSeconds(0.5f);
                }

                if (!resultItem.IsInstalled)
                {
                    throw new Exception($"Failed to install item: download task ended with status {downloadTask.Status}, " +
                                        $"exception was {downloadTask.Exception?.GetInnermost()?.ToString().CleanupStackTrace() ?? "[NULL]"}");
                }

                ContentPackage? pkgToNuke
                    = ContentPackageManager.WorkshopPackages.FirstOrDefault(p => p.SteamWorkshopId == resultId);
                if (pkgToNuke != null)
                {
                    Directory.Delete(pkgToNuke.Dir, recursive: true);
                    ContentPackageManager.WorkshopPackages.Refresh();
                }

                bool installed = false;
                TaskPool.Add(
                    "InstallNewlyPublished",
                    SteamManager.Workshop.WaitForInstall(resultItem),
                    (t) =>
                    {
                        installed = true;
                    });
                while (!installed)
                {
                    currentStepText.Text = TextManager.Get("PublishPopupInstall");
                    yield return new WaitForSeconds(0.5f);
                }

                ContentPackageManager.WorkshopPackages.Refresh();
                ContentPackageManager.EnabledPackages.RefreshUpdatedMods();
                
                var localModProject = new ModProject(localPackage)
                {
                    SteamWorkshopId = resultId
                };
                localModProject.DiscardHashAndInstallTime();
                localModProject.Save(localPackage.Path);
                ContentPackageManager.ReloadContentPackage(localPackage);
                DeselectPublishedItem();

                if (result.Value.NeedsWorkshopAgreement)
                {
                    SteamManager.OverlayCustomUrl(resultItem.Url);
                }
                new GUIMessageBox(string.Empty, TextManager.GetWithVariable("workshopitempublished", "[itemname]", localPackage.Name));
            }
            else if (resultException != null)
            {
                throw new Exception($"Failed to publish item: {resultException.Message} {resultException.StackTrace.CleanupStackTrace()}");
            }
            else
            {
                new GUIMessageBox(TextManager.Get("error"), TextManager.GetWithVariable("workshopitempublishfailed", "[itemname]", localPackage.Name));
            }

            SteamManager.Workshop.DeletePublishStagingCopy();
            messageBox.Close();
        }
    }
}
