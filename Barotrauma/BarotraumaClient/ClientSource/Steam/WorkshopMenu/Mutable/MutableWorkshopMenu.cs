#nullable enable
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
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

        public void Apply()
        {
            ContentPackageManager.EnabledPackages.SetCore(EnabledCorePackage);
            ContentPackageManager.EnabledPackages.SetRegular(enabledRegularModsList.Content.Children
                .Select(c => c.UserData as RegularPackage).OfType<RegularPackage>().ToArray());
            PopulateInstalledModLists(forceRefreshEnabled: true, refreshDisabled: true);
            ContentPackageManager.LogEnabledRegularPackageErrors();
            enabledCoreDropdown.ButtonTextColor =
                EnabledCorePackage.HasAnyErrors
                    ? GUIStyle.Red
                    : GUIStyle.TextColorNormal;
        }
    }
}
