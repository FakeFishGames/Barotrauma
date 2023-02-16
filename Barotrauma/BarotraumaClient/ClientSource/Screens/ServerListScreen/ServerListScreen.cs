using Barotrauma.Extensions;
using Barotrauma.IO;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

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

        //friends list
        public sealed class FriendInfo
        {
            public string Name;

            public readonly AccountId Id;

            public enum Status
            {
                Offline,
                NotPlaying,
                PlayingAnotherGame,
                PlayingBarotrauma
            }

            public readonly Status CurrentStatus;

            public string ServerName;

            public Option<ConnectCommand> ConnectCommand;
            public Option<Sprite> Avatar;

            public bool IsInServer
                => CurrentStatus == Status.PlayingBarotrauma && ConnectCommand.IsSome();

            public bool IsPlayingBarotrauma
                => CurrentStatus == Status.PlayingBarotrauma;

            public bool PlayingAnotherGame
                => CurrentStatus == Status.PlayingAnotherGame;

            public bool IsOnline
                => CurrentStatus != Status.Offline;

            public LocalizedString StatusText
                => CurrentStatus switch
                {
                    Status.Offline => "",
                    _ when ConnectCommand.IsSome()
                        => TextManager.GetWithVariable("FriendPlayingOnServer", "[servername]", ServerName),
                    _ => TextManager.Get($"Friend{CurrentStatus}")
                };

            public FriendInfo(string name, AccountId id, Status status)
            {
                Name = name;
                Id = id;
                CurrentStatus = status;
                ConnectCommand = Option<ConnectCommand>.None();
                Avatar = Option<Sprite>.None();
            }
        }

        private GUILayoutGroup friendsButtonHolder;

        private GUIButton friendsDropdownButton;
        private GUIListBox friendsDropdown;

        private readonly FriendProvider friendProvider = new SteamFriendProvider();

        private List<FriendInfo> friendsList;
        private GUIFrame friendPopup;
        private double friendsListUpdateTime;

        public enum TabEnum
        {
            All,
            Favorites,
            Recent
        }

        public struct Tab
        {
            public readonly string Storage;
            public readonly GUIButton Button;
            
            private readonly List<ServerInfo> servers;
            public IReadOnlyList<ServerInfo> Servers => servers;

            public Tab(TabEnum tabEnum, ServerListScreen serverListScreen, GUILayoutGroup tabber, string storage)
            {
                Storage = storage;
                servers = new List<ServerInfo>();
                Button = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), tabber.RectTransform),
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

        private readonly ServerProvider serverProvider
            = new CompositeServerProvider(new SteamDedicatedServerProvider(), new SteamP2PServerProvider());

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
        public ServerListScreen()
        {
            selectedServer = Option<ServerInfo>.None();
            GameMain.Instance.ResolutionChanged += CreateUI;
            CreateUI();
        }

        private string GetDefaultUserName()
        {
            return friendProvider.GetUserName();
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

            var title = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.33f), topRow.RectTransform), TextManager.Get("JoinServer"), font: GUIStyle.LargeFont)
            {
                Padding = Vector4.Zero,
                ForceUpperCase = ForceUpperCase.Yes,
                AutoScaleHorizontal = true
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

            if (string.IsNullOrEmpty(ClientNameBox.Text))
            {
                ClientNameBox.Text = GetDefaultUserName();
            }
            ClientNameBox.OnTextChanged += (textbox, text) =>
            {
                MultiplayerPreferences.Instance.PlayerName = text;
                return true;
            };

            var tabButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - sidebarWidth - infoHolder.RelativeSpacing, 0.5f), infoHolder.RectTransform), isHorizontal: true);

            tabs[TabEnum.All] = new Tab(TabEnum.All, this, tabButtonHolder, "");
            tabs[TabEnum.Favorites] = new Tab(TabEnum.Favorites, this, tabButtonHolder, "Data/favoriteservers.xml");
            tabs[TabEnum.Recent] = new Tab(TabEnum.Recent, this, tabButtonHolder, "Data/recentservers.xml");

            var friendsButtonFrame = new GUIFrame(new RectTransform(new Vector2(0.31f, 2.0f), tabButtonHolder.RectTransform, Anchor.BottomRight), style: "InnerFrame")
            {
                IgnoreLayoutGroups = true
            };

            friendsButtonHolder = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.9f), friendsButtonFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopLeft) { RelativeSpacing = 0.01f, IsHorizontal = true };
            friendsList = new List<FriendInfo>();

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
                    if (!(obj is ServerInfo serverInfo)) { return false; }

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
                        JoinServer(serverInfo.Endpoint, serverInfo.ServerName);
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
                existingServerInfo.Endpoint == serverInfo.Endpoint);
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
            if (info.Endpoint.Address.IsLocalHost) { return; }
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
            if (!(obj is ColumnLabel sortBy)) { return false; }
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
                int comparison = sortedAscending ? 1 : -1;
                return CompareServer(sortBy, s1, s2) * comparison;
            });
        }

        private void InsertServer(ServerInfo serverInfo, GUIComponent component)
        {
            var children = serverList.Content.RectTransform.Children.Reverse().ToList();

            int comparison = sortedAscending ? 1 : -1;
            foreach (var child in children)
            {
                if (child.GUIComponent.UserData is not ServerInfo serverInfo2 || serverInfo.Equals(serverInfo2)) { continue; }
                if (CompareServer(sortedBy, serverInfo, serverInfo2) * comparison >= 0)
                {
                    var index = serverList.Content.RectTransform.GetChildIndex(child);
                    component.RectTransform.RepositionChildInHierarchy(index + 1);
                    return;
                }
            }
            component.RectTransform.SetAsFirstChild();
        }

        private static int CompareServer(ColumnLabel sortBy, ServerInfo s1, ServerInfo s2)
        {
            switch (sortBy)
            {
                case ColumnLabel.ServerListCompatible:
                    bool s1Compatible = NetworkMember.IsCompatible(GameMain.Version, s1.GameVersion);
                    bool s2Compatible = NetworkMember.IsCompatible(GameMain.Version, s2.GameVersion);

                    if (s1Compatible == s2Compatible) { return 0; }
                    return s1Compatible ? -1 : 1;
                case ColumnLabel.ServerListHasPassword:
                    if (s1.HasPassword == s2.HasPassword) { return 0; }
                    return s1.HasPassword ? 1 : -1;
                case ColumnLabel.ServerListName:
                    // I think we actually want culture-specific sorting here?
                    return string.Compare(s1.ServerName, s2.ServerName, StringComparison.CurrentCulture);
                case ColumnLabel.ServerListRoundStarted:
                    if (s1.GameStarted == s2.GameStarted) { return 0; }
                    return s1.GameStarted ? 1 : -1;
                case ColumnLabel.ServerListPlayers:
                    return s2.PlayerCount.CompareTo(s1.PlayerCount);
                case ColumnLabel.ServerListPing:
                    return (s1.Ping.TryUnwrap(out var s1Ping), s2.Ping.TryUnwrap(out var s2Ping)) switch
                    {
                        (false, false) => 0,
                        (true, true) => s2Ping.CompareTo(s1Ping),
                        (false, true) => 1,
                        (true, false) => -1
                    };
                default:
                    return 0;
            }
        }
        
        public override void Select()
        {
            base.Select();
            
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
            GameSettings.SaveCurrentConfig();
        }

        public override void Update(double deltaTime)
        {
            base.Update(deltaTime);

            UpdateFriendsList();
            panelAnimator?.Update();

            scanServersButton.Enabled = (DateTime.Now - lastRefreshTime) >= AllowedRefreshInterval;

            if (PlayerInput.PrimaryMouseButtonClicked())
            {
                friendPopup = null;
                if (friendsDropdown != null && friendsDropdownButton != null &&
                    !friendsDropdown.Rect.Contains(PlayerInput.MousePosition) &&
                    !friendsDropdownButton.Rect.Contains(PlayerInput.MousePosition))
                {
                    friendsDropdown.Visible = false;
                }
            }
        }

        private void FilterServers()
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
                if ((serverInfo.TraitorsEnabled == YesNoMaybe.Yes || serverInfo.TraitorsEnabled == YesNoMaybe.Maybe) != (filterTraitorValue == TernaryOption.Enabled)) 
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform), TextManager.Get("ServerEndpoint"), textAlignment: Alignment.Center);
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
                    JoinServer(endpoint, "");
                }
                else if (LidgrenEndpoint.ParseFromWithHostNameCheck(endpointBox.Text, tryParseHostName: true).TryUnwrap(out var lidgrenEndpoint))
                {
                    JoinServer(lidgrenEndpoint, "");
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

                #warning Interface with server providers to get up-to-date info on the given server

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

        private bool JoinFriend(GUIButton button, object userdata)
        {
            if (!(userdata is FriendInfo { IsInServer: true } info)) { return false; }

            GameMain.Instance.ConnectCommand = info.ConnectCommand;
            return false;
        }

        private bool OpenFriendPopup(GUIButton button, object userdata)
        {
            if (!(userdata is FriendInfo { IsInServer: true } info)) { return false; }

            if (info.IsInServer
                && info.ConnectCommand.TryUnwrap(out var command)
                && command.EndpointOrLobby.TryGet(out ConnectCommand.NameAndEndpoint nameAndEndpoint))
            {
                const int framePadding = 5;

                friendPopup = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas));

                var serverNameText = new GUITextBlock(new RectTransform(new Vector2(0.7f, 1.0f), friendPopup.RectTransform, Anchor.CenterLeft), nameAndEndpoint.ServerName ?? "[Unnamed]");
                serverNameText.RectTransform.AbsoluteOffset = new Point(framePadding, 0);

                var joinButton = new GUIButton(new RectTransform(new Vector2(0.3f, 1.0f), friendPopup.RectTransform, Anchor.CenterRight), TextManager.Get("ServerListJoin"))
                {
                    UserData = info
                };
                joinButton.OnClicked = JoinFriend;
                joinButton.RectTransform.AbsoluteOffset = new Point(framePadding, 0);

                Point joinButtonTextSize = joinButton.Font.MeasureString(joinButton.Text).ToPoint();
                int joinButtonHeight = joinButton.RectTransform.NonScaledSize.Y;
                int totalAdditionalTextPadding = (joinButtonHeight - joinButtonTextSize.Y);

                // Make the final button sized so that the space between the text and the edges in the X direction is the same as the Y direction.
                Point finalButtonSize = new Point(joinButtonTextSize.X + totalAdditionalTextPadding, joinButtonHeight);

                // Add padding to the server name to match the padding on the button text.
                serverNameText.Padding = new Vector4(totalAdditionalTextPadding / 2);

                // Get the dimensions of the text we want to show, plus the extra padding we added.
                Point serverNameSize = serverNameText.Font.MeasureString(serverNameText.Text).ToPoint() + new Point(totalAdditionalTextPadding, totalAdditionalTextPadding);

                // Now determine how large the parent frame has to be to exactly fit our two controls.
                Point frameDims = new Point(serverNameSize.X + finalButtonSize.X + framePadding*2, Math.Max(serverNameSize.Y, finalButtonSize.Y) + framePadding * 2);

                var popupPos = PlayerInput.MousePosition.ToPoint();
                if(popupPos.X+frameDims.X > GUI.Canvas.NonScaledSize.X)
                {
                    // Prevent the Join button from going off the end of the screen if the server name is long or we click a user towards the edge.
                    popupPos.X = GUI.Canvas.NonScaledSize.X - frameDims.X;
                }

                // Apply the size and position changes.
                friendPopup.RectTransform.NonScaledSize = frameDims;
                friendPopup.RectTransform.RelativeOffset = Vector2.Zero;
                friendPopup.RectTransform.AbsoluteOffset = popupPos;

                joinButton.RectTransform.NonScaledSize = finalButtonSize;

                friendPopup.RectTransform.RecalculateChildren(true);
                friendPopup.RectTransform.SetPosition(Anchor.TopLeft);
            }

            return false;
        }

        public enum AvatarSize
        {
            Small,
            Medium,
            Large
        }

        private void UpdateFriendsList()
        {
            if (friendsListUpdateTime > Timing.TotalTime) { return; }
            friendsListUpdateTime = Timing.TotalTime + 5.0;

            float prevDropdownScroll = friendsDropdown?.ScrollBar.BarScrollValue ?? 0.0f;

            friendsDropdown ??= new GUIListBox(new RectTransform(Vector2.One, GUI.Canvas))
            {
                OutlineColor = Color.Black,
                Visible = false
            };
            friendsDropdown.ClearChildren();

            var avatarSize = friendsButtonHolder.RectTransform.Rect.Height switch
            {
                var h when h <= 24 => AvatarSize.Small,
                var h when h <= 48 => AvatarSize.Medium,
                _ => AvatarSize.Large
            };
            
            FriendInfo[] friends = friendProvider.RetrieveFriends();

            foreach (var friend in friends)
            {
                int existingIndex = friendsList.FindIndex(f => f.Id == friend.Id);
                if (existingIndex >= 0)
                {
                    friend.Avatar = friend.Avatar.Fallback(friendsList[existingIndex].Avatar);
                }

                if (friend.Avatar.IsNone())
                {
                    friendProvider.RetrieveAvatar(friend, avatarSize);
                }
            }

            friendsList.Clear(); friendsList.AddRange(friends.OrderByDescending(f => f.CurrentStatus));

            friendsButtonHolder.ClearChildren();

            if (friendsList.Count > 0)
            {
                friendsDropdownButton = new GUIButton(new RectTransform(Vector2.One, friendsButtonHolder.RectTransform, Anchor.BottomRight, Pivot.BottomRight, scaleBasis: ScaleBasis.BothHeight), "\u2022 \u2022 \u2022", style: "GUIButtonFriendsDropdown")
                {
                    OnClicked = (button, udt) =>
                    {
                        friendsDropdown.RectTransform.NonScaledSize = new Point(friendsButtonHolder.Rect.Height * 5 * 166 / 100, friendsButtonHolder.Rect.Height * 4 * 166 / 100);
                        friendsDropdown.RectTransform.AbsoluteOffset = new Point(friendsButtonHolder.Rect.X, friendsButtonHolder.Rect.Bottom);
                        friendsDropdown.RectTransform.RecalculateChildren(true);
                        friendsDropdown.Visible = !friendsDropdown.Visible;
                        return false;
                    }
                };
            }
            else
            {
                friendsDropdownButton = null;
                friendsDropdown.Visible = false;
            }

            for (int i = 0; i < friendsList.Count; i++)
            {
                var friend = friendsList[i];

                if (i < 5)
                {
                    string style = friend.IsPlayingBarotrauma
                        ? "GUIButtonFriendPlaying"
                        : "GUIButtonFriendNotPlaying";

                    var guiButton = new GUIButton(new RectTransform(Vector2.One, friendsButtonHolder.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: style)
                    {
                        UserData = friend,
                        OnClicked = OpenFriendPopup
                    };
                    guiButton.ToolTip = friend.Name + "\n" + friend.StatusText;

                    if (friend.Avatar.TryUnwrap(out Sprite sprite))
                    {
                        new GUICustomComponent(new RectTransform(Vector2.One, guiButton.RectTransform, Anchor.Center),
                            onDraw: (sb, component) =>
                            {
                                var destinationRect = component.Rect;
                                destinationRect.Inflate(-GUI.IntScale(4), -GUI.IntScale(4));
                                sb.Draw(sprite.Texture, destinationRect, Color.White);

                                if (!GUI.IsMouseOn(guiButton))
                                {
                                    return;
                                }

                                sb.End();
                                sb.Begin(
                                    SpriteSortMode.Deferred,
                                    blendState: BlendState.Additive,
                                    samplerState: GUI.SamplerState,
                                    rasterizerState: GameMain.ScissorTestEnable);
                                sb.Draw(sprite.Texture, destinationRect, Color.White * 0.5f);
                                sb.End();
                                sb.Begin(
                                    SpriteSortMode.Deferred,
                                    samplerState: GUI.SamplerState,
                                    rasterizerState: GameMain.ScissorTestEnable);
                            }) { CanBeFocused = false };
                    }
                }

                var friendFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.167f), friendsDropdown.Content.RectTransform), style: "GUIFrameFriendsDropdown");
                if (friend.Avatar.TryUnwrap(out var avatar))
                {
                    GUIImage guiImage =
                        new GUIImage(
                            new RectTransform(Vector2.One * 0.9f, friendFrame.RectTransform, Anchor.CenterLeft,
                                scaleBasis: ScaleBasis.BothHeight) { RelativeOffset = new Vector2(0.02f, 0.02f) },
                            avatar, null, true);
                }

                var textBlock = new GUITextBlock(new RectTransform(Vector2.One * 0.8f, friendFrame.RectTransform, Anchor.CenterLeft, scaleBasis: ScaleBasis.BothHeight) { RelativeOffset = new Vector2(1.0f / 7.7f, 0.0f) }, friend.Name + "\n" + friend.StatusText)
                {
                    Font = GUIStyle.SmallFont
                };
                if (friend.IsPlayingBarotrauma) { textBlock.TextColor = GUIStyle.Green; }
                if (friend.PlayingAnotherGame) { textBlock.TextColor = GUIStyle.Blue; }

                if (friend.IsInServer)
                {
                    var joinButton = new GUIButton(new RectTransform(new Vector2(0.25f, 0.6f), friendFrame.RectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.05f, 0.0f) }, TextManager.Get("ServerListJoin"), style: "GUIButtonJoinFriend")
                    {
                        UserData = friend,
                        OnClicked = JoinFriend
                    };
                }
            }

            friendsDropdown.RectTransform.NonScaledSize = new Point(friendsButtonHolder.Rect.Height * 5 * 166 / 100, friendsButtonHolder.Rect.Height * 4 * 166 / 100);
            friendsDropdown.RectTransform.AbsoluteOffset = new Point(friendsButtonHolder.Rect.X, friendsButtonHolder.Rect.Bottom);
            friendsDropdown.RectTransform.RecalculateChildren(true);

            friendsDropdown.ScrollBar.BarScrollValue = prevDropdownScroll;
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
            serverProvider.Cancel();
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
            serverProvider.RetrieveServers(onServerDataReceived, onQueryCompleted);
        }

        private GUIComponent FindFrameMatchingServerInfo(ServerInfo serverInfo)
        {
            bool matches(GUIComponent c)
                => c.UserData is ServerInfo info
                   && info.Equals(serverInfo);

#if DEBUG
            if (serverList.Content.Children.Count(matches) > 1)
            {
                DebugConsole.ThrowError($"There are several entries in the server list for endpoint {serverInfo.Endpoint}");
            }
#endif

            return serverList.Content.FindChild(matches);
        }

        private object currentServerDataRecvCallbackObj = null;
        private (Action<ServerInfo> OnServerDataReceived, Action OnQueryCompleted) MakeServerQueryCallbacks()
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
                serverInfo =>
                {
                    if (!shouldRunCallback()) { return; }

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
            if (serverInfo.PlayerCount > serverInfo.MaxPlayers) { return; }
            if (serverInfo.PlayerCount < 0) { return; }
            if (serverInfo.MaxPlayers <= 0) { return; }

            RemoveMsgFromServerList(MsgUserData.RefreshingServerList);
            RemoveMsgFromServerList(MsgUserData.NoServers);
            var serverFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.06f), serverList.Content.RectTransform) { MinSize = new Point(0, 35) },
                style: "ListBoxElement")
            {
                UserData = serverInfo
            };
            new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), serverFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = false
            };
            UpdateServerInfoUI(serverInfo);
            if (!skipPing) { PingUtils.GetServerPing(serverInfo, UpdateServerInfoUI); }

            InsertServer(serverInfo, serverFrame);
        }

        private void UpdateServerInfoUI(ServerInfo serverInfo)
        {
            var serverFrame = FindFrameMatchingServerInfo(serverInfo);
            if (serverFrame == null) { return; }

            serverFrame.UserData = serverInfo;

            serverFrame.ToolTip = "";
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
            
            void errorTooltip(RichString toolTip)
            {
                sections.Values.ForEach(c =>
                {
                    c.CanBeFocused = false;
                    c.Children.First().CanBeFocused = false;
                });
                serverFrame.ToolTip = toolTip;
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
                $"[{serverInfo.Endpoint.GetType().Name}] " +
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
            else
            {
                serverPingText.Text = "?";
                serverPingText.TextColor = Color.DarkRed;
            }

            if (!serverInfo.Checked)
            {
                errorTooltip(TextManager.Get("ServerOffline"));
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
                LocalizedString toolTip = "";
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
                errorTooltip(toolTip);

                serverName.TextColor *= 0.5f;
                serverPlayers.TextColor *= 0.5f;
            }
            else
            {
                LocalizedString toolTip = "";
                foreach (var contentPackage in serverInfo.ContentPackages)
                {
                    if (ContentPackageManager.EnabledPackages.All.None(cp => cp.Hash.StringRepresentation == contentPackage.Hash))
                    {
                        if (toolTip != "") { toolTip += "\n"; }
                        toolTip += TextManager.GetWithVariable("ServerListIncompatibleContentPackageWorkshopAvailable", "[contentpackage]", contentPackage.Name);
                        break;
                    }
                }
                errorTooltip(toolTip);
            }

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

            SortList(sortedBy, toggle: false);
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

        public void JoinServer(Endpoint endpoint, string serverName)
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

#if !DEBUG
            try
            {
#endif
            GameMain.Client = new GameClient(MultiplayerPreferences.Instance.PlayerName.FallbackNullOrEmpty(GetDefaultUserName()), endpoint, serverName, Option<int>.None());
#if !DEBUG
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to start the client", e);
            }
#endif
        }
        
        private static Color GetPingTextColor(int ping)
        {
            if (ping < 0) { return Color.DarkRed; }
            return ToolBox.GradientLerp(ping / 200.0f, GUIStyle.Green, GUIStyle.Orange, GUIStyle.Red);
        }

        public override void Draw(double deltaTime, GraphicsDevice graphics, SpriteBatch spriteBatch)
        {
            graphics.Clear(Color.CornflowerBlue);

            GameMain.TitleScreen.DrawLoadingText = false;

            spriteBatch.Begin(SpriteSortMode.Deferred, null, GUI.SamplerState, null, GameMain.ScissorTestEnable);
            GameMain.MainMenuScreen.DrawBackground(graphics, spriteBatch);            
            GUI.Draw(Cam, spriteBatch);
            spriteBatch.End();
        }

        public override void AddToGUIUpdateList()
        {
            menu.AddToGUIUpdateList();
            friendPopup?.AddToGUIUpdateList();
            friendsDropdown?.AddToGUIUpdateList();
        }

        public void StoreServerFilters()
        {
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

        public void LoadServerFilters()
        {
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
        }
        
    }
}
