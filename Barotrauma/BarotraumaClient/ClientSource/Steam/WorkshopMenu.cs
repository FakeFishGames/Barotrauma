#nullable enable
using System;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Xml.Linq;
using Barotrauma.IO;
using Microsoft.Xna.Framework.Graphics;
using ItemOrPackage = Barotrauma.Either<Steamworks.Ugc.Item, Barotrauma.ContentPackage>;

namespace Barotrauma.Steam
{
    public partial class WorkshopMenu
    {
        public enum Tab
        {
            InstalledMods,
            //Overrides, //TODO: implement later
            PopularMods,
            Publish
        }

        private GUILayoutGroup tabber;
        private Dictionary<Tab, (GUIButton Button, GUIFrame Content)> tabContents;

        private GUIFrame contentFrame;

        private CorePackage enabledCorePackage => enabledCoreDropdown.SelectedData as CorePackage ?? throw new Exception("Valid core package not selected");

        private readonly GUIDropDown enabledCoreDropdown;
        private readonly GUIListBox enabledRegularModsList;
        private readonly GUIListBox disabledRegularModsList;
        private readonly Action<ItemOrPackage> onInstalledInfoButtonHit;

        private CancellationTokenSource taskCancelSrc = new CancellationTokenSource();
        private readonly HashSet<SteamManager.Workshop.ItemThumbnail> itemThumbnails = new HashSet<SteamManager.Workshop.ItemThumbnail>();

        private readonly GUIListBox popularModsList;
        private readonly GUIListBox selfModsList;

        public WorkshopMenu(GUIFrame parent)
        {
            var mainLayout = new GUILayoutGroup(new RectTransform(Vector2.One, parent.RectTransform), isHorizontal: false);

            tabber = new GUILayoutGroup(new RectTransform((1.0f, 0.05f), mainLayout.RectTransform), isHorizontal: true) { Stretch = true };
            tabContents = new Dictionary<Tab, (GUIButton Button, GUIFrame Content)>();

            contentFrame = new GUIFrame(new RectTransform((1.0f, 0.95f), mainLayout.RectTransform), style: null);

            CreateInstalledModsTab(out enabledCoreDropdown, out enabledRegularModsList, out disabledRegularModsList, out onInstalledInfoButtonHit);
            CreatePopularModsTab(out popularModsList);
            CreatePublishTab(out selfModsList);

            SelectTab(Tab.InstalledMods);
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

                to.BarScroll = to.BarScroll * (oldCount / newCount);
            }
        }
        
        private void CreateInstalledModsTab(
            out GUIDropDown enabledCoreDropdown,
            out GUIListBox enabledRegularModsList,
            out GUIListBox disabledRegularModsList,
            out Action<ItemOrPackage> onInstalledInfoButtonHit)
        {
            GUIFrame content = CreateNewContentFrame(Tab.InstalledMods);
            
            CreateWorkshopItemDetailContainer(
                content,
                out var outerContainer,
                onSelected: (itemOrPackage, selectedFrame) =>
                {
                    if (itemOrPackage.TryGet(out Steamworks.Ugc.Item item)) { PopulateFrameWithItemInfo(item, selectedFrame); }
                },
                onDeselected: PopulateInstalledModLists,
                out onInstalledInfoButtonHit, out var deselect);

            GUILayoutGroup mainLayout =
                new GUILayoutGroup(new RectTransform(Vector2.One, outerContainer.Content.RectTransform), childAnchor: Anchor.TopCenter);
            mainLayout.RectTransform.SetAsFirstChild();
            GUILayoutGroup coreSelectionLayout =
                new GUILayoutGroup(new RectTransform((0.5f, 0.15f), mainLayout.RectTransform));
            Label(coreSelectionLayout, TextManager.Get("enabledcore"), GUIStyle.SubHeadingFont, heightScale: 1.0f / 0.15f);
            enabledCoreDropdown = Dropdown<CorePackage>(coreSelectionLayout,
                (p) => p.Name,
                ContentPackageManager.CorePackages.ToArray(),
                ContentPackageManager.EnabledPackages.Core!,
                (p) => { },
                heightScale: 1.0f / 0.15f);

            var (left, center, right) = CreateSidebars(mainLayout, centerWidth: 0.05f, leftWidth: 0.475f, rightWidth: 0.475f, height: 0.78f);
            right.ChildAnchor = Anchor.TopRight;

            Action swapFunc(GUIListBox from, GUIListBox to)
            {
                return () =>
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

            Action? currentCenterCallback = null;

            //enabled mods
            Label(left, TextManager.Get("enabledregular"), GUIStyle.SubHeadingFont);
            var enabledModsList = new GUIListBox(new RectTransform((1.0f, 0.92f), left.RectTransform))
            {
                CurrentDragMode = GUIListBox.DragMode.DragOutsideBox,
                CurrentSelectMode = GUIListBox.SelectMode.RequireShiftToSelectMultiple,
                HideDraggedElement = true
            };
            enabledRegularModsList = enabledModsList;
            
            //disabled mods
            Label(right, TextManager.Get("disabledregular"), GUIStyle.SubHeadingFont);
            var disabledModsList = new GUIListBox(new RectTransform((1.0f, 0.92f), right.RectTransform))
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
                        currentCenterCallback?.Invoke();
                        return false;
                    }
                };

            enabledModsList.OnSelected = (frame, o) =>
            {
                disabledModsList.Deselect();

                centerButton.Visible = true;
                centerButton.ApplyStyle(GUIStyle.GetComponentStyle("GUIButtonToggleRight"));

                currentCenterCallback = swapFunc(enabledModsList, disabledModsList);
                
                return true;
            };
            disabledModsList.OnSelected = (frame, o) =>
            {
                enabledModsList.Deselect();
                
                centerButton.Visible = true;
                centerButton.ApplyStyle(GUIStyle.GetComponentStyle("GUIButtonToggleLeft"));
                
                currentCenterCallback = swapFunc(disabledModsList, enabledModsList);
                
                return true;
            };
            
            var searchRectT = NewItemRectT(mainLayout, heightScale: 1.0f);
            searchRectT.RelativeSize = (0.5f, searchRectT.RelativeSize.Y);
            var searchHolder = new GUIFrame(searchRectT, style: null);
            var searchBox = new GUITextBox(new RectTransform(Vector2.One, searchHolder.RectTransform), "");
            var searchTitle = new GUITextBlock(new RectTransform(Vector2.One, searchHolder.RectTransform) {Anchor = Anchor.TopLeft},
                textColor: Color.DarkGray * 0.6f,
                text: TextManager.Get("Search") + "...",
                textAlignment: Alignment.CenterLeft)
            {
                CanBeFocused = false
            };
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = searchBox.Text.IsNullOrWhiteSpace(); };

            searchBox.OnTextChanged += (sender, str) =>
            {
                enabledModsList.Content.Children.Concat(disabledModsList.Content.Children)
                    .ForEach(c => c.Visible = str.IsNullOrWhiteSpace()
                                              || (c.UserData is ContentPackage p
                                                  && p.Name.Contains(str, StringComparison.OrdinalIgnoreCase)));
                return true;
            };

            new GUICustomComponent(new RectTransform(Vector2.Zero, content.RectTransform),
                onUpdate: (f, component) =>
                {
                    HandleDraggingAcrossModLists(enabledModsList, disabledModsList);
                    HandleDraggingAcrossModLists(disabledModsList, enabledModsList);
                },
                onDraw: (spriteBatch, component) =>
                {
                    enabledModsList.DraggedElement?.DrawManually(spriteBatch, true, true);
                    disabledModsList.DraggedElement?.DrawManually(spriteBatch, true, true);
                });
        }

        private void PopulateInstalledModLists()
        {
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
                
                var frameContent = new GUILayoutGroup(new RectTransform((0.95f, 0.9f), modFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    Stretch = true,
                    RelativeSpacing = 0.02f
                };
                
                var dragIndicator = new GUIButton(new RectTransform((0.1f, 0.5f), frameContent.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                    style: "GUIDragIndicator")
                {
                    CanBeFocused = false
                };

                var modNameScissor
                    = new GUIScissorComponent(new RectTransform((0.8f, 1.0f), frameContent.RectTransform));
                var modName = new GUITextBlock(new RectTransform(Vector2.One, modNameScissor.Content.RectTransform), text: mod.Name);
                if (ContentPackageManager.LocalPackages.Contains(mod))
                {
                    var editButton = new GUIButton(new RectTransform(Vector2.One, frameContent.RectTransform, scaleBasis: ScaleBasis.Smallest), "",
                            style: "WorkshopMenu.EditButton")
                        {
                            OnClicked = (button, o) =>
                            {
                                ToolBox.OpenFileWithShell(mod.Dir);
                                return false;
                            }
                        };
                }
                else if (ContentPackageManager.WorkshopPackages.Contains(mod))
                {
                    var infoButton = new GUIButton(
                        new RectTransform(Vector2.One, frameContent.RectTransform, scaleBasis: ScaleBasis.Smallest), "",
                        style: "WorkshopMenu.InfoButton")
                    {
                        OnClicked = (button, o) =>
                        {
                            TaskPool.Add($"PrepareToShow{mod.SteamWorkshopId}Info", SteamManager.Workshop.GetItem(mod.SteamWorkshopId),
                                t =>
                                {
                                    if (!t.TryGetResult(out Steamworks.Ugc.Item? item)) { return; }
                                    if (item is null) { return; }
                                    onInstalledInfoButtonHit(item.Value);
                                });
                            return false;
                        }
                    };
                    TaskPool.Add(
                        $"DetermineUpdateRequired{mod.SteamWorkshopId}",
                        mod.IsUpToDate(),
                        t =>
                        {
                            if (!t.TryGetResult(out bool isUpToDate)) { return; }

                            if (!isUpToDate)
                            {
                                infoButton.ApplyStyle(GUIStyle.ComponentStyles["WorkshopMenu.InfoButtonUpdate"]);
                            }
                        });
                }
            }
            
            enabledRegularModsList.ClearChildren();
            for (int i = 0; i < ContentPackageManager.EnabledPackages.Regular.Count; i++)
            {
                var mod = ContentPackageManager.EnabledPackages.Regular[i];
                addRegularModToList(mod, enabledRegularModsList);
            }

            disabledRegularModsList.ClearChildren();
            foreach (var mod in ContentPackageManager.RegularPackages)
            {
                if (ContentPackageManager.EnabledPackages.Regular.Contains(mod)) { continue; }
                addRegularModToList(mod, disabledRegularModsList);
            }
        }

        private void CreatePopularModsTab(out GUIListBox popularModsList)
        {
            GUIFrame content = CreateNewContentFrame(Tab.PopularMods);
            
            CreateWorkshopItemList(content, out _, out popularModsList, onSelected: PopulateFrameWithItemInfo);
        }

        private void CreatePublishTab(out GUIListBox selfModsList)
        {
            GUIFrame content = CreateNewContentFrame(Tab.Publish);

            CreateWorkshopItemOrPackageList(content, out _, out selfModsList, onSelected: PopulatePublishTab);
        }

        public void Apply()
        {
            ContentPackageManager.EnabledPackages.SetCore(enabledCorePackage);
            ContentPackageManager.EnabledPackages.SetRegular(enabledRegularModsList.Content.Children
                .Where(c => c.UserData is RegularPackage).Select(c => (RegularPackage)c.UserData).ToArray());
        }
    }
}
