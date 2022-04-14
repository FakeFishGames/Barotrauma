#nullable enable
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using ItemOrPackage = Barotrauma.Either<Steamworks.Ugc.Item, Barotrauma.ContentPackage>;

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

        private readonly GUILayoutGroup tabber;
        private readonly Dictionary<Tab, (GUIButton Button, GUIFrame Content)> tabContents;

        private readonly GUIFrame contentFrame;

        private CorePackage EnabledCorePackage => enabledCoreDropdown.SelectedData as CorePackage ?? throw new Exception("Valid core package not selected");

        private readonly GUIDropDown enabledCoreDropdown;
        private readonly GUIListBox enabledRegularModsList;
        private readonly GUIListBox disabledRegularModsList;
        private readonly Action<ItemOrPackage> onInstalledInfoButtonHit;
        private readonly GUITextBox modsListFilter;
        private readonly GUIButton bulkUpdateButton;

        private CancellationTokenSource taskCancelSrc = new CancellationTokenSource();
        private readonly HashSet<SteamManager.Workshop.ItemThumbnail> itemThumbnails = new HashSet<SteamManager.Workshop.ItemThumbnail>();

        private readonly GUIListBox popularModsList;
        private readonly GUIListBox selfModsList;

        private uint memSubscribedModCount = 0;
        
        public MutableWorkshopMenu(GUIFrame parent) : base(parent)
        {
            var mainLayout
                = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform), isHorizontal: false);

            tabber = new GUILayoutGroup(new RectTransform((1.0f, 0.05f), mainLayout.RectTransform), isHorizontal: true)
                { Stretch = true };
            tabContents = new Dictionary<Tab, (GUIButton Button, GUIFrame Content)>();

            contentFrame = new GUIFrame(new RectTransform((1.0f, 0.95f), mainLayout.RectTransform), style: null);

            new GUICustomComponent(new RectTransform(Vector2.Zero, mainLayout.RectTransform),
                onUpdate: (f, component) => UpdateSubscribedModInstalls());
            
            CreateInstalledModsTab(
                out enabledCoreDropdown,
                out enabledRegularModsList,
                out disabledRegularModsList,
                out onInstalledInfoButtonHit,
                out modsListFilter,
                out bulkUpdateButton);
            CreatePopularModsTab(out popularModsList);
            CreatePublishTab(out selfModsList);

            SelectTab(Tab.InstalledMods);
        }

        private void UpdateSubscribedModInstalls()
        {
            if (!SteamManager.IsInitialized) { return; }

            uint numSubscribedMods = Steamworks.SteamUGC.NumSubscribedItems;
            if (numSubscribedMods == memSubscribedModCount) { return; }
            memSubscribedModCount = numSubscribedMods;

            var subscribedIds = Steamworks.SteamUGC.GetSubscribedItems().ToHashSet();
            var installedIds = ContentPackageManager.WorkshopPackages.Select(p => p.SteamWorkshopId).ToHashSet();
            foreach (var id in subscribedIds.Where(id2 => !installedIds.Contains(id2)))
            {
                Steamworks.Ugc.Item item = new Steamworks.Ugc.Item(id);
                if (!item.IsDownloading && !SteamManager.Workshop.IsInstalling(item))
                {
                    SteamManager.Workshop.DownloadModThenEnqueueInstall(item);
                }
            }

            TaskPool.Add("RemoveUnsubscribedItems", SteamManager.Workshop.GetPublishedItems(), t =>
            {
                if (!t.TryGetResult(out ISet<Steamworks.Ugc.Item> publishedItems)) { return; }

                var allRequiredInstalled = subscribedIds.Union(publishedItems.Select(it => it.Id)).ToHashSet();
                foreach (var id in installedIds.Where(id2 => !allRequiredInstalled.Contains(id2)))
                {
                    Steamworks.Ugc.Item item = new Steamworks.Ugc.Item(id);
                    SteamManager.Workshop.Uninstall(item);
                }
            });
        }
        
        private void SwitchContent(GUIFrame newContent)
        {
            contentFrame.Children.ForEach(c => c.Visible = false);
            newContent.Visible = true;
        }

        public void SelectTab(Tab tab)
        {
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
                case Tab.PopularMods:
                    PopulateItemList(popularModsList, SteamManager.Workshop.GetPopularItems(), includeSubscribeButton: true);
                    break;
                case Tab.Publish:
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
                    SelectTab(tab);
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

        private static (GUILayoutGroup Left, GUIFrame center, GUILayoutGroup Right) CreateSidebars(
            GUIComponent parent,
            float leftWidth = 0.3875f,
            float centerWidth = 0.025f,
            float rightWidth = 0.5875f,
            bool split = false,
            float height = 1.0f)
        {
            GUILayoutGroup layout = new GUILayoutGroup(new RectTransform((1.0f, height), parent.RectTransform), isHorizontal: true);
            GUILayoutGroup left = new GUILayoutGroup(new RectTransform((leftWidth, 1.0f), layout.RectTransform), isHorizontal: false);
            var center = new GUIFrame(new RectTransform((centerWidth, 1.0f), layout.RectTransform), style: null);
            if (split)
            {
                new GUICustomComponent(new RectTransform(Vector2.One, center.RectTransform),
                    onDraw: (sb, c) =>
                    {
                        sb.DrawLine((c.Rect.Center.X, c.Rect.Top), (c.Rect.Center.X, c.Rect.Bottom), GUIStyle.TextColorDim, 2f);
                    });
            }
            GUILayoutGroup right = new GUILayoutGroup(new RectTransform((rightWidth, 1.0f), layout.RectTransform), isHorizontal: false);
            return (left, center, right);
        }

        private void HandleDraggingAcrossModLists(GUIListBox from, GUIListBox to)
        {
            if (to.Rect.Contains(PlayerInput.MousePosition) && from.DraggedElement != null)
            {
                //move the dragged elements to the index determined previously
                var draggedElement = from.DraggedElement;
                
                var selected = from.AllSelected.ToList();
                selected.Sort((a, b) => from.Content.GetChildIndex(a) - from.Content.GetChildIndex(b));
                
                float oldCount = to.Content.CountChildren;
                float newCount = oldCount + selected.Count;
                
                var offset = draggedElement.RectTransform.AbsoluteOffset;
                offset += from.Content.Rect.Location;
                offset -= to.Content.Rect.Location;
                
                for (int i = 0; i < selected.Count; i++)
                {
                    var c = selected[i];
                    c.Parent.RemoveChild(c);
                    c.RectTransform.Parent = to.Content.RectTransform;
                    c.RectTransform.RepositionChildInHierarchy((int)oldCount+i);
                }

                from.DraggedElement = null;
                from.Deselect();
                from.RecalculateChildren();
                from.RectTransform.RecalculateScale(true);
                to.RecalculateChildren();
                to.RectTransform.RecalculateScale(true);
                to.Select(selected);
                
                //recalculate the dragged element's offset so it doesn't jump around
                draggedElement.RectTransform.AbsoluteOffset = offset;
                
                to.DraggedElement = draggedElement;

                to.BarScroll *= (oldCount / newCount);
            }
        }

        private Action? currentSwapFunc = null;

        private void SetSwapFunc(GUIListBox from, GUIListBox to)
        {
            currentSwapFunc = () =>
            {
                to.Deselect();
                var selected = from.AllSelected.ToArray();
                foreach (var frame in selected)
                {
                    frame.Parent.RemoveChild(frame);
                    frame.RectTransform.Parent = to.Content.RectTransform;
                }
                from.RecalculateChildren();
                from.RectTransform.RecalculateScale(true);
                to.RecalculateChildren();
                to.RectTransform.RecalculateScale(true);
                to.Select(selected);
            };
        }
        
        private void CreateInstalledModsTab(
            out GUIDropDown enabledCoreDropdown,
            out GUIListBox enabledRegularModsList,
            out GUIListBox disabledRegularModsList,
            out Action<ItemOrPackage> onInstalledInfoButtonHit,
            out GUITextBox modsListFilter,
            out GUIButton bulkUpdateButton)
        {
            GUIFrame content = CreateNewContentFrame(Tab.InstalledMods);
            
            CreateWorkshopItemDetailContainer(
                content,
                out var outerContainer,
                onSelected: (itemOrPackage, selectedFrame) =>
                {
                    if (itemOrPackage.TryGet(out Steamworks.Ugc.Item item)) { PopulateFrameWithItemInfo(item, selectedFrame); }
                },
                onDeselected: () => PopulateInstalledModLists(),
                out onInstalledInfoButtonHit, out var deselect);

            GUILayoutGroup mainLayout =
                new GUILayoutGroup(new RectTransform(Vector2.One, outerContainer.Content.RectTransform), childAnchor: Anchor.TopCenter);
            mainLayout.RectTransform.SetAsFirstChild();

            var (topLeft, _, topRight) = CreateSidebars(mainLayout, centerWidth: 0.05f, leftWidth: 0.475f, rightWidth: 0.475f, height: 0.13f);
            topLeft.Stretch = true;
            Label(topLeft, TextManager.Get("enabledcore"), GUIStyle.SubHeadingFont, heightScale: 1.0f);
            enabledCoreDropdown = Dropdown<CorePackage>(topLeft,
                (p) => p.Name,
                ContentPackageManager.CorePackages.ToArray(),
                ContentPackageManager.EnabledPackages.Core!,
                (p) => { },
                heightScale: 1.0f / 13.0f);
            Label(topLeft, "", GUIStyle.SubHeadingFont, heightScale: 1.0f);
            topRight.ChildAnchor = Anchor.CenterLeft;

            var topRightButtons = new GUILayoutGroup(new RectTransform((1.0f, 0.5f), topRight.RectTransform),
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            void padTopRight(float width=1.0f)
            {
                new GUIFrame(new RectTransform((width, 1.0f), topRightButtons.RectTransform), style: null);
            }

            padTopRight();
            //TODO: put stuff here
            padTopRight(width: 3.0f);
            var refreshListsButton
                = new GUIButton(
                    new RectTransform(Vector2.One, topRightButtons.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    text: "", style: "GUIReloadButton")
                {
                    OnClicked = (b, o) =>
                    {
                        PopulateInstalledModLists();
                        return false;
                    },
                    ToolTip = TextManager.Get("RefreshModLists")
                };
            bulkUpdateButton
                = new GUIButton(
                    new RectTransform(Vector2.One, topRightButtons.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    text: "", style: "GUIUpdateButton")
                {
                    OnClicked = (b, o) =>
                    {
                        BulkDownloader.PrepareUpdates();
                        return false;
                    },
                    Enabled = false
                };
            padTopRight(width: 0.1f);

            var (left, center, right) = CreateSidebars(mainLayout, centerWidth: 0.05f, leftWidth: 0.475f, rightWidth: 0.475f, height: 0.8f);
            right.ChildAnchor = Anchor.TopRight;

            //enabled mods
            Label(left, TextManager.Get("enabledregular"), GUIStyle.SubHeadingFont);
            var enabledModsList = new GUIListBox(new RectTransform((1.0f, 0.93f), left.RectTransform))
            {
                CurrentDragMode = GUIListBox.DragMode.DragOutsideBox,
                CurrentSelectMode = GUIListBox.SelectMode.RequireShiftToSelectMultiple,
                HideDraggedElement = true
            };
            enabledRegularModsList = enabledModsList;
            
            //disabled mods
            Label(right, TextManager.Get("disabledregular"), GUIStyle.SubHeadingFont);
            var disabledModsList = new GUIListBox(new RectTransform((1.0f, 0.93f), right.RectTransform))
            {
                CurrentDragMode = GUIListBox.DragMode.DragOutsideBox,
                CurrentSelectMode = GUIListBox.SelectMode.RequireShiftToSelectMultiple,
                HideDraggedElement = true
            };
            disabledRegularModsList = disabledModsList;
            
            var centerButton =
                new GUIButton(
                    new RectTransform(Vector2.One * 0.95f, center.RectTransform, scaleBasis: ScaleBasis.BothWidth,
                        anchor: Anchor.Center),
                    style: "GUIButtonToggleLeft")
                {
                    Visible = false,
                    OnClicked = (button, o) =>
                    {
                        currentSwapFunc?.Invoke();
                        return false;
                    }
                };

            enabledModsList.OnSelected = (frame, o) =>
            {
                disabledModsList.Deselect();

                centerButton.Visible = true;
                centerButton.ApplyStyle(GUIStyle.GetComponentStyle("GUIButtonToggleRight"));

                SetSwapFunc(enabledModsList, disabledModsList);
                
                return true;
            };
            disabledModsList.OnSelected = (frame, o) =>
            {
                enabledModsList.Deselect();
                
                centerButton.Visible = true;
                centerButton.ApplyStyle(GUIStyle.GetComponentStyle("GUIButtonToggleLeft"));
                
                SetSwapFunc(disabledModsList, enabledModsList);
                
                return true;
            };
            
            var searchBox = CreateSearchBox(mainLayout, width: 0.5f);
            modsListFilter = searchBox;

            new GUICustomComponent(new RectTransform(Vector2.Zero, content.RectTransform),
                onUpdate: (f, component) =>
                {
                    HandleDraggingAcrossModLists(enabledModsList, disabledModsList);
                    HandleDraggingAcrossModLists(disabledModsList, enabledModsList);
                    if (PlayerInput.PrimaryMouseButtonClicked()
                        && !GUI.IsMouseOn(enabledModsList)
                        && !GUI.IsMouseOn(disabledModsList)
                        && GUIContextMenu.CurrentContextMenu is null)
                    {
                        enabledModsList.Deselect();
                        disabledModsList.Deselect();
                    }
                    else if (!PlayerInput.IsCtrlDown() && !PlayerInput.IsShiftDown() && PlayerInput.DoubleClicked())
                    {
                        currentSwapFunc?.Invoke();
                    }
                },
                onDraw: (spriteBatch, component) =>
                {
                    enabledModsList.DraggedElement?.DrawManually(spriteBatch, true, true);
                    disabledModsList.DraggedElement?.DrawManually(spriteBatch, true, true);
                });
        }

        protected override void UpdateModListItemVisibility()
        {
            string str = modsListFilter.Text;
            enabledRegularModsList.Content.Children.Concat(disabledRegularModsList.Content.Children)
                .ForEach(c => c.Visible = str.IsNullOrWhiteSpace()
                                          || (c.UserData is ContentPackage p
                                              && p.Name.Contains(str, StringComparison.OrdinalIgnoreCase)));
        }

        private void PrepareToShowModInfo(ContentPackage mod)
        {
            TaskPool.Add($"PrepareToShow{mod.SteamWorkshopId}Info", SteamManager.Workshop.GetItem(mod.SteamWorkshopId),
                t =>
                {
                    if (!t.TryGetResult(out Steamworks.Ugc.Item? item)) { return; }
                    if (item is null) { return; }
                    onInstalledInfoButtonHit(item.Value);
                });
        }
        
        public void PopulateInstalledModLists(bool forceRefreshEnabled = false, bool refreshDisabled = true)
        {
            bulkUpdateButton.Enabled = false;
            bulkUpdateButton.ToolTip = "";
            ContentPackageManager.UpdateContentPackageList();
            
            SwapDropdownValues<CorePackage>(enabledCoreDropdown,
                (p) => p.Name,
                ContentPackageManager.CorePackages.ToArray(),
                ContentPackageManager.EnabledPackages.Core!,
                (p) => { });
            
            void addRegularModToList(RegularPackage mod, GUIListBox list)
            {
                var modFrame = new GUIFrame(new RectTransform((1.0f, 0.08f), list.Content.RectTransform),
                    style: "ListBoxElement")
                {
                    UserData = mod
                };

                var contextMenuHandler = new GUICustomComponent(new RectTransform(Vector2.Zero, modFrame.RectTransform),
                    onUpdate: (f, component) =>
                    {
                        var parentList = modFrame.Parent?.Parent?.Parent as GUIListBox; //lovely jank :)
                        if (parentList is null) { return; }
                        if (GUI.MouseOn == modFrame && parentList.DraggedElement is null && PlayerInput.SecondaryMouseButtonClicked())
                        {
                            if (!parentList.AllSelected.Contains(modFrame)) { parentList.Select(parentList.Content.GetChildIndex(modFrame)); }
                            static void noop() { }

                            List<ContextMenuOption> contextMenuOptions = new List<ContextMenuOption>();
                            if (ContentPackageManager.WorkshopPackages.Contains(mod))
                            {
                                contextMenuOptions.Add(
                                    new ContextMenuOption("ViewWorkshopModDetails".ToIdentifier(), isEnabled: true, onSelected: () => PrepareToShowModInfo(mod)));
                            }

                            Identifier swapLabel
                                = ((parentList == enabledRegularModsList ? "Disable" : "Enable")
                                + (parentList.AllSelected.Count > 1 ? "SelectedWorkshopMods" : "WorkshopMod"))
                                .ToIdentifier();
                            
                            contextMenuOptions.Add(new ContextMenuOption(swapLabel,
                                isEnabled: true, onSelected: currentSwapFunc ?? noop));
                            
                            GUIContextMenu.CreateContextMenu(
                                pos: PlayerInput.MousePosition,
                                header:  ToolBox.LimitString(mod.Name, GUIStyle.SubHeadingFont, GUI.IntScale(300f)),
                                headerColor: null,
                                contextMenuOptions.ToArray());
                        }
                    });
                
                var frameContent = new GUILayoutGroup(new RectTransform((0.95f, 0.9f), modFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };
                
                var dragIndicator = new GUIButton(new RectTransform((0.5f, 0.5f), frameContent.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIDragIndicator")
                {
                    CanBeFocused = false
                };

                var modNameScissor = new GUIScissorComponent(new RectTransform((0.8f, 1.0f), frameContent.RectTransform))
                {
                    CanBeFocused = false
                };
                var modName = new GUITextBlock(new RectTransform(Vector2.One, modNameScissor.Content.RectTransform),
                    text: mod.Name)
                {
                    CanBeFocused = false
                };
                if (mod.Errors.Any())
                {
                    CreateModErrorInfo(mod, modFrame, modName);
                }
                if (ContentPackageManager.LocalPackages.Contains(mod))
                {
                    var editButton = new GUIButton(new RectTransform(Vector2.One, frameContent.RectTransform, scaleBasis: ScaleBasis.Smallest), "",
                            style: "WorkshopMenu.EditButton")
                        {
                            OnClicked = (button, o) =>
                            {
                                ToolBox.OpenFileWithShell(mod.Dir);
                                return false;
                            },
                            ToolTip = TextManager.Get("OpenLocalModInExplorer")
                        };
                }
                else if (ContentPackageManager.WorkshopPackages.Contains(mod))
                {
                    var infoButton = new GUIButton(
                        new RectTransform(Vector2.One, frameContent.RectTransform, scaleBasis: ScaleBasis.Smallest), "",
                        style: null)
                    {
                        CanBeSelected = false,
                        OnClicked = (button, o) =>
                        {
                            PrepareToShowModInfo(mod);
                            return false;
                        }
                    };
                    if (!SteamManager.IsInitialized)
                    {
                        infoButton.Enabled = false;
                    }
                    TaskPool.Add(
                        $"DetermineUpdateRequired{mod.SteamWorkshopId}",
                        mod.IsUpToDate(),
                        t =>
                        {
                            if (!t.TryGetResult(out bool isUpToDate)) { return; }

                            if (!isUpToDate)
                            {
                                infoButton.CanBeSelected = true;
                                infoButton.ApplyStyle(GUIStyle.ComponentStyles["WorkshopMenu.InfoButtonUpdate"]);
                                infoButton.ToolTip = TextManager.Get("ViewModDetailsUpdateAvailable");
                                bulkUpdateButton.Enabled = true;
                                bulkUpdateButton.ToolTip = TextManager.Get("ModUpdatesAvailable");
                            }
                        });
                }
            }

            void addRegularModsToList(IEnumerable<RegularPackage> mods, GUIListBox list)
            {
                list.ClearChildren();
                foreach (var mod in mods)
                {
                    addRegularModToList(mod, list);
                }
            }

            var enabledMods =
                (forceRefreshEnabled || (enabledRegularModsList.Content.CountChildren + disabledRegularModsList.Content.CountChildren == 0)
                ? ContentPackageManager.EnabledPackages.Regular
                : enabledRegularModsList.Content.Children
                    .Select(c => c.UserData)
                    .OfType<RegularPackage>()
                    .Where(p => ContentPackageManager.RegularPackages.Contains(p)))
                .ToArray();
            var disabledMods = ContentPackageManager.RegularPackages.Where(p => !enabledMods.Contains(p));
            
            addRegularModsToList(enabledMods, enabledRegularModsList);
            if (refreshDisabled) { addRegularModsToList(disabledMods, disabledRegularModsList); }

            TaskPool.Add(
                $"DetermineWorkshopModIcons",
                SteamManager.Workshop.GetPublishedItems(),
                t =>
                {
                    if (!t.TryGetResult(out ISet<Steamworks.Ugc.Item> items)) { return; }
                    var ids = items.Select(it => it.Id).ToHashSet();

                    foreach (var child in enabledRegularModsList.Content.Children
                                 .Concat(disabledRegularModsList.Content.Children))
                    {
                        var mod = child.UserData as RegularPackage;
                        if (mod is null || !ContentPackageManager.WorkshopPackages.Contains(mod)) { continue; }
                        
                        var btn = child.GetChild<GUILayoutGroup>()?.GetAllChildren<GUIButton>().Last();
                        if (btn is null) { continue; }
                        if (btn.Style != null) { continue; }

                        btn.ApplyStyle(
                            GUIStyle.GetComponentStyle(
                                ids.Contains(mod.SteamWorkshopId)
                                    ? "WorkshopMenu.PublishedIcon"
                                    : "WorkshopMenu.DownloadedIcon"));
                        btn.ToolTip = TextManager.Get(
                            ids.Contains(mod.SteamWorkshopId)
                                ? "PublishedWorkshopMod"
                                : "DownloadedWorkshopMod");
                        btn.HoverCursor = CursorState.Default;
                    }
                });
            
            UpdateModListItemVisibility();
        }

        private void CreatePopularModsTab(out GUIListBox popularModsList)
        {
            GUIFrame content = CreateNewContentFrame(Tab.PopularMods);
            if (!SteamManager.IsInitialized)
            {
                tabContents[Tab.PopularMods].Button.Enabled = false;
            }
            GUIFrame listFrame = new GUIFrame(new RectTransform((1.0f, 0.95f), content.RectTransform), style: null);
            CreateWorkshopItemList(listFrame, out _, out popularModsList, onSelected: PopulateFrameWithItemInfo);
            new GUIButton(new RectTransform((1.0f, 0.05f), content.RectTransform, Anchor.BottomLeft),
                style: "GUIButtonSmall", text: TextManager.Get("FindModsButton"))
            {
                OnClicked = (button, o) =>
                {
                    SteamManager.OverlayCustomURL($"https://steamcommunity.com/app/{SteamManager.AppID}/workshop/");
                    return false;
                }
            };
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

        public void Apply()
        {
            ContentPackageManager.EnabledPackages.SetCore(EnabledCorePackage);
            ContentPackageManager.EnabledPackages.SetRegular(enabledRegularModsList.Content.Children
                .Select(c => c.UserData as RegularPackage).OfType<RegularPackage>().ToArray());
            PopulateInstalledModLists(forceRefreshEnabled: true, refreshDisabled: true);
            ContentPackageManager.LogEnabledRegularPackageErrors();
        }
    }
}
