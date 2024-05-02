using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Barotrauma.Steam;

namespace Barotrauma
{
    sealed class ServerListScreen : Screen
    {
        private enum MsgUserData
        {
            RefreshingServerList,
            NoServers,
            NoMatchingServers
        }
        
        //how often the client is allowed to refresh servers
        private static readonly TimeSpan AllowedRefreshInterval = TimeSpan.FromSeconds(3);
        
        private DateTime lastRefreshTime = DateTime.Now;

        private GUIFrame menu;

        private GUIListBox serverList;
        private PanelAnimator panelAnimator;
        private GUIFrame serverPreviewContainer;
        private GUIListBox serverPreview;

        private GUIButton joinButton;
        private Option<ServerInfo> selectedServer;

        private GUIButton scanServersButton;

        private enum TernaryOption
        {
            Any,
            Enabled,
            Disabled
        }

        public enum TabEnum
        {
            All,
            Favorites,
            Recent
        }

        public readonly struct Tab
        {
            public readonly string Storage;
            public readonly GUIButton Button;
            
            private readonly List<ServerInfo> servers;
            public IReadOnlyList<ServerInfo> Servers => servers;

            public Tab(TabEnum tabEnum, ServerListScreen serverListScreen, GUILayoutGroup tabber, string storage)
            {
                Storage = storage;
                servers = new List<ServerInfo>();
                Button = new GUIButton(new RectTransform(new Vector2(0.33f, 1.0f), tabber.RectTransform),
                    TextManager.Get($"ServerListTab.{tabEnum}"), style: "GUITabButton")
                {
                    OnClicked = (_,__) =>
                    {
                        serverListScreen.selectedTab = tabEnum;
                        return false;
                    }
                };

                Reload();
            }
            
            public void Reload()
            {
                if (Storage.IsNullOrEmpty()) { return; }
                servers.Clear();
                XDocument doc = XMLExtensions.TryLoadXml(Storage, out _);
                if (doc?.Root is null) { return; }
                servers.AddRange(doc.Root.Elements().Select(ServerInfo.FromXElement).NotNone().Distinct());
            }

            public bool Contains(ServerInfo info) => servers.Contains(info);
            public bool Remove(ServerInfo info) => servers.Remove(info);
            public void AddOrUpdate(ServerInfo info)
            {
                servers.Remove(info); servers.Add(info);
            }

            public void Clear() => servers.Clear();

            public void Save()
            {
                XDocument doc = new XDocument();
                XElement rootElement = new XElement("servers");
                doc.Add(rootElement);

                foreach (ServerInfo info in servers)
                {
                    rootElement.Add(info.ToXElement());
                }

                doc.SaveSafe(Storage);
            }
        }

        private readonly Dictionary<TabEnum, Tab> tabs = new Dictionary<TabEnum, Tab>();

        private TabEnum _selectedTabBackingField;
        private TabEnum selectedTab
        {
            get => _selectedTabBackingField;
            set
            {
                _selectedTabBackingField = value;
                tabs.ForEach(kvp => kvp.Value.Button.Selected = (value == kvp.Key));
                if (Screen.Selected == this) { RefreshServers(); }
            }
        }

        private ServerProvider serverProvider = null;

        public GUITextBox ClientNameBox { get; private set; }

        enum ColumnLabel
        {
            ServerListCompatible,
            ServerListHasPassword,
            ServerListName,
            ServerListRoundStarted,
            ServerListPlayers,
            ServerListPing
        }
        private struct Column
        {
            public float RelativeWidth;
            public ColumnLabel Label;

            public static implicit operator Column((float W, ColumnLabel L) pair) =>
                new Column { RelativeWidth = pair.W, Label = pair.L };

            public static Column[] Normalize(params Column[] columns)
            {
                var totalWidth = columns.Select(c => c.RelativeWidth).Aggregate((a, b) => a + b);
                for (int i = 0; i < columns.Length; i++)
                {
                    columns[i].RelativeWidth /= totalWidth;
                }
                return columns;
            }
        }

        private static readonly ImmutableDictionary<ColumnLabel, Column> columns =
            Column.Normalize(
                (0.1f, ColumnLabel.ServerListCompatible),
                (0.1f, ColumnLabel.ServerListHasPassword),
                (0.7f, ColumnLabel.ServerListName),
                (0.12f, ColumnLabel.ServerListRoundStarted),
                (0.08f, ColumnLabel.ServerListPlayers),
                (0.08f, ColumnLabel.ServerListPing)
            ).Select(c => (c.Label, c)).ToImmutableDictionary();
        
        private GUILayoutGroup labelHolder;
        private readonly List<GUITextBlock> labelTexts = new List<GUITextBlock>();

        //filters
        private GUITextBox searchBox;
        private GUITickBox filterSameVersion;
        private GUITickBox filterPassword;
        private GUITickBox filterFull;
        private GUITickBox filterEmpty;
        private GUIDropDown languageDropdown;
        private Dictionary<Identifier, GUIDropDown> ternaryFilters;
        private Dictionary<Identifier, GUITickBox> filterTickBoxes;
        private Dictionary<Identifier, GUITickBox> playStyleTickBoxes;
        private Dictionary<Identifier, GUITickBox> gameModeTickBoxes;
        private GUITickBox filterOffensive;

        //GUIDropDown sends the OnSelected event before SelectedData is set, so we have to cache it manually.
        private TernaryOption filterFriendlyFireValue = TernaryOption.Any;
        private TernaryOption filterKarmaValue = TernaryOption.Any;
        private TernaryOption filterTraitorValue = TernaryOption.Any;
        private TernaryOption filterVoipValue = TernaryOption.Any;
        private TernaryOption filterModdedValue = TernaryOption.Any;

        private ColumnLabel sortedBy;
        private bool sortedAscending = true;

        private const float sidebarWidth = 0.2f;
        public ServerListScreen() : base()
        {
            selectedServer = Option<ServerInfo>.None();
            GameMain.Instance.ResolutionChanged += CreateUI;
            CreateUI();
        }

        private static Task<string> GetDefaultUserName()
        {
            return new CompositeFriendProvider(new SteamFriendProvider(), new EpicFriendProvider()).GetSelfUserName();
        }

        private void AddTernaryFilter(RectTransform parent, float elementHeight, Identifier tag, Action<TernaryOption> valueSetter)
        {
            var filterLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, elementHeight), parent), isHorizontal: true)
            {
                Stretch = true
            };

            var box = new GUIFrame(new RectTransform(Vector2.One, filterLayoutGroup.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight)
            {
                IsFixedSize = true,
            }, null)
            {
                HoverColor = Color.Gray,
                SelectedColor = Color.DarkGray,
                CanBeFocused = false
            };
            if (box.RectTransform.MinSize.Y > 0)
            {
                box.RectTransform.MinSize = new Point(box.RectTransform.MinSize.Y);
                box.RectTransform.Resize(box.RectTransform.MinSize);
            }
            Vector2 textBlockScale = new Vector2((float)(filterLayoutGroup.Rect.Width - filterLayoutGroup.Rect.Height) / (float)Math.Max(filterLayoutGroup.Rect.Width, 1.0), 1.0f);

            var filterLabel = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f) * textBlockScale, filterLayoutGroup.RectTransform, Anchor.CenterLeft), TextManager.Get("servertag." + tag + ".label"), textAlignment: Alignment.CenterLeft)
            {
                UserData = TextManager.Get($"servertag.{tag}.label")
            };
            GUIStyle.Apply(filterLabel, "GUITextBlock", null);

            var dropDown = new GUIDropDown(new RectTransform(new Vector2(0.4f, 1.0f) * textBlockScale, filterLayoutGroup.RectTransform, Anchor.CenterLeft), elementCount: 3);
            dropDown.AddItem(TextManager.Get("any"), TernaryOption.Any);
            dropDown.AddItem(TextManager.Get($"servertag.{tag}.true"), TernaryOption.Enabled, TextManager.Get(
                $"servertagdescription.{tag}.true"));
            dropDown.AddItem(TextManager.Get($"servertag.{tag}.false"), TernaryOption.Disabled, TextManager.Get(
                $"servertagdescription.{tag}.false"));
            dropDown.SelectItem(TernaryOption.Any);
            dropDown.OnSelected = (_, data) => {
                valueSetter((TernaryOption)data);
                FilterServers();
                StoreServerFilters();
                return true;
            };

            ternaryFilters.Add(tag, dropDown);
        }

        private void CreateUI()
        {
            menu = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.85f), GUI.Canvas, Anchor.Center) { MinSize = new Point(GameMain.GraphicsHeight, 0) });

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.98f), menu.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            //-------------------------------------------------------------------------------------
            //Top row
            //-------------------------------------------------------------------------------------

            var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform)) { Stretch = true };

            var titleContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.995f, 0.33f), topRow.RectTransform), isHorizontal: true) { Stretch = true };
            
            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), titleContainer.RectTransform), TextManager.Get("JoinServer"), font: GUIStyle.LargeFont)
            {
                Padding = Vector4.Zero,
                ForceUpperCase = ForceUpperCase.Yes,
                AutoScaleHorizontal = true
            };

            var friendsButton = new GUIButton(
                new RectTransform(Vector2.One * 0.9f, titleContainer.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                style: "FriendsButton")
            {
                OnClicked = (_, _) =>
                {
                    if (SocialOverlay.Instance is { } socialOverlay) { socialOverlay.IsOpen = true; }
                    return false;
                },
                ToolTip = TextManager.GetWithVariable("SocialOverlayShortcutHint", "[shortcut]", SocialOverlay.ShortcutBindText)
            };
            new GUIFrame(new RectTransform(Vector2.One, friendsButton.RectTransform, Anchor.Center),
                style: "FriendsButtonIcon")
            {
                CanBeFocused = false
            };

            var infoHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.33f), topRow.RectTransform), isHorizontal: true, Anchor.BottomLeft) { RelativeSpacing = 0.01f,  Stretch = false };

            var clientNameHolder = new GUILayoutGroup(new RectTransform(new Vector2(sidebarWidth, 1.0f), infoHolder.RectTransform)) { RelativeSpacing = 0.05f };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), clientNameHolder.RectTransform), TextManager.Get("YourName"), font: GUIStyle.SubHeadingFont);
            ClientNameBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), clientNameHolder.RectTransform), "")
            {
                Text = MultiplayerPreferences.Instance.PlayerName,
                MaxTextLength = Client.MaxNameLength,
                OverflowClip = true
            };

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - sidebarWidth - infoHolder.RelativeSpacing, 0.5f), infoHolder.RectTransform), isHorizontal: true);

            tabs[TabEnum.All] = new Tab(TabEnum.All, this, tabButtonHolder, "");
            tabs[TabEnum.Favorites] = new Tab(TabEnum.Favorites, this, tabButtonHolder, "Data/favoriteservers.xml");
            tabs[TabEnum.Recent] = new Tab(TabEnum.Recent, this, tabButtonHolder, "Data/recentservers.xml");

            //-------------------------------------------------------------------------------------
            // Bottom row
            //-------------------------------------------------------------------------------------

            var bottomRow = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f - topRow.RectTransform.RelativeSize.Y),
                paddedFrame.RectTransform, Anchor.CenterRight))
            {
                Stretch = true
            };

            var serverListHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), bottomRow.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                OutlineColor = Color.Black
            };

            GUILayoutGroup serverListContainer = null;
            GUIFrame filtersHolder = null;

            // filters -------------------------------------------

            filtersHolder = new GUIFrame(new RectTransform(new Vector2(sidebarWidth, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black
            };

            float elementHeight = 0.05f;
            var filterTitle = new GUITextBlock(new RectTransform(new Vector2(1.0f, elementHeight), filtersHolder.RectTransform), TextManager.Get("FilterServers"), font: GUIStyle.SubHeadingFont)
            {
                Padding = Vector4.Zero,
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };

            var searchHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, elementHeight), filtersHolder.RectTransform) { RelativeOffset = new Vector2(0.0f, elementHeight) }, isHorizontal: true) { Stretch = true };

            var searchTitle = new GUITextBlock(new RectTransform(new Vector2(0.001f, 1.0f), searchHolder.RectTransform), TextManager.Get("Search") + "...");
            searchBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 1.0f), searchHolder.RectTransform), "");
            searchBox.OnSelected += (sender, userdata) => { searchTitle.Visible = false; };
            searchBox.OnDeselected += (sender, userdata) => { searchTitle.Visible = true; };
            searchBox.OnTextChanged += (txtBox, txt) => { FilterServers(); return true; };

            var filters = new GUIListBox(new RectTransform(new Vector2(0.98f, 1.0f - elementHeight * 2), filtersHolder.RectTransform, Anchor.BottomLeft))
            {
                ScrollBarVisible = true,
                Spacing = (int)(5 * GUI.Scale)
            };

            ternaryFilters = new Dictionary<Identifier, GUIDropDown>();
            filterTickBoxes = new Dictionary<Identifier, GUITickBox>();

            RectTransform createFilterRectT()
                => new RectTransform(new Vector2(1.0f, elementHeight), filters.Content.RectTransform);
            
            GUITickBox addTickBox(Identifier key, LocalizedString text = null, bool defaultState = false, bool addTooltip = false)
            {
                text ??= TextManager.Get(key);
                var tickBox = new GUITickBox(createFilterRectT(), text)
                {
                    UserData = text,
                    Selected = defaultState,
                    ToolTip = addTooltip ? text : null,
                    OnSelected = (tickBox) =>
                    {
                        FilterServers();
                        StoreServerFilters();
                        return true;
                    }
                };
                filterTickBoxes.Add(key, tickBox);
                return tickBox;
            }

            filterSameVersion = addTickBox("FilterSameVersion".ToIdentifier(), defaultState: true);
            filterPassword = addTickBox("FilterPassword".ToIdentifier());
            filterFull = addTickBox("FilterFullServers".ToIdentifier());
            filterEmpty = addTickBox("FilterEmptyServers".ToIdentifier());
            filterOffensive = addTickBox("FilterOffensiveServers".ToIdentifier());

            // Language filter
            if (ServerLanguageOptions.Options.Any())
            {
                var languageKey = "Language".ToIdentifier();
                var allLanguagesKey = "AllLanguages".ToIdentifier();

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get(languageKey), font: GUIStyle.SubHeadingFont)
                {
                    CanBeFocused = false
                };

                languageDropdown = new GUIDropDown(createFilterRectT(), selectMultiple: true);

                languageDropdown.AddItem(TextManager.Get(allLanguagesKey), allLanguagesKey);
                var allTickbox = languageDropdown.ListBox.Content.FindChild(allLanguagesKey)?.GetChild<GUITickBox>();

                // Spacer between "All" and the individual languages
                new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), languageDropdown.ListBox.Content.RectTransform)
                {
                    MinSize = new Point(0, GUI.IntScaleCeiling(2))
                }, style: null)
                {
                    Color = Color.DarkGray,
                    CanBeFocused = false
                };
                
                var selectedLanguages
                    = ServerListFilters.Instance.GetAttributeLanguageIdentifierArray(
                        languageKey,
                        Array.Empty<LanguageIdentifier>());
                foreach (var (label, identifier, _) in ServerLanguageOptions.Options)
                {
                    languageDropdown.AddItem(label, identifier);
                }

                if (!selectedLanguages.Any())
                {
                    selectedLanguages = ServerLanguageOptions.Options.Select(o => o.Identifier).ToArray();
                }

                foreach (var lang in selectedLanguages)
                {
                    languageDropdown.SelectItem(lang);
                }

                if (ServerLanguageOptions.Options.All(o => selectedLanguages.Any(l => o.Identifier == l)))
                {
                    languageDropdown.SelectItem(allLanguagesKey);
                    languageDropdown.Text = TextManager.Get(allLanguagesKey);
                }

                var langTickboxes = languageDropdown.ListBox.Content.Children
                    .Where(c => c.UserData is LanguageIdentifier)
                    .Select(c => c.GetChild<GUITickBox>())
                    .ToArray();

                bool inSelectedCall = false;
                languageDropdown.OnSelected = (_, userData) =>
                {
                    if (inSelectedCall) { return true; }
                    try
                    {
                        inSelectedCall = true;

                        if (Equals(allLanguagesKey, userData))
                        {
                            foreach (var tb in langTickboxes)
                            {
                                tb.Selected = allTickbox.Selected;
                            }
                        }

                        bool noneSelected = langTickboxes.All(tb => !tb.Selected);
                        bool allSelected = langTickboxes.All(tb => tb.Selected);

                        if (allSelected != allTickbox.Selected)
                        {
                            allTickbox.Selected = allSelected;
                        }

                        if (allSelected)
                        {
                            languageDropdown.Text = TextManager.Get(allLanguagesKey);
                        }
                        else if (noneSelected)
                        {
                            languageDropdown.Text = TextManager.Get("None");
                        }

                        var languages = languageDropdown.SelectedDataMultiple.OfType<LanguageIdentifier>();

                        ServerListFilters.Instance.SetAttribute(languageKey, string.Join(", ", languages));
                        GameSettings.SaveCurrentConfig();
                        return true;
                    }
                    finally
                    {
                        inSelectedCall = false;
                        FilterServers();
                    }
                };
            }
            
            // Filter Tags
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("servertags"), font: GUIStyle.SubHeadingFont)
            {
                CanBeFocused = false
            };

            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "karma".ToIdentifier(), (value) => { filterKarmaValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "traitors".ToIdentifier(), (value) => { filterTraitorValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "friendlyfire".ToIdentifier(), (value) => { filterFriendlyFireValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "voip".ToIdentifier(), (value) => { filterVoipValue = value; });
            AddTernaryFilter(filters.Content.RectTransform, elementHeight, "modded".ToIdentifier(), (value) => { filterModdedValue = value; });

            // Play Style Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("ServerSettingsPlayStyle"), font: GUIStyle.SubHeadingFont)
            {
                CanBeFocused = false
            };

            playStyleTickBoxes = new Dictionary<Identifier, GUITickBox>();
            foreach (PlayStyle playStyle in Enum.GetValues(typeof(PlayStyle)))
            {
                var selectionTick = addTickBox($"servertag.{playStyle}".ToIdentifier(), defaultState: true, addTooltip: true);
                selectionTick.UserData = playStyle;
                playStyleTickBoxes.Add($"servertag.{playStyle}".ToIdentifier(), selectionTick);
            }

            // Game mode Selection
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.05f), filters.Content.RectTransform), TextManager.Get("gamemode"), font: GUIStyle.SubHeadingFont) { CanBeFocused = false };

            gameModeTickBoxes = new Dictionary<Identifier, GUITickBox>();
            foreach (GameModePreset mode in GameModePreset.List)
            {
                if (mode.IsSinglePlayer) { continue; }

                var selectionTick = addTickBox(mode.Identifier, mode.Name, defaultState: true, addTooltip: true);
                selectionTick.UserData = mode.Identifier;
                gameModeTickBoxes.Add(mode.Identifier, selectionTick);
            }

            filters.Content.RectTransform.SizeChanged += () =>
            {
                filters.Content.RectTransform.RecalculateChildren(true, true);
                filterTickBoxes.ForEach(t => t.Value.Text = t.Value.UserData is LocalizedString lStr ? lStr : t.Value.UserData.ToString());
                gameModeTickBoxes.ForEach(tb => tb.Value.Text = tb.Value.ToolTip);
                playStyleTickBoxes.ForEach(tb => tb.Value.Text = tb.Value.ToolTip);
                GUITextBlock.AutoScaleAndNormalize(
                    filterTickBoxes.Values.Select(tb => tb.TextBlock)
                    .Concat(ternaryFilters.Values.Select(dd => dd.Parent.GetChild<GUITextBlock>())),
                    defaultScale: 1.0f);
                if (filterTickBoxes.Values.First().TextBlock.TextScale < 0.8f)
                {
                    filterTickBoxes.ForEach(t => t.Value.TextBlock.TextScale = 1.0f);
                    filterTickBoxes.ForEach(t => t.Value.TextBlock.Text = ToolBox.LimitString(t.Value.TextBlock.Text, t.Value.TextBlock.Font, (int)(filters.Content.Rect.Width * 0.8f)));
                }
            };

            // server list ---------------------------------------------------------------------

            serverListContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), serverListHolder.RectTransform)) { Stretch = true };

            labelHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), serverListContainer.RectTransform) { MinSize = new Point(0, 15) },
                isHorizontal: true, childAnchor: Anchor.BottomLeft)
            {
                Stretch = false
            };
            
            foreach (var column in columns.Values)
            {
                var label = TextManager.Get(column.Label.ToString());
                var btn = new GUIButton(new RectTransform(new Vector2(column.RelativeWidth, 1.0f), labelHolder.RectTransform),
                    text: label, textAlignment: Alignment.Center, style: "GUIButtonSmall")
                {
                    ToolTip = label,
                    ForceUpperCase = ForceUpperCase.Yes,
                    UserData = column.Label,
                    OnClicked = SortList
                };
                btn.Color *= 0.5f;
                labelTexts.Add(btn.TextBlock);
                
                GUIImage arrowImg(object userData, SpriteEffects sprEffects)
                    => new GUIImage(new RectTransform(new Vector2(0.5f, 0.3f), btn.RectTransform, Anchor.BottomCenter, scaleBasis: ScaleBasis.BothHeight), style: "GUIButtonVerticalArrow", scaleToFit: true)
                    {
                        CanBeFocused = false,
                        UserData = userData,
                        SpriteEffects = sprEffects,
                        Visible = false
                    };

                arrowImg("arrowup", SpriteEffects.None);
                arrowImg("arrowdown", SpriteEffects.FlipVertically);
            }

            serverList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.0f), serverListContainer.RectTransform, Anchor.Center))
            {
                PlaySoundOnSelect = true,
                ScrollBarVisible = true,
                OnSelected = (btn, obj) =>
                {
                    if (GUI.MouseOn is GUIButton) { return false; }
                    if (obj is not ServerInfo serverInfo) { return false; }

                    joinButton.Enabled = true;
                    selectedServer = Option<ServerInfo>.Some(serverInfo);
                    if (!serverPreviewContainer.Visible)
                    {
                        serverPreviewContainer.RectTransform.RelativeSize = new Vector2(sidebarWidth, 1.0f);
                        serverPreviewContainer.Visible = true;
                        serverPreviewContainer.IgnoreLayoutGroups = false;
                    }
                    serverInfo.CreatePreviewWindow(serverPreview.Content);
                    serverPreview.ForceLayoutRecalculation();
                    panelAnimator.RightEnabled = true;
                    panelAnimator.RightVisible = true;
                    btn.Children.ForEach(c => c.SpriteEffects = serverPreviewContainer.Visible ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
                    return true;
                }
            };

            //server preview panel --------------------------------------------------
            serverPreviewContainer = new GUIFrame(new RectTransform(new Vector2(sidebarWidth, 1.0f), serverListHolder.RectTransform, Anchor.Center), style: null)
            {
                Color = new Color(12, 14, 15, 255) * 0.5f,
                OutlineColor = Color.Black,
                IgnoreLayoutGroups = true
            };
            serverPreview = new GUIListBox(new RectTransform(Vector2.One, serverPreviewContainer.RectTransform, Anchor.Center))
            {
                Padding = Vector4.One * 10 * GUI.Scale,
                HoverCursor = CursorState.Default,
                OnSelected = (component, o) => false
            };

            panelAnimator = new PanelAnimator(new RectTransform(Vector2.One, serverListHolder.RectTransform),
                filtersHolder,
                serverListContainer,
                serverPreviewContainer);
            panelAnimator.RightEnabled = false;
            
            // Spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.02f), bottomRow.RectTransform), style: null);

            var buttonContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.075f), bottomRow.RectTransform, Anchor.Center), isHorizontal: true)
            {
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            GUIButton button = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("Back"))
            {
                OnClicked = GameMain.MainMenuScreen.ReturnToMainMenu
            };

            scanServersButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListRefresh"))
            {
				OnClicked = (btn, userdata) => { RefreshServers(); return true; }
			};

            var directJoinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("serverlistdirectjoin"))
            {
                OnClicked = (btn, userdata) => 
                {
                    if (string.IsNullOrWhiteSpace(ClientNameBox.Text))
                    {
                        ClientNameBox.Flash();
                        ClientNameBox.Select();
                        SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                        return false;
                    }
                    ShowDirectJoinPrompt(); 
                    return true; 
                }
            };

            joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.9f), buttonContainer.RectTransform),
                TextManager.Get("ServerListJoin"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (selectedServer.TryUnwrap(out var serverInfo))
                    {
                        JoinServer(serverInfo.Endpoints, serverInfo.ServerName);
                    }
                    return true;
                },
                Enabled = false
            };

            buttonContainer.RectTransform.MinSize = new Point(0, (int)(buttonContainer.RectTransform.Children.Max(c => c.MinSize.Y) * 1.2f));

            //--------------------------------------------------------

            bottomRow.Recalculate();
            serverListHolder.Recalculate();
            serverListContainer.Recalculate();
            labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
            labelHolder.RectTransform.AbsoluteOffset = new Point((int)serverList.Padding.X, 0);
            labelHolder.Recalculate();

            serverList.Content.RectTransform.SizeChanged += () =>
            {
                labelHolder.RectTransform.MaxSize = new Point(serverList.Content.Rect.Width, int.MaxValue);
                labelHolder.RectTransform.AbsoluteOffset = new Point((int)serverList.Padding.X, 0);
                labelHolder.Recalculate();
                foreach (GUITextBlock labelText in labelTexts)
                {
                    labelText.Text = ToolBox.LimitString(labelText.ToolTip, labelText.Font, labelText.Rect.Width);
                }
            };

            button.SelectedColor = button.Color;

            selectedTab = TabEnum.All;
        }

        public void UpdateOrAddServerInfo(ServerInfo serverInfo)
        {
            GUIComponent existingElement = serverList.Content.FindChild(d => 
                d.UserData is ServerInfo existingServerInfo &&
                existingServerInfo.Endpoints.Any(serverInfo.Endpoints.Contains));
            if (existingElement == null)
            {
                AddToServerList(serverInfo);
            }
            else
            {
                existingElement.UserData = serverInfo;
            }
        }

        public void AddToRecentServers(ServerInfo info)
        {
            if (info.Endpoints.First().Address.IsLocalHost) { return; }
            tabs[TabEnum.Recent].AddOrUpdate(info);
            tabs[TabEnum.Recent].Save();
        }

        public bool IsFavorite(ServerInfo info)
            => tabs[TabEnum.Favorites].Contains(info);

        public void AddToFavoriteServers(ServerInfo info)
        {
            tabs[TabEnum.Favorites].AddOrUpdate(info);
            tabs[TabEnum.Favorites].Save();
        }

        public void RemoveFromFavoriteServers(ServerInfo info)
        {
            tabs[TabEnum.Favorites].Remove(info);
            tabs[TabEnum.Favorites].Save();
        }

        private bool SortList(GUIButton button, object obj)
        {
            if (obj is not ColumnLabel sortBy) { return false; }
            SortList(sortBy, toggle: true);
            return true;
        }

        private void SortList(ColumnLabel sortBy, bool toggle)
        {
            if (labelHolder.GetChildByUserData(sortBy) is not GUIButton button) { return; }

            sortedBy = sortBy;

            var arrowUp = button.GetChildByUserData("arrowup");
            var arrowDown = button.GetChildByUserData("arrowdown");
            
            //disable arrow buttons in other labels
            foreach (var child in button.Parent.Children)
            {
                if (child != button)
                {
                    child.GetChildByUserData("arrowup").Visible = false;
                    child.GetChildByUserData("arrowdown").Visible = false;
                }
            }

            sortedAscending = arrowUp.Visible;
            if (toggle)
            {
                sortedAscending = !sortedAscending;
            }

            arrowUp.Visible = sortedAscending;
            arrowDown.Visible = !sortedAscending;
            serverList.Content.RectTransform.SortChildren((c1, c2) => 
            {
                if (c1.GUIComponent.UserData is not ServerInfo s1) { return 0; }
                if (c2.GUIComponent.UserData is not ServerInfo s2) { return 0; }
                return CompareServer(sortBy, s1, s2, sortedAscending);
            });
        }

        public void HideServerPreview()
        {
            serverPreviewContainer.Visible = false;
            panelAnimator.RightEnabled = false;
            panelAnimator.RightVisible = false;
        }

        private void InsertServer(ServerInfo serverInfo, GUIComponent component)
        {
            var children = serverList.Content.RectTransform.Children.Reverse().ToList();

            foreach (var child in children)
            {
                if (child.GUIComponent.UserData is not ServerInfo serverInfo2 || serverInfo.Equals(serverInfo2)) { continue; }
                if (CompareServer(sortedBy, serverInfo, serverInfo2, sortedAscending) >= 0)
                {
                    var index = serverList.Content.RectTransform.GetChildIndex(child);
                    component.RectTransform.RepositionChildInHierarchy(Math.Min(index + 1, serverList.Content.CountChildren - 1));
                    return;
                }
            }
            component.RectTransform.SetAsFirstChild();
        }

        private static int CompareServer(ColumnLabel sortBy, ServerInfo s1, ServerInfo s2, bool ascending)
        {
            //always put servers with unknown ping at the bottom (unless we're specifically sorting by ping)
            //servers without a ping are often unreachable/spam
            bool s1HasPing = s1.Ping.IsSome();
            bool s2HasPing = s2.Ping.IsSome();
            if (s1HasPing != s2HasPing)
            {
                return s1HasPing ? -1 : 1;
            }            

            int comparison = ascending ? 1 : -1;
            switch (sortBy)
            {
                case ColumnLabel.ServerListCompatible:
                    bool s1Compatible = NetworkMember.IsCompatible(GameMain.Version, s1.GameVersion);
                    bool s2Compatible = NetworkMember.IsCompatible(GameMain.Version, s2.GameVersion);

                    if (s1Compatible == s2Compatible) { return 0; }
                    return (s1Compatible ? -1 : 1) * comparison;
                case ColumnLabel.ServerListHasPassword:
                    if (s1.HasPassword == s2.HasPassword) { return 0; }
                    return (s1.HasPassword ? 1 : -1) * comparison;
                case ColumnLabel.ServerListName:
                    // I think we actually want culture-specific sorting here?
                    return string.Compare(s1.ServerName, s2.ServerName, StringComparison.CurrentCulture) * comparison;
                case ColumnLabel.ServerListRoundStarted:
                    if (s1.GameStarted == s2.GameStarted) { return 0; }
                    return (s1.GameStarted ? 1 : -1) * comparison;
                case ColumnLabel.ServerListPlayers:
                    return s2.PlayerCount.CompareTo(s1.PlayerCount) * comparison;
                case ColumnLabel.ServerListPing:
                    return (s1.Ping.TryUnwrap(out var s1Ping), s2.Ping.TryUnwrap(out var s2Ping)) switch
                    {
                        (false, false) => 0,
                        (true, true) => s2Ping.CompareTo(s1Ping),
                        (false, true) => 1,
                        (true, false) => -1
                    } * comparison;
                default:
                    return 0;
            }
        }

        public override void Select()
        {
            base.Select();
          
            if (string.IsNullOrEmpty(ClientNameBox.Text))
            {
                TaskPool.Add("GetDefaultUserName",
                    GetDefaultUserName(),
                    t =>
                    {
                        if (!t.TryGetResult(out string name)) { return; }
                        if (ClientNameBox.Text.IsNullOrEmpty())
                        {
                            ClientNameBox.Text = name;
                            string nameWithoutInvisibleSymbols = string.Empty;
                            foreach (char c in ClientNameBox.Text)
                            {
                                Vector2 size = ClientNameBox.Font.MeasureChar(c);
                                if (size.X > 0 && size.Y > 0)
                                {
                                    nameWithoutInvisibleSymbols += c;
                                }
                            }
                            if (nameWithoutInvisibleSymbols != ClientNameBox.Text)
                            {
                                MultiplayerPreferences.Instance.PlayerName = ClientNameBox.Text = nameWithoutInvisibleSymbols;
                                new GUIMessageBox(TextManager.Get("Warning"), TextManager.GetWithVariable("NameContainsInvisibleSymbols", "[name]", nameWithoutInvisibleSymbols));
                            }
                        }
                    });
            }

            ClientNameBox.OnTextChanged += (textbox, text) =>
            {
                MultiplayerPreferences.Instance.PlayerName = text;
                return true;
            };
            if (EosInterface.IdQueries.IsLoggedIntoEosConnect)
            {
                if (SteamManager.IsInitialized)
                {
                    serverProvider = new CompositeServerProvider(
                        new EosServerProvider(),
                        new SteamDedicatedServerProvider(),
                        new SteamP2PServerProvider());
                }
                else
                {
                    serverProvider = new EosServerProvider();
                }
            }
            else if (SteamManager.IsInitialized)
            {
                serverProvider = new CompositeServerProvider(
                    new SteamDedicatedServerProvider(),
                    new SteamP2PServerProvider());
            }
            else
            {
                serverProvider = null;
            }

            Steamworks.SteamMatchmaking.ResetActions();

            selectedTab = TabEnum.All;
            GameMain.ServerListScreen.LoadServerFilters();
            if (GameSettings.CurrentConfig.ShowOffensiveServerPrompt)
            {
                var filterOffensivePrompt = new GUIMessageBox(string.Empty, TextManager.Get("FilterOffensiveServersPrompt"), new LocalizedString[] { TextManager.Get("yes"), TextManager.Get("no") });
                filterOffensivePrompt.Buttons[0].OnClicked = (btn, userData) =>
                {
                    filterOffensive.Selected = true;
                    filterOffensivePrompt.Close();
                    return true;
                };
                filterOffensivePrompt.Buttons[1].OnClicked = filterOffensivePrompt.Close;

                var config = GameSettings.CurrentConfig;
                config.ShowOffensiveServerPrompt = false;
                GameSettings.SetCurrentConfig(config);
            }

            if (GameMain.Client != null)
            {
                GameMain.Client.Quit();
                GameMain.Client = null;
            }

            RefreshServers();
        }

        public override void Deselect()
        {
            base.Deselect();
            serverProvider?.Cancel();
            GameSettings.SaveCurrentConfig();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            panelAnimator?.Update();

            scanServersButton.Enabled = (DateTime.Now - lastRefreshTime) >= AllowedRefreshInterval;
        }

        public void FilterServers()
        {
            RemoveMsgFromServerList(MsgUserData.NoMatchingServers);
            foreach (GUIComponent child in serverList.Content.Children)
            {
                if (child.UserData is not ServerInfo serverInfo) { continue; }
                child.Visible = ShouldShowServer(serverInfo);
            }

            if (serverList.Content.Children.All(c => !c.Visible))
            {
                PutMsgInServerList(MsgUserData.NoMatchingServers);
            }
            serverList.UpdateScrollBarSize();
        }

        private bool AllLanguagesVisible
        {
            get
            {
                if (languageDropdown is null) { return true; }

                // CountChildren-1 because there's a separator element in there that can't be selected
                int tickBoxCount = languageDropdown.ListBox.Content.CountChildren - 1;
                int selectedCount = languageDropdown.SelectedIndexMultiple.Count();
                
                return selectedCount >= tickBoxCount;
            }
        }
        
        private bool ShouldShowServer(ServerInfo serverInfo)
        {
#if !DEBUG
            //never show newer versions
            //(ignore revision number, it doesn't affect compatibility)
            if (ToolBox.VersionNewerIgnoreRevision(GameMain.Version, serverInfo.GameVersion))
            {
                return false;
            }
#endif
            if (SpamServerFilters.IsFiltered(serverInfo)) { return false; }

            if (!string.IsNullOrEmpty(searchBox.Text) && !serverInfo.ServerName.Contains(searchBox.Text, StringComparison.OrdinalIgnoreCase)) { return false; }

            if (filterSameVersion.Selected)
            {
                if (!NetworkMember.IsCompatible(serverInfo.GameVersion, GameMain.Version)) { return false; }
            }
            if (filterPassword.Selected)
            {
                if (serverInfo.HasPassword) { return false; }
            }
            if (filterFull.Selected)
            {
                if (serverInfo.PlayerCount >= serverInfo.MaxPlayers) { return false; }
            }
            if (filterEmpty.Selected)
            {
                if (serverInfo.PlayerCount <= 0) { return false; }
            }
            if (filterOffensive.Selected)
            {
                if (ForbiddenWordFilter.IsForbidden(serverInfo.ServerName)) { return false; }
            }

            if (filterKarmaValue != TernaryOption.Any)
            {
                if (serverInfo.KarmaEnabled != (filterKarmaValue == TernaryOption.Enabled)) { return false; }
            }
            if (filterFriendlyFireValue != TernaryOption.Any)
            {
                if (serverInfo.FriendlyFireEnabled != (filterFriendlyFireValue == TernaryOption.Enabled)) { return false; }
            }
            if (filterTraitorValue != TernaryOption.Any)
            {
                if ((serverInfo.TraitorProbability > 0.0f) != (filterTraitorValue == TernaryOption.Enabled)) 
                { 
                    return false; 
                }
            }
            if (filterVoipValue != TernaryOption.Any)
            {
                if (serverInfo.VoipEnabled != (filterVoipValue == TernaryOption.Enabled)) { return false; }
            }
            if (filterModdedValue != TernaryOption.Any)
            {
                if (serverInfo.IsModded != (filterModdedValue == TernaryOption.Enabled)) { return false; }
            }

            foreach (GUITickBox tickBox in playStyleTickBoxes.Values)
            {
                var playStyle = (PlayStyle)tickBox.UserData;
                if (!tickBox.Selected && serverInfo.PlayStyle == playStyle)
                {
                    return false;
                }
            }

            if (!AllLanguagesVisible)
            {
                if (!languageDropdown.SelectedDataMultiple.OfType<LanguageIdentifier>().Contains(serverInfo.Language))
                {
                    return false;
                }
            }

            foreach (GUITickBox tickBox in gameModeTickBoxes.Values)
            {
                var gameMode = (Identifier)tickBox.UserData;
                if (!tickBox.Selected && !serverInfo.GameMode.IsEmpty && serverInfo.GameMode == gameMode)
                {
                    return false;
                }
            }

            return true;
        }

        private void ShowDirectJoinPrompt()
        {
            var msgBox = new GUIMessageBox(TextManager.Get("ServerListDirectJoin"), "",
                new LocalizedString[] { TextManager.Get("ServerListJoin"), TextManager.Get("AddToFavorites"), TextManager.Get("Cancel") },
                relativeSize: new Vector2(0.25f, 0.2f), minSize: new Point(400, 150));
            msgBox.Content.ChildAnchor = Anchor.TopCenter;

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.5f), msgBox.Content.RectTransform), childAnchor: Anchor.TopCenter)
            {
                IgnoreLayoutGroups = false,
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform),
                SteamManager.IsInitialized ? TextManager.Get("ServerEndpoint") : TextManager.Get("ServerIP"), textAlignment: Alignment.Center);
            var endpointBox = new GUITextBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform));

            content.RectTransform.NonScaledSize = new Point(content.Rect.Width, (int)(content.RectTransform.Children.Sum(c => c.Rect.Height)));
            content.RectTransform.IsFixedSize = true;
            msgBox.InnerFrame.RectTransform.MinSize = new Point(0, (int)((content.RectTransform.NonScaledSize.Y + msgBox.Content.RectTransform.Children.Sum(c => c.NonScaledSize.Y + msgBox.Content.AbsoluteSpacing)) * 1.1f));

            var okButton = msgBox.Buttons[0];
            okButton.Enabled = false;
            okButton.OnClicked = (btn, userdata) =>
            {
                if (Endpoint.Parse(endpointBox.Text).TryUnwrap(out var endpoint))
                {
                    if (endpoint is SteamP2PEndpoint && !SteamManager.IsInitialized)
                    {
                        new GUIMessageBox(TextManager.Get("error"), TextManager.Get("CannotJoinSteamServer.SteamNotInitialized"));
                    }
                    else
                    {
                        JoinServer(endpoint.ToEnumerable().ToImmutableArray(), "");
                    }
                }
                else if (LidgrenEndpoint.ParseFromWithHostNameCheck(endpointBox.Text, tryParseHostName: true).TryUnwrap(out var lidgrenEndpoint))
                {
                    JoinServer(((Endpoint)lidgrenEndpoint).ToEnumerable().ToImmutableArray(), "");
                }
                else
                {
                    new GUIMessageBox(TextManager.Get("error"), TextManager.GetWithVariable("invalidipaddress", "[serverip]:[port]", endpointBox.Text));
                    endpointBox.Flash();
                }
                msgBox.Close();
                return false;
            };

            var favoriteButton = msgBox.Buttons[1];
            favoriteButton.Enabled = false;
            favoriteButton.OnClicked = (button, userdata) =>
            {
                if (!Endpoint.Parse(endpointBox.Text).TryUnwrap(out var endpoint)) { return false; }

                var serverInfo = new ServerInfo(endpoint)
                {
                    ServerName = "Server",
                    GameVersion = GameMain.Version
                };

                var serverFrame = serverList.Content.FindChild(d =>
                    d.UserData is ServerInfo info
                    && info.Equals(serverInfo));

                if (serverFrame != null)
                {
                    serverInfo = (ServerInfo)serverFrame.UserData;
                }
                else
                {
                    AddToServerList(serverInfo);
                }

                AddToFavoriteServers(serverInfo);

                selectedTab = TabEnum.Favorites;
                FilterServers();

                msgBox.Close();
                return false;
            };

            var cancelButton = msgBox.Buttons[2];
            cancelButton.OnClicked = msgBox.Close;

            endpointBox.OnTextChanged += (textBox, text) =>
            {
                okButton.Enabled = favoriteButton.Enabled = !string.IsNullOrEmpty(text);                
                return true;
            };
        }

        private void RemoveMsgFromServerList()
        {
            serverList.Content.Children
                .Where(c => c.UserData is MsgUserData)
                .ForEachMod(serverList.Content.RemoveChild);
        }

        private void RemoveMsgFromServerList(MsgUserData userData)
        {
            serverList.Content.RemoveChild(serverList.Content.FindChild(userData));
        }
        
        private void PutMsgInServerList(MsgUserData userData)
        {
            RemoveMsgFromServerList();
            new GUITextBlock(new RectTransform(Vector2.One, serverList.Content.RectTransform),
                TextManager.Get(userData.ToString()), textAlignment: Alignment.Center)
            {
                CanBeFocused = false,
                UserData = userData
            };
        }
        
        private void RefreshServers()
        {
            lastRefreshTime = DateTime.Now;
            serverProvider?.Cancel();
            currentServerDataRecvCallbackObj = null;

            PingUtils.QueryPingData();

            tabs[TabEnum.All].Clear();
            serverList.ClearChildren();
            serverPreview.Content.ClearChildren();
            panelAnimator.RightEnabled = false;
            joinButton.Enabled = false;
            selectedServer = Option.None;

            if (selectedTab == TabEnum.All)
            {
                PutMsgInServerList(MsgUserData.RefreshingServerList);
            }
            else
            {
                var servers = tabs[selectedTab].Servers.ToArray();
                foreach (var server in servers)
                {
                    server.Ping = Option<int>.None();
                    AddToServerList(server, skipPing: true);
                }

                if (!servers.Any())
                {
                    PutMsgInServerList(MsgUserData.NoServers);
                    return;
                }
            }

            var (onServerDataReceived, onQueryCompleted) = MakeServerQueryCallbacks();
            serverProvider?.RetrieveServers(onServerDataReceived, onQueryCompleted);
        }

        private GUIComponent FindFrameMatchingServerInfo(ServerInfo serverInfo)
        {
            bool matches(GUIComponent c)
                => c.UserData is ServerInfo info
                   && info.Equals(serverInfo);

#if DEBUG
            if (serverList.Content.Children.Count(matches) > 1)
            {
                DebugConsole.ThrowError($"There are several entries in the server list for endpoints {string.Join(", ", serverInfo.Endpoints)}");
            }
#endif

            return serverList.Content.FindChild(matches);
        }

        private object currentServerDataRecvCallbackObj = null;
        private (Action<ServerInfo, ServerProvider> OnServerDataReceived, Action OnQueryCompleted) MakeServerQueryCallbacks()
        {
            var uniqueObject = new object();
            currentServerDataRecvCallbackObj = uniqueObject;

            bool shouldRunCallback()
            {
                // If currentServerDataRecvCallbackObj != uniqueObject, then one of the following happened:
                // - The query this call is associated to was meant to be over
                // - Another query was started before the one associated to this call was finished
                // In either case, do not add the received info to the server list.
                return ReferenceEquals(currentServerDataRecvCallbackObj, uniqueObject);
            }
            
            return (
                (serverInfo, serverProvider) =>
                {
                    if (!shouldRunCallback()) { return; }

                    if (serverProvider is not EosServerProvider
                        && EosInterface.IdQueries.IsLoggedIntoEosConnect)
                    {
                        if (serverInfo.EosCrossplay)
                        {
                            // EosServerProvider should get us this server,
                            // don't add it again
                            return;
                        }
                    }

                    if (selectedTab == TabEnum.All)
                    {
                        AddToServerList(serverInfo);
                    }
                    else
                    {
                        if (FindFrameMatchingServerInfo(serverInfo) == null) { return; }
                        UpdateServerInfoUI(serverInfo);
                        PingUtils.GetServerPing(serverInfo, UpdateServerInfoUI);
                    }
                },
                () =>
                {
                    if (shouldRunCallback()) { ServerQueryFinished(); }
                }
            );
        }

        private void AddToServerList(ServerInfo serverInfo, bool skipPing = false)
        {
            const int MaxAllowedPlayers = 1000;
            const int MaxAllowedSimilarServers = 10;
            const float MinSimilarityPercentage = 0.8f;

            if (string.IsNullOrWhiteSpace(serverInfo.ServerName)) { return; }
            if (serverInfo.PlayerCount > serverInfo.MaxPlayers) { return; }
            if (serverInfo.PlayerCount < 0) { return; }
            if (serverInfo.MaxPlayers <= 0) { return; }
            //no way a legit server can have this many players
            if (serverInfo.MaxPlayers > MaxAllowedPlayers) { return; }

            int similarServerCount = 0;
            string serverInfoStr = getServerInfoStr(serverInfo);
            foreach (var serverElement in serverList.Content.Children)
            {
                if (!serverElement.Visible) { continue; }
                if (serverElement.UserData is not ServerInfo otherServer || otherServer == serverInfo) { continue; }
                if (ToolBox.LevenshteinDistance(serverInfoStr, getServerInfoStr(otherServer)) < serverInfoStr.Length * (1.0f - MinSimilarityPercentage))
                {
                    similarServerCount++;
                    if (similarServerCount > MaxAllowedSimilarServers) 
                    {  
                        DebugConsole.Log($"Server {serverInfo.ServerName} seems to be almost identical to {otherServer.ServerName}. Hiding as a potential spam server.");
                        break;
                    }
                }
            }
            if (similarServerCount > MaxAllowedSimilarServers) { return; }

            static string getServerInfoStr(ServerInfo serverInfo)
            {
                string str = serverInfo.ServerName + serverInfo.ServerMessage + serverInfo.MaxPlayers;
                if (str.Length > 200) { return str.Substring(0, 200); }
                return str;
            }

            RemoveMsgFromServerList(MsgUserData.RefreshingServerList);
            RemoveMsgFromServerList(MsgUserData.NoServers);
            var serverFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), serverList.Content.RectTransform) { MinSize = new Point(0, 35) },
                style: "ListBoxElement")
            {
                UserData = serverInfo,
            };

            serverFrame.OnSecondaryClicked += (_, data) =>
            {
                if (data is not ServerInfo info) { return false; }
                CreateContextMenu(info);
                return true;
            };

            new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), serverFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = false
            };
            UpdateServerInfoUI(serverInfo);
            if (!skipPing) { PingUtils.GetServerPing(serverInfo, UpdateServerInfoUI); }
        }

        private static readonly Vector2 confirmPopupSize = new Vector2(0.2f, 0.2625f);
        private static readonly Point confirmPopupMinSize = new Point(300, 300);

        private void CreateContextMenu(ServerInfo info)
        {
            var favoriteOption = new ContextMenuOption(IsFavorite(info) ? "removefromfavorites" : "addtofavorites", isEnabled: true, () =>
            {
                if (IsFavorite(info))
                {
                    RemoveFromFavoriteServers(info);
                }
                else
                {
                    AddToFavoriteServers(info);
                }
                FilterServers();
            });
            var reportOption = new ContextMenuOption("reportserver", isEnabled: true, () => { CreateReportPrompt(info); });
            var filterOption = new ContextMenuOption("filterserver", isEnabled: true, () =>
            {
                CreateFilterServerPrompt(info);
            })
            {
                Tooltip = TextManager.Get("filterservertooltip")
            };

            GUIContextMenu.CreateContextMenu(favoriteOption, filterOption, reportOption);
        }

        public static void CreateFilterServerPrompt(ServerInfo info)
        {
            GUI.AskForConfirmation(
                header: TextManager.Get("filterserver"),
                body: TextManager.GetWithVariables("filterserverconfirm", ("[server]", info.ServerName), ("[filepath]", SpamServerFilter.SavePath)),
                onConfirm: () =>
                {
                    SpamServerFilters.AddServerToLocalSpamList(info);

                    if (GameMain.ServerListScreen is not { } serverListScreen) { return; }

                    if (serverListScreen.selectedServer.TryUnwrap(out var selectedServer) && selectedServer.Equals(info))
                    {
                        serverListScreen.HideServerPreview();
                    }
                    serverListScreen.FilterServers();
                }, relativeSize: confirmPopupSize, minSize: confirmPopupMinSize);
        }

        private enum ReportReason
        {
            Spam,
            Advertising,
            Inappropriate
        }

        public static void CreateReportPrompt(ServerInfo info)
        {
            if (!GameAnalyticsManager.SendUserStatistics)
            {
                GUI.NotifyPrompt(TextManager.Get("reportserver"), TextManager.Get("reportserverdisabled"));
                return;
            }

            var msgBox = new GUIMessageBox(
                headerText: TextManager.Get("reportserver"),
                text: string.Empty,
                relativeSize: new Vector2(0.2f, 0.4f),
                minSize: new Point(380, 430),
                buttons: Array.Empty<LocalizedString>());

            var layout = new GUILayoutGroup(new RectTransform(Vector2.One, msgBox.Content.RectTransform, Anchor.Center));

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.3f), layout.RectTransform), TextManager.GetWithVariable("reportserverexplanation", "[server]", info.ServerName), wrap: true)
            {
                ToolTip = TextManager.Get("reportserverprompttooltip")
            };

            var listBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.3f), layout.RectTransform));

            var enums = Enum.GetValues<ReportReason>();
            foreach (ReportReason reason in enums)
            {
                new GUITickBox(new RectTransform(new Vector2(1f, 1f / enums.Length), listBox.Content.RectTransform), TextManager.Get($"reportreason.{reason}"))
                {
                    UserData = reason
                };
            }

            // padding
            new GUIFrame(new RectTransform(new Vector2(1f, 0.05f), layout.RectTransform), style: null);

            var buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), layout.RectTransform))
            {
                Stretch = true
            };

            var reportAndHideButton = new GUIButton(new RectTransform(new Vector2(1f, 0.333f), buttonLayout.RectTransform), TextManager.Get("reportoption.reportandhide"))
            {
                Enabled = false,
                OnClicked = (_, _) =>
                {
                    CreateFilterServerPrompt(info);
                    msgBox.Close();
                    return true;
                }
            };
            var reportButton = new GUIButton(new RectTransform(new Vector2(1f, 0.333f), buttonLayout.RectTransform), TextManager.Get("reportoption.report"))
            {
                Enabled = false,
                OnClicked = (_, _) =>
                {
                    ReportServer(info, GetUserSelectedReasons());
                    msgBox.Close();
                    return true;
                }
            };

            new GUIButton(new RectTransform(new Vector2(1f, 0.333f), buttonLayout.RectTransform), TextManager.Get("cancel"))
            {
                OnClicked = (_, _) =>
                {
                    msgBox.Close();
                    return true;
                }
            };

            foreach (var child in listBox.Content.GetAllChildren<GUITickBox>())
            {
                child.OnSelected += _ =>
                {
                    reportAndHideButton.Enabled = reportButton.Enabled = GetUserSelectedReasons().Any();
                    return true;
                };
            }

            IEnumerable<ReportReason> GetUserSelectedReasons()
                => listBox.Content.Children
                          .Where(static c => c.UserData is ReportReason && c.Selected)
                          .Select(static c => (ReportReason)c.UserData).ToArray();
        }

        private static void ReportServer(ServerInfo info, IEnumerable<ReportReason> reasons)
        {
            if (!reasons.Any()) { return; }
            GameAnalyticsManager.AddErrorEvent(GameAnalyticsManager.ErrorSeverity.Info, $"[Spam] Reported server: Name: \"{info.ServerName}\", Message: \"{info.ServerMessage}\", Endpoint: \"{info.Endpoints.First().StringRepresentation}\". Reason: \"{string.Join(", ", reasons)}\".");
        }

        private void UpdateServerInfoUI(ServerInfo serverInfo)
        {
            var serverFrame = FindFrameMatchingServerInfo(serverInfo);
            if (serverFrame == null) { return; }

            serverFrame.UserData = serverInfo;

            var serverContent = serverFrame.Children.First() as GUILayoutGroup;
            serverContent.ClearChildren();

            Dictionary<ColumnLabel, GUIFrame> sections = new Dictionary<ColumnLabel, GUIFrame>();
            foreach (ColumnLabel label in Enum.GetValues(typeof(ColumnLabel)))
            {
                sections[label] =
                    new GUIFrame(
                        new RectTransform(new Vector2(columns[label].RelativeWidth, 1.0f), serverContent.RectTransform),
                        style: null);
            }

            void disableElementFocus()
            {
                sections.Values.ForEach(c =>
                {
                    c.CanBeFocused = false;
                    c.Children.First().CanBeFocused = false;
                });
            }

            RectTransform columnRT(ColumnLabel label, float scale = 0.95f)
                => new RectTransform(Vector2.One * scale, sections[label].RectTransform, Anchor.Center);

            void sectionTooltip(ColumnLabel label, RichString toolTip)
            {
                var section = sections[label];
                section.CanBeFocused = true;
                section.ToolTip = toolTip;
            }

            var compatibleBox = new GUITickBox(columnRT(ColumnLabel.ServerListCompatible), label: "")
            {
                CanBeFocused = false,
                Selected =
                    NetworkMember.IsCompatible(GameMain.Version, serverInfo.GameVersion),
                UserData = "compatible"
            };

            var passwordBox = new GUITickBox(columnRT(ColumnLabel.ServerListHasPassword, scale: 0.6f), label: "", style: "GUIServerListPasswordTickBox")
            {
				Selected = serverInfo.HasPassword,
                UserData = "password",
                CanBeFocused = false
            };
            sectionTooltip(ColumnLabel.ServerListHasPassword,
                TextManager.Get((serverInfo.HasPassword) ? "ServerListHasPassword" : "FilterPassword"));

            var serverName = new GUITextBlock(columnRT(ColumnLabel.ServerListName),
#if DEBUG
                $"[{serverInfo.Endpoints.First().GetType().Name}] " +
#endif
                serverInfo.ServerName,
                style: "GUIServerListTextBox") { CanBeFocused = false };

            if (serverInfo.IsModded)
            {
                serverName.TextColor = GUIStyle.ModdedServerColor;
            }

            new GUITickBox(columnRT(ColumnLabel.ServerListRoundStarted), label: "")
            {
				Selected = serverInfo.GameStarted,
                CanBeFocused = false
			};
            sectionTooltip(ColumnLabel.ServerListRoundStarted,
                TextManager.Get(serverInfo.GameStarted ? "ServerListRoundStarted" : "ServerListRoundNotStarted"));

            var serverPlayers = new GUITextBlock(columnRT(ColumnLabel.ServerListPlayers),
                $"{serverInfo.PlayerCount}/{serverInfo.MaxPlayers}", style: "GUIServerListTextBox", textAlignment: Alignment.Right)
            {
                ToolTip = TextManager.Get("ServerListPlayers")
            };

            var serverPingText = new GUITextBlock(columnRT(ColumnLabel.ServerListPing), "?", 
                style: "GUIServerListTextBox", textColor: Color.White * 0.5f, textAlignment: Alignment.Right)
            {
                ToolTip = TextManager.Get("ServerListPing")
            };

            if (serverInfo.Ping.TryUnwrap(out var ping))
            {
                serverPingText.Text = ping.ToString();
                serverPingText.TextColor = GetPingTextColor(ping);
            }
            else if ((serverInfo.Endpoints.Length == 1 && serverInfo.Endpoints.First() is EosP2PEndpoint)
                     || (!SteamManager.IsInitialized && serverInfo.Endpoints.Any(e => e is P2PEndpoint)))
            {
                serverPingText.Text = "-";
                serverPingText.ToolTip = TextManager.Get("EosPingUnavailable");
                serverPingText.TextAlignment = Alignment.Center;
            }
            else
            {
                serverPingText.Text = "?";
                serverPingText.TextColor = Color.DarkRed;
            }

            LocalizedString toolTip = "";
            if (!serverInfo.Checked)
            {
                toolTip = TextManager.Get("ServerOffline");
                serverName.TextColor *= 0.8f;
                serverPlayers.TextColor *= 0.8f;
            }
            else if (!serverInfo.ContentPackages.Any())
            {
                compatibleBox.Selected = false;
                new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.8f), compatibleBox.Box.RectTransform, Anchor.Center),
                    " ? ", GUIStyle.Orange * 0.85f, textAlignment: Alignment.Center)
                {
                    ToolTip = TextManager.Get("ServerListUnknownContentPackage")
                };
            }
            else if (!compatibleBox.Selected)
            {
                if (serverInfo.GameVersion != GameMain.Version)
                {
                    toolTip = TextManager.GetWithVariable("ServerListIncompatibleVersion", "[version]", serverInfo.GameVersion.ToString());
                }

                int maxIncompatibleToList = 10;
                List<LocalizedString> incompatibleModNames = new List<LocalizedString>();
                foreach (var contentPackage in serverInfo.ContentPackages)
                {
                    bool listAsIncompatible = !ContentPackageManager.EnabledPackages.All.Any(cp => cp.Hash.StringRepresentation == contentPackage.Hash);
                    if (listAsIncompatible)
                    {
                        incompatibleModNames.Add(TextManager.GetWithVariables("ModNameAndHashFormat", 
                            ("[name]", contentPackage.Name), 
                            ("[hash]", Md5Hash.GetShortHash(contentPackage.Hash))));
                    }
                }
                if (incompatibleModNames.Any())
                {
                    toolTip += '\n' + TextManager.Get("ModDownloadHeader") + "\n" + string.Join(", ", incompatibleModNames.Take(maxIncompatibleToList));
                    if (incompatibleModNames.Count > maxIncompatibleToList)
                    {
                        toolTip += '\n' + TextManager.GetWithVariable("workshopitemdownloadprompttruncated", "[number]", (incompatibleModNames.Count - maxIncompatibleToList).ToString());
                    }
                }

                serverName.TextColor *= 0.5f;
                serverPlayers.TextColor *= 0.5f;
            }
            else
            {
                foreach (var contentPackage in serverInfo.ContentPackages)
                {
                    if (ContentPackageManager.EnabledPackages.All.None(cp => cp.Hash.StringRepresentation == contentPackage.Hash))
                    {
                        if (toolTip != "") { toolTip += "\n"; }
                        toolTip += TextManager.GetWithVariable("ServerListIncompatibleContentPackageWorkshopAvailable", "[contentpackage]", contentPackage.Name);
                        break;
                    }
                }
            }
            disableElementFocus();

            string separator = toolTip.IsNullOrWhiteSpace() ? "" : "\n\n";
            serverFrame.ToolTip = RichString.Rich(toolTip + separator + $"‖color:gui.blue‖{TextManager.GetWithVariable("serverlisttooltip", "[button]", PlayerInput.SecondaryMouseLabel)}‖end‖");

            foreach (var section in sections.Values)
            {
                var child = section.Children.First();
                child.RectTransform.ScaleBasis
                    = child is GUITextBlock ? ScaleBasis.Normal : ScaleBasis.BothHeight;
            }

            // The next twenty-something lines are an optimization.
            // The issue is that the serverlist has a ton of text elements,
            // and resizing all of them is extremely expensive. However, since
            // you don't see most of them most of the time, it makes sense to
            // just resize them lazily based on when you actually can see them.
            // That would entail a UI refactor of some kind, and I don't want to
            // do that just yet, so here's a hack instead!
            bool isDirty = true;
            void markAsDirty() => isDirty = true;
            serverContent.GetAllChildren().ForEach(c =>
            {
                c.RectTransform.ResetSizeChanged();
                c.RectTransform.SizeChanged += markAsDirty;
            });
            new GUICustomComponent(new RectTransform(Vector2.Zero, serverContent.RectTransform), onUpdate: (_, __) =>
            {
                if (serverFrame.MouseRect.Height <= 0 || !isDirty) { return; }
                serverContent.GetAllChildren().ForEach(c =>
                {
                    switch (c)
                    {
                        case GUITextBlock textBlock:
                            textBlock.SetTextPos();
                            break;
                        case GUITickBox tickBox:
                            tickBox.ResizeBox();
                            break;
                    }
                });
                serverName.Text = ToolBox.LimitString(serverInfo.ServerName, serverName.Font, serverName.Rect.Width);
                isDirty = false;
            });
            // Hacky optimization ends here
            
            serverContent.Recalculate();

            if (tabs[TabEnum.Favorites].Contains(serverInfo))
            {
                AddToFavoriteServers(serverInfo);
            }

            InsertServer(serverInfo, serverFrame);
            FilterServers();
        }

        private void ServerQueryFinished()
        {
            currentServerDataRecvCallbackObj = null;
            if (!serverList.Content.Children.Any(c => c.UserData is ServerInfo))
            {
                PutMsgInServerList(MsgUserData.NoServers);
            }
            else if (serverList.Content.Children.All(c => !c.Visible))
            {
                PutMsgInServerList(MsgUserData.NoMatchingServers);
            }
        }

        public void JoinServer(ImmutableArray<Endpoint> endpoints, string serverName)
        {
            if (string.IsNullOrWhiteSpace(ClientNameBox.Text))
            {
                ClientNameBox.Flash();
                ClientNameBox.Select();
                SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                return;
            }

            MultiplayerPreferences.Instance.PlayerName = ClientNameBox.Text;
            GameSettings.SaveCurrentConfig();

            if (MultiplayerPreferences.Instance.PlayerName.IsNullOrEmpty())
            {
                TaskPool.Add("GetDefaultUserName",
                    GetDefaultUserName(),
                    t =>
                    {
                        if (!t.TryGetResult(out string name)) { return; }
                        startClient(name);
                    });
            }
            else
            {
                startClient(MultiplayerPreferences.Instance.PlayerName);
            }

            void startClient(string name)
            {
#if !DEBUG
                try
                {
#endif
                    GameMain.Client = new GameClient(name, endpoints, serverName, Option.None);
#if !DEBUG
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to start the client", e);
                }
#endif
            }
        }

        private static Color GetPingTextColor(int ping)
        {
            if (ping < 0) { return Color.DarkRed; }
            return ToolBox.GradientLerp(ping / 200.0f, GUIStyle.Green, GUIStyle.Orange, GUIStyle.Red);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch);            
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
        }

        public void StoreServerFilters()
        {
            if (loadingServerFilters) { return; }
            foreach (KeyValuePair<Identifier, GUITickBox> filterBox in filterTickBoxes)
            {
                ServerListFilters.Instance.SetAttribute(filterBox.Key, filterBox.Value.Selected.ToString());
            }
            foreach (KeyValuePair<Identifier, GUIDropDown> ternaryFilter in ternaryFilters)
            {
                ServerListFilters.Instance.SetAttribute(ternaryFilter.Key, ternaryFilter.Value.SelectedData.ToString());
            }
            GameSettings.SaveCurrentConfig();
        }

        private bool loadingServerFilters;
        public void LoadServerFilters()
        {
            loadingServerFilters = true;
            XDocument currentConfigDoc = XMLExtensions.TryLoadXml(GameSettings.PlayerConfigPath);
            ServerListFilters.Init(currentConfigDoc.Root.GetChildElement("serverfilters"));
            foreach (KeyValuePair<Identifier, GUITickBox> filterBox in filterTickBoxes)
            {
                filterBox.Value.Selected =
                    ServerListFilters.Instance.GetAttributeBool(filterBox.Key, filterBox.Value.Selected);
            }
            foreach (KeyValuePair<Identifier, GUIDropDown> ternaryFilter in ternaryFilters)
            {
                TernaryOption ternaryOption =
                    ServerListFilters.Instance.GetAttributeEnum(
                        ternaryFilter.Key,
                        (TernaryOption)ternaryFilter.Value.SelectedData);

                var child = ternaryFilter.Value.ListBox.Content.GetChildByUserData(ternaryOption);
                ternaryFilter.Value.Select(ternaryFilter.Value.ListBox.Content.GetChildIndex(child));
            }
            loadingServerFilters = false;
        }
        
    }
}
