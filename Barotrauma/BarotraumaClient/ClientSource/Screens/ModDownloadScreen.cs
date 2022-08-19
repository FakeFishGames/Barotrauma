#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using ServerContentPackage = Barotrauma.Networking.ClientPeer.ServerContentPackage;

namespace Barotrauma
{
    class ModDownloadScreen : Screen
    {
        private readonly Queue<ServerContentPackage> pendingDownloads =
            new Queue<ServerContentPackage>();
        private ServerContentPackage? currentDownload;

        private readonly List<ContentPackage> downloadedPackages = new List<ContentPackage>();
        public IEnumerable<ContentPackage> DownloadedPackages => downloadedPackages;
        
        private bool confirmDownload;

        public void Reset()
        {
            pendingDownloads.Clear();
            downloadedPackages.Clear();
            currentDownload = null;
            confirmDownload = false;
        }

        private void DeletePrevDownloads()
        {
            if (Directory.Exists(ModReceiver.DownloadFolder))
            {
                Directory.Delete(ModReceiver.DownloadFolder, recursive: true);
            }
        }
        
        public override void Select()
        {
            base.Select();
            DeletePrevDownloads();
            Reset();
            
            Frame.ClearChildren();

            var mainVisibleFrame = new GUIFrame(new RectTransform((0.6f, 0.8f), Frame.RectTransform, Anchor.Center));
            GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(Vector2.One * 0.93f, mainVisibleFrame.RectTransform, Anchor.Center));

            void mainLayoutSpacing()
                => new GUIFrame(new RectTransform((1.0f, 0.02f), mainLayout.RectTransform), style: null);
            
            var serverName = new GUITextBlock(new RectTransform((1.0f, 0.08f), mainLayout.RectTransform),
                "", font: GUIStyle.LargeFont,
                textAlignment: Alignment.CenterLeft)
            {
                TextGetter = () => GameMain.NetLobbyScreen.ServerName.Text
            };
            mainLayoutSpacing();
            var downloadList = new GUIListBox(new RectTransform((1.0f, 0.76f), mainLayout.RectTransform));
            mainLayoutSpacing();
            var disconnectButton = new GUIButton(new RectTransform((0.3f, 0.1f), mainLayout.RectTransform),
                TextManager.Get("Disconnect"))
            {
                OnClicked = (guiButton, o) =>
                {
                    GameMain.Client?.Quit();
                    GameMain.MainMenuScreen.Select();
                    return false;
                }
            };

            if (!GameMain.Client.IsServerOwner)
            {
                if (GameMain.Client.ClientPeer.ServerContentPackages.Length == 0)
                {
                    string errorMsg = $"Error in ModDownloadScreen: the list of mods the server has enabled was empty. Content package list received: {GameMain.Client.ClientPeer.ContentPackageOrderReceived}";
                    GameAnalyticsManager.AddErrorEventOnce("ModDownloadScreen.Select:NoContentPackages", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    throw new InvalidOperationException(errorMsg);
                }
                if (GameMain.Client.ClientPeer.ServerContentPackages.None(p => p.CorePackage != null))
                {
                    string errorMsg = $"Error in ModDownloadScreen: no core packages in the list of mods the server has enabled. Content package list received: {GameMain.Client.ClientPeer.ContentPackageOrderReceived}";
                    GameAnalyticsManager.AddErrorEventOnce("ModDownloadScreen.Select:NoCorePackage", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    throw new InvalidOperationException(errorMsg);
                }
            }

            var missingPackages = GameMain.Client.ClientPeer.ServerContentPackages
                .Where(sp => sp.ContentPackage is null).ToArray();
            if (!missingPackages.Any())
            {
                if (!GameMain.Client.IsServerOwner)
                {
                    ContentPackageManager.EnabledPackages.BackUp();
                    ContentPackageManager.EnabledPackages.SetCore(
                        GameMain.Client.ClientPeer.ServerContentPackages
                            .Select(p => p.CorePackage)
                            .OfType<CorePackage>().First());
                    List<RegularPackage> regularPackages =
                        GameMain.Client.ClientPeer.ServerContentPackages
                            .Select(p => p.RegularPackage)
                            .OfType<RegularPackage>().ToList();
                    //keep enabled client-side-only mods enabled
                    regularPackages.AddRange(ContentPackageManager.EnabledPackages.Regular.Where(p => !p.HasMultiplayerSyncedContent));
                    ContentPackageManager.EnabledPackages.SetRegular(regularPackages);
                }
                GameMain.NetLobbyScreen.Select();
                return;
            }

            GUIMessageBox msgBox = new GUIMessageBox(
                TextManager.Get("ModDownloadTitle"),
                "",
                Array.Empty<LocalizedString>(),
                relativeSize: (0.5f, 0.75f));

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

            var header = textBlock(TextManager.Get("ModDownloadHeader"), GUIStyle.Font);
            innerLayoutSpacing(0.05f);

            var msgBoxModList = new GUIListBox(new RectTransform(Vector2.One, innerLayout.RectTransform));
            
            innerLayoutSpacing(0.05f);
            var footer = textBlock(TextManager.Get("ModDownloadFooter"), GUIStyle.Font, Alignment.Center);
            
            innerLayoutSpacing(0.05f);
            GUILayoutGroup buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), innerLayout.RectTransform), isHorizontal: true);

            void buttonContainerSpacing(float width)
                => new GUIFrame(new RectTransform((width, 1.0f), buttonContainer.RectTransform), style: null);

            void button(LocalizedString text, Action action, float width = 0.3f)
                => new GUIButton(new RectTransform((width, 1.0f), buttonContainer.RectTransform), text)
                {
                    OnClicked = (_, __) =>
                    {
                        action();
                        msgBox.Close();
                        return false;
                    }
                };

            buttonContainerSpacing(0.1f);
            button(TextManager.Get("Yes"), () => confirmDownload = true);
            buttonContainerSpacing(0.2f);
            button(TextManager.Get("No"), () =>
            {
                GameMain.Client?.Quit();
                GameMain.MainMenuScreen.Select();
            });
            buttonContainerSpacing(0.1f);

            var missingIds = missingPackages.Where(
                mp => mp.WorkshopId != 0
                   && ContentPackageManager.WorkshopPackages.All(wp
                       => wp.SteamWorkshopId != mp.WorkshopId))
                .Select(mp => mp.WorkshopId)
                .ToArray();
            if (missingIds.Any() && SteamManager.IsInitialized)
            {
                buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), innerLayout.RectTransform), isHorizontal: true);
                buttonContainerSpacing(0.15f);
                button(TextManager.Get("SubscribeToAllOnWorkshop"), () =>
                {
                    if (GameMain.Client != null)
                    {
                        BulkDownloader.SubscribeToServerMods(missingIds,
                            new ConnectCommand(
                                serverName: GameMain.Client.ServerName,
                                endpoint: GameMain.Client.ClientPeer.ServerEndpoint));
                        GameMain.Client.Quit();
                    }
                    GameMain.MainMenuScreen.Select();
                }, width: 0.7f);
                buttonContainerSpacing(0.15f);
            }

            foreach (var p in missingPackages)
            {
                pendingDownloads.Enqueue(p);
                
                //Message box frame
                new GUITextBlock(new RectTransform((1.0f, 0.1f), msgBoxModList.Content.RectTransform), p.Name)
                {
                    CanBeFocused = false
                };
                
                //Download progress frame
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
                else
                {
                    var serverPackages = GameMain.Client.ClientPeer.ServerContentPackages;
                    CorePackage corePackage
                        = downloadedPackages.FirstOrDefault(p => p is CorePackage) as CorePackage
                          ?? serverPackages.FirstOrDefault(p => p.CorePackage != null)
                              ?.CorePackage
                          ?? throw new Exception($"Failed to find core package to enable");
                    List<RegularPackage> regularPackages
                        = serverPackages.Where(p => p.CorePackage is null)
                            .Select(p =>
                                p.RegularPackage
                                ?? downloadedPackages.FirstOrDefault(d => d is RegularPackage && d.Hash.Equals(p.Hash))
                                ?? throw new Exception($"Could not find regular package \"{p.Name}\""))
                            .Cast<RegularPackage>()
                            .ToList();
                    foreach (var regularPackage in regularPackages)
                    {
                        DebugConsole.NewMessage($"Enabling \"{regularPackage.Name}\" ({regularPackage.Dir})", Color.Lime);
                    }

                    //keep enabled client-side-only mods enabled
                    regularPackages.AddRange(ContentPackageManager.EnabledPackages.Regular.Where(p => !p.HasMultiplayerSyncedContent));

                    ContentPackageManager.EnabledPackages.BackUp();
                    ContentPackageManager.EnabledPackages.SetCore(corePackage);
                    ContentPackageManager.EnabledPackages.SetRegular(regularPackages);

                    GameMain.NetLobbyScreen.Select();
                }
            }
        }

        public void CurrentDownloadFinished(FileReceiver.FileTransferIn transfer)
        {
            if (currentDownload is null) { throw new Exception("Current download is null"); }

            string path = transfer.FilePath;
            if (!path.EndsWith(ModReceiver.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            string dir = path.RemoveFromEnd(ModReceiver.Extension, StringComparison.OrdinalIgnoreCase);
            
            SaveUtil.DecompressToDirectory(path, dir, file => { });
            ContentPackage newPackage
                = ContentPackage.TryLoad($"{dir}/{ContentPackage.FileListFileName}")
                ?? throw new Exception($"Failed to load downloaded mod \"{currentDownload.Name}\"");
            if (!currentDownload.Hash.Equals(newPackage.Hash))
            {
                throw new Exception($"Hash mismatch for downloaded mod \"{currentDownload.Name}\" (expected {currentDownload.Hash}, got {newPackage.Hash})");
            }
            downloadedPackages.Add(newPackage);
            
            currentDownload = null;

        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch); //wtf

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            
            GUI.Draw(Cam, spriteBatch);

            spriteBatch.End();
        }
    }
}
