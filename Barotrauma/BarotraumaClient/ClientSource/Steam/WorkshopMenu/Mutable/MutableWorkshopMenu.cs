#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Barotrauma.Steam
{
    sealed partial class MutableWorkshopMenu : WorkshopMenu
    {
        public enum Tab
        {
            InstalledMods,
            //Overrides, //TODO: implement later
            PopularMods,
            Publish
        }

        private enum Filter
        {
            ShowLocal,
            ShowWorkshop,
            ShowPublished,
            ShowOnlySubs,
            ShowOnlyItemAssemblies
        }

        public Tab CurrentTab { get; private set; }

        private readonly GUILayoutGroup tabber;
        private readonly Dictionary<Tab, (GUIButton Button, GUIFrame Content)> tabContents;

        private readonly GUIFrame contentFrame;

        private CancellationTokenSource taskCancelSrc = new CancellationTokenSource();
        private readonly HashSet<SteamManager.Workshop.ItemThumbnail> itemThumbnails = new HashSet<SteamManager.Workshop.ItemThumbnail>();

        private readonly Option<GUIListBox> popularModsListOption;
        private readonly Option<GUIListBox> selfModsListOption;

        private uint memSubscribedModCount = 0;

        private static bool EnableWorkshopSupport => SteamManager.IsInitialized;

        public MutableWorkshopMenu(GUIFrame parent) : base(parent)
        {
            var mainLayout
                = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform), isHorizontal: false)
                {
                    Stretch = true,
                    AbsoluteSpacing = GUI.IntScale(4)
                };

            Vector2 tabberSize = EnableWorkshopSupport ? (1.0f, 0.05f) : Vector2.Zero;

            tabber = new GUILayoutGroup(new RectTransform(tabberSize, mainLayout.RectTransform), isHorizontal: true)
                { Stretch = true };
            tabContents = new Dictionary<Tab, (GUIButton Button, GUIFrame Content)>();

            if (EnableWorkshopSupport)
            {
                new GUIButton(new RectTransform((1.0f, 0.05f), mainLayout.RectTransform, Anchor.BottomLeft),
                    style: "GUIButtonSmall", text: TextManager.Get("FindModsButton"))
                {
                    OnClicked = (button, o) =>
                    {
                        SteamManager.OverlayCustomUrl($"https://steamcommunity.com/app/{SteamManager.AppID}/workshop/");
                        return false;
                    }
                };
            }
            else
            {
                tabber.Visible = false;
            }

            contentFrame = new GUIFrame(new RectTransform((1.0f, 0.95f), mainLayout.RectTransform), style: null);

            new GUICustomComponent(new RectTransform(Vector2.Zero, mainLayout.RectTransform),
                onUpdate: (f, component) => UpdateSubscribedModInstalls());

            CreateInstalledModsTab(
                out enabledCoreDropdown,
                out enabledRegularModsList,
                out disabledRegularModsList,
                out onInstalledInfoButtonHit,
                out modsListFilter,
                out modsListFilterTickboxes,
                out bulkUpdateButtonOption);

            if (EnableWorkshopSupport)
            {
                CreatePopularModsTab(out GUIListBox popularModList);
                CreatePublishTab(out GUIListBox selfModsList);

                popularModsListOption = Option<GUIListBox>.Some(popularModList);
                selfModsListOption = Option<GUIListBox>.Some(selfModsList);
            }
            else
            {
                popularModsListOption = Option.None;
                selfModsListOption = Option.None;
            }

            SelectTab(Tab.InstalledMods);
        }

        private void SwitchContent(GUIFrame newContent)
        {
            contentFrame.Children.ForEach(c => c.Visible = false);
            newContent.Visible = true;
        }

        public void SelectTab(Tab tab)
        {
            CurrentTab = tab;
            SwitchContent(tabContents[tab].Content);
            tabber.Children.ForEach(c =>
            {
                if (c is GUIButton btn) { btn.Selected = btn == tabContents[tab].Button; }
            });
            if (!taskCancelSrc.IsCancellationRequested) { taskCancelSrc.Cancel(); }
            itemThumbnails.ForEach(t => t.Dispose());
            itemThumbnails.Clear();
            switch (tab)
            {
                case Tab.InstalledMods:
                    PopulateInstalledModLists();
                    break;
                case Tab.PopularMods when popularModsListOption.TryUnwrap(out var popularModsList):
                    PopulateItemList(popularModsList, SteamManager.Workshop.GetPopularItems(), includeSubscribeButton: true);
                    break;
                case Tab.Publish when selfModsListOption.TryUnwrap(out var selfModsList):
                    PopulateItemList(selfModsList, SteamManager.Workshop.GetPublishedItems(), includeSubscribeButton: false, onFill: AddUnpublishedMods);
                    break;
            }
        }

        private void AddButtonToTabber(Tab tab, GUIFrame content)
        {
            var button = new GUIButton(new RectTransform(Vector2.One, tabber.RectTransform, Anchor.BottomCenter, Pivot.BottomCenter), TextManager.Get($"workshopmenutab.{tab}"), style: "GUITabButton")
            {
                OnClicked = (b, _) =>
                {
                    if (tab != CurrentTab)
                    {
                        SelectTab(tab);
                    }
                    return false;
                }
            };
            button.RectTransform.MaxSize = RectTransform.MaxPoint;
            button.Children.ForEach(c => c.RectTransform.MaxSize = RectTransform.MaxPoint);

            tabContents.Add(tab, (button, content));
        }

        private GUIFrame CreateNewContentFrame(Tab tab)
        {
            var content = new GUIFrame(new RectTransform(Vector2.One * 0.98f, contentFrame.RectTransform, Anchor.Center, Pivot.Center), style: null);
            AddButtonToTabber(tab, content);
            return content;
        }

        private void CreatePopularModsTab(out GUIListBox popularModsList)
        {
            GUIFrame content = CreateNewContentFrame(Tab.PopularMods);
            if (!SteamManager.IsInitialized)
            {
                tabContents[Tab.PopularMods].Button.Enabled = false;
            }
            GUIFrame listFrame = new GUIFrame(new RectTransform(Vector2.One, content.RectTransform), style: null);
            CreateWorkshopItemList(listFrame, out _, out popularModsList, onSelected: PopulateFrameWithItemInfo);
        }

        private void CreatePublishTab(out GUIListBox selfModsList)
        {
            GUIFrame content = CreateNewContentFrame(Tab.Publish);
            if (!SteamManager.IsInitialized)
            {
                tabContents[Tab.Publish].Button.Enabled = false;
            }
            CreateWorkshopItemOrPackageList(content, out _, out selfModsList, onSelected: PopulatePublishTab);
        }

        public void ApplyButtonPressed()
        {
            List<ContentPackage> enableContentPackages = 
                enabledRegularModsList.Content.Children
                .Select(static c => c.UserData as ContentPackage)
                .OfType<ContentPackage>()
                .Concat([EnabledCorePackage])
                .ToList();

            List<ContentPackage> newContentPackages =
                enableContentPackages
                .Where(static cp => !ContentPackageManager.EnabledPackages.All.Contains(cp) && cp != ContentPackageManager.EnabledPackages.Core)
                .ToList();

            List<ContentPackage> contentPackagesWithWarnings =
                newContentPackages
                .Where(static p => p.FilesThatShouldPromptWarning.Any())
                .ToList();

            PromptContentPackagesUnloadingIfNeeded(newContentPackages, onContinue: () =>
            {
                if (contentPackagesWithWarnings.Count > 0)
                {
                    PromptWarnings(contentPackagesWithWarnings, () =>
                    {
                        Apply();
                        GameSettings.SaveCurrentConfig();
                    },
                    () =>
                    {
                        GameSettings.SaveCurrentConfig();
                    });
                }
                else
                {
                    Apply();
                }
            });
        }

        public static bool CheckIfCanSafelyUpdateFiles(IEnumerable<Steamworks.Ugc.Item> items)
        {
            IEnumerable<ContentPackage> contentPackages =
                ContentPackageManager.AllPackages
                .Union(ContentPackageManager.EnabledPackages.All)
                .Except(ContentPackageManager.LocalPackages)
                .Where(static package => package.UgcId.IsSome())
                .Where(package => items.Any(item => package.UgcId == ContentPackageId.Parse(item.Id.ToString())));

            return CheckIfCanSafelyUpdateFiles(contentPackages);
        }

        /// <summary>
        /// Checks the content packages to see if they can safely have their files updated, if not 
        /// this function will return false and show a popup to the user. Content packages are 
        /// considered safe to have their files updated if they don't contain a plugin or if the 
        /// plugin is completely unloaded.
        /// </summary>
        public static bool CheckIfCanSafelyUpdateFiles(IEnumerable<ContentPackage> contentPackages)
        {
            List<ContentPackage> contentPackagesUnloading = contentPackages
                .Where(package => 
                // Get packages with plugins that are either still unloading or currently active.
                (package.GetFiles<PluginInfoFile>().Any() && ContentPackageManager.EnabledPackages.All.Contains(package)) || 
                package.GetFiles<PluginInfoFile>().Any(PluginLoader.IsPluginFileUnloading))
                .ToList();

            if (contentPackagesUnloading.Count == 0)
            {
                return true;
            }

            GUIMessageBox messageBox = new GUIMessageBox(
                TextManager.Get("error"),
                relativeSize: new Vector2(0.25f, 0.45f),
                minSize: new Point(400, 500),
                text: string.Empty);

            GUILayoutGroup msgBoxLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.75f), messageBox.Content.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBoxLayout.RectTransform), TextManager.Get("PackageActivePopup.Header"),
                font: GUIStyle.SubHeadingFont, wrap: true, textAlignment: Alignment.Center);

            GUIListBox packageListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), msgBoxLayout.RectTransform))
            {
                CurrentSelectMode = GUIListBox.SelectMode.None
            };

            foreach (ContentPackage package in contentPackagesUnloading)
            {
                GUIFrame packageFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.2f), packageListBox.Content.RectTransform), style: "ListBoxElement");
                new GUITextBlock(new RectTransform(new Vector2(1f, 1f), packageFrame.RectTransform), package.Name);
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0f), msgBoxLayout.RectTransform), TextManager.Get("PackageActivePopup.Body"), wrap: true)
            {
                Wrap = true
            };

            return false;
        }

        public static bool PromptContentPackagesUnloadingIfNeeded(IEnumerable<ContentPackage> contentPackages, Action onContinue)
        {
            List<ContentPackage> contentPackagesUnloading = contentPackages
                .Where(x => x.GetFiles<PluginInfoFile>().Any(PluginLoader.IsPluginFileUnloading))
                .ToList();

            if (contentPackagesUnloading.Count == 0)
            {
                onContinue();
                return false;
            }

            GUIMessageBox messageBox = new GUIMessageBox(
                TextManager.Get("warning"),
                relativeSize: new Vector2(0.3f, 0.55f),
                minSize: new Point(400, 500),
                text: string.Empty,
                buttons: []);

            GUILayoutGroup msgBoxLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.75f), messageBox.Content.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBoxLayout.RectTransform), TextManager.Get("PluginUnload.Header"), 
                font: GUIStyle.SubHeadingFont, wrap: true, textAlignment: Alignment.Center);

            GUIListBox packageListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), msgBoxLayout.RectTransform))
            {
                CurrentSelectMode = GUIListBox.SelectMode.None
            };

            foreach (ContentPackage package in contentPackagesUnloading)
            {
                GUIFrame packageFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), packageListBox.Content.RectTransform), style: "ListBoxElement");
                new GUITextBlock(new RectTransform(new Vector2(1f, 1f), packageFrame.RectTransform), package.Name);
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0f), msgBoxLayout.RectTransform), TextManager.Get("PluginUnload.Body"), wrap: true)
            {
                Wrap = true
            };

            GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), messageBox.Content.RectTransform, Anchor.BottomCenter), isHorizontal: false, childAnchor: Anchor.TopCenter);

            new GUIButton(new RectTransform(new Vector2(0.8f, 0.0f), buttonLayout.RectTransform), TextManager.Get("PluginUnload.Restart"))
            {
                TextBlock = { AutoScaleHorizontal = true },
                OnClicked = (btn, userdata) =>
                {
                    ToolBox.RestartApplication();
                    messageBox.Close();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(0.8f, 0.0f), buttonLayout.RectTransform), TextManager.Get("PluginUnload.Continue"))
            {
                OnClicked = (btn, userdata) =>
                {
                    onContinue();
                    messageBox.Close();
                    return true;
                }
            };

            return true;
        }

        public static void PromptWarnings(IEnumerable<ContentPackage> contentPackages, Action onContinue, Action onCancel)
        {
            GUIMessageBox messageBox = new GUIMessageBox(
                TextManager.Get("warning"),
                relativeSize: new Vector2(0.33f, 0.55f),
                minSize: new Point(500, 500),
                text: string.Empty,
                buttons: []);

            GUILayoutGroup msgBoxLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.75f), messageBox.Content.RectTransform), isHorizontal: false, childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBoxLayout.RectTransform), TextManager.Get("codemodwarning.header"), font: GUIStyle.SubHeadingFont, wrap: true, textAlignment: Alignment.Center)
            {
                TextColor = GUIStyle.Red
            };

            GUIListBox packageListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), msgBoxLayout.RectTransform));

            const string arrowUserData = "arrow";
            foreach (ContentPackage package in contentPackages)
            {
                GUIFrame packageFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.25f), packageListBox.Content.RectTransform), style: "ListBoxElement")
                {
                    UserData = package
                };

                GUILayoutGroup cpLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1f), packageFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    RelativeSpacing = 0.01f,
                    Stretch = true
                };

                string warningTooltip = $"‖color:gui.red‖{TextManager.Get("codemodwarning.codechanges")}‖end‖";
                GUIButton? warningButton = null;

                var approvalStatus = package.Files.OfType<PluginInfoFile>().Select(ContentPackageManager.GetApprovalStatus);
                if (approvalStatus.Any(static status => status.ContainsValue(PluginApprovalStatus.Modified) || status.ContainsValue(PluginApprovalStatus.New)))
                {
                    warningButton = new GUIButton(new RectTransform(Vector2.One, cpLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), string.Empty, style: "CodeModModifiedWarningIcon")
                    {
                        Enabled = false,
                        ToolTip = RichString.Rich(warningTooltip),
                    };
                }
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), cpLayout.RectTransform), package.Name);

                var subscriberCount = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), cpLayout.RectTransform), "")
                {
                    OverflowClip = true,
                    TextColor = GUIStyle.Blue,
                    TextAlignment = Alignment.CenterLeft
                };

                var workshopButton = new GUIButton(new RectTransform(new Vector2(0.0f, 1.0f), cpLayout.RectTransform), style: "GUIButtonSmall", text: TextManager.Get("steamworkshopbutton"))
                {
                    Enabled = false
                };
                workshopButton.RectTransform.MinSize = new Point((int)workshopButton.TextBlock.TextSize.X + GUI.IntScale(10), 0);

                package.TryFetchUgcItem(() =>
                {
                    if (package.UgcItem.TryUnwrap(out Steamworks.Ugc.Item item))
                    {
                        workshopButton.Enabled = true;

                        workshopButton.OnClicked = (btn, userdata) =>
                        {
                            SteamManager.OverlayCustomUrl(SteamWorkshopId.ToWorkshopURL(item.Id.ToString()));
                            return true;
                        };

                        string subsText = ToolBox.FormatCompactNumber((long)item.NumSubscriptions);

                        subscriberCount.Text = subsText;
                        subscriberCount.ToolTip = TextManager.GetWithVariable("WorkshopSubscriberCount.ToolTip", "[subscribercount]", item.NumSubscriptions.ToString());

                        var subscriberIcon = new GUIImage(new RectTransform(new Vector2(0.2f, 1f), subscriberCount.RectTransform, Anchor.CenterLeft), "GUIStarIconBright")
                        {
                            CanBeFocused = false
                        };

                        subscriberCount.TextOffset = new Vector2(subscriberIcon.Rect.Width, GUI.IntScale(2f));

                        if (warningButton != null)
                        {
                            warningButton.Enabled = true;
                            warningButton.ToolTip = RichString.Rich($"{warningTooltip}\n\n{TextManager.Get("codemodwarning.workshopchangelog")}");
                            warningButton.OnClicked = (button, o) =>
                            {
                                SteamManager.OverlayCustomUrl(item.ChangelogUrl);
                                return true;
                            };
                        }
                    }
                });

                new GUIImage(new RectTransform(Vector2.One, cpLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "SlideDownArrow")
                {
                    UserData = arrowUserData,
                    Color = Color.White,
                    CanBeFocused = false
                };
            }
            packageListBox.OnSelected = OnContentPackageSelected;

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0f), msgBoxLayout.RectTransform), TextManager.Get("codemodwarning.body"), wrap: true)
            {
                Wrap = true
            };

            for (int i = 0; i <= 3; i++)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBoxLayout.RectTransform),
                                 TextManager.Get($"codemodwarning.risks.{i}"));
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), msgBoxLayout.RectTransform), TextManager.Get("codemodwarning.footer"), wrap: true);

            GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), messageBox.Content.RectTransform, Anchor.BottomCenter), isHorizontal: false, childAnchor: Anchor.TopCenter);

            GUIButton continueButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.0f), buttonLayout.RectTransform), TextManager.Get("codemodwarning.accept"))
            {
                TextBlock = { AutoScaleHorizontal = true },
                OnClicked = (btn, userdata) =>
                {
                    ContentPackageManager.ApproveContentPackages(contentPackages);
                    onContinue();
                    messageBox.Close();
                    return true;
                }
            };

            // cancel button
            new GUIButton(new RectTransform(new Vector2(0.8f, 0.0f), buttonLayout.RectTransform), TextManager.Get("cancel"))
            {
                OnClicked = (btn, userdata) =>
                {
                    onCancel();
                    messageBox.Close();
                    return true;
                }
            };

            GUIImage warningIcon = new GUIImage(new RectTransform(new Vector2(0.15f), messageBox.InnerFrame.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.TopRight, pivot: Pivot.Center), style: "CommandShortcutNode")
            {
                IgnoreLayoutGroups = true,
                Color = GUIStyle.Red
            };

            float blinkTimer = 0f;
            new GUICustomComponent(new RectTransform(Vector2.Zero, messageBox.InnerFrame.RectTransform), onUpdate: (f, component) =>
            {
                blinkTimer += f;
                if (blinkTimer > 1.0f) { blinkTimer = 0.0f; }
                warningIcon.Color = blinkTimer < 0.75f ? GUIStyle.Red : Microsoft.Xna.Framework.Color.Transparent;
            })
            {
                IgnoreLayoutGroups = true
            };
            return;

            bool OnContentPackageSelected(GUIComponent component, object o)
            {
                if (GUI.MouseOn is GUIButton) { return false; }
                if (component is GUIListBox) { return false; }
                ContentPackage selectedPackage = (ContentPackage)o;
                GUIComponent arrow = component.FindChild(arrowUserData, recursive: true);

                if (arrow != null)
                {
                    arrow.SpriteEffects = SpriteEffects.None;
                }

                foreach (GUIComponent child in packageListBox.Content.Children.ToArray())
                {
                    if (child is GUIListBox && child.UserData == selectedPackage)
                    {
                        packageListBox.RemoveChild(child);
                        return true;
                    }
                }

                if (arrow != null)
                {
                    arrow.SpriteEffects = SpriteEffects.FlipVertically;
                }

                List<PluginAssemblyInfo> pluginInfos = selectedPackage.FilesThatShouldPromptWarning
                    .SelectMany(static f => f.AssemblyInfos)
                    .Where(static p => p.MatchesPlatform)
                    .ToList();


                float listHeight = 0.25f;
                float elementHeight = 1f;
                if (pluginInfos.Count == 2)
                {
                    listHeight = 0.5f;
                    elementHeight = 0.5f;
                }
                else if (pluginInfos.Count > 2)
                {
                    // Partially show third element so user knows there's more
                    listHeight = 0.7f;
                    elementHeight = 0.4f;
                }

                GUIListBox assemblyListBox = new GUIListBox(new RectTransform(new Vector2(1.0f, listHeight), packageListBox.Content.RectTransform))
                {
                    UserData = selectedPackage
                };

                foreach (PluginAssemblyInfo pluginInfo in pluginInfos)
                {
                    PluginApprovalStatus approvalStatus = ContentPackageManager.GetApprovalStatus(pluginInfo);
                    GUIFrame fileFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, elementHeight), assemblyListBox.Content.RectTransform), style: "ListBoxElement");
                    GUIFont font = GUIStyle.LargeFont;

                    string textSingular = "ERROR";
                    Color color = Color.Magenta;

                    switch (approvalStatus)
                    {
                        case PluginApprovalStatus.Moved:
                            textSingular = "Moved";
                            color = GUIStyle.Orange;
                            break;
                        case PluginApprovalStatus.New:
                            textSingular = "New";
                            color = GUIStyle.Red;
                            break;
                        case PluginApprovalStatus.Modified:
                            textSingular = "Modified";
                            color = Color.MediumSlateBlue;
                            break;
                        // The text will never be shown in UI, but the color will be used by the tooltip
                        case PluginApprovalStatus.Approved:
                            textSingular = "Approved";
                            color = GUIStyle.Green;
                            break;
                    }

                    // repeat the text for smooth looping
                    string text = $"{textSingular} {textSingular} {textSingular} {textSingular} ";
                    Vector2 textSize = font.MeasureString(text);
                    Color backgroundColor = color * 0.5f;

                    if (approvalStatus != PluginApprovalStatus.Approved)
                    {
                        new GUICustomComponent(new RectTransform(Vector2.One, fileFrame.RectTransform), onDraw: (sb, component) =>
                        {
                            const float stripeRotation = MathF.PI / 4f; // 45 deg
                            float timer = (float)component.UserData;

                            const float stripeWidth = 0.1f;
                            int stripeWidthPx = (int)(stripeWidth * component.Rect.Width);

                            bool stripe = false;
                            for (float x = component.Rect.X - (timer * 2f) * stripeWidthPx; x < component.Rect.Right + stripeWidthPx; x += stripeWidthPx)
                            {
                                if (stripe)
                                {
                                    RectangleF rect = new RectangleF(
                                        x: x,
                                        y: component.Rect.Center.Y,
                                        width: stripeWidthPx * 0.67f,
                                        height: component.Rect.Height * 2.5f);

                                    GUI.DrawFilledRectangle(sb,
                                                            position: rect.Location,
                                                            size: rect.Size,
                                                            pivot: rect.Size / 2f,
                                                            rotation: -stripeRotation,
                                                            clr: backgroundColor);
                                }
                                else // text
                                {
                                    // I do not know where 0.177f comes from,
                                    // came from trial and error from trying to line up the text
                                    // so it wraps smoothly
                                    float step = textSize.X * 0.177f;
                                    float offset = MathHelper.Lerp(-step, step, timer);

                                    float positionOffset = offset / 2f;

                                    // normally would use a matrix here to rotate the position,
                                    // but since it's always at 45 degrees angle, we can take a
                                    // shortcut and just move it by half to both directions
                                    font.DrawString(sb,
                                                    text: text,
                                                    position: new Vector2(x - positionOffset, component.Rect.Center.Y - positionOffset),
                                                    color: backgroundColor,
                                                    rotation: stripeRotation,
                                                    origin: textSize / 2f,
                                                    scale: 1f,
                                                    spriteEffects: SpriteEffects.None,
                                                    layerDepth: 0f,
                                                    alignment: Alignment.CenterLeft);
                                }

                                stripe = !stripe;
                            }
                        }, onUpdate: static (f, customComponent) =>
                        {
                            float timer = (float)customComponent.UserData;
                            timer += f * 0.15f;

                            if (timer > 1f)
                            {
                                timer = 0f;
                            }

                            customComponent.UserData = timer;
                        })
                        {
                            UserData = 0f,
                            HideElementsOutsideFrame = true
                        };
                    }

                    GUILayoutGroup fileLayout = new GUILayoutGroup(new RectTransform(Vector2.One, fileFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                    {
                        Stretch = true,
                        RelativeSpacing = 0.005f
                    };
                    string fileName = Path.GetFileName(pluginInfo.AssemblyPath.Value);

                    string clientServerLabel = $"codemodwarning.assembly{(
                        pluginInfo.Targets.HasFlag(PluginTargets.Any) ? "shared" :
                        pluginInfo.Targets.HasFlag(PluginTargets.Client) ? "client" : "server" )}";

                    GUIColor labelColor = pluginInfo.Targets.HasFlag(PluginTargets.Any) ? GUIStyle.Yellow :
                        pluginInfo.Targets.HasFlag(PluginTargets.Client) ? GUIStyle.Orange : GUIStyle.Blue;

                    new GUITextBlock(new RectTransform(new Vector2(0.15f, 0.9f), fileLayout.RectTransform), style: "GUIButtonSmallFreeScale", text: TextManager.Get(clientServerLabel), textAlignment: Alignment.Center)
                    {
                        AutoScaleHorizontal = true,
                        TextColor = GUIStyle.TextColorNormal,
                        ToolTip = TextManager.Get($"{clientServerLabel}tooltip"),
                        Color = labelColor
                    };

                    new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), fileLayout.RectTransform), fileName)
                    {
                        ToolTip = RichString.Rich($"{pluginInfo.AssemblyPath.FullPath}\n\n‖color:{color.ToStringHex()}‖{TextManager.Get($"assemblynotification.{approvalStatus}")}‖end‖"),
                        AutoScaleHorizontal = true,
                        Shadow = true
                    };

                    new GUIButton(new RectTransform(new Vector2(0.15f, 1.0f), fileLayout.RectTransform), style: "GUIButtonSmall", text: TextManager.Get("codemodwarning.openfolder"))
                    {
                        TextBlock =
                        {
                            AutoScaleHorizontal = true,
                        },
                        ToolTip = TextManager.Get("codemodwarning.openfoldertooltip"),
                        OnClicked = (btn, userdata) =>
                        {
                            string directory = Path.GetDirectoryName(pluginInfo.AssemblyPath.FullPath) ?? string.Empty;
                            if (string.IsNullOrEmpty(directory)) { return false; }

                            ToolBox.OpenFileWithShell(directory);
                            return true;
                        }
                    };

                    new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), fileLayout.RectTransform), style: "GUIButtonSmall", text: TextManager.Get("codemodwarning.decompile"))
                    {
                        TextBlock =
                        {
                            AutoScaleHorizontal = true,
                        },
                        ToolTip = TextManager.Get("codemodwarning.decompiletooltip"),
                        OnClicked = (btn, userdata) =>
                        {
                            PromptDecompile(pluginInfo);
                            return true;
                        }
                    };
                }

                int index = packageListBox.Content.RectTransform.GetChildIndex(component.RectTransform);

                if (index + 1 < packageListBox.Content.CountChildren)
                {
                    assemblyListBox.RectTransform.RepositionChildInHierarchy(index + 1);
                }

                return true;
            }
        }

        private static void PromptDecompile(PluginAssemblyInfo file)
        {
            GUIMessageBox messageBox = new GUIMessageBox(
                headerText: TextManager.Get("codemodwarning.decompile"),
                text: TextManager.Get("codemodwarning.decompile.body"),
                buttons:
                [
                    TextManager.Get("codemodwarning.decompile.ingametool"),
                    TextManager.Get("codemodwarning.decompile.owntool"),
                    TextManager.Get("cancel")
                ]);

            GUIMessageBox.MessageBoxes.Remove(messageBox);
            GUIMessageBox.MessageBoxes.Insert(0, messageBox);

            messageBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                AssemblyDecompiler.OpenDecompilePopup(file.AssemblyPath.FullPath);
                messageBox.Close();
                return true;
            };
            messageBox.Buttons[1].OnClicked = (btn, userdata) =>
            {
                try
                {
                    ToolBox.OpenFileWithShell(file.AssemblyPath.FullPath);
                }
                catch (Exception)
                {
                    DebugConsole.ThrowError("Failed to open the assembly file. Make sure the decompiler of your choice is installed and associated with .dll files.");
                }
                messageBox.Close();
                return true;
            };

            messageBox.Buttons[2].OnClicked = (btn, userdata) =>
            {
                messageBox.Close();
                return true;
            };
        }

        private void Apply()
        {
            bool hasAnyChanges = HasUnsavedModListChanges();

            List<RegularPackage> regular = enabledRegularModsList.Content.Children.Select(c => c.UserData as RegularPackage)
                .OfType<RegularPackage>().ToList();

            ContentPackageManager.EnabledPackages.SetCore(EnabledCorePackage);

            if (GameSettings.CurrentConfig.AutoOrderModList)
            {
                ContentPackageManager.EnabledPackages.TryReOrderPackagesToMatchDependencies(regular);
            }
            ContentPackageManager.EnabledPackages.SetRegular(regular);

            PopulateInstalledModLists(forceRefreshEnabled: true, refreshDisabled: true);
            ContentPackageManager.LogEnabledRegularPackageErrors();

            enabledCoreDropdown.ButtonTextColor =
                EnabledCorePackage.HasAnyErrors
                    ? GUIStyle.Red
                    : GUIStyle.TextColorNormal;

            GameMain.MainMenuScreen.RefreshDlcNotificationVisibility();

            if (hasAnyChanges)
            {
                SettingsMenu.Instance?.RecreateModSettingsTab(shouldNotifySettings: true);
            }
        }

        public void SetContentPackageEnabledState(ContentPackage installedContentPackage, bool enabled)
        {
            if (enabled)
            {
                if (disabledRegularModsList.Content.Children.FirstOrDefault(c => c.UserData == installedContentPackage) is { } match)
                {
                    match.RectTransform.Parent = enabledRegularModsList.Content.RectTransform;
                }
            }
            else
            {
                if (enabledRegularModsList.Content.Children.FirstOrDefault(c => c.UserData == installedContentPackage) is { } match)
                {
                    match.RectTransform.Parent = disabledRegularModsList.Content.RectTransform;
                }
            }

            if (installedContentPackage.SteamDlcAppId.IsSome())
            {
                SettingsMenu.Instance?.RefreshDlcTab();
            }
        }

        public bool GetContentPackageEnabledState(ContentPackage installedContentPackage)
        {
            return enabledRegularModsList.Content.Children.Any(c => c.UserData == installedContentPackage);
        }

        /// <summary>
        /// Checks if the mod list in the UI differs from the currently applied enabled packages.
        /// </summary>
        public bool HasUnsavedModListChanges()
        {
            if (EnabledCorePackage != ContentPackageManager.EnabledPackages.Core)
            {
                return true;
            }
            var uiEnabledMods = enabledRegularModsList.Content.Children
                .Select(static c => c.UserData as RegularPackage)
                .OfType<RegularPackage>();
            return !uiEnabledMods.SequenceEqual(ContentPackageManager.EnabledPackages.Regular);
        }
    }
}
