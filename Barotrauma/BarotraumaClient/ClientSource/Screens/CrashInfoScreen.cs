#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Xml.Linq;
using Barotrauma.Steam;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class CrashInfoScreen : Screen
    {
        // next to the executable
        public const string CrashDataPath = ".last_crash.xml";

        private readonly GUIListBox crashDataList;
        private readonly GUITextBox crashDataBox;
        private readonly GUIListBox modList;

        private readonly GUITextBlock modNameText;
        private readonly GUITextBlock modVersionText;
        private readonly GUIFrame modImageContainer;

        private readonly GUIButton openWorkshopBtn;
        private readonly GUIButton openRepositoryBtn;
        private readonly GUIButton disableCpButton;

        private readonly GUIButton reportToModAuthorBtn;

        // Mutable variables, captured in button callbacks
        private Option<ContentPackage> blamedMod = Option.None;
        private bool blamedModDisabled;
        private string repoUrl = "https://github.com/FakeFishGames/Barotrauma";
        private string crashLogPath = string.Empty;

        // Mod images, for disposal
        private SteamManager.Workshop.ItemThumbnail? loadedThumbnail;
        private Texture2D? loadedThumbnailLocal;

        [StringSyntax(StringSyntaxAttribute.Uri)]
        private const string StreamWorkshopUrlFormat = "https://steamcommunity.com/sharedfiles/filedetails/?id={0}",
                             BaroIssueTrackerUrl = "https://github.com/FakeFishGames/Barotrauma/discussions";

        public CrashInfoScreen()
        {
            // I don't think we need to care about reloading the UI on resolution change
            // since this screen is only shown once when the game starts
            Frame.ClearChildren();
            var backgroundFrame = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.8f), Frame.RectTransform, Anchor.Center));
            var mainLayout = new GUILayoutGroup(new RectTransform(ToolBox.PaddingSizeParentRelative(backgroundFrame.RectTransform, 0.9f), backgroundFrame.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            GUITextBlock header = new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), mainLayout.RectTransform), TextManager.Get("crashhandler.header"), textAlignment: Alignment.Center, font: GUIStyle.LargeFont);
            GUITextBlock headerFooter = new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), mainLayout.RectTransform), TextManager.Get("crashhandler.headerdescription"), textAlignment: Alignment.Center);

            crashDataList = new GUIListBox(new RectTransform(new Vector2(1f, 0.5f), mainLayout.RectTransform));

            GUILayoutGroup dummyLayout = new GUILayoutGroup(new RectTransform(Vector2.One, crashDataList.Content.RectTransform));
            crashDataBox = new GUITextBox(new RectTransform(Vector2.One, dummyLayout.RectTransform), text: string.Empty, wrap: true, textAlignment: Alignment.TopLeft, style: "GUITextBoxNoBorder")
            {
                TextBlock =
                {
                    TextColor = GUIStyle.Red,
                    SelectedTextColor = GUIStyle.Red,
                    HoverTextColor = GUIStyle.Red
                },
                Readonly = true,
            };

            GUILayoutGroup modLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), mainLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };
            GUILayoutGroup blamedModLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), modLayout.RectTransform));

            // "Blamed mod" Header
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), blamedModLayout.RectTransform), TextManager.Get("crashhandler.suscontentpackage"), textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont);
            GUILayoutGroup modInfoLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.7f), blamedModLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            GUIFrame imageHolder = new GUIFrame(new RectTransform(new Vector2(0.25f, 1f), modInfoLayout.RectTransform), style: null);
            modImageContainer = new GUIFrame(new RectTransform(Vector2.One, imageHolder.RectTransform, scaleBasis: ScaleBasis.BothWidth, anchor: Anchor.CenterLeft), style: "InnerFrame");

            // Vanilla icon - for when there is no content package to blame (this gets cleared if there is)
            new GUIImage(new RectTransform(Vector2.One, modImageContainer.RectTransform, scaleBasis: ScaleBasis.BothWidth, anchor: Anchor.CenterLeft), style: "VanillaIcon", scaleToFit: true);

            GUILayoutGroup modInfoTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1f), modInfoLayout.RectTransform), isHorizontal: false, childAnchor: Anchor.Center);
            GUILayoutGroup paddedModInfoTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.8f), modInfoTextLayout.RectTransform), isHorizontal: false);
            modNameText = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), paddedModInfoTextLayout.RectTransform), "Vanilla", textAlignment: Alignment.CenterLeft, font: GUIStyle.LargeFont);
            modVersionText = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), paddedModInfoTextLayout.RectTransform), TextManager.GetWithVariable("crashhandler.versionformat", "[value]", GameMain.Version.ToString()), textAlignment: Alignment.CenterLeft);

            GUILayoutGroup modButtonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), blamedModLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };

            openWorkshopBtn = new GUIButton(new RectTransform(new Vector2(0.33f, 1f), modButtonLayout.RectTransform), TextManager.Get("steamworkshopbutton"), style: "GUIButtonSmall")
            {
                Enabled = false,
                OnClicked = (button, userData) =>
                {
                    if (blamedMod.TryUnwrap(out ContentPackage? cp)
                        && cp.UgcId.TryUnwrap(out ContentPackageId? ugcId)
                        && ugcId is SteamWorkshopId steamWorkshopId)
                    {
                        OpenUrl(string.Format(StreamWorkshopUrlFormat, steamWorkshopId.Value.ToString()));
                    }

                    return true;
                }
            };

            openRepositoryBtn = new GUIButton(new RectTransform(new Vector2(0.33f, 1f), modButtonLayout.RectTransform), TextManager.Get("crashhandler.repository"), style: "GUIButtonSmall")
            {
                OnClicked = (button, userData) =>
                {
                    OpenUrl(repoUrl);
                    return true;
                }
            };

            disableCpButton = new GUIButton(new RectTransform(new Vector2(0.33f, 1f), modButtonLayout.RectTransform), TextManager.Get("DisableWorkshopMod"), style: "GUIButtonSmall")
            {
                Enabled = false,
                ToolTip = TextManager.Get("crashhandler.vanillacannotdisable"),
                OnClicked = (button, userData) =>
                {
                    if (blamedMod.TryUnwrap(out ContentPackage? cp))
                    {
                        ContentPackageManager.EnabledPackages.DisableMods([cp]);
                        GameSettings.SaveCurrentConfig();
                    }

                    blamedModDisabled = true;
                    if (modImageContainer.GetChild<GUIImage>() is { } img)
                    {
                        // I don't know why setting gray color here makes the icon look green, just reduce the alpha
                        img.Color *= 0.5f;
                    }

                    modImageContainer.ToolTip = TextManager.Get("crashhandler.modisdisabled");

                    button.Enabled = false;
                    return true;
                }
            };

            GUILayoutGroup modListLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), modLayout.RectTransform));
            // "Enabled mods" Header
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.1f), modListLayout.RectTransform), TextManager.Get("crashhandler.enabledmods"), textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont);
            modList = new GUIListBox(new RectTransform(new Vector2(1f, 0.9f), modListLayout.RectTransform));

            GUILayoutGroup bottomBtnLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.15f), mainLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };
            GUILayoutGroup reportBtnLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), bottomBtnLayout.RectTransform));

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), reportBtnLayout.RectTransform), TextManager.Get("crashhandler.reporttoheader"), textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);
            reportToModAuthorBtn = new GUIButton(new RectTransform(new Vector2(1f, 0.4f), reportBtnLayout.RectTransform), TextManager.Get("crashhandler.modauthor"))
            {
                Enabled = false,
                OnClicked = (button, userData) =>
                {
                    // PLUGIN_TODO: way for mod authors to specify issue tracker
                    // This also needs to exclude DLC, so this can left for after
                    // we implemented assembly signing
                    OpenUrl(repoUrl);
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1f, 0.4f), reportBtnLayout.RectTransform), TextManager.Get("crashhandler.barotraumaissuetracker"))
            {
                OnClicked = (button, userData) =>
                {
                    OpenUrl(BaroIssueTrackerUrl);
                    return true;
                }
            };

            GUILayoutGroup closeBtnLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), bottomBtnLayout.RectTransform));

            // for spacing
            new GUIFrame(new RectTransform(new Vector2(1f, 0.2f), closeBtnLayout.RectTransform), style: null);

            new GUIButton(new RectTransform(new Vector2(1f, 0.4f), closeBtnLayout.RectTransform), TextManager.Get("crashhandler.opencrashreport"))
            {
                OnClicked = (button, userData) =>
                {
                    if (string.IsNullOrWhiteSpace(crashLogPath)) { return false; }

                    try
                    {
                        ToolBox.OpenFileWithShell(crashLogPath);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to open the crash data file", e);
                    }

                    return true;
                }
            };
            new GUIButton(new RectTransform(new Vector2(1f, 0.4f), closeBtnLayout.RectTransform), TextManager.Get("crashhandler.close"))
            {
                OnClicked = static (button, userData) =>
                {
                    try
                    {
                        File.Delete(CrashDataPath);
                    }
                    catch (Exception e)
                    {
                        DebugConsole.ThrowError("Failed to delete the crash data file", e);
                    }

                    GameMain.MainMenuScreen.Select();
                    return true;
                }
            };

            Frame.ForceLayoutRecalculation();
        }

        private static void OpenUrl([StringSyntax(StringSyntaxAttribute.Uri)] string url)
        {
            if (SteamManager.IsInitialized)
            {
                SteamManager.OverlayCustomUrl(url);
            }
            else
            {
                GameMain.ShowOpenUriPrompt(url);
            }
        }

        public static bool ShouldShowCrashScreen()
        {
            try
            {
                return File.Exists(CrashDataPath);
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to access the crash data file", e);
                return false;
            }
        }

        public override void Select()
        {
            base.Select();

            XDocument crashFile;
            try
            {
                crashFile = XDocument.Load(CrashDataPath);
            }
            catch (Exception e)
            {
                // this should in theory never happen since we check ShouldShowCrashScreen before calling this
                DebugConsole.ThrowError("Failed access the crash data file", e);
                return;
            }

            XElement? root = crashFile.Root;
            if (root is null) { return; }

            crashLogPath = root.GetAttributeString("crashlog", string.Empty);
            string[] enabledContentPackages = root.GetAttributeStringArray("enabledcontentpackages", []);

            modList.Content.ClearChildren();
            foreach (string ecp in enabledContentPackages)
            {
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.15f), modList.Content.RectTransform), ecp, textAlignment: Alignment.CenterLeft);
            }

            foreach (XElement element in root.Elements())
            {
                Identifier name = element.Name.ToIdentifier();

                if (name == "Exception")
                {
                    string exception =
                        "Exception: " +
                        element.GetAttributeString("message", string.Empty) +
                        $" ({element.GetAttributeString("type", string.Empty)})" +
                        '\n' +
                        element.ElementInnerText();

                    crashDataBox.Text = exception;
                }
                else if (name == "ContentPackage")
                {
                    string contentPackageHash = element.GetAttributeString("hash", string.Empty);

                    foreach (ContentPackage cp in ContentPackageManager.AllPackages)
                    {
                        if (cp.Hash.StringRepresentation == contentPackageHash)
                        {
                            blamedMod = Option.Some(cp);

                            modNameText.Text = ToolBox.LimitString(cp.Name, modNameText.Font, modNameText.Rect.Width);
                            modVersionText.Text = TextManager.GetWithVariable("crashhandler.versionformat", "[value]", cp.ModVersion);
                            disableCpButton.Enabled = true;
                            disableCpButton.ToolTip = string.Empty;
                            reportToModAuthorBtn.Enabled = true;
                            openWorkshopBtn.Enabled = cp.UgcId.IsSome();

                            if (cp.ModIconPath.TryUnwrap(out var modIconPath))
                            {
                                SetLocalThumbnail(modImageContainer, modIconPath.FullPath);
                            }
                            else if (cp.UgcId.TryUnwrap(out ContentPackageId? cpId) && cpId is SteamWorkshopId workshopId)
                            {
                                SetWorkshopThumbnail(modImageContainer, workshopId);
                            }
                            else
                            {
                                ShowDefaultThumbnail(modImageContainer);
                            }

                            break;
                        }
                    }
                }
                else if (name == "Plugin")
                {
                    repoUrl = element.GetAttributeString("repository", string.Empty);
                    openRepositoryBtn.Enabled = true;
                }
            }
        }

        private void SetWorkshopThumbnail(GUIFrame container, SteamWorkshopId workshopId)
        {
            container.ClearChildren();

            TaskPool.Add($"LoadCrashContentPackage{workshopId}", SteamManager.Workshop.GetItem(workshopId.Value),
                         t =>
                         {
                             if (!t.TryGetResult(out Option<Steamworks.Ugc.Item> itemOption)) { return; }

                             if (!itemOption.TryUnwrap(out var item)) { return; }

                             LoadThumbnail(item);
                         });
            return;

            void LoadThumbnail(Steamworks.Ugc.Item item)
            {
                CancellationTokenSource taskCancelSrc = new CancellationTokenSource();
                var thumbnail = new SteamManager.Workshop.ItemThumbnail(item, taskCancelSrc.Token);
                loadedThumbnail = thumbnail;
                CreateAsyncThumbnailComponent(
                    container, textureGetter: () => loadedThumbnail.Texture,
                    throbberEnabled: () => thumbnail.Loading || thumbnail.Texture is { IsDisposed: false },
                    grayScaleEnabled: () => blamedModDisabled);
            }
        }

        private void SetLocalThumbnail(GUIFrame container, string path)
        {
            container.ClearChildren();
            Texture2D thumbnail = Sprite.LoadTexture(path, compress: false);
            loadedThumbnailLocal = thumbnail;
            CreateAsyncThumbnailComponent(
                container,
                textureGetter: () => loadedThumbnailLocal,
                throbberEnabled: () => loadedThumbnailLocal is { IsDisposed: false },
                grayScaleEnabled: () => blamedModDisabled);
        }

        private void ShowDefaultThumbnail(GUIFrame container)
        {
            container.ClearChildren();
            new GUIImage(new RectTransform(Vector2.One, container.RectTransform), style: "DefaultWorkshopIcon")
            {
                CanBeFocused = false
            };
        }

        private void ResizeTextBoxToFit(GUITextBox textBox, GUIListBox container)
        {
            string wrappedText = textBox.TextBlock.WrappedText.Value;
            int measuredHeight = (int)MathF.Ceiling((textBox.Font.MeasureString(wrappedText).Y * textBox.TextBlock.TextScale) + textBox.Padding.Y + textBox.Padding.W);

            textBox.Parent.RectTransform.NonScaledSize =
                (container.Content.Rect.Width,
                 Math.Max(measuredHeight, container.Content.Rect.Height));
            container.UpdateScrollBarSize();
        }

        // no idea why the game doesn't like calling ResizeTextBoxToFit after setting the text
        // so we'll just do it in the update loop, twice for good measure
        private int updateCount = 2;

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);
            if (updateCount > 0)
            {
                updateCount--;
                ResizeTextBoxToFit(crashDataBox, crashDataList);
            }
        }

        public override void Deselect()
        {
            base.Deselect();

            modImageContainer.ClearChildren();
            loadedThumbnail?.Dispose();
            loadedThumbnailLocal?.Dispose();
            loadedThumbnail = null;
            loadedThumbnailLocal = null;
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch);
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }

        private static readonly Vector3 GrayColor = new(0.299f, 0.587f, 0.114f);

        /// <summary>
        /// A copy of <see cref="MutableWorkshopMenu.CreateAsyncThumbnailComponent"/>
        /// that supports shaders
        /// </summary>
        private static void CreateAsyncThumbnailComponent(GUIFrame thumbnailContainer, Func<Texture2D?> textureGetter, Func<bool> throbberEnabled, Func<bool> grayScaleEnabled)
        {
            int randomThrobberOffset = Rand.Range(0, 10);
            new GUICustomComponent(
                new RectTransform(Vector2.One, thumbnailContainer.RectTransform, Anchor.Center),
                onDraw: (spriteBatch, component) =>
                {
                    bool grayScale = grayScaleEnabled();
                    if (grayScale)
                    {
                        spriteBatch.SwapEffect(GameMain.GameScreen.MonochromeEffect);
                        GameMain.GameScreen.MonochromeEffect.Parameters["monoColor"].SetValue(GrayColor);
                    }
                    Rectangle rect = component.Rect;
                    Texture2D? texture = textureGetter();
                    if (texture != null)
                    {
                        rect.Location += (4, 4);
                        rect.Size -= (8, 8);
                        Point destinationSizeMaxWidth = (rect.Width, rect.Width * texture.Height / texture.Width);
                        Point destinationSizeMaxHeight = (rect.Height * texture.Width / texture.Height, rect.Height);
                        Point destinationSize = destinationSizeMaxHeight.X > rect.Width
                            ? destinationSizeMaxWidth
                            : destinationSizeMaxHeight;
                        Rectangle destinationRectangle = new Rectangle(
                            rect.Center.X - destinationSize.X / 2,
                            rect.Center.Y - destinationSize.Y / 2,
                            destinationSize.X,
                            destinationSize.Y);
                        spriteBatch.Draw(texture, destinationRectangle, Color.White);
                    }
                    else if (throbberEnabled())
                    {
                        var sheet = GUIStyle.GenericThrobber;
                        Vector2 pos = rect.Center.ToVector2() - Vector2.One * rect.Height * 0.4f;
                        sheet.Draw(spriteBatch, ((int)Math.Floor(Timing.TotalTime * 24.0f) + randomThrobberOffset) % sheet.FrameCount, pos, Color.White,
                            origin: Vector2.Zero, rotate: 0.0f,
                            scale: Vector2.One * component.Rect.Height / sheet.FrameSize.ToVector2() * 0.8f);
                    }
                    if (grayScale)
                    {
                        spriteBatch.SwapEffect();
                    }
                })
            {
                Color = Color.White,
                CanBeFocused = false
            };
        }
    }
}