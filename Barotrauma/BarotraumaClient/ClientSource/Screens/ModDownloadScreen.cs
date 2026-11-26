#nullable enable
using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Color = Microsoft.Xna.Framework.Color;
using ServerContentPackage = Barotrauma.Networking.ServerContentPackage;

namespace Barotrauma
{
    public class ModDownloadScreen : Screen
    {
        private readonly Queue<ServerContentPackage> pendingDownloads =
            new Queue<ServerContentPackage>();
        private ServerContentPackage? currentDownload;

        private readonly List<ContentPackage> downloadedPackages = new List<ContentPackage>();
        public IEnumerable<ContentPackage> DownloadedPackages => downloadedPackages;

        private bool confirmDownload;
        private bool waitingToEnablePackages = true;

        public void Reset()
        {
            pendingDownloads.Clear();
            downloadedPackages.Clear();
            currentDownload = null;
            confirmDownload = false;
            waitingToEnablePackages = true;
        }

        private void DeletePrevDownloads()
        {
            if (Directory.Exists(ModReceiver.DownloadFolder))
            {
                Directory.Delete(ModReceiver.DownloadFolder, recursive: true);
            }
        }
        
        [DoesNotReturn]
        private static void LogAndThrowException(string errorMsg, string analyticsId)
        {
            GameAnalyticsManager.AddErrorEventOnce(analyticsId, GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
            throw new InvalidOperationException(errorMsg);
        }

        public override void Select()
        {
            base.Select();
            DeletePrevDownloads();
            Reset();

            bool allowDownloads = GameMain.Client.ClientPeer is { AllowModDownloads: true };

            Frame.ClearChildren();

            var mainVisibleFrame = new GUIFrame(new RectTransform((0.6f, 0.8f), Frame.RectTransform, Anchor.Center));
            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(Vector2.One * 0.93f, mainVisibleFrame.RectTransform, Anchor.Center));

            void mainLayoutSpacing()
                => new GUIFrame(new RectTransform((1.0f, 0.02f), mainLayout.RectTransform), style: null);
            
            var serverName = new GUITextBlock(new RectTransform((1.0f, 0.08f), mainLayout.RectTransform),
                "", font: GUIStyle.LargeFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextGetter = () => GameMain.Client?.ServerName ?? "Not connected"
            };
            mainLayoutSpacing();
            var downloadList = new GUIListBox(new RectTransform((1.0f, 0.76f), mainLayout.RectTransform));
            mainLayoutSpacing();
            var disconnectButton = new GUIButton(new RectTransform((0.3f, 0.1f), mainLayout.RectTransform),
                TextManager.Get("Disconnect"))
            {
                OnClicked = (guiButton, o) =>
                {
                    DisconnectFromServer();
                    return false;
                }
            };

            if (!GameMain.Client.IsServerOwner && GameMain.Client.ClientPeer.ServerContentPackages.Length == 0)
            {
                LogAndThrowException("Error in ModDownloadScreen: the list of mods the server has enabled was empty. "
                      +$"Content package list received: {GameMain.Client.ClientPeer.ContentPackageOrderReceived}",
                    analyticsId: "ModDownloadScreen.Select:NoContentPackages");
            }

            var missingPackages = GameMain.Client.ClientPeer.ServerContentPackages
                .Where(sp => sp.ContentPackage is null).ToArray();
            if (!missingPackages.Any(p => p.IsMandatory))
            {
                if (GameMain.Client.IsServerOwner)
                {
                    GameMain.NetLobbyScreen.Select();
                }
                else
                {
                    var corePackage = GameMain.Client.ClientPeer.ServerContentPackages
                        .Select(p => p.CorePackage)
                        .OfType<CorePackage>().FirstOrDefault();
                    if (corePackage is null)
                    {
                        LogAndThrowException($"Error in ModDownloadScreen: no core packages in the list of mods the server has enabled. " +
                                       $"Content package list received: {GameMain.Client.ClientPeer.ContentPackageOrderReceived}",
                            analyticsId: "ModDownloadScreen.Select:NoCorePackage");
                    }

                    List<RegularPackage> regularPackages =
                        GameMain.Client.ClientPeer.ServerContentPackages
                        .Select(p => p.RegularPackage)
                        .OfType<RegularPackage>().ToList();

                    List<ContentPackage> newContentPackagesWithWarnings = regularPackages
                        .OfType<ContentPackage>()
                        .Concat([corePackage])
                        .Except(ContentPackageManager.EnabledPackages.All)
                        .Where(static p => p.FilesThatShouldPromptWarning.Any())
                        .ToList();

                    if (newContentPackagesWithWarnings.Count == 0)
                    {
                        enableAndSelect();
                    }
                    else
                    {
                        MutableWorkshopMenu.PromptWarnings(newContentPackagesWithWarnings, enableAndSelect, DisconnectFromServer);
                    }

                    void enableAndSelect()
                    {
                        waitingToEnablePackages = false;
                        ContentPackageManager.EnabledPackages.BackUp();
                        ContentPackageManager.EnabledPackages.SetCore(corePackage);
                        //keep enabled client-side-only mods enabled
                        regularPackages.AddRange(ContentPackageManager.EnabledPackages.Regular.Where(p => !p.HasMultiplayerSyncedContent && !regularPackages.Contains(p)));
                        ContentPackageManager.EnabledPackages.SetRegular(regularPackages);

                        RefreshAndSelectLobby();
                    }
                }
                return;
            }

            if (missingPackages.FirstOrDefault(p => p.IsVanilla) is { } mismatchedVanilla)
            {
                LogAndThrowException("Error in ModDownloadScreen: mismatched Vanilla package: "
                                     +$"local hash is {ContentPackageManager.VanillaCorePackage?.Hash.StringRepresentation ?? "[NULL]"}, "
                                     +$"remote hash is {mismatchedVanilla.Hash.StringRepresentation}. "
                                     +$"Content package list received: {GameMain.Client.ClientPeer.ContentPackageOrderReceived}",
                    analyticsId: "ModDownloadScreen.Select:MismatchedVanilla");
            }

            GUIMessageBox msgBox = new GUIMessageBox(
                TextManager.Get("ModDownloadTitle"),
                "",
                Array.Empty<LocalizedString>(),
                relativeSize: (0.57f, 0.75f));

            GUILayoutGroup innerLayout = msgBox.Content;
            innerLayout.Stretch = true;

            void innerLayoutSpacing(float height)
                => new GUIFrame(new RectTransform((1.0f, height), innerLayout.RectTransform), style: null);

            GUITextBlock textBlock(LocalizedString str, GUIFont font, Alignment alignment = Alignment.CenterLeft)
            {
                var tb = new GUITextBlock(new RectTransform(Point.Zero, innerLayout.RectTransform), str,
                    wrap: true, textAlignment: alignment, font: font);
                new GUICustomComponent(new RectTransform(Vector2.Zero, tb.RectTransform), onUpdate:
                    (deltaTime, component) =>
                    {
                        if (tb.RectTransform.NonScaledSize.X != innerLayout.Rect.Width)
                        {
                            tb.RectTransform.NonScaledSize = (innerLayout.Rect.Width, 0);
                            tb.RectTransform.NonScaledSize = (innerLayout.Rect.Width,
                                (int)tb.Font.MeasureString(tb.WrappedText).Y);
                        }
                    });
                return tb;
            }

            var header = textBlock(TextManager.Get("ModDownload.Header"), GUIStyle.Font, Alignment.Center);
            innerLayoutSpacing(0.05f);

            var topColumnButtons = new GUILayoutGroup(new RectTransform((1f, 0.05f), innerLayout.RectTransform), isHorizontal: true);
            new GUIButton(new RectTransform((0.35f, 1f), topColumnButtons.RectTransform), TextManager.Get("name"), style: "GUIButtonSmall") { CanBeFocused = false, CanBeSelected = false };
            new GUIButton(new RectTransform((0.3f, 1f), topColumnButtons.RectTransform), TextManager.Get("infobutton"), style: "GUIButtonSmall") { CanBeFocused = false, CanBeSelected = false };
            new GUIButton(new RectTransform((0.1f, 1f), topColumnButtons.RectTransform), TextManager.Get("ModDownload.Server"), style: "GUIButtonSmall") { CanBeFocused = false, CanBeSelected = false };
            new GUIButton(new RectTransform((0.1f, 1f), topColumnButtons.RectTransform), TextManager.Get("ModDownload.Client"), style: "GUIButtonSmall") { CanBeFocused = false, CanBeSelected = false };
            new GUIButton(new RectTransform((0.15f, 1f), topColumnButtons.RectTransform), "", style: "GUIButtonSmall") { CanBeFocused = false, CanBeSelected = false };

            var msgBoxModList = new GUIListBox(new RectTransform(Vector2.One, innerLayout.RectTransform));
            
            innerLayoutSpacing(0.05f);
            var footer = textBlock(TextManager.Get(allowDownloads ? "ModDownload.Footer" : "ModDownloadFooterFail"), GUIStyle.Font, Alignment.Center);
            
            innerLayoutSpacing(0.05f);
            GUILayoutGroup buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), innerLayout.RectTransform), isHorizontal: true);

            void buttonContainerSpacing(float width)
                => new GUIFrame(new RectTransform((width, 1.0f), buttonContainer.RectTransform), style: null);

            GUIButton button(LocalizedString text, Action action, float width = 0.3f)
                => new GUIButton(new RectTransform((width, 1.0f), buttonContainer.RectTransform), text)
                {
                    OnClicked = (_, __) =>
                    {
                        action();
                        msgBox.Close();
                        return false;
                    }
                };

            if (allowDownloads)
            {
                buttonContainerSpacing(0.1f);
                var yesButton = button(TextManager.Get("ModDownload.DownloadFromServer"), () => confirmDownload = true);
                yesButton.Enabled = !missingPackages.Any(static p => !p.CanBeDownloaded);
                if (!yesButton.Enabled)
                {
                    yesButton.ToolTip = TextManager.Get("ModDownload.DownloadFromServer.Disabled.ToolTip");
                }
                buttonContainerSpacing(0.2f);
                button(TextManager.Get("Cancel"), () =>
                {
                    DisconnectFromServer();
                });
                buttonContainerSpacing(0.1f);
            }
            else
            {
                buttonContainerSpacing(0.15f);
                button(TextManager.Get("Cancel"), () =>
                {
                    GameMain.Client?.Quit();
                    GameMain.MainMenuScreen.Select();
                }, width: 0.7f);
                buttonContainerSpacing(0.15f);
            }

            List<ContentPackageId> missingIds = new List<ContentPackageId>();

            foreach (var p in missingPackages.Where(static p => p.IsMandatory).OrderBy(static p => p.CanBeDownloaded))
            {
                pendingDownloads.Enqueue(p);

                Option<ContentPackageId> parsedId = p.UgcId != null ? ContentPackageId.Parse(p.UgcId) : Option<ContentPackageId>.None();
                parsedId.TryUnwrap(out ContentPackageId? contentPackageId);

                ContentPackage? matchingWorkshopPackage = parsedId.TryUnwrap(out ContentPackageId? id) 
                    ? ContentPackageManager.WorkshopPackages.FirstOrDefault(wp => wp.UgcId.Equals(id)) : null;

                DlcInfo? dlcInfo = DlcInfo.RetrieveDLCsFromCatalog().FirstOrNull(dlcInfo => dlcInfo.AppId.ToString() == p.SteamAppId);

                #region MessageBox
                var messageBoxLayout = new GUILayoutGroup(new RectTransform((1.0f, 0.1f), msgBoxModList.Content.RectTransform), isHorizontal: true)
                {
                    CanBeFocused = false
                };

                var modNameTextBlock = new GUITextBlock(new RectTransform((0.35f, 1f), messageBoxLayout.RectTransform), p.Name);

                if (!p.CanBeDownloaded && p.SteamAppId.IsNullOrEmpty())
                {
                    modNameTextBlock.TextColor = GUIStyle.Red;
                    modNameTextBlock.ToolTip = TextManager.Get("ModDownload.NotAllowedToBeDownloaded");

                    var modNameIcon = new GUIImage(new RectTransform(new Point((int)(modNameTextBlock.Rect.Height * 0.5f)), modNameTextBlock.RectTransform, Anchor.CenterLeft), style: "GUIButtonWarningRed")
                    {
                        CanBeFocused = false
                    };

                    modNameTextBlock.TextOffset = new Vector2(modNameIcon.Rect.Width, 0);
                }
                else if (dlcInfo != null)
                {
                    modNameTextBlock.TextColor = new Color(239, 115, 15);
                }

                var infoTextBlock = new GUITextBlock(new RectTransform((0.3f, 1f), messageBoxLayout.RectTransform), "")
                {
                    ToolTip = ""
                };
                var infoTextIcon = new GUIImage(new RectTransform(new Point((int)(infoTextBlock.Rect.Height * 0.6f)), infoTextBlock.RectTransform, Anchor.CenterLeft), style: "GUIButtonInfo")
                {
                    CanBeFocused = false
                };
                infoTextBlock.TextOffset = new Vector2(infoTextIcon.Rect.Width, 0);

                if (dlcInfo != null)
                {
                    infoTextBlock.Text = TextManager.Get("ModDownload.Dlc");
                    infoTextBlock.ToolTip = TextManager.Get("ModDownload.Dlc.Tooltip");
                    infoTextBlock.TextColor = GUIStyle.Blue;
                }
                else if (contentPackageId != null)
                {
                    if (matchingWorkshopPackage == null)
                    {
                        missingIds.Add(contentPackageId);
                        infoTextBlock.Text = TextManager.Get("ModDownload.AvailableOnWorkshop");
                        infoTextBlock.ToolTip = TextManager.Get("ModDownload.AvailableOnWorkshop.Tooltip");
                        infoTextBlock.TextColor = GUIStyle.Blue;
                    }
                    else if (Version.TryParse(matchingWorkshopPackage.ModVersion, out Version? localVersion) &&
                        Version.TryParse(p.Version, out Version? serverVersion))
                    {
                        if (serverVersion > localVersion)
                        {
                            missingIds.Add(contentPackageId);
                            infoTextBlock.Text = TextManager.Get("ModDownload.WorkshopUpdateAvailable");
                            infoTextBlock.ToolTip = TextManager.Get("ModDownload.WorkshopUpdateAvailable.Tooltip");
                            infoTextBlock.TextColor = GUIStyle.Blue;
                        }
                        else if (serverVersion == localVersion)
                        {
                            infoTextBlock.Text = TextManager.Get("ModDownload.HashMismatch");
                            infoTextBlock.ToolTip = RichString.Rich(TextManager.Get("ModDownload.HashMismatch.Tooltip"));
                            infoTextBlock.TextColor = GUIStyle.Yellow;
                        }
                        else
                        {
                            infoTextBlock.Text = TextManager.Get("ModDownload.ServerOutdated");
                            infoTextBlock.ToolTip = TextManager.Get("ModDownload.ServerOutdated.Tooltip");
                            infoTextBlock.TextColor = GUIStyle.Yellow;
                        }
                    }
                }
                else
                {
                    infoTextBlock.Text = TextManager.Get("ModDownload.NotOnWorkshop");
                    infoTextBlock.ToolTip = TextManager.Get("ModDownload.NotOnWorkshop.Tooltip");
                    infoTextBlock.TextColor = GUIStyle.Yellow;
                }

                new GUITextBlock(new RectTransform((0.1f, 1f), messageBoxLayout.RectTransform), p.Version, textAlignment: Alignment.Center)
                {
                    ToolTip = TextManager.Get("ModDownload.ServerVersion")
                };

                new GUITextBlock(new RectTransform((0.1f, 1f), messageBoxLayout.RectTransform), matchingWorkshopPackage?.ModVersion ?? "0.0.0", textAlignment: Alignment.Center)
                {
                    ToolTip = TextManager.Get("ModDownload.ClientVersion"),
                    Enabled = matchingWorkshopPackage != null
                };

                var workshopButton = new GUIButton(new RectTransform((0.15f, 1f), messageBoxLayout.RectTransform), TextManager.Get("ModDownload.OpenOnWorkshopButton"))
                {
                    Enabled = false
                };

                if (dlcInfo != null)
                {
                    workshopButton.Text = TextManager.Get("ModDownload.OpenSteamPage");
                    workshopButton.Enabled = true;

                    workshopButton.OnClicked = (GUIButton button, object obj) =>
                    {
                        dlcInfo.Value.OpenStorePage();
                        return true;
                    };
                }
                else if (contentPackageId != null)
                {
                    workshopButton.Enabled = true;
                    workshopButton.OnClicked = (GUIButton button, object obj) =>
                    {
                        SteamManager.OverlayCustomUrl(SteamWorkshopId.ToWorkshopURL(contentPackageId.ToString()));
                        return true;
                    };
                }

                #endregion

                #region DownloadProgress
                var downloadFrame = new GUIFrame(new RectTransform((1.0f, 0.06f), downloadList.Content.RectTransform),
                    style: "ListBoxElement")
                {
                    UserData = p,
                    CanBeFocused = false
                };
                new GUITextBlock(new RectTransform((0.5f, 1.0f), downloadFrame.RectTransform), p.Name)
                {
                    CanBeFocused = false
                };
                var downloadProgress = new GUIProgressBar(
                    new RectTransform((0.5f, 0.75f), downloadFrame.RectTransform, Anchor.CenterRight),
                    0.0f, color: GUIStyle.Green);
                downloadProgress.ProgressGetter = () =>
                {
                    if (currentDownload == p)
                    {
                        FileReceiver.FileTransferIn? getTransfer() => GameMain.Client?.FileReceiver.ActiveTransfers.FirstOrDefault(t => t.FileType == FileTransferType.Mod);
                        
                        if (downloadProgress.GetAnyChild<GUITextBlock>() is null)
                        {
                            GUILayoutGroup progressBarLayout
                                = new GUILayoutGroup(new RectTransform(Vector2.One, downloadProgress.RectTransform), isHorizontal: true);

                            void progressBarText(float width, Alignment textAlignment, Func<string> getter)
                            {
                                var textContainer = new GUIFrame(new RectTransform((width, 1.0f), progressBarLayout.RectTransform),
                                    style: null);
                                var textShadow = new GUITextBlock(new RectTransform(Vector2.One, textContainer.RectTransform) { AbsoluteOffset = new Point(GUI.IntScale(3)) }, "",
                                    textColor: Color.Black, textAlignment: textAlignment);
                                var text = new GUITextBlock(new RectTransform(Vector2.One, textContainer.RectTransform), "",
                                    textAlignment: textAlignment);
                                new GUICustomComponent(new RectTransform(Vector2.Zero, textContainer.RectTransform), onUpdate:
                                    (f, component) =>
                                    {
                                        string str = getter();
                                        if (text.Text?.SanitizedValue != str)
                                        {
                                            text.Text = str;
                                            textShadow.Text = str;
                                        }
                                    });
                            }
                            progressBarText(0.475f, Alignment.CenterRight, () => MathUtils.GetBytesReadable(getTransfer()?.Received ?? 0));
                            progressBarText(0.05f, Alignment.Center, () => "/");
                            progressBarText(0.475f, Alignment.CenterLeft, () => MathUtils.GetBytesReadable(getTransfer()?.FileSize ?? 0));
                        }
                        
                        return getTransfer()?.Progress ?? 0.0f;
                    }

                    if (!pendingDownloads.Contains(p))
                    {
                        downloadProgress.GetAllChildren<GUITextBlock>().ToArray().ForEach(c => downloadProgress.RemoveChild(c));
                        return 1.0f;
                    }

                    return 0.0f;
                };
                #endregion
            }

            if (missingIds.Any() && SteamManager.IsInitialized)
            {
                buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), innerLayout.RectTransform), isHorizontal: true);
                buttonContainerSpacing(0.15f);
                button(TextManager.Get("ModDownload.DownloadFromWorkshop"), () =>
                {
                    if (GameMain.Client != null)
                    {
                        BulkDownloader.SubscribeToServerMods(missingIds.OfType<SteamWorkshopId>().Select(id => id.Value),
                            new ConnectCommand(
                                serverName: GameMain.Client.ServerName,
                                endpoint: GameMain.Client.ClientPeer.ServerEndpoint));
                    }
                    DisconnectFromServer();
                }, width: 0.7f);
                buttonContainerSpacing(0.15f);
            }
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            if (GameMain.Client is null) { return; }
            if (!confirmDownload) { return; }
            if (currentDownload is null)
            {
                if (pendingDownloads.TryDequeue(out currentDownload))
                {
                    GameMain.Client.RequestFile(FileTransferType.Mod, currentDownload.Name, currentDownload.Hash.StringRepresentation);
                }
                else if (waitingToEnablePackages)
                {
                    waitingToEnablePackages = false;

                    var serverPackages = GameMain.Client.ClientPeer.ServerContentPackages;
                    CorePackage corePackage
                        = downloadedPackages.FirstOrDefault(p => p is CorePackage) as CorePackage
                          ?? serverPackages.FirstOrDefault(p => p.CorePackage != null)?.CorePackage
                          ?? throw new Exception($"Failed to find core package to enable");

                    List<RegularPackage> regularPackages = new List<RegularPackage>();
                    foreach (var p in serverPackages)
                    {
                        if (p.CorePackage != null)
                        {
                            // This package is one of our installed core packages
                            continue;
                        }

                        if (corePackage.Hash.Equals(p.Hash))
                        {
                            // This package is the core package we downloaded from the server
                            continue;
                        }
                        RegularPackage? matchingPackage =
                            p.RegularPackage ?? downloadedPackages.FirstOrDefault(d => d is RegularPackage && d.Hash.Equals(p.Hash)) as RegularPackage;
                        if (matchingPackage is null)
                        {
                            if (!p.IsMandatory)
                            {
                                //we don't need to care about missing non-mandatory (= submarine) mods
                                continue;
                            }
                            else
                            {
                                throw new Exception($"Could not find regular package \"{p.Name}\"");
                            }
                        }
                        regularPackages.Add(matchingPackage);
                    }

                    // Check if the server didn't sneak in content packages that are not allowed to be downloaded directly
                    foreach (var downloadedPackage in downloadedPackages)
                    {
                        if (downloadedPackage.AllFiles.Any(static f => !f.CanBeDownloadedFromServer))
                        {
                            DebugConsole.ThrowError(
                                $"The downloaded content package \"{downloadedPackage.Name}\" contains files that are not permitted to be downloaded directly from the " +
                                $"server (e.g. plugin files). Under normal circumstances, the server should never send such packages. This may indicate that the server " +
                                $"you attempted to connect to is malicious.");

                            DisconnectFromServer();
                            return;
                        }
                    }

                    foreach (var regularPackage in regularPackages)
                    {
                        DebugConsole.NewMessage($"Enabling \"{regularPackage.Name}\" ({regularPackage.Dir})", Color.Lime);
                    }

                    //keep enabled client-side-only mods enabled
                    regularPackages.AddRange(ContentPackageManager.EnabledPackages.Regular.Where(p => !p.HasMultiplayerSyncedContent && !regularPackages.Contains(p)));

                    List<ContentPackage> newContentPackagesWithWarnings = regularPackages
                        .OfType<ContentPackage>()
                        .Concat([corePackage])
                        .Except(ContentPackageManager.EnabledPackages.All)
                        .Where(static p => p.FilesThatShouldPromptWarning.Any())
                        .ToList();

                    if (newContentPackagesWithWarnings.Count == 0)
                    {
                        enableAndSelect();
                    }
                    else
                    {
                        MutableWorkshopMenu.PromptWarnings(newContentPackagesWithWarnings, enableAndSelect, DisconnectFromServer);
                    }

                    void enableAndSelect()
                    {
                        ContentPackageManager.EnabledPackages.BackUp();
                        ContentPackageManager.EnabledPackages.SetCore(corePackage);
                        ContentPackageManager.EnabledPackages.SetRegular(regularPackages);

                        RefreshAndSelectLobby();
                    }
                }
            }
            else if (GameMain.Client.FileReceiver.ActiveTransfers.None())
            {
                GameMain.Client.RequestFile(FileTransferType.Mod, currentDownload.Name, currentDownload.Hash.StringRepresentation);
            }
        }

        internal void CurrentDownloadFinished(FileReceiver.FileTransferIn transfer)
        {
            if (currentDownload is null) { throw new Exception("Current download is null"); }

            string path = transfer.FilePath;
            if (!path.EndsWith(ModReceiver.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            string dir = path.RemoveFromEnd(ModReceiver.Extension, StringComparison.OrdinalIgnoreCase);
            
            SaveUtil.DecompressToDirectory(path, dir);
            var result = ContentPackage.TryLoad(Path.Combine(dir, ContentPackage.FileListFileName).CleanUpPathCrossPlatform());

            if (!result.TryUnwrapSuccess(out var newPackage))
            {
                throw new Exception($"Failed to load downloaded mod \"{currentDownload.Name}\"",
                    result.TryUnwrapFailure(out var exception) ? exception : null);
            }
            if (!currentDownload.Hash.Equals(newPackage.Hash))
            {
                throw new Exception($"Hash mismatch for downloaded mod \"{currentDownload.Name}\" (expected {currentDownload.Hash}, got {newPackage.Hash})");
            }
            downloadedPackages.Add(newPackage);
            
            currentDownload = null;

        }

        private void RefreshAndSelectLobby()
        {
            // Invalidate a possible stale game session
            GameMain.GameSession = null;
            //see if any of the packages we enabled contain subs that we were missing previously, and update their paths
            for (int i = GameMain.Client.ServerSubmarines.Count - 1; i >= 0; i--)
            {
                SubmarineInfo serverSub = GameMain.Client.ServerSubmarines[i];

                // Ignore subs that aren't in the download folder
                if (serverSub.FilePath == null || !serverSub.FilePath.Contains(SaveUtil.SubmarineDownloadFolder)) { continue; }

                var matchingSub = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == serverSub.Name && s.MD5Hash == serverSub.MD5Hash);

                if (matchingSub != null)
                {
                    // Found a matching sub from a local content package, replace the submarine info that came from the
                    // server with it so we get complete information about the sub
                    GameMain.Client.ServerSubmarines[i] = matchingSub;
                }
            }

            GameMain.NetLobbyScreen.UpdateSubList(GameMain.NetLobbyScreen.SubList, GameMain.Client.ServerSubmarines);
            GameMain.NetLobbyScreen.Select();
        }

        private void DisconnectFromServer()
        {
            GameMain.Client?.Quit();
            GameMain.MainMenuScreen.Select();
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch);            
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }
    }
}
