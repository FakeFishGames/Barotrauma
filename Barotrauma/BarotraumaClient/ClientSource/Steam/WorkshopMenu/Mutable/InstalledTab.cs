#nullable enable
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ItemOrPackage = Barotrauma.Either<Steamworks.Ugc.Item, Barotrauma.ContentPackage>;

namespace Barotrauma.Steam
{
    sealed partial class MutableWorkshopMenu : WorkshopMenu
    {
        private CorePackage EnabledCorePackage => enabledCoreDropdown.SelectedData as CorePackage ?? throw new Exception("Valid core package not selected");

        public bool ViewingItemDetails { get; private set; }

        private readonly GUIDropDown enabledCoreDropdown;
        private readonly GUIListBox enabledRegularModsList;
        private readonly GUIListBox disabledRegularModsList;
        private readonly Action<ItemOrPackage> onInstalledInfoButtonHit;
        private readonly GUITextBox modsListFilter;
        private readonly Dictionary<Filter, GUITickBox> modsListFilterTickboxes;
        private readonly GUIButton bulkUpdateButton;

        private GUIComponent? draggedElement = null;
        private GUIListBox? draggedElementOrigin = null;

        private void UpdateSubscribedModInstalls()
        {
            if (!SteamManager.IsInitialized) { return; }

            uint numSubscribedMods = SteamManager.GetNumSubscribedItems();
            if (numSubscribedMods == memSubscribedModCount) { return; }
            memSubscribedModCount = numSubscribedMods;

            var subscribedIds = SteamManager.GetSubscribedItems().ToHashSet();
            var installedIds = ContentPackageManager.WorkshopPackages
                .Select(p => p.UgcId)
                .NotNone()
                .OfType<SteamWorkshopId>()
                .Select(id => id.Value)
                .ToHashSet();
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
                bool needsRefresh = false;
                foreach (var id in installedIds.Where(id2 => !allRequiredInstalled.Contains(id2)))
                {
                    Steamworks.Ugc.Item item = new Steamworks.Ugc.Item(id);
                    SteamManager.Workshop.Uninstall(item);
                    needsRefresh = true;
                }

                if (needsRefresh)
                {
                    PopulateInstalledModLists();
                }
            });
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
        private GUISoundType? swapSoundType = null;

        private void PlaySwapSound()
        {
            SoundPlayer.PlayUISound(swapSoundType);
        }

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

            if (to == enabledRegularModsList)
            {
                swapSoundType = GUISoundType.Increase;
            }
            else if (to == disabledRegularModsList)
            {
                swapSoundType = GUISoundType.Decrease;
            }
            else
            {
                swapSoundType = null;
            }
        }

        private void CreateInstalledModsTab(
            out GUIDropDown enabledCoreDropdown,
            out GUIListBox enabledRegularModsList,
            out GUIListBox disabledRegularModsList,
            out Action<ItemOrPackage> onInstalledInfoButtonHit,
            out GUITextBox modsListFilter,
            out Dictionary<Filter, GUITickBox> modsListFilterTickboxes,
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
                HideDraggedElement = true,
                PlaySoundOnSelect = true,
                SoundOnDragStart = GUISoundType.Select,
                SoundOnDragStop = GUISoundType.Increase,
            };
            enabledRegularModsList = enabledModsList;
            
            //disabled mods
            Label(right, TextManager.Get("disabledregular"), GUIStyle.SubHeadingFont);
            var disabledModsList = new GUIListBox(new RectTransform((1.0f, 0.93f), right.RectTransform))
            {
                CurrentDragMode = GUIListBox.DragMode.DragOutsideBox,
                CurrentSelectMode = GUIListBox.SelectMode.RequireShiftToSelectMultiple,
                HideDraggedElement = true,
                PlaySoundOnSelect = true,
                SoundOnDragStart = GUISoundType.Select,
                SoundOnDragStop = GUISoundType.Decrease,
            };
            disabledRegularModsList = disabledModsList;
            
            var centerButton =
                new GUIButton(
                    new RectTransform(Vector2.One * 0.95f, center.RectTransform, scaleBasis: ScaleBasis.BothWidth,
                        anchor: Anchor.Center),
                    style: "GUIButtonToggleLeft")
                {
                    PlaySoundOnSelect = false,
                    Visible = false,
                    OnClicked = (button, o) =>
                    {
                        if (currentSwapFunc != null)
                        {
                            PlaySwapSound();
                            currentSwapFunc.Invoke();
                        }
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

            var filterContainer = new GUILayoutGroup(NewItemRectT(mainLayout, heightScale: 1.0f), isHorizontal: true)
                { Stretch = true, RelativeSpacing = 0.01f };

            void padFilterContainer(float width = 0.25f)
                => new GUIFrame(new RectTransform((width, 1.0f), filterContainer!.RectTransform), style: null);
            
            GUIButton filterLayoutButton(string style)
                => new GUIButton(
                    new RectTransform(Vector2.One, filterContainer!.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    "", style: style);
            
            padFilterContainer(width: 0.2f);
            var loadPresetBtn = filterLayoutButton("OpenButton");
            loadPresetBtn.ToolTip = TextManager.Get("LoadModListPresetHeader");
            loadPresetBtn.OnClicked = OpenLoadPreset;
            var savePresetBtn = filterLayoutButton("SaveButton");
            savePresetBtn.ToolTip = TextManager.Get("SaveModListPresetHeader");
            savePresetBtn.OnClicked = OpenSavePreset;
            padFilterContainer(width: 0.05f);
            var searchRectT = new RectTransform((0.5f, 1.0f), filterContainer.RectTransform);
            var searchBox = CreateSearchBox(searchRectT);
            modsListFilter = searchBox;

            var filterTickboxes = new Dictionary<Filter, GUITickBox>();
            modsListFilterTickboxes = filterTickboxes;

            var filterTickboxesDropdown
                = filterLayoutButton("SetupVisibilityButton");
            var filterTickboxesContainer
                = new GUIFrame(new RectTransform((0.3f, 0.2f), content.RectTransform,
                    scaleBasis: ScaleBasis.BothWidth), style: "InnerFrame");
            var filterTickboxesUpdater
                = new GUICustomComponent(new RectTransform(Vector2.Zero, content.RectTransform),
                    onUpdate: (f, component) =>
                    {
                        filterTickboxesContainer.Visible = filterTickboxesDropdown.Selected;
                        filterTickboxesContainer.RectTransform.AbsoluteOffset
                            = (filterTickboxesDropdown.Rect.Location - content.Rect.Location)
                                + (filterTickboxesDropdown.Rect.Width / 2, 0)
                                - (filterTickboxesContainer.Rect.Size.ToVector2() * (0.5f, 1.0f)).ToPoint();
                        filterTickboxesContainer.RectTransform.NonScaledSize
                            = new Point(filterTickboxes.Select(tb => (int)tb.Value.Font.MeasureString(tb.Value.GetChild<GUITextBlock>().Text).X).Max(),
                                  filterTickboxes.Select(tb => tb.Value.Rect.Height).Aggregate((a,b) => a+b))
                              +(filterTickboxes.Values.First().Rect.Height * 4, filterTickboxes.Values.First().Rect.Height / 2);
                        if (PlayerInput.PrimaryMouseButtonClicked()
                            && !GUI.IsMouseOn(filterTickboxesDropdown)
                            && !GUI.IsMouseOn(filterTickboxesContainer))
                        {
                            filterTickboxesDropdown.Selected = false;
                        }
                    });

            var filterTickboxesLayout
                = new GUILayoutGroup(new RectTransform(Vector2.One * 0.95f, filterTickboxesContainer.RectTransform, Anchor.Center));

            void addFilterTickbox(Filter filter, string? style, bool selected)
            {
                var tickbox = new GUITickBox(NewItemRectT(filterTickboxesLayout!, heightScale: 0.5f), "")
                {
                    Selected = selected,
                    OnSelected = _ =>
                    {
                        UpdateModListItemVisibility();
                        return true;
                    }
                };
                filterTickboxes!.Add(filter, tickbox);
                var text = new GUITextBlock(new RectTransform((1.0f, 1.0f), tickbox.RectTransform, Anchor.CenterRight)
                    {
                        AbsoluteOffset = (-tickbox.Box.Rect.Width * 2, 0),
                    },
                    TextManager.Get($"ModFilter.{filter}"))
                {
                    CanBeFocused = false
                };
                var icon = new GUIFrame(
                    new RectTransform(Vector2.One, text.RectTransform, Anchor.CenterLeft, Pivot.CenterRight,
                        scaleBasis: ScaleBasis.BothHeight), style: style)
                {
                    CanBeFocused = false
                };
            }
            
            addFilterTickbox(Filter.ShowLocal, "WorkshopMenu.EditButton", selected: true);
            addFilterTickbox(Filter.ShowWorkshop, "WorkshopMenu.DownloadedIcon", selected: true);
            addFilterTickbox(Filter.ShowPublished, "WorkshopMenu.PublishedIcon", selected: true);
            addFilterTickbox(Filter.ShowOnlySubs, null, selected: false);
            addFilterTickbox(Filter.ShowOnlyItemAssemblies, null, selected: false);
            
            padFilterContainer();

            new GUICustomComponent(new RectTransform(Vector2.Zero, content.RectTransform),
                onUpdate: (f, component) =>
                {
                    HandleDraggingAcrossModLists(enabledModsList, disabledModsList);
                    HandleDraggingAcrossModLists(disabledModsList, enabledModsList);
                    UpdateDraggingSounds();
                    
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

            void UpdateDraggingSounds()
            {
                if (draggedElement != null)
                {
                    if (enabledModsList.DraggedElement == null && disabledModsList.DraggedElement == null)
                    {
                        SetDragOrigin(null);
                    }
                    CheckDragStopSound(enabledModsList);
                    CheckDragStopSound(disabledModsList);
                }
                else if (enabledModsList.DraggedElement != null)
                {
                    SetDragOrigin(enabledModsList);
                }
                else if (disabledModsList.DraggedElement != null)
                {
                    SetDragOrigin(disabledModsList);
                }

                void SetDragOrigin(GUIListBox? listBox)
                {
                    draggedElement = listBox?.DraggedElement;
                    draggedElementOrigin = listBox;
                }

                void CheckDragStopSound(GUIListBox listBox)
                {
                    listBox.PlaySoundOnDragStop = listBox.DraggedElement != null && draggedElementOrigin != listBox;
                }
            }
        }

        protected override void UpdateModListItemVisibility()
        {
            string str = modsListFilter.Text;
            enabledRegularModsList.Content.Children.Concat(disabledRegularModsList.Content.Children)
                .ForEach(c => c.Visible = !(c.UserData is ContentPackage p)
                                          || ModNameMatches(p, str) && ModMatchesTickboxes(p, c));
        }

        private bool ModMatchesTickboxes(ContentPackage p, GUIComponent guiItem)
        {
            var iconBtn = guiItem.GetChild<GUILayoutGroup>()?.GetAllChildren<GUIButton>().Last();

            bool matches = false;
            matches |= modsListFilterTickboxes[Filter.ShowLocal].Selected
                       && ContentPackageManager.LocalPackages.Contains(p);
            matches |= modsListFilterTickboxes[Filter.ShowPublished].Selected
                       && (ContentPackageManager.WorkshopPackages.Contains(p)
                            && iconBtn?.Style?.Identifier == "WorkshopMenu.PublishedIcon");
            matches |= modsListFilterTickboxes[Filter.ShowWorkshop].Selected
                       && (ContentPackageManager.WorkshopPackages.Contains(p)
                            && iconBtn?.Style?.Identifier != "WorkshopMenu.PublishedIcon");

            if (modsListFilterTickboxes[Filter.ShowOnlySubs].Selected
                && modsListFilterTickboxes[Filter.ShowOnlyItemAssemblies].Selected
                && p.Files.All(f => f is BaseSubFile || f is ItemAssemblyFile))
            {
                //Both the subs-only tickbox and the item-assembly-only tickbox
                //are enabled, and all files match either of them so show this mod
            }
            else if (modsListFilterTickboxes[Filter.ShowOnlySubs].Selected
                && p.Files.Any(f => !(f is BaseSubFile)))
            {
                matches = false;
            }
            else if (modsListFilterTickboxes[Filter.ShowOnlyItemAssemblies].Selected
                && p.Files.Any(f => !(f is ItemAssemblyFile)))
            {
                matches = false;
            }

            return matches;
        }

        private void PrepareToShowModInfo(ContentPackage mod)
        {
            if (!mod.UgcId.TryUnwrap(out var ugcId)
                || !(ugcId is SteamWorkshopId workshopId)) { return; }
            TaskPool.Add($"PrepareToShow{mod.UgcId}Info", SteamManager.Workshop.GetItem(workshopId.Value),
                t =>
                {
                    if (!t.TryGetResult(out Steamworks.Ugc.Item? item)) { return; }
                    if (item is null) { return; }
                    onInstalledInfoButtonHit(item.Value);
                });
        }
        
        public void PopulateInstalledModLists(bool forceRefreshEnabled = false, bool refreshDisabled = true)
        {
            ViewingItemDetails = false;
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

                            var labelConditions
                                = (parentList == enabledRegularModsList, parentList.AllSelected.Count > 1);
                            Identifier swapLabel = (labelConditions switch
                            {
                                (false, true) => "EnableSelectedWorkshopMods",
                                (false, false) => "EnableWorkshopMod",
                                (true, true) => "DisableSelectedWorkshopMods",
                                (true, false) => "DisableWorkshopMod"
                            }).ToIdentifier();
                            
                            contextMenuOptions.Add(new ContextMenuOption(swapLabel,
                                isEnabled: true, onSelected: currentSwapFunc ?? noop));

                            var selectedMods = parentList.AllSelected.Select(it => it.UserData)
                                .OfType<ContentPackage>().ToArray();
                            if (selectedMods.All(ContentPackageManager.LocalPackages.Contains) && selectedMods.Length > 1)
                            {
                                contextMenuOptions.Add(new ContextMenuOption("MergeSelectedMods".ToIdentifier(), isEnabled: true,
                                    onSelected: () => ModMerger.AskMerge(selectedMods)));
                            }

                            GUIButton? iconBtn(GUIComponent component) => component.GetChild<GUILayoutGroup>()?.GetAllChildren<GUIButton>().Last();
                            if (selectedMods.All(ContentPackageManager.WorkshopPackages.Contains)
                                && parentList.AllSelected.All(c => iconBtn(c)?.Style?.Identifier == "WorkshopMenu.DownloadedIcon")
                                && selectedMods.Length > 0)
                            {
                                contextMenuOptions.Add(new ContextMenuOption(
                                    (selectedMods.Length > 1 ? "UnsubscribeFromAllSelected" : "WorkshopItemUnsubscribe").ToIdentifier(),
                                    isEnabled: true,
                                    onSelected: () =>
                                    {
                                        var workshopIds = selectedMods
                                            .Select(m => m.UgcId)
                                            .NotNone()
                                            .OfType<SteamWorkshopId>()
                                            .Select(id => id.Value);
                                        TaskPool.AddIfNotFound($"UnsubFromSelected", Task.WhenAll(workshopIds.Select(SteamManager.Workshop.GetItem)),
                                            t =>
                                            {
                                                if (!t.TryGetResult(out Steamworks.Ugc.Item?[] items)) { return; }
                                                items.ForEach(it =>
                                                {
                                                    if (!(it is { } item)) { return; }

                                                    item.Unsubscribe();
                                                    SteamManager.Workshop.Uninstall(item);
                                                    PopulateInstalledModLists();
                                                });
                                            });
                                    }));
                            }
                            
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
                    TaskPool.AddIfNotFound(
                        $"DetermineUpdateRequired{mod.UgcId}",
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

            TaskPool.AddIfNotFound(
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
                        if (!mod.UgcId.TryUnwrap(out var ugcId)) { continue; }
                        if (!(ugcId is SteamWorkshopId workshopId)) { continue; }
                        
                        var btn = child.GetChild<GUILayoutGroup>()?.GetAllChildren<GUIButton>().Last();
                        if (btn is null) { continue; }
                        if (btn.Style != null) { continue; }

                        btn.ApplyStyle(
                            GUIStyle.GetComponentStyle(
                                ids.Contains(workshopId.Value)
                                    ? "WorkshopMenu.PublishedIcon"
                                    : "WorkshopMenu.DownloadedIcon"));
                        btn.ToolTip = TextManager.Get(
                            ids.Contains(workshopId.Value)
                                ? "PublishedWorkshopMod"
                                : "DownloadedWorkshopMod");
                        btn.HoverCursor = CursorState.Default;
                    }
                });
            
            UpdateModListItemVisibility();
        }
    }
}
